using System;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// How the game was started: its command line (-flag, -flag value) and, in a browser, its page
    /// address's options (?flag, ?key=value). The one place either is read, for the game, the
    /// co-op menu and the test and reel harnesses alike.
    /// </summary>
    public static class LaunchArgs
    {
        /// <summary>For tests: a command line and a page address to read instead of the real ones.</summary>
        internal static string[] TestArgs;
        internal static string TestUrl;

        public static string[] Args => TestArgs ?? Environment.GetCommandLineArgs();
        public static bool OnWeb => TestUrl != null || Application.platform == RuntimePlatform.WebGLPlayer;
        /// <summary>The page's address (empty outside a browser).</summary>
        public static string PageUrl => TestUrl ?? (Application.platform == RuntimePlatform.WebGLPlayer ? Application.absoluteURL : "");

        // ---------------------------------------------------------------- the command line

        /// <summary>Is `flag` (e.g. "-autotest") on the command line?</summary>
        public static bool Has(string flag) => Array.IndexOf(Args, flag) >= 0;

        /// <summary>What follows `flag` on the command line (whatever it is), or null.</summary>
        public static string ValueOf(string flag)
        {
            var args = Args;
            int i = Array.IndexOf(args, flag);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        /// <summary>The folder given after `flag` ("-reel D:\out"), or `fallback` when the flag
        /// has none (the next thing is another flag, or nothing).</summary>
        public static string FolderAfter(string flag, string fallback)
        {
            string v = ValueOf(flag);
            return v != null && !v.StartsWith("-") ? v : fallback;
        }

        // ---------------------------------------------------------------- the page address

        /// <summary>A page address's query: what is between the ? and any #, or null if it has
        /// none (a ? after the # is part of the fragment, not the query).</summary>
        static string QueryOf(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            int q = url.IndexOf('?'), hash = url.IndexOf('#');
            if (q < 0 || (hash >= 0 && hash < q)) return null;
            return hash < 0 ? url.Substring(q + 1) : url.Substring(q + 1, hash - q - 1);
        }

        /// <summary>The value of a URL's query option (?key=value), or null if it has none.</summary>
        public static string QueryValue(string url, string key)
        {
            string query = QueryOf(url);
            if (query == null) return null;
            foreach (var part in query.Split('&'))
            {
                var kv = part.Split('=');
                if (kv[0] == key && kv.Length > 1) return Uri.UnescapeDataString(kv[1]);
            }
            return null;
        }

        /// <summary>Is `key` one of the URL's query options (?key, ?key=..., &amp;key...)?</summary>
        public static bool HasQueryKey(string url, string key)
        {
            string query = QueryOf(url);
            if (query == null) return false;
            foreach (var part in query.Split('&'))
                if (part.Split('=')[0] == key) return true;
            return false;
        }

        /// <summary>In a browser, is ?key one of the page's options?</summary>
        public static bool PageHas(string key) => OnWeb && HasQueryKey(PageUrl, key);

        /// <summary>In a browser, the page's ?key=value (null if it has none).</summary>
        public static string PageValue(string key) => OnWeb ? QueryValue(PageUrl, key) : null;

        /// <summary>A page option: its value, "" for one without a value, or null if it isn't there.</summary>
        public static string PageOption(string key) => PageHas(key) ? PageValue(key) ?? "" : null;

        // ---------------------------------------------------------------- both

        /// <summary>A switch given either way: "-name" on the command line or "?name" on the page.</summary>
        public static bool Switch(string name) => Has("-" + name) || PageHas(name);

        /// <summary>Is a test, a reel or an AI player (-coopbot) driving the game (so no one is there to
        /// answer menus, and the player's own save is left alone)?</summary>
        public static bool Automated
        {
            get
            {
                foreach (var m in new[] { "-systemtest", "-reel", "-autotest", "-webpadtest", "-cooptest", "-coopbot" }) if (Has(m)) return true;
                return PageHas("systemtest") || PageHas("padtest");
            }
        }
    }
}
