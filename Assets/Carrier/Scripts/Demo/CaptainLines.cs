using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// What the captain says when things happen: Resources/Captains/lines.txt, a few lines for
    /// each event under an [EVENT] heading. A line is drawn at random from its event's lines,
    /// not repeating until they have all been said. {name}, {ship}, {place} and {count} are
    /// filled in (a line that needs one that isn't known is skipped).
    /// </summary>
    public static class CaptainLines
    {
        /// <summary>Every event the captain can speak about (her reasons, without numbers).</summary>
        public static readonly string[] Events =
        {
            "REPORTING FOR DUTY", "ALL QUIET", "ON THE SURFACE", "CONTACT", "HOSTILES NEARBY", "SHIELDS TAKING FIRE",
            "RETURNING FIRE", "SHIELDS DOWN", "HULL HIT", "HULL CRITICAL", "MISSILE INBOUND", "POINT DEFENCE FIRING",
            "GUNS FIRING", "MISSILES AWAY", "LAUNCHING THE WING", "TARGET LOCKED", "MAIN GUN CHARGING", "MAIN GUN FIRING",
            "TARGET DESTROYED", "WARP DRIVE CHARGING", "IN WARP", "ARRIVED", "GOING DOWN", "TOUCHDOWN", "TAKING OFF",
            "DOCKING", "DOCKED", "MINING", "ROCK MINED OUT", "HOLD FULL", "SCANNING", "SHIP LOST", "BACK IN ACTION",
        };

        static Dictionary<string, List<string>> lines;
        static readonly Dictionary<string, List<int>> bags = new Dictionary<string, List<int>>();
        static readonly System.Random rng = new System.Random();

        /// <summary>How many lines there are in all.</summary>
        public static int Count { get { Load(); int n = 0; foreach (var l in lines.Values) n += l.Count; return n; } }

        /// <summary>How many lines an event has.</summary>
        public static int CountFor(string ev) { Load(); return lines.TryGetValue(ev, out var l) ? l.Count : 0; }

        static void Load()
        {
            if (lines != null) return;
            lines = new Dictionary<string, List<string>>();
            var text = Resources.Load<TextAsset>("Captains/lines");
            if (text == null) return;
            List<string> current = null;
            foreach (var raw in text.text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    string ev = line.Substring(1, line.Length - 2).Trim();
                    if (!lines.TryGetValue(ev, out current)) lines[ev] = current = new List<string>();
                    continue;
                }
                current?.Add(line);
            }
        }

        /// <summary>The event a reason belongs to: "6 HOSTILES NEARBY" and "HOSTILE NEARBY" are
        /// both HOSTILES NEARBY, "DOCKED: REPAIRED" is DOCKED.</summary>
        public static string EventOf(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return "";
            string r = reason.Trim();
            int i = 0;
            while (i < r.Length && char.IsDigit(r[i])) i++;
            r = r.Substring(i).Trim();
            if (r == "HOSTILE NEARBY") r = "HOSTILES NEARBY";
            int colon = r.IndexOf(':');
            if (colon > 0) r = r.Substring(0, colon).Trim();
            return r;
        }

        /// <summary>A line for an event, with its blanks filled by `fill` (which returns null for
        /// one it can't fill), or null if the event has none that can be said now.</summary>
        public static string Pick(string ev, Func<string, string> fill)
        {
            Load();
            if (!lines.TryGetValue(ev, out var list) || list.Count == 0) return null;
            if (!bags.TryGetValue(ev, out var bag) || bag.Count == 0)
            {
                bag = new List<int>();
                for (int i = 0; i < list.Count; i++) bag.Add(i);
                for (int i = bag.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (bag[i], bag[j]) = (bag[j], bag[i]); }
                bags[ev] = bag;
            }
            // the next one in the bag that can be filled in
            for (int tries = 0; tries < bag.Count; tries++)
            {
                int idx = bag[0];
                bag.RemoveAt(0);
                string said = Fill(list[idx], fill);
                if (said != null) return said;
                bag.Add(idx);
            }
            return null;
        }

        static string Fill(string line, Func<string, string> fill)
        {
            int start;
            while ((start = line.IndexOf('{')) >= 0)
            {
                int end = line.IndexOf('}', start);
                if (end < 0) break;
                string value = fill(line.Substring(start + 1, end - start - 1));
                if (string.IsNullOrEmpty(value)) return null;
                line = line.Substring(0, start) + value + line.Substring(end + 1);
            }
            return line;
        }
    }
}
