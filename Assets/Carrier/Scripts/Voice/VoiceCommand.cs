using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CarrierGame.Demo
{
    /// <summary>
    /// What the player said, as a message to the AI wingman on the comms channel (VoiceInput sends
    /// it with CoopChat.Say, as if it had been typed): the filler words left out ("um", "uh"),
    /// spoken numbers as figures ("two thousand credits": "2000 credits"), and the wingman's name
    /// put first when it wasn't said ("mine at the Inner Belt": "Wingman, mine at the Inner Belt").
    /// The bot reads the rest as it reads typed orders (CoopBot), so nothing else is needed there.
    /// </summary>
    public static class VoiceCommand
    {
        static readonly HashSet<string> Filler = new HashSet<string> { "um", "umm", "uh", "uhh", "er", "erm", "ah", "hmm", "mm" };

        static readonly Dictionary<string, int> Units = new Dictionary<string, int>
        {
            ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7,
            ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14,
            ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19,
            ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90,
        };

        /// <summary>The message to send for `heard` (null: nothing to send). `bots` are the AI
        /// wingmen in the game, by name; `lastSpokenTo` the one the player last spoke to (the one a
        /// command without a name goes to, when there are several; else the first).</summary>
        public static string ToChat(string heard, IList<string> bots, string lastSpokenTo = null)
        {
            string said = Numbers(Clean(heard));
            if (said.Length == 0 || bots == null || bots.Count == 0) return null;
            // (already to a wingman by name, or to them all: as it was said)
            foreach (var name in bots)
                if (!string.IsNullOrEmpty(name) && CoopBot.AddressedPart(said, name) != null) return Capital(said);
            string to = bots[0];
            foreach (var name in bots) if (name == lastSpokenTo) to = name;
            return $"{to}, {said}";
        }

        /// <summary>The words without the filler, the punctuation a recogniser adds at the end, or
        /// doubled spaces.</summary>
        public static string Clean(string heard)
        {
            if (string.IsNullOrWhiteSpace(heard)) return "";
            var words = new List<string>();
            foreach (var w0 in heard.Trim().Split(' '))
            {
                string w = w0.Trim();
                if (w.Length == 0) continue;
                if (Filler.Contains(w.Trim(',', '.', '!', '?').ToLowerInvariant())) continue;
                words.Add(w);
            }
            return string.Join(" ", words).TrimEnd('.', '!', ',', ' ');
        }

        /// <summary>Spoken numbers as figures: "two thousand five hundred" is 2500, "a hundred"
        /// 100, "forty two" 42. Words that aren't numbers are left as they are.</summary>
        public static string Numbers(string said)
        {
            if (string.IsNullOrEmpty(said)) return "";
            var words = said.Split(' ');
            var sb = new StringBuilder();
            int i = 0;
            while (i < words.Length)
            {
                int j = i;
                long total = 0, part = 0;
                bool any = false;
                while (j < words.Length)
                {
                    string w = words[j].ToLowerInvariant().Trim(',', '.');
                    string tail = words[j].Length > 0 && (words[j].EndsWith(",") || words[j].EndsWith(".")) ? words[j].Substring(words[j].Length - 1) : "";
                    if (Units.TryGetValue(w, out int u)) { part += u; any = true; }
                    else if (w == "hundred" && (any || (j > i && IsA(words[j - 1])))) { part = (part == 0 ? 1 : part) * 100; any = true; }
                    else if (w == "thousand" && (any || (j > i && IsA(words[j - 1])))) { total += (part == 0 ? 1 : part) * 1000; part = 0; any = true; }
                    else if (w == "and" && any && j + 1 < words.Length && Units.ContainsKey(words[j + 1].ToLowerInvariant().Trim(',', '.'))) { }
                    else if (IsA(w) && !any && j + 1 < words.Length && IsScale(words[j + 1])) { }
                    else break;
                    j++;
                    if (tail.Length > 0) break;   // (a comma ends a number: "two, three")
                }
                if (any)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(total + part);
                    string last = words[j - 1];
                    if (last.EndsWith(",") || last.EndsWith(".")) sb.Append(last[last.Length - 1]);
                    i = j;
                }
                else
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(words[i]);
                    i++;
                }
            }
            return sb.ToString();
        }

        static readonly string[] Orders = { "follow me", "hold", "stop", "attack", "dock", "mine", "start mining", "sell", "sell the ore",
                                            "cargo", "status", "report", "regroup", "form up", "help", "what's in your hold" };

        /// <summary>Phrases for an offline keyword recogniser (Windows without dictation): each
        /// order, alone and after each wingman's name (and "all ships"), and "mine at", "warp to"
        /// each place.</summary>
        public static string[] Phrases(IEnumerable<string> bots, IEnumerable<string> places)
        {
            var list = new HashSet<string>();
            var calls = new List<string> { "" , "all ships " };
            foreach (var b in bots) if (!string.IsNullOrWhiteSpace(b)) calls.Add(b.Trim() + " ");
            var orders = new List<string>(Orders);
            foreach (var p in places)
                if (!string.IsNullOrWhiteSpace(p)) { orders.Add("warp to " + p.Trim()); orders.Add("mine at " + p.Trim()); }
            foreach (var c in calls)
                foreach (var o in orders) list.Add(c + o);
            return list.ToArray();
        }

        static bool IsA(string w) => w.ToLowerInvariant().Trim(',', '.') == "a";
        static bool IsScale(string w) { w = w.ToLowerInvariant().Trim(',', '.'); return w == "hundred" || w == "thousand"; }

        static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
