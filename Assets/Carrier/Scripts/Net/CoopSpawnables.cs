using System;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// The prefabs an enemy can be made from during play, by key, so another player's game can
    /// make the same one (Resources/CoopSpawnables, made by the scene builder).
    /// </summary>
    [CreateAssetMenu(menuName = "Carrier/Co-op spawnables")]
    public class CoopSpawnables : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string key;
            public GameObject prefab;
        }

        public Entry[] entries = new Entry[0];

        public const string ResourcePath = "CoopSpawnables";

        static CoopSpawnables loaded;
        static bool tried;

        /// <summary>The prefab under a key (null: none, or no registry).</summary>
        public static GameObject Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (!tried) { tried = true; loaded = Resources.Load<CoopSpawnables>(ResourcePath); }
            if (loaded == null) return null;
            foreach (var e in loaded.entries) if (e.key == key) return e.prefab;
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { loaded = null; tried = false; }
    }
}
