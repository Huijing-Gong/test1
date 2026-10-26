using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Read-only verification of the authored result. The installer alone creates
// assets; this check writes only its evidence report in the disposable project.
public static class AdventureTrioStyleChecks
{
    const string Source = "Assets/RPGMonsterWave03PBR";
    const string ShaderName = "FoldedRealm/AdventureCreatureToon";
    const string DemonPrefab = "Assets/Art/Enemies/DemonKingTrial/DemonKingScoutTrial.prefab";
    const string Marker = "_AdventureTrio_v1";
    const string Output = "LocalDocs/Enemies/SilverSentinelRemodel-20261021";
    const string Report = Output + "/static-validation.txt";
    const string NativeKnightPrefab = "Assets/RPGMonsterWave03PBR/Prefab/Characters/BishopKnightPBR.prefab";
    const string OriginalSpearMeshPath = "Assets/RPGMonsterWave03PBR/Mesh/Characters/BishopKnight_Mesh.fbx";
    const string OriginalSpearMaterialPath = "Assets/Art/Enemies/AdventureTrio/Styled/Knight/KnightSpear.mat";
    static readonly string[] MaterialNames = {
        "Fishman/FishmanBody", "Fishman/FishmanHarpoon", "Mushroom/MushroomSoft",
        "Knight/KnightArmor", "Knight/KnightShield", "Knight/KnightSpear" };
    // Independent expected values, not read back from the installer: a broken
    // builder must not silently redefine what this palette regression accepts.
    static readonly Dictionary<string, Color> SentinelPalette = new Dictionary<string, Color> {
        { "Silver", SentinelColor(0xD4D8DD) }, { "Indigo", SentinelColor(0x172B4B) },
        { "Gold", SentinelColor(0xDBAE58) }, { "Dark", SentinelColor(0x101923) },
        { "Eye", SentinelColor(0x7AE5F2) } };
    static int assertions;

