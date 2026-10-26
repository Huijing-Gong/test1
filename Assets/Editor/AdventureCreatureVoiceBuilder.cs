using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using CompressionLevel = System.IO.Compression.CompressionLevel;

// Offline, scratch-project-only preparation. Raw downloaded source packs never
// become runtime assets; only the selected processed one-shots are retained.
public static class AdventureCreatureVoiceBuilder
{
    const string Stage = "Assets/Editor/AdventureVoiceSourceStage";
    const string Destination = "Assets/Art/Enemies/AdventureTrio/Resources/CreatureVoices";
    const string Reports = "LocalDocs/Enemies/AdventureTrio-20261021/Audio";
    const string SourceArchive = Reports + "/Selected_CC0_Sources.zip";
    const int OutputRate = 22050;
    const float TargetPeak = .68f;
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    sealed class Entry
    {
        public string role, cue, source, author, page, archive;
        public float pitch;
        public Entry(string role, string cue, string source, string author, string page, string archive, float pitch)
        { this.role = role; this.cue = cue; this.source = source; this.author = author; this.page = page; this.archive = archive; this.pitch = pitch; }
    }

    sealed class Built
    {
        public Entry entry;
        public float[] samples;
        public int rate, channels;
        public float inputSeconds, trimmedSeconds, peak, rms;
        public string rawHash, hash;
    }

