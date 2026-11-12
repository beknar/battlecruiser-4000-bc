using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarrierGame
{
    /// <summary>
    /// Frees memory between scenes: whenever a scene is closed (a restart reloads the level) and
    /// before a new one is opened (a planet's surface is built on landing and thrown away on
    /// take-off), collect the garbage and unload the assets nothing uses any more. Each
    /// collection is logged with the managed heap before and after.
    /// </summary>
    public static class SceneMemory
    {
        /// <summary>Collections so far (they carry on across scene loads).</summary>
        public static int Collections { get; private set; }
        public static string LastReason { get; private set; } = "";
        /// <summary>Managed heap (MB) before and after the last collection.</summary>
        public static float LastBeforeMB { get; private set; }
        public static float LastAfterMB { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            Collections = 0;
            LastReason = "";
            SceneManager.sceneUnloaded -= OnUnloaded;
            SceneManager.sceneUnloaded += OnUnloaded;
        }

        static void OnUnloaded(Scene s) => Collect($"closed {s.name}");

        static float soonAt = -1f;
        static string soonReason;
        static int soonAfter;
        static SoonRunner runner;

        /// <summary>Collect in `seconds` (many asks in a row make one collection; none if one is
        /// done meanwhile anyway).</summary>
        public static void CollectSoon(string reason, float seconds)
        {
            if (!Application.isPlaying) return;
            if (soonAt < 0f) { soonAt = Time.unscaledTime + seconds; soonReason = reason; soonAfter = Collections; }
            if (runner == null)
            {
                var go = new GameObject("SceneMemory") { hideFlags = HideFlags.HideAndDontSave };
                UnityEngine.Object.DontDestroyOnLoad(go);
                runner = go.AddComponent<SoonRunner>();
            }
        }

        class SoonRunner : MonoBehaviour
        {
            void Update()
            {
                if (soonAt < 0f || Time.unscaledTime < soonAt) return;
                soonAt = -1f;
                if (Collections == soonAfter) Collect(soonReason);
            }
        }

        /// <summary>Collect the garbage and unload unused assets now.</summary>
        public static AsyncOperation Collect(string reason)
        {
            float before = GC.GetTotalMemory(false) / 1048576f;
            GC.Collect();
            var op = Resources.UnloadUnusedAssets();
            Collections++;
            LastReason = reason;
            LastBeforeMB = before;
            LastAfterMB = GC.GetTotalMemory(false) / 1048576f;
            Debug.Log($"[SceneMemory] {reason}: garbage collected, managed heap {before:F1} -> {LastAfterMB:F1} MB, unused assets unloading");
            return op;
        }
    }
}
