using System.Collections.Generic;
using UnityEngine;
using System.IO;
using PurrNet.Logging;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
#endif

namespace PurrNet
{
    [CreateAssetMenu(fileName = "NetworkPrefabs", menuName = "PurrNet/Network Prefabs", order = -201)]
    public class NetworkPrefabs : ScriptableObject
#if UNITY_EDITOR
        , IPreprocessBuildWithReport
#endif
    {
        public bool autoGenerate = true;
        public Object folder;
        public List<GameObject> prefabs = new();

#if UNITY_EDITOR
        public int callbackOrder { get; }
        public void OnPreprocessBuild(BuildReport report)
        {
            if (autoGenerate)
                Generate();
        }
        
        private void OnValidate()
        {
            // Keep this method for future validations
        }
#endif

        /// <summary>
        /// Editor only method to generate network prefabs from a specified folder.
        /// </summary>
        public void Generate()
        {
        #if UNITY_EDITOR
            EditorUtility.DisplayProgressBar("Getting Network Prefabs", "Checking existing...",  0f);
            if (folder == null)
            {
                PurrLogger.LogError("Network prefabs project folder is not set!", this);
                return;
            }

            string folderPath = AssetDatabase.GetAssetPath(folder);
            if (string.IsNullOrEmpty(folderPath))
            {
                PurrLogger.LogError($"Invalid folder path: {folderPath}", this);
                return;
            }

            // Track paths of existing prefabs for quick lookup
            HashSet<string> existingPaths = new HashSet<string>();
            foreach (var prefab in prefabs)
            {
                if (prefab != null)
                {
                    existingPaths.Add(AssetDatabase.GetAssetPath(prefab));
                }
            }
            
            EditorUtility.DisplayProgressBar("Getting Network Prefabs", "Finding paths...",  0.1f);

            List<GameObject> foundPrefabs = new();
            string[] guids = AssetDatabase.FindAssets("t:GameObject", new[] { folderPath });
            for (var i = 0; i < guids.Length; i++)
            {
                var guid = guids[i];
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                GameObject gameObject = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (gameObject != null)
                {
                    EditorUtility.DisplayProgressBar("Getting Network Prefabs", $"Looking at {gameObject.name}",  0.1f + 0.7f * ((i + 1f) / guids.Length));
                    NetworkBehaviour networkBehaviour = gameObject.GetComponent<NetworkBehaviour>();
                    if (!networkBehaviour)
                        networkBehaviour = gameObject.GetComponentInChildren<NetworkBehaviour>();

                    if (!networkBehaviour)
                        continue;

                    foundPrefabs.Add(gameObject);
                }
            }

            EditorUtility.DisplayProgressBar("Getting Network Prefabs", "Sorting...",  0.9f);
            // Order by creation time
            foundPrefabs.Sort((a, b) =>
            {
                string pathA = AssetDatabase.GetAssetPath(a);
                string pathB = AssetDatabase.GetAssetPath(b);

                FileInfo fileInfoA = new FileInfo(pathA);
                FileInfo fileInfoB = new FileInfo(pathB);

                return fileInfoA.CreationTime.CompareTo(fileInfoB.CreationTime);
            });
            
            EditorUtility.DisplayProgressBar("Getting Network Prefabs", "Removing invalid prefabs...",  0.95f);
            // Remove invalid or no longer existing prefabs
            prefabs.RemoveAll(prefab => prefab == null || !File.Exists(AssetDatabase.GetAssetPath(prefab)));

            // Add new prefabs found in the folder to the list if they don't already exist
            foreach (var foundPrefab in foundPrefabs)
            {
                string foundPath = AssetDatabase.GetAssetPath(foundPrefab);
                if (!existingPaths.Contains(foundPath))
                {
                    prefabs.Add(foundPrefab);
                }
            }

            EditorUtility.SetDirty(this);
            EditorUtility.ClearProgressBar();

            //PurrLogger.Log($"{prefabs.Count} prefabs found in {folderPath}", new LogStyle( Color.green));
        #endif
        }
    }
}