    public static void Build()
    {
        var project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
        if (!project.StartsWith("/private/tmp/adventure-trio-", StringComparison.Ordinal))
            throw new InvalidOperationException("Voice preparation is restricted to /private/tmp/adventure-trio- scratch projects.");

        string sources = Environment.GetEnvironmentVariable("ADVENTURE_CREATURE_VOICE_SOURCES");
        if (string.IsNullOrWhiteSpace(sources)) sources = "/private/tmp/trio-voice-research-20261021.QeZM5T";
        sources = Path.GetFullPath(sources);
        var entries = Entries();
        foreach (var entry in entries)
            if (!HasSource(sources, entry.source))
                throw new FileNotFoundException("Missing verified voice source in the research folder and Selected_CC0_Sources.zip: " + entry.source);
        if (Directory.Exists(Stage) || AssetDatabase.IsValidFolder(Stage))
            throw new InvalidOperationException("The temporary voice source folder already exists; refusing to remove or overwrite an unowned folder.");

        Directory.CreateDirectory(Reports);
        PreserveSelectedSources(entries, sources);
        var results = new List<Built>();
        bool ownsStage = false;
        try
        {
            Directory.CreateDirectory(Stage); ownsStage = true;
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                string staged = Stage + "/" + i.ToString("D2", Invariant) + "_" + entry.role + "_" + entry.cue + Path.GetExtension(entry.source);
                File.WriteAllBytes(staged, ReadSource(sources, entry.source));
                AssetDatabase.ImportAsset(staged, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var importer = AssetImporter.GetAtPath(staged) as AudioImporter;
                if (!importer) throw new InvalidOperationException("No AudioImporter for " + staged);
                importer.forceToMono = false;
                importer.loadInBackground = false;
                var setting = importer.defaultSampleSettings;
                setting.loadType = AudioClipLoadType.DecompressOnLoad;
                setting.compressionFormat = AudioCompressionFormat.PCM;
                setting.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                setting.preloadAudioData = true;
                importer.defaultSampleSettings = setting;
                importer.SaveAndReimport();

                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(staged);
                if (!clip || clip.samples <= 0 || clip.channels <= 0 || clip.frequency <= 0)
                    throw new InvalidDataException("Invalid source audio: " + staged);
                if (!clip.LoadAudioData()) throw new InvalidOperationException("Cannot load source samples: " + staged);
                var interleaved = new float[clip.samples * clip.channels];
                if (!clip.GetData(interleaved, 0)) throw new InvalidOperationException("Cannot decode source samples: " + staged);
                CheckFinite(interleaved, staged);
                float[] mono = ToMono(interleaved, clip.channels);
                float[] trimmed = Trim(mono, clip.frequency);
                float maxSeconds = entry.role == "Knight" && entry.cue == "Death1" ? 1.75f : 1.3f;
                float[] prepared = Resample(trimmed, clip.frequency, entry.pitch, maxSeconds);
                HighPassAndNormalise(prepared);
                CheckFinite(prepared, entry.role + "/" + entry.cue);
                float peak = prepared.Max(value => Mathf.Abs(value));
                float rms = Mathf.Sqrt((float)prepared.Sum(value => (double)value * value) / prepared.Length);
                if (peak < .01f || peak > .681f || rms < .001f)
                    throw new InvalidDataException("Unexpected processed level: " + entry.role + "/" + entry.cue);

                string folder = Destination + "/" + entry.role;
                Directory.CreateDirectory(folder);
                string path = folder + "/" + entry.cue + ".wav";
                WriteWave(path, prepared);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                results.Add(new Built
                {
                    entry = entry, samples = prepared, rate = clip.frequency, channels = clip.channels,
                    inputSeconds = (float)clip.samples / clip.frequency,
                    trimmedSeconds = (float)trimmed.Length / clip.frequency,
                    peak = peak, rms = rms, rawHash = Hash(staged), hash = Hash(path)
                });
                clip.UnloadAudioData();
            }
            WriteReports(results, sources);
            AssetDatabase.SaveAssets();
            Debug.Log("Adventure creature voices: PASS. Prepared 24 verified-source one-shots, four comparison reels and source/licence reports. Subjective listening approval remains pending.");
        }
        finally
        {
            // AssetDatabase owns generated .meta files too. Never recursively
            // delete a broad Assets folder or any downloaded original pack.
            if (ownsStage && (AssetDatabase.IsValidFolder(Stage) || Directory.Exists(Stage)))
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                if (!AssetDatabase.DeleteAsset(Stage))
                    Debug.LogWarning("Could not remove the owned temporary voice source stage: " + Stage);
            }
        }
    }

    static Entry[] Entries()
    {
        const string frogPage = "https://opengameart.org/content/mutant-frog";
        const string frogArchive = "https://opengameart.org/sites/default/files/mutant_frog.zip";
        const string creaturePage = "https://opengameart.org/content/80-cc0-creature-sfx";
        const string creatureArchive = "https://opengameart.org/sites/default/files/80-CC0-creature-SFX_0.zip";
        const string knightPage = "https://opengameart.org/content/voice-clip-pack-male-adventurer-rpg";
        const string knightArchive = "https://opengameart.org/sites/default/files/RPG%20Male%20Adventurer.zip";
        var list = new List<Entry>();
        Action<string,string,string,float> creature = (role, cue, name, pitch) =>
            list.Add(new Entry(role, cue, "creature/" + name + ".ogg", "rubberduck", creaturePage, creatureArchive, pitch));
        list.Add(new Entry("Fishman", "Alert1", "frog/mutant_frog-1.ogg", "AntumDeluge", frogPage, frogArchive, 1.05f));
        list.Add(new Entry("Fishman", "Attack1", "frog/mutant_frog-2.ogg", "AntumDeluge", frogPage, frogArchive, 1.05f));
        creature("Fishman", "Attack2", "burble_01", 1.05f);
        creature("Fishman", "Hurt1", "hurt_03", 1.05f);
        creature("Fishman", "Hurt2", "hurt_04", 1.05f);
        creature("Fishman", "Death1", "burble_02", 1.05f);
        creature("Mushroom", "Alert1", "cute_03", 1.08f);
        creature("Mushroom", "Attack1", "cute_01", 1.08f);
        creature("Mushroom", "Attack2", "cute_07", 1.08f);
        creature("Mushroom", "Hurt1", "cute_08", 1.08f);
        creature("Mushroom", "Hurt2", "cute_10", 1.08f);
        creature("Mushroom", "Death1", "cute_09", 1.08f);
        foreach (var pair in new[]
        {
            new[] { "Alert1", "attackbig3" }, new[] { "Attack1", "attack0" }, new[] { "Attack2", "attack4" },
            new[] { "Hurt1", "hurt3" }, new[] { "Hurt2", "hurt6" }, new[] { "Death1", "death1" }
        })
            list.Add(new Entry("Knight", pair[0], "adventurer/RPG Male Adventurer/" + pair[1] + ".wav", "Brandon Song (wolfwoot)", knightPage, knightArchive, .90f));
        creature("DemonKing", "Alert1", "grunt_01", .82f);
        creature("DemonKing", "Attack1", "grunt_02", .82f);
        creature("DemonKing", "Attack2", "grunt_03", .82f);
        creature("DemonKing", "Hurt1", "hurt_01", .82f);
        creature("DemonKing", "Hurt2", "grunt_04", .82f);
        creature("DemonKing", "Death1", "monster_07", .82f);
        return list.ToArray();
    }

    static bool HasSource(string root, string relative)
    {
        if (File.Exists(Path.Combine(root, relative))) return true;
        if (!File.Exists(SourceArchive)) return false;
        using (var archive = ZipFile.OpenRead(SourceArchive)) return archive.GetEntry(relative.Replace('\\', '/')) != null;
    }

    static byte[] ReadSource(string root, string relative)
    {
        string disk = Path.Combine(root, relative);
        if (File.Exists(disk)) return File.ReadAllBytes(disk);
        if (File.Exists(SourceArchive))
        {
            using (var archive = ZipFile.OpenRead(SourceArchive))
            {
                var entry = archive.GetEntry(relative.Replace('\\', '/'));
                if (entry != null)
                    using (var input = entry.Open())
                    using (var memory = new MemoryStream()) { input.CopyTo(memory); return memory.ToArray(); }
            }
        }
        throw new FileNotFoundException("Selected voice original is missing: " + relative);
    }

    static void PreserveSelectedSources(Entry[] entries, string root)
    {
        // Rebuilds can use the compact source archive after the disposable
        // research folder has gone. It stays outside Assets / player builds.
        bool completeExternal = entries.All(entry => File.Exists(Path.Combine(root, entry.source)));
        if (!completeExternal)
        {
            if (!File.Exists(SourceArchive)) throw new FileNotFoundException("No reproducible selected-source archive is available.");
            return;
        }
        string temporary = SourceArchive + ".building";
        if (File.Exists(temporary)) throw new IOException("Refusing to overwrite a pre-existing temporary source archive: " + temporary);
        try
        {
            using (var output = File.Create(temporary))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, false, Encoding.UTF8))
            {
                foreach (string relative in entries.Select(entry => entry.source).Distinct(StringComparer.Ordinal))
                {
                    var item = archive.CreateEntry(relative.Replace('\\', '/'), CompressionLevel.Optimal);
                    item.LastWriteTime = new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);
                    using (var input = File.OpenRead(Path.Combine(root, relative)))
                    using (var writer = item.Open()) input.CopyTo(writer);
                }
                foreach (string relative in new[] { "frog/LICENSE.txt", "RESEARCH.zh-CN.md" })
                    if (File.Exists(Path.Combine(root, relative)))
                    {
                        var item = archive.CreateEntry(relative, CompressionLevel.Optimal);
                        item.LastWriteTime = new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);
                        using (var input = File.OpenRead(Path.Combine(root, relative)))
                        using (var writer = item.Open()) input.CopyTo(writer);
                    }
                var mapping = archive.CreateEntry("SOURCE_AUTHORS_LICENSES.csv", CompressionLevel.Optimal);
                mapping.LastWriteTime = new DateTimeOffset(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);
                using (var text = new StreamWriter(mapping.Open(), new UTF8Encoding(false)))
                {
                    text.WriteLine("role,event,source_file,author,license,source_page,source_archive,original_sha256");
                    foreach (var entry in entries)
                        text.WriteLine(string.Join(",", Csv(entry.role), Csv(entry.cue), Csv(entry.source), Csv(entry.author),
                            "CC0-1.0", Csv(entry.page), Csv(entry.archive), Hash(Path.Combine(root, entry.source))));
                }
            }
            File.Copy(temporary, SourceArchive, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    static float[] ToMono(float[] input, int channels)
    {
        var output = new float[input.Length / channels];
        for (int frame = 0; frame < output.Length; frame++)
        {
            double sum = 0;
            for (int channel = 0; channel < channels; channel++) sum += input[frame * channels + channel];
            output[frame] = (float)(sum / channels);
        }
        return output;
    }

    static float[] Trim(float[] samples, int rate)
    {
        float peak = samples.Max(value => Mathf.Abs(value));
        if (peak < .00001f) throw new InvalidDataException("Decoded voice source is silent.");
        float threshold = peak * .01f;
        int first = 0, last = samples.Length - 1;
        while (first < last && Mathf.Abs(samples[first]) < threshold) first++;
        while (last > first && Mathf.Abs(samples[last]) < threshold) last--;
        first = Mathf.Max(0, first - Mathf.RoundToInt(rate * .018f));
        last = Mathf.Min(samples.Length - 1, last + Mathf.RoundToInt(rate * .060f));
        var trimmed = new float[last - first + 1];
        Array.Copy(samples, first, trimmed, 0, trimmed.Length);
        return trimmed;
    }

    static float[] Resample(float[] input, int rate, float pitch, float maximumSeconds)
    {
        // These modest speed/pitch changes are declared derivatives, not newly
        // recorded alternate takes. Do not manufacture spoken dialogue.
        double step = rate * (double)pitch / OutputRate;
        int count = Math.Max(2, Math.Min((int)Math.Ceiling(input.Length / step), (int)(maximumSeconds * OutputRate)));
        var output = new float[count];
        for (int i = 0; i < count; i++)
        {
            double position = i * step;
            int a = Math.Min(input.Length - 1, (int)position), b = Math.Min(input.Length - 1, a + 1);
            output[i] = Mathf.Lerp(input[a], input[b], (float)(position - a));
        }
        return output;
    }

    static void HighPassAndNormalise(float[] samples)
    {
        float rc = 1f / (2f * Mathf.PI * 45f), dt = 1f / OutputRate, alpha = rc / (rc + dt);
        float previousInput = 0, previousOutput = 0, peak = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float current = alpha * (previousOutput + samples[i] - previousInput);
            previousInput = samples[i]; previousOutput = current;
            samples[i] = current; peak = Mathf.Max(peak, Mathf.Abs(current));
        }
        if (peak < .00001f) throw new InvalidDataException("High-passed voice source is silent.");
        float gain = TargetPeak / peak;
        int fadeIn = Mathf.Max(1, Mathf.RoundToInt(OutputRate * .008f));
        int fadeOut = Mathf.Max(1, Mathf.RoundToInt(OutputRate * .035f));
        for (int i = 0; i < samples.Length; i++)
        {
            float envelope = Mathf.Min(1f, Mathf.Min((float)i / fadeIn, (float)(samples.Length - 1 - i) / fadeOut));
            samples[i] *= gain * Mathf.Max(0, envelope);
        }
    }

    static void WriteWave(string path, float[] samples)
    {
        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples.Length * 2);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
            writer.Write(OutputRate); writer.Write(OutputRate * 2); writer.Write((short)2); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples.Length * 2);
            foreach (float sample in samples) writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue));
        }
    }

    static void WriteReports(List<Built> results, string sources)
    {
        var sourceCsv = new StringBuilder("role,event,asset,source_file,author,license,source_page,source_archive,original_sha256,processed_sha256,input_seconds,trimmed_seconds,output_seconds,source_rate,source_channels,pitch_factor,peak,rms\n");
        foreach (var result in results)
        {
            var e = result.entry;
            sourceCsv.AppendLine(string.Join(",", new[]
            {
                Csv(e.role), Csv(e.cue), Csv(Destination + "/" + e.role + "/" + e.cue + ".wav"), Csv(e.source),
                Csv(e.author), "CC0-1.0", Csv(e.page), Csv(e.archive), result.rawHash, result.hash,
                Number(result.inputSeconds), Number(result.trimmedSeconds), Number((float)result.samples.Length / OutputRate),
                result.rate.ToString(Invariant), result.channels.ToString(Invariant), Number(e.pitch), Number(result.peak), Number(result.rms)
            }));
        }
        File.WriteAllText(Reports + "/Voice_Source_Mapping.csv", sourceCsv.ToString(), new UTF8Encoding(false));

        var timeline = new StringBuilder("preview_file,role,event,start_seconds,end_seconds,source_file\n");
        int roleIndex = 1;
        foreach (string role in new[] { "Fishman", "Mushroom", "Knight", "DemonKing" })
        {
            string title = role == "Fishman" ? "Fishman_Aquatic_Vocals" : role == "Mushroom" ? "Smiling_Mushroom_Cute_Vocals" :
                role == "Knight" ? "Shield_Spear_Knight_Effort_Vocals" : "DemonKing_Heavy_Creature_Vocals";
            string file = (roleIndex++).ToString("D2", Invariant) + "_" + title + "_Preview.wav";
            var sequence = new List<float>();
            foreach (var result in results.Where(item => item.entry.role == role))
            {
                float start = (float)sequence.Count / OutputRate;
                sequence.AddRange(result.samples);
                float end = (float)sequence.Count / OutputRate;
                timeline.AppendLine(string.Join(",", Csv(file), Csv(role), Csv(result.entry.cue), Number(start), Number(end), Csv(result.entry.source)));
                sequence.AddRange(new float[OutputRate / 4]);
            }
            WriteWave(Reports + "/" + file, sequence.ToArray());
        }
        File.WriteAllText(Reports + "/Voice_Preview_Timestamps.csv", timeline.ToString(), new UTF8Encoding(false));

        string credits = "# 敌人配音来源与许可\n\n" +
            "所有接入录音使用 CC0 1.0（https://creativecommons.org/publicdomain/zero/1.0/）。不使用付费、抓取影视台词或游戏原声。\n\n" +
            "- AntumDeluge，Mutant Frog：https://opengameart.org/content/mutant-frog 。鱼人的两条蛙声为两次原始录音；同一条录音的 WAV / OGG 不算两个变体。\n" +
            "- rubberduck，80 CC0 creature SFX：https://opengameart.org/content/80-cc0-creature-sfx 。用于鱼人补充声、笑脸蘑菇和 DemonKing；原作者分为 cute / burble / grunt / hurt / monster 等类别。\n" +
            "- Brandon Song（wolfwoot），Voice Clip Pack - Male Adventurer RPG：https://opengameart.org/content/voice-clip-pack-male-adventurer-rpg 。骑士用攻击、受伤、死亡分类文件；CC0 不强制署名，按作者建议保留 Brandon Song 致谢。\n\n" +
            "精确的每文件原路径、公开下载 URL、原始 SHA-256、处理后 SHA-256 和处理参数见 Voice_Source_Mapping.csv。所有输出均经单声道混合、首尾静音裁切、线性变调重采样、45 Hz 高通、峰值归一和短淡入淡出；没有添加混响或对白。角色事件分配为项目设计，不能当成原作者对剧情的标注。\n\n" +
            "比较过但未接入：Kenney Voiceover Pack (Fighter) 的实际文件是英文格斗播报，不是骑士出招呼气；Ogrebane Monster Sound Effects 2 保留为备选，没有导入正式资源。\n";
        File.WriteAllText(Reports + "/CC0_Credits.zh-CN.md", credits, new UTF8Encoding(false));
        if (HasSource(sources, "RESEARCH.zh-CN.md"))
            File.WriteAllBytes(Reports + "/RESEARCH.zh-CN.md", ReadSource(sources, "RESEARCH.zh-CN.md"));
        if (HasSource(sources, "frog/LICENSE.txt"))
            File.WriteAllBytes(Reports + "/CC0-1.0_LegalCode.txt", ReadSource(sources, "frog/LICENSE.txt"));

        long runtimeBytes = results.Sum(item => new FileInfo(Destination + "/" + item.entry.role + "/" + item.entry.cue + ".wav").Length);
        var note = new StringBuilder("# 新敌人配音检查\n\n");
        note.AppendLine("每种角色包含 Alert1、Attack1、Attack2、Hurt1、Hurt2、Death1。24 条实际 PCM 资源合计 " + runtimeBytes.ToString(Invariant) + " 字节；只保留使用中的处理后音频，未导入整套来源原包。\n");
        note.AppendLine("四个 *_Preview.wav 分别依次串联上述六个事件，间隔 0.25 秒；准确位置见 Voice_Preview_Timestamps.csv，可直接逐类对比。\n");
        note.AppendLine("技术检查通过：全部来源存在、Unity 实际解码成功、样本为有限数、输出 22,050 Hz / 单声道 / 16-bit PCM；峰值不超过 0.681；最长 1.3 秒，骑士 Death1 最长 1.75 秒。已加 8 ms 淡入、35 ms 淡出，无额外对白、混响或固定循环。\n");
        note.AppendLine("**听感验收状态：待人工确认。** 目前完成来源、分类、解码、信号及游戏触发技术核查；没有可用的听音输入工具，不把波形与元数据检查称为主观试听。蘑菇可爱、鱼人湿润咕噜、骑士面罩内呼气、DemonKing 低沉用力是设计方向。\n");
        note.AppendLine("变调倍数：Fishman 1.05、Mushroom 1.08、Knight 0.90、DemonKing 0.82。变调只是导出处理，不算新增录音；声音数量为选中的原始素材事件映射。\n");
        note.AppendLine("原始来源、授权、哈希和强度数据见 Voice_Source_Mapping.csv；引用与衍生处理说明见 CC0_Credits.zh-CN.md。");
        note.AppendLine("\n可复现来源：Selected_CC0_Sources.zip 仅含实际选用原文件（去重）、作者/许可映射和许可文本，不含整套下载原包，且位于 Assets 外。临时研究目录删除后，Builder 会直接从此 ZIP 读取原样本重新处理；也可用 ADVENTURE_CREATURE_VOICE_SOURCES 指向原研究资料目录。执行仍受 /private/tmp/adventure-trio- 测试副本路径保护。");
        File.WriteAllText(Reports + "/README.zh-CN.md", note.ToString(), new UTF8Encoding(false));
    }

    static void CheckFinite(float[] samples, string label)
    {
        foreach (float sample in samples)
            if (float.IsNaN(sample) || float.IsInfinity(sample))
                throw new InvalidDataException("Non-finite audio sample in " + label);
    }

    static string Hash(string path)
    {
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
    static string Number(float value) { return value.ToString("0.000000", Invariant); }
    static string Csv(string value) { return "\"" + value.Replace("\"", "\"\"") + "\""; }
}
