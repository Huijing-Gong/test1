using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using System.Reflection;

public static class RioUIPresentationChecks
{
    [MenuItem("ArtLab/Rio UI/Check all dialogue text layouts", true)]
    public static bool CanCheckLayouts() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("ArtLab/Rio UI/Check all dialogue text layouts")]
    public static void CheckLayouts()
    {
        var host = new GameObject("Dialogue layout test") { hideFlags = HideFlags.HideAndDontSave };
        var presenter = host.AddComponent<RioDialoguePresentation>();
        var npc = host.AddComponent<NPCDialogue>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Action<string, object> set = (name, value) => typeof(RioDialoguePresentation).GetField(name, flags).SetValue(presenter, value);
        Func<string, object[], object> call = (name, args) => typeof(RioDialoguePresentation).GetMethod(name, flags).Invoke(presenter, args);
        GameObject canvas = null;
        int checkedPages = 0;
        try
        {
            call("BuildCanvas", new object[] { Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") });
            canvas = (GameObject)typeof(RioDialoguePresentation).GetField("canvasObject", flags).GetValue(presenter);
            canvas.hideFlags = HideFlags.HideAndDontSave;
            var image = (RawImage)typeof(RioDialoguePresentation).GetField("dialogue", flags).GetValue(presenter);
            foreach (string character in new[] { "Magician", "Rio", "Intro" })
            {
                bool side = character != "Intro";
                string folder = side ? "SideWorldDialogue/" + character : "RioDialogue/Dialogue";
                var sequence = JsonUtility.FromJson<RioDialoguePresentation.Sequence>(Resources.Load<TextAsset>(folder + "/timing").text);
                var sourceObject = GameObject.Find(character == "Magician" ? "SideWorldA_Guide" : character == "Rio" ? "SideWorldB_Guide" : "IntroNPC");
                var source = sourceObject != null ? sourceObject.GetComponent<NPCDialogue>() : null;
                if (source == null) throw new InvalidOperationException("Open OverworldBaseline to check all three guide panels.");
                string[] lines = new string[source.LineCount];
                for (int i = 0; i < lines.Length; i++) lines[i] = source.GetLine(i);
                npc.ConfigureDialogue("", lines);
                npc.SetNPCName(character == "Magician" ? SideWorldEntrance.GuideAName : character == "Rio" ? SideWorldEntrance.GuideBName : source.NPCName);
                set("target", npc); set("activeTarget", npc); set("activeSideDialogue", side);
                call(side ? "ConfigureSideDialogueLayout" : "ConfigureIntroDialogueLayout", new object[0]);
                var speaker = (Text)typeof(RioDialoguePresentation).GetField("speakerText", flags).GetValue(presenter);
                speaker.text = npc.NPCName;
                for (int page = 0; page < lines.Length; page++)
                {
                    set("page", page);
                    call("Display", new object[] { image, sequence, folder, side ? 8 : page == 0 ? 12 : 20, !side });
                    Canvas.ForceUpdateCanvases();
                    if (speaker.text != npc.NPCName) throw new InvalidOperationException("Name changed on page " + page);
                    foreach (Text text in canvas.GetComponentsInChildren<Text>()) CheckTextFits(text);
                    checkedPages++;
                }
            }
            // Check the fourth (plain/default) dialogue style with the longest line.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Demo/Prefabs/UI/DialogueCanvas.prefab");
            var fallback = UnityEngine.Object.Instantiate(prefab, host.transform);
            fallback.hideFlags = HideFlags.HideAndDontSave;
            var ui = fallback.GetComponent<DialogueUI>();
            typeof(DialogueUI).GetMethod("ConfigureReadableFallback", flags).Invoke(ui, null);
            var panel = (GameObject)typeof(DialogueUI).GetField("dialoguePanel", flags).GetValue(ui);
            panel.SetActive(true);
            var body = (Text)typeof(DialogueUI).GetField("dialogueText", flags).GetValue(ui);
            body.text = "Finish this dialogue to enter, or Esc to stay. B returns here at any time. Claim a new trial reward after winning.";
            Canvas.ForceUpdateCanvases();
            foreach (Text text in panel.GetComponentsInChildren<Text>()) CheckTextFits(text);
            Debug.Log("DIALOGUE_LAYOUT_PASS: " + checkedPages + " illustrated pages + default panel; stable names, full text and control rows fit.");
        }
        finally
        {
            if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    static void CheckTextFits(Text text)
    {
        if (text.preferredHeight > text.rectTransform.rect.height + 1)
            throw new InvalidOperationException(text.name + " clips vertically: needs " + text.preferredHeight + ", has " + text.rectTransform.rect.height);
        if (text.name == "Live controls" && text.rectTransform.anchorMin.y < .27f)
            throw new InvalidOperationException("Controls overlap the illustration's bottom border.");
    }

    [MenuItem("ArtLab/Rio UI/Validate imported PNG sequences")]
    public static void Validate()
    {
        int files = 0;
        foreach (string folder in new[] { "Prompt", "Dialogue" })
        {
            var json = Resources.Load<TextAsset>("RioDialogue/" + folder + "/timing");
            if (json == null) throw new InvalidOperationException("Missing " + folder + " timeline");
            var sequence = JsonUtility.FromJson<RioDialoguePresentation.Sequence>(json.text);
            if (sequence.frames.Length != (folder == "Prompt" ? 46 : 29)) throw new InvalidOperationException("Unexpected frame count");
            foreach (var frame in sequence.frames)
            {
                if (frame.durationMs <= 0) throw new InvalidOperationException("Invalid frame timing");
                string asset = "Assets/_Demo/Resources/RioDialogue/" + folder + "/" + frame.file;
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(asset);
                string resource = "RioDialogue/" + folder + "/" + Path.ChangeExtension(frame.file, null);
                if (Resources.Load<Texture2D>(resource) == null)
                    throw new InvalidOperationException("Runtime Texture2D resource unavailable: " + resource);
                var importer = AssetImporter.GetAtPath(asset) as TextureImporter;
                if (texture == null || texture.width != sequence.width || texture.height != sequence.height)
                    throw new InvalidOperationException("Texture dimensions/import failed: " + asset);
                if (importer == null || importer.textureShape != TextureImporterShape.Texture2D || importer.mipmapEnabled || importer.npotScale != TextureImporterNPOTScale.None || !importer.alphaIsTransparency)
                    throw new InvalidOperationException("Incorrect transparency/resize settings: " + asset);
                files++;
            }
        }
        Debug.Log("RIO_UI_ASSET_CHECK_PASS: " + files + " timeline references; dimensions, alpha import and timing validated. Play Mode verification is separate.");
    }
}
