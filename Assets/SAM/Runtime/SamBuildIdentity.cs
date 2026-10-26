using System;
using System.IO;
using UnityEngine;

namespace SamProduction
{
    public static class SamBuildIdentity
    {
        [Serializable] private sealed class Identity { public string build_id; }
        public static string Id
        {
            get
            {
                string path = Path.Combine(Application.streamingAssetsPath, "SAM_BuildIdentity.json");
                if (!File.Exists(path)) return "unrecorded_development_build";
                return JsonUtility.FromJson<Identity>(File.ReadAllText(path)).build_id;
            }
        }
    }
}
