using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CarrierGame.Demo
{
    /// <summary>
    /// What the player said, as a message on the comms channel (VoiceInput sends it with
    /// CoopChat.Say, as if it had been typed): the filler words left out ("um", "uh"), the
    /// wingman's name put first when no one was named ("mine at the Inner Belt": "Wingman, mine at
    /// the Inner Belt"), and, in an order to hand over credits, the amount as figures ("give me
    /// two thousand credits": "give me 2000 credits"; anywhere else a number word is left alone,
    /// as in a ship's name, "Snare Two"). The bot reads the rest as it reads typed orders.
    /// </summary>
    public static class VoiceCommand
    {
        static readonly HashSet<string> Filler = new HashSet<string> { "um", "umm", "uh", "uhh", "er", "erm", "ah", "hmm", "mm" };

        static readonly Dictionary<string, int> Ones = new Dictionary<string, int>
        {
            ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7,
            ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14,
            ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19,
        };
        static readonly Dictionary<string, int> Tens = new Dictionary<string, int>
        {
            ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90,
        };
        static readonly string[] MoneyVerbs = { "give", "transfer", "send", "pay", "hand", "wire" };
        static readonly string[] Everyone = { "all ships", "everyone", "wingmen" };

        /// <summary>The message to send for `heard` (null: nothing to send). `bots` are the AI
        /// wingmen in the game, by name; `lastSpokenTo` the one the player last spoke to (the one a
        /// command naming no one goes to; else the first); `people`, the other players' names (a
        /// message to one of them goes as it was said). `to`: the wingman it's for (null: all of
        /// them, or a person).</summary>
        public static string ToChat(string heard, IList<string> bots, string lastSpokenTo, out string to, IList<string> people = null)
        {
            to = null;
            string said = Clean(heard);
            if (said.Length == 0 || bots == null || bots.Count == 0) return null;
            // (already to someone by name, or to them all: as it was said; the longest name that
            // matches, so "Wingman 2" isn't taken for "Wingman")
            var names = bots.Concat(people ?? new string[0]).Concat(Everyone).Where(n => !string.IsNullOrEmpty(n)).OrderByDescending(n => n.Length);
            foreach (var name in names)
            {
                string rest = CoopBot.AddressedPart(said, name);
                if (rest == null) continue;
                to = bots.Contains(name) ? name : null;
                return Capital(MoneyFigures(said, rest));
            }
            to = bots[0];
            foreach (var name in bots) if (name == lastSpokenTo) to = name;
            return $"{to}, {MoneyFigures(said, said)}";
        }

        /// <summary>As ToChat, without the wingman it went to.</summary>
        public static string ToChat(string heard, IList<string> bots, string lastSpokenTo = null) => ToChat(heard, bots, lastSpokenTo, out _);

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
            return string.Join(" ", words).TrimEnd('.', '!', '?', ',', ' ');
        }

        /// <summary>In an order to hand over credits (`order`, the words after any name, starts
        /// with give, transfer, pay ...), the spoken amount as figures; anything else as it is.</summary>
        static string MoneyFigures(string said, string order)
        {
            string first = (order ?? "").TrimStart().Split(' ').FirstOrDefault()?.ToLowerInvariant() ?? "";
            return MoneyVerbs.Contains(first) ? Numbers(said) : said;
        }

        /// <summary>Spoken numbers as figures: "two thousand five hundred" is 2500, "a hundred"
        /// 100, "forty two" 42, "three thousand and fifty" 3050. A unit after a unit isn't added
        /// ("one two" stays two numbers), and "and" joins only after a hundred or a thousand.</summary>
        public static string Numbers(string said)
        {
            if (string.IsNullOrEmpty(said)) return "";
            var words = said.Split(' ');
            var sb = new StringBuilder();
            void Put(string s) { if (sb.Length > 0) sb.Append(' '); sb.Append(s); }
            int i = 0;
            while (i < words.Length)
            {
                long total = 0, part = 0;
                bool any = false, tensOpen = false, scaleJustSaid = false;
                int j = i;
                string tail = "";
                while (j < words.Length)
                {
                    string raw = words[j];
                    string w = raw.ToLowerInvariant().Trim(',', '.', '?', '!');
                    string end = raw.Length > 0 && ",.?!".IndexOf(raw[raw.Length - 1]) >= 0 ? raw.Substring(raw.Length - 1) : "";
                    bool next = j + 1 < words.Length;
                    string nw = next ? words[j + 1].ToLowerInvariant().Trim(',', '.', '?', '!') : "";
                    if (Ones.TryGetValue(w, out int u) && (!any || (tensOpen && u < 10) || scaleJustSaid))
                    { part += u; any = true; tensOpen = false; scaleJustSaid = false; }
                    else if (Tens.TryGetValue(w, out int t) && (!any || scaleJustSaid))
                    { part += t; any = true; tensOpen = true; scaleJustSaid = false; }
                    else if (w == "hundred" && (any || (j > i && IsA(words[j - 1]))) && part < 100 && !scaleJustSaid)
                    { part = (part == 0 ? 1 : part) * 100; any = true; tensOpen = false; scaleJustSaid = true; }
                    else if (w == "thousand" && (any || (j > i && IsA(words[j - 1]))) && !scaleJustSaid)
                    { total += (part == 0 ? 1 : part) * 1000; part = 0; any = true; tensOpen = false; scaleJustSaid = true; }
                    else if (w == "and" && any && scaleJustSaid && next && (Ones.ContainsKey(nw) || Tens.ContainsKey(nw))) { }
                    else if (w == "a" && !any && next && (nw == "hundred" || nw == "thousand")) { }
                    else break;
                    j++;
                    if (end.Length > 0) { tail = end; break; }   // (a comma ends a number: "two, three")
                }
                if (any) { Put((total + part) + tail); i = j; }
                else { Put(words[i]); i++; }
            }
            return sb.ToString();
        }

        static bool IsA(string w) => w.ToLowerInvariant().Trim(',', '.') == "a";

        static readonly string[] Orders = { "follow me", "hold", "stop", "attack", "dock", "mine", "start mining", "sell", "sell the ore",
                                            "cargo", "status", "report", "regroup", "form up", "help", "what's in your hold" };

        /// <summary>Phrases for an offline keyword recogniser (Windows without dictation): each
        /// order, alone and after each wingman's name (and "all ships"), and "mine at", "warp to"
        /// each place, with and without "the".</summary>
        public static string[] Phrases(IEnumerable<string> bots, IEnumerable<string> places)
        {
            var list = new HashSet<string>();
            var calls = new List<string> { "", "all ships " };
            foreach (var b in bots) if (!string.IsNullOrWhiteSpace(b)) calls.Add(b.Trim() + " ");
            var orders = new List<string>(Orders);
            foreach (var p in places)
                if (!string.IsNullOrWhiteSpace(p))
                {
                    string place = p.Trim();
                    orders.Add("warp to " + place);
                    orders.Add("mine at " + place);
                    if (!place.StartsWith("The ", System.StringComparison.OrdinalIgnoreCase))
                    {
                        orders.Add("warp to the " + place);
                        orders.Add("mine at the " + place);
                    }
                }
            foreach (var c in calls)
                foreach (var o in orders) list.Add(c + o);
            return list.ToArray();
        }

        static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
