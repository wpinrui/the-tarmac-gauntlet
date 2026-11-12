using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TarmacGauntlet.Visuals
{
    /// <summary>
    /// Loads art by its path under Assets/Art. The editor reads the asset database. A built game reads the
    /// ArtLibrary resource, which the build menu fills with every asset under Assets/Art just before it builds.
    /// </summary>
    public sealed class ArtLibrary : ScriptableObject
    {
        public const string ResourceName = "ArtLibrary";

        [Serializable]
        public struct Entry
        {
            public string Path;
            public Object Asset;
        }

        public Entry[] Entries = Array.Empty<Entry>();

#if !UNITY_EDITOR
        private static Dictionary<string, Object> byPath;
#endif

        public static T Load<T>(string path) where T : Object
        {
            if (string.IsNullOrEmpty(path)) return null;
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
#else
            if (byPath == null)
            {
                byPath = new Dictionary<string, Object>();
                ArtLibrary library = Resources.Load<ArtLibrary>(ResourceName);
                if (library == null) Debug.LogError("The ArtLibrary resource is missing, so the game has no car art.");
                else foreach (Entry e in library.Entries) byPath[e.Path] = e.Asset;
            }
            return byPath.TryGetValue(path, out Object asset) ? asset as T : null;
#endif
        }
    }
}
