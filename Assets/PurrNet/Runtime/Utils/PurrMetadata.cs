#if UNITY_EDITOR
using UnityEditor;
#else
using UnityEngine.Scripting;
#endif
using UnityEngine;

namespace PurrNet.Utils
{
    public static class PurrMetadata
    {
        public const string PACKAGE_JSON_GUID = "0ec978dbed50a6f4b9a57580867f1fae";

        public static string version { get; private set; } = "v?";

#if UNITY_EDITOR

        static PurrMetadata()
        {
            version = ReadFromPackageJson() ?? "v?";
        }

        static string ReadFromPackageJson()
        {
            var path = AssetDatabase.GUIDToAssetPath(PACKAGE_JSON_GUID);

            if (string.IsNullOrEmpty(path))
                return null;

            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);

            if (asset == null)
                return null;

            var json = JsonUtility.FromJson<PackageJson>(asset.text);
            return 'v' + (string.IsNullOrEmpty(json?.version) ? "?" : json.version);
        }

        [System.Serializable]
        private class PackageJson
        {
            public string version;
        }
#else
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration), Preserve]
        static void Init() { }

        static void SetBakedVersion(string version)
        {
            PurrMetadata.version = version;
        }
#endif
    }
}