    [MenuItem("Tools/Adventure Trio/Validate Approved Styles Read Only")]
    public static void RunAutomation()
    {
        Guard();
        Directory.CreateDirectory(Output);
        File.WriteAllText(Report, "Silver Sentinel geometry remodel: actual Unity edit-mode validation.\n" +
            "Read-only scene/asset inspection; SHA-256 source preservation; no scene/asset save or navigation bake.\n");
        assertions = 0;
        string sceneHash = Hash(AdventureTrioInstaller.ScenePath);
        var before = SnapshotGenerated();
        try
        {
            var sourceHashes = ValidateSourceManifest();
            AdventureTrioStyleInstaller.Validate();
            Need(true, "Installer.Validate performs the production supported-footprint, clearance, navigation and encounter-spacing checks read-only");
            var scene = SceneManager.GetSceneByPath(AdventureTrioInstaller.ScenePath);
            Need(scene.IsValid() && scene.isLoaded && !scene.isDirty, "Validated authored world is loaded without unsaved edits");
            var enemies = InScene<Health>(scene).Where(h => h.Team == HealthTeam.Enemy).ToArray();
            Need(enemies.Length == 20, "Exactly 20 authored enemy encounters remain");
            var group = scene.GetRootGameObjects().Single(g => g.name == Marker);
            Need(group.transform.childCount == 3 && group.GetComponentsInChildren<AdventureCreatureAI>(true).Length == 3,
                "One existing trio marker still owns exactly the three new species");
            Need(InScene<BishopKnightAI>(scene).Length == 1, "Exactly one Knight territorial miniboss exists");
            var knight = InScene<BishopKnightAI>(scene).Single();
            var capsule = knight.GetComponent<CapsuleCollider>();
            Need(knight.IsTerritoryBoss && knight.GetComponent<Health>().MaxHealth == 240 && knight.transform.localScale == Vector3.one &&
                capsule && Mathf.Abs(capsule.height - 3.15f) < .001f && Mathf.Abs(capsule.radius - .8f) < .001f &&
                Vector3.Distance(capsule.center, new Vector3(0, 1.575f, 0)) < .001f,
                "Silver Sentinel is a 240HP territorial miniboss with 3.15m/.8m capsule on a unit physical root");
            Need(knight.noticeRange == 9f && knight.leashRange == 11f && knight.walkSpeed == 1.6f,
                "Knight retains the authored 9m notice / 11m leash / 1.6m/s movement");
            Need(AdventureTrioStyleInstaller.IsRemodeledKnight(knight.gameObject),
                "Authored Silver Sentinel actually uses independent remodeled geometry, not a palette-only vendor mesh");
            SilverSentinelRemodelInstaller.ValidateActor(knight.gameObject);
            Need(true, "Positive remodel validation confirms independent geometry, preserved native rig bindings, valid weights and visible weapon contact bounds");
            ValidateOriginalSpear(knight.gameObject);
            ValidateRemodelMaterials();

            var shader = Shader.Find(ShaderName);
            Need(shader && shader.isSupported, "Creature toon shader is supported by the actual Unity graphics device: " + SystemInfo.graphicsDeviceType);
            var errors = ShaderUtil.GetShaderMessages(shader).Where(m => m.severity.ToString() == "Error").ToArray();
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                Log("SHADER " + message.severity + " " + message.file + ":" + message.line + " " + message.message);
            Need(errors.Length == 0, "ShaderUtil reports no creature-toon shader compilation errors");
            foreach (string name in MaterialNames) ValidateMaterial(name, shader);
            var materials = AssetDatabase.FindAssets("t:Material", new[] { AdventureTrioStyleInstaller.Folder })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToArray();
            Need(materials.SequenceEqual(MaterialNames.Select(n => AdventureTrioStyleInstaller.Folder + "/" + n + ".mat").OrderBy(p => p)),
                "Styled folders contain exactly the six independent approved material assets");

            ValidateFishMeshes(group.GetComponentInChildren<FishmanAI>(true));
            foreach (string name in new[] { "Fishman", "SmilingMushroom", "BishopKnight" })
            {
                string path = AdventureTrioInstaller.Folder + "/" + name + ".prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Need(prefab, "Existing generated prefab loads: " + path);
                AdventureTrioStyleInstaller.ValidateStyledActor(prefab, name == "SmilingMushroom" ? "Mushroom" : name);
                if (name == "BishopKnight") ValidateOriginalSpear(prefab);
                Need(true, "Saved prefab matches scene style, " + (name == "BishopKnight" ? "approved remodeled meshes / native rig" : "original native mesh / rig") + " and exact renderer bindings: " + name);
            }
            AdventureTrioStyleInstaller.AuditFishTechCoverage();
            Need(true, "Read-only bind-pose tech coverage audit wrote its diagnostic CSVs");
            foreach (var entry in sourceHashes) NeedHash(entry.Key, entry.Value);
            Need(Hash(AdventureTrioInstaller.ScenePath) == sceneHash && !scene.isDirty,
                "Read-only validation leaves authored scene bytes unchanged and scene clean");
            foreach (var entry in before) NeedHash(entry.Key, entry.Value);
            Need(true, "Generated styles, prefabs, shader and runtime asset bytes/metas unchanged by validation");
            Log("FINAL PASS; " + assertions + " assertions; source hashes, protected Fishman/Mushroom meshes, remodeled native-rig Knight, exact encounters and production placement validated.");
        }
        catch (Exception error)
        {
            Log("FINAL FAIL " + error);
            throw;
        }
    }

    static void ValidateOriginalSpear(GameObject actor)
    {
        var ai = actor.GetComponent<BishopKnightAI>();
        Need(ai && ai.animator, "Original spear check uses the actual Knight and native animator: " + actor.name);
        var spear = ai.animator.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Bishop_Spear");
        var native = AssetDatabase.LoadAssetAtPath<GameObject>(NativeKnightPrefab);
        Need(native, "Exact original vendor Knight prefab is available for positive spear identity verification");
        var original = native.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Bishop_Spear");
        var actualMesh = spear.GetComponent<MeshFilter>().sharedMesh;
        var nativeMesh = original.GetComponent<MeshFilter>().sharedMesh;
        Need(actualMesh && actualMesh == nativeMesh && AssetDatabase.GetAssetPath(actualMesh) == OriginalSpearMeshPath,
            "Spear is the exact original BishopKnight_Mesh.fbx submesh object, not a remodel or a different standalone prop");
        Need(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(actualMesh, out string guid, out long fileId) &&
            guid == "ea7f9c6be05f14148a01e9342f7a0db5" && fileId == 8973125684353788261L && actualMesh.vertexCount == 377,
            "Original spear retains its exact source GUID, local file ID and 377 native vertices");
        var material = AssetDatabase.LoadAssetAtPath<Material>(OriginalSpearMaterialPath);
        Need(material && spear.sharedMaterials.Length == 1 && spear.sharedMaterial == material &&
            AssetDatabase.GetAssetPath(spear.sharedMaterial) == OriginalSpearMaterialPath && material.shader && material.shader.name == ShaderName,
            "Spear uses its exact pre-remodel single KnightSpear material and AdventureCreatureToon shader");
        Need(ai.weapon == spear.transform && material.HasProperty("_BaseColor") && SameSentinelColor(material.GetColor("_BaseColor"), Color.white),
            "Original spear still owns native contacts and its feedback-compatible white surface tint");
    }

