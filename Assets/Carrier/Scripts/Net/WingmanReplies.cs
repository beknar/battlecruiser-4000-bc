using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The AI wingman's replies that are always the same, or differ only by a place
    /// (Resources/Data/wingman_replies.json): CoopBot says them through Get, and the wingman's
    /// stock voice is recorded from the same file (Tools/Voice/render_stock.py), so what it says
    /// and what was recorded can't drift apart.
    /// </summary>
    public static class WingmanReplies
    {
        public const string Resource = "Data/wingman_replies";

        [Serializable] class Entry { public string key, line; }
        [Serializable] class FileData { public string note; public Entry[] replies; }

        static Dictionary<string, string> lines;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => lines = null;

        /// <summary>Every reply by its key (from the file, read once).</summary>
        public static IReadOnlyDictionary<string, string> All
        {
            get
            {
                if (lines != null) return lines;
                lines = new Dictionary<string, string>();
                var ta = Resources.Load<TextAsset>(Resource);
                var data = ta != null ? JsonUtility.FromJson<FileData>(ta.text) : null;
                if (data?.replies == null) { Debug.LogWarning($"[Wingman] no replies ({Resource})"); return lines; }
                foreach (var e in data.replies)
                    if (e != null && !string.IsNullOrEmpty(e.key)) lines[e.key] = e.line ?? "";
                return lines;
            }
        }

        /// <summary>The reply `key`, its blanks ({place}, {field}, {name}) filled.</summary>
        public static string Get(string key, params (string blank, string value)[] blanks)
        {
            if (!All.TryGetValue(key, out var line)) { Debug.LogWarning($"[Wingman] no reply '{key}'"); return key; }
            if (blanks.Length == 0) return line;
            var facts = new Dictionary<string, string>();
            foreach (var (blank, value) in blanks) facts[blank.ToLowerInvariant()] = value ?? "";
            return WingmanPersonality.Fill(line, facts);
        }
    }
}
