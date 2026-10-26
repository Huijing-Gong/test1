using System;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace KingOfGraphic.MechBossMigration.Editor
{
    // Editor-only source compression; builds contain ordinary Unity native assets.
    // Source data was serialized with Unity 6000.3.22f1. Keep this importer with it.
    [ScriptedImporter(3, new[] { "mechdata", "mechfx" })]
    public sealed class MechNativeImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            string directory = Path.Combine("Library", "MechBossNativeImport",
                AssetDatabase.AssetPathToGUID(context.assetPath));
            string temporary = Path.Combine(directory, "native.asset");
            Directory.CreateDirectory(directory);
            try
            {
                using (var input = File.OpenRead(context.assetPath))
                {
                    int stride = 1;
                    var header = new byte[4];
                    if (input.Read(header, 0, 4) == 4 && header[0] == 'M' &&
                        header[1] == 'B' && header[2] == 'N' && header[3] == '1')
                    {
                        stride = input.ReadByte();
                        if (stride < 1 || stride > 16)
                            throw new InvalidDataException("Invalid byte shuffle stride.");
                    }
                    else input.Position = 0;
                    using (var compressed = new GZipStream(input, CompressionMode.Decompress))
                    using (var output = new MemoryStream())
                    {
                        compressed.CopyTo(output);
                        byte[] encoded = output.ToArray();
                        byte[] native = new byte[encoded.Length];
                        int cursor = 0;
                        for (int lane = 0; lane < stride; lane++)
                            for (int position = lane; position < native.Length; position += stride)
                                native[position] = encoded[cursor++];
                        File.WriteAllBytes(temporary, native);
                    }
                }

                var objects = UnityEditorInternal.InternalEditorUtility
                    .LoadSerializedFileAndForget(temporary);
                UnityEngine.Object main = null;
                if (objects != null && objects.Length == 1 &&
                    (objects[0] is Mesh || objects[0] is AnimationClip || objects[0] is Avatar))
                    main = objects[0];
                else if (Path.GetExtension(context.assetPath) == ".mechfx" && objects != null)
                    foreach (var item in objects)
                        if (item is GameObject candidate && candidate.transform.parent == null)
                        {
                            if (main != null) throw new InvalidDataException("Multiple FX roots.");
                            main = candidate;
                        }
                if (main == null) throw new InvalidDataException("Unsupported native asset.");

                context.AddObjectToAsset("main", main);
                context.SetMainObject(main);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                if (Directory.Exists(directory)) Directory.Delete(directory);
            }
        }
    }
}