    static void ValidateRemodelMaterials()
    {
        var shader = Shader.Find("FoldedRealm/SilverSentinelToon");
        Need(shader && shader.isSupported, "Independent Silver Sentinel toon shader is supported by the actual Unity graphics device");
        foreach (var message in ShaderUtil.GetShaderMessages(shader))
            Log("SENTINEL SHADER " + message.severity + " " + message.file + ":" + message.line + " " + message.message);
        Need(!ShaderUtil.GetShaderMessages(shader).Any(m => m.severity.ToString() == "Error"),
            "Silver Sentinel shader has no actual Unity compilation errors");
        var materials = AssetDatabase.FindAssets("t:Material", new[] { AdventureTrioStyleInstaller.SentinelFolder })
            .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Material>).ToArray();
        Need(materials.Length == 5 && materials.All(m => m && m.shader == shader),
            "Remodel owns exactly five independent Silver / Indigo / Gold / Dark / Eye materials on its own shader");
        Need(materials.Select(m => m.name).OrderBy(n => n).SequenceEqual(SentinelPalette.Keys.OrderBy(n => n)),
            "Remodel has exactly one material for each approved palette role, with no duplicate role replacing another");
        foreach (var material in materials)
        {
            Need(material.HasProperty("_ArmorColor") && SameSentinelColor(material.GetColor("_ArmorColor"), SentinelPalette[material.name]),
                "Per-material _ArmorColor retains the exact approved independent " + material.name + " palette");
            Need(material.HasProperty("_BaseColor") && SameSentinelColor(material.GetColor("_BaseColor"), Color.white),
                "Shared CombatFeedback tint is neutral white, not an armor role color: " + material.name);
            Need(material.GetTexturePropertyNames().Select(material.GetTexture).Where(t => t).All(t =>
                t == Texture2D.whiteTexture || t == Texture2D.blackTexture ||
                AssetDatabase.GetAssetPath(t).StartsWith(AdventureTrioStyleInstaller.SentinelFolder + "/", StringComparison.Ordinal)),
                "Remodeled material uses only its own textures or exact immutable Unity white/black builtin objects: " + material.name);
            foreach (string property in new[] { "_StyleClock", "_TechPulse", "_StyleLive" })
                Need(material.HasProperty(property), "Remodel material preserves instance-clock / death-glow property " + property + ": " + material.name);
        }
        Color gold = materials.Single(m => m.name == "Gold").GetColor("_ArmorColor");
        Color indigo = materials.Single(m => m.name == "Indigo").GetColor("_ArmorColor");
        Color dark = materials.Single(m => m.name == "Dark").GetColor("_ArmorColor");
        Need(gold.r > gold.g && gold.g > gold.b && indigo.b > indigo.g && indigo.g > indigo.r &&
            Mathf.Max(dark.r, dark.g, dark.b) < .22f && (gold - indigo).maxColorComponent > .4f,
            "Gold remains warm, Indigo remains blue and joints remain dark rather than all collapsing to Silver");
    }

    static Color SentinelColor(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
    static bool SameSentinelColor(Color actual, Color expected) =>
        Mathf.Abs(actual.r - expected.r) < .002f && Mathf.Abs(actual.g - expected.g) < .002f &&
        Mathf.Abs(actual.b - expected.b) < .002f && Mathf.Abs(actual.a - expected.a) < .002f;

    static Dictionary<string, string> ValidateSourceManifest()
    {
        string path = AdventureTrioStyleInstaller.Output + "/source-sha256.txt";
        Need(File.Exists(path), "Installer source-SHA256 manifest exists");
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            Need(line.Length > 66 && line.Substring(64, 2) == "  " && line.Take(64).All(Uri.IsHexDigit), "SHA256 manifest record is well formed");
            string asset = line.Substring(66).Replace('\\', '/');
            Need(IsAssetPath(asset) && !hashes.ContainsKey(asset),
                "SHA256 manifest record names one unique project asset: " + asset);
            hashes.Add(asset, line.Substring(0, 64).ToLowerInvariant());
            NeedHash(asset, hashes[asset]);
        }
        string[] vendor = Directory.GetFiles(Source, "*", SearchOption.AllDirectories).Select(p => p.Replace('\\', '/')).ToArray();
        Need(vendor.Length > 0 && vendor.All(hashes.ContainsKey), "Source manifest covers every shared vendor model, PBR material, texture, animation and importer meta");
        Need(hashes.ContainsKey(Source + "/Material/PBR_Default.mat") && hashes.ContainsKey(Source + "/Texture/DefaultPBR/Albedo.png"),
            "Shared vendor PBR material and palette atlas are explicitly SHA256 protected");
        Need(AssetDatabase.LoadAssetAtPath<GameObject>(DemonPrefab), "Existing purple DemonKing source prefab loads");
        string[] demon = AssetDatabase.GetDependencies(DemonPrefab, true).Where(p => IsAssetPath(p) && AssetFileExists(p))
            .SelectMany(p => AssetFileExists(p + ".meta") ? new[] { p, p + ".meta" } : new[] { p }).Distinct().ToArray();
        string[] missingDemon = demon.Where(p => !hashes.ContainsKey(p)).OrderBy(p => p).ToArray();
        File.WriteAllLines(Output + "/source-coverage-missing.txt", missingDemon);
        foreach (string missing in missingDemon) Log("MISSING SOURCE SHA256 COVERAGE " + missing);
        Need(demon.Length > 0 && missingDemon.Length == 0, "Manifest covers existing DemonKing prefab and every actual material, shader, texture, animation and importer dependency" +
            (missingDemon.Length == 0 ? "" : "; missing:\n" + string.Join("\n", missingDemon)));
        Need(demon.Any(p => p.EndsWith("/Armour.mat", StringComparison.Ordinal)) && demon.Any(p => p.EndsWith("/DemonKingPalette.shader", StringComparison.Ordinal)),
            "Purple DemonKing armour and its palette shader are explicitly protected");
        Log("SOURCE SHA256 VERIFIED " + hashes.Count + " files; shared vendor=" + vendor.Length + "; DemonKing dependencies=" + demon.Length);
        return hashes;
    }

    static void ValidateMaterial(string relative, Shader shader)
    {
        string stem = AdventureTrioStyleInstaller.Folder + "/" + relative;
        var material = AssetDatabase.LoadAssetAtPath<Material>(stem + ".mat");
        if(relative=="Knight/KnightShield")
        {
            KnightShieldFinishInstaller.Validate(material);
            Need(material.shader.name==KnightShieldFinishInstaller.ShaderName,"Original shield uses its dedicated perimeter/panel shader");
        }
        else Need(material && material.shader == shader && material.shader.name == ShaderName, "Independent approved toon material: " + relative);
        foreach (string property in new[] { "_BaseMap", "_BaseColor", "_ShadowColor", "_DeepShadowColor", "_Metallic", "_Smoothness",
            "_SpecularStrength", "_FormStrength", "_EmissionMap", "_EmissionColor", "_EmissionStrength", "_AmbientStrength", "_RimStrength",
            "_TechMask", "_TechStrength", "_TechWeapon", "_StyleClock", "_TechPulse", "_StyleLive" })
            Need(material.HasProperty(property), relative + " retains required shader property " + property);
        ValidateTexture(material, "_BaseMap", stem + "_Albedo.png", true);
        ValidateTexture(material, "_EmissionMap", stem + "_Emission.png", false);
        ValidateTexture(material, "_TechMask", stem + "_TechMask.png", false);
        bool fish = relative == "Fishman/FishmanBody", weapon = relative == "Fishman/FishmanHarpoon";
        Need(Mathf.Abs(material.GetFloat("_TechStrength") - (fish ? .65f : weapon ? .8f : 0f)) < .001f &&
            Mathf.Abs(material.GetFloat("_TechWeapon") - (weapon ? 1f : 0f)) < .001f,
            relative + " has the exact regional tech strength and weapon mode");
        var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        try
        {
            Need(decoded.LoadImage(File.ReadAllBytes(stem + "_TechMask.png")), relative + " independent RGB tech PNG decodes");
            var pixels = decoded.GetPixels32();
            Need(fish ? pixels.Any(p => p.r > 0) && pixels.Any(p => p.g > 0) && pixels.All(p => p.b == 0) :
                weapon ? pixels.Any(p => p.b > 0) && pixels.All(p => p.r == 0 && p.g == 0) :
                pixels.All(p => p.r == 0 && p.g == 0 && p.b == 0),
                relative + " tech RGB channels restrict skin(R), fins(G), weapon(B) and exclude non-fish materials");
        }
        finally { Object.DestroyImmediate(decoded); }
    }

    static void ValidateTexture(Material material, string property, string path, bool srgb)
    {
        var texture = material.GetTexture(property) as Texture2D;
        Need(texture && AssetDatabase.GetAssetPath(texture) == path && texture.width == 512 && texture.height == 512,
            material.name + " " + property + " points at its own 512-square generated texture");
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        Need(importer && importer.sRGBTexture == srgb && importer.textureType == TextureImporterType.Default &&
            importer.textureCompression == TextureImporterCompression.Uncompressed && !importer.mipmapEnabled &&
            importer.filterMode == FilterMode.Point && importer.wrapMode == TextureWrapMode.Clamp,
            material.name + " " + property + " retains correct " + (srgb ? "sRGB colour" : "linear data mask") + " importer");
    }

    static void ValidateFishMeshes(FishmanAI fish)
    {
        Need(fish && fish.animator, "Authored fish and original native animator are assigned");
        var native = AssetDatabase.LoadAssetAtPath<GameObject>(Source + "/Prefab/Characters/FishmanPBR.prefab");
        Need(native, "Original vendor Fishman prefab loads");
        var sources = native.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in fish.GetComponent<AdventureCreatureStyle>().targetRenderers)
        {
            var sourceRenderer = sources.Single(r => r.name == renderer.name && r.GetType() == renderer.GetType());
            var original = MeshOf(sourceRenderer); var actual = MeshOf(renderer);
            Need(actual && original && actual != original && AssetDatabase.GetAssetPath(actual).StartsWith(AdventureTrioStyleInstaller.Folder + "/Fishman/", StringComparison.Ordinal),
                "Fishman mesh is an independent local copy: " + renderer.name);
            Need(actual.vertexCount == original.vertexCount && actual.vertices.SequenceEqual(original.vertices) &&
                actual.normals.SequenceEqual(original.normals) && actual.tangents.SequenceEqual(original.tangents) &&
                actual.uv.SequenceEqual(original.uv) && actual.boneWeights.SequenceEqual(original.boneWeights) && actual.bindposes.SequenceEqual(original.bindposes),
                renderer.name + " preserves native geometry, UV0, normals, tangents, bone weights and bind poses exactly");
            Need(actual.subMeshCount == original.subMeshCount && Enumerable.Range(0, actual.subMeshCount).All(i =>
                actual.GetTopology(i) == original.GetTopology(i) && actual.GetIndices(i).SequenceEqual(original.GetIndices(i))),
                renderer.name + " preserves every original submesh topology/index buffer");
            var uv3 = new List<Vector4>(); actual.GetUVs(2, uv3);
            Need(uv3.Count == actual.vertexCount && uv3.All(v => Finite01(v.x) && Finite01(v.y) && Finite01(v.z)),
                renderer.name + " has one finite normalized bind-pose UV3 coordinate per vertex");
            var vertices = original.vertices; var bounds = original.bounds; var size = bounds.size;
            int major = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            int second = (major + 1) % 3, third = (major + 2) % 3;
            bool weapon = renderer.name.Equals("Harpoon", StringComparison.OrdinalIgnoreCase);
            Need(Enumerable.Range(0, vertices.Length).All(i => {
                var p = vertices[i]; var n = new Vector3(Normalize(p.x, bounds.min.x, bounds.max.x),
                    Normalize(p.y, bounds.min.y, bounds.max.y), Normalize(p.z, bounds.min.z, bounds.max.z));
                var expected = weapon ? new Vector3(n[major], n[second], n[third]) : n;
                return Vector3.Distance(new Vector3(uv3[i].x, uv3[i].y, uv3[i].z), expected) < .00001f;
            }), renderer.name + " UV3 equals source bind-pose XYZ, with harpoon long axis mapped to X");
        }
    }

    static Dictionary<string, string> SnapshotGenerated()
    {
        var paths = new HashSet<string>();
        foreach (string folder in new[] { AdventureTrioStyleInstaller.Folder, AdventureTrioInstaller.Folder, AdventureTrioStyleInstaller.SentinelFolder })
            if (Directory.Exists(folder)) foreach (string path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)) paths.Add(path);
        foreach (string path in new[] { "Assets/Art/Enemies/AdventureTrio/AdventureCreatureStyle.cs",
            "Assets/Art/Enemies/AdventureTrio/AdventureCreatureToon.shader" })
        { if (File.Exists(path)) paths.Add(path); if (File.Exists(path + ".meta")) paths.Add(path + ".meta"); }
        return paths.ToDictionary(p => p, Hash);
    }
    static Mesh MeshOf(Renderer renderer) => renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
    static float Normalize(float value, float min, float max) => max - min < .000001f ? .5f : Mathf.Clamp01((value - min) / (max - min));
    static bool Finite01(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0 && value <= 1;
    static T[] InScene<T>(Scene scene) where T : Component => Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(c => c.gameObject.scene == scene).ToArray();
    static void NeedHash(string path, string expected)
    { if (!AssetFileExists(path) || Hash(path) != expected) throw new InvalidOperationException("Protected asset bytes changed or disappeared: " + path); }
    static string Hash(string path)
    { using (var stream = File.OpenRead(ReadableAssetPath(path))) using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
    static bool IsAssetPath(string path) => path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal);
    static bool AssetFileExists(string path) => File.Exists(ReadableAssetPath(path));
    static string ReadableAssetPath(string path)
    {
        string logical = path.Replace('\\', '/');
        string[] parts = logical.Split('/');
        if (!IsAssetPath(logical) || Path.IsPathRooted(logical) || parts.Any(p => p.Length == 0 || p == "." || p == ".." || p.Contains(":")))
            throw new InvalidOperationException("Only project Assets or registered Packages files may be hashed: " + path);
        string root, relative;
        if (logical.StartsWith("Assets/", StringComparison.Ordinal))
        {
            root = Path.GetFullPath(Application.dataPath);
            relative = logical.Substring("Assets/".Length);
        }
        else
        {
            string lookup = logical.EndsWith(".meta", StringComparison.Ordinal) ? logical.Substring(0, logical.Length - 5) : logical;
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(lookup);
            if (package == null || string.IsNullOrEmpty(package.resolvedPath) || parts.Length < 3 || package.name != parts[1])
                throw new InvalidOperationException("Package dependency has no matching registered resolved root: " + path);
            root = Path.GetFullPath(package.resolvedPath);
            relative = logical.Substring(("Packages/" + package.name + "/").Length);
        }
        string resolved = Path.GetFullPath(Path.Combine(root, relative));
        if (!resolved.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Asset dependency escapes its registered root: " + path);
        return resolved;
    }
    static void Guard()
    {
        if (!Application.dataPath.StartsWith("/private/tmp/adventure-trio-", StringComparison.Ordinal)) throw new InvalidOperationException("Disposable adventure-trio scratch project only.");
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play before static style checks.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Unsaved scene edits present; validation discarded nothing.");
    }
    static void Need(bool success, string message)
    { if (!success) throw new InvalidOperationException(message); assertions++; Log("PASS " + message); }
    static void Log(string message)
    { File.AppendAllText(Report, message + "\n"); Debug.Log("ADVENTURE_TRIO_STYLE_QA " + message); }
}
