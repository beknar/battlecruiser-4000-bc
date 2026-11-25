using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CarrierGame.Demo
{
    /// <summary>
    /// What the player said, as a message on the comms channel (VoiceInput sends it with
    /// CoopChat.Say, as if it had been typed): the filler words left out ("um", "uh"), the
    /// wingman's name put first when no one was named ("mine at the Inner Belt": "Wingman, mine at
    /// the Inner Belt"), and, in an order to hand over credits or ore, the amount as figures ("give
    /// me two thousand credits": "give me 2000 credits"; anywhere else a number word is left alone,
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
        static readonly string[] Everyone = CoopWire.AllCalls;

        /// <summary>The message to send for `heard` (null: nothing to send). `bots` are the AI
        /// wingmen in the game, by name; `lastSpokenTo` the one the player last spoke to (the one a
        /// command naming no one goes to; else the first); `people`, the other players' names (a
        /// message to one of them goes as it was said). `to`: the wingman it's for (null: all of
        /// them, or a person).</summary>
        public static string ToChat(string heard, IList<string> bots, string lastSpokenTo, out string to, IList<string> people = null) =>
            ToChat(heard, bots, lastSpokenTo, out to, out _, people);

        /// <summary>As ToChat, saying what was heard wrong and put right (`corrected`, null if
        /// nothing): a wingman's name misheard ("women mine": "Wingman, mine"), or an order's
        /// words ("Wingman, cellar": "Wingman, sell ore"). A message to a person, or talk, is left
        /// as it was said.</summary>
        public static string ToChat(string heard, IList<string> bots, string lastSpokenTo, out string to, out string corrected, IList<string> people = null)
        {
            to = null;
            corrected = null;
            string said = Clean(heard);
            if (said.Length == 0 || bots == null || bots.Count == 0) return null;
            // (already to someone by name, or to them all: as it was said; the longest name that
            // matches, so "Wingman 2" isn't taken for "Wingman")
            var names = bots.Concat(people ?? new string[0]).Concat(Everyone).Where(n => !string.IsNullOrEmpty(n)).OrderByDescending(n => n.Length);
            foreach (var name in names)
            {
                string rest = CoopBot.AddressedPart(said, name);
                if (rest == null) continue;
                bool toBots = bots.Contains(name) || Everyone.Contains(name.ToLowerInvariant());
                to = bots.Contains(name) ? name : null;
                // (a person's message goes as it was said; an order to a wingman, its words put right)
                string fixedRest = toBots ? FixOrder(rest, out corrected) : rest;
                string msg = fixedRest == rest ? said : said.Substring(0, said.Length - rest.Length) + fixedRest;
                return Capital(MoneyFigures(msg, fixedRest));
            }
            // (a wingman's name misheard, first: "women mine", "hey man mine")
            string bot = NameHeardAs(said, bots, out string after, out string asHeard);
            if (bot != null)
            {
                to = bot;
                string fixedAfter = FixOrder(after, out string orderFix);
                corrected = $"\"{asHeard}\" as {bot}" + (orderFix != null ? $", {orderFix}" : "");
                return $"{bot}, {MoneyFigures(fixedAfter, fixedAfter)}";
            }
            to = bots[0];
            foreach (var name in bots) if (name == lastSpokenTo) to = name;
            string order = FixOrder(said, out corrected);
            return $"{to}, {MoneyFigures(order, order)}";
        }

        /// <summary>As ToChat, without the wingman it went to.</summary>
        public static string ToChat(string heard, IList<string> bots, string lastSpokenTo = null) => ToChat(heard, bots, lastSpokenTo, out _);

        /// <summary>Of several guesses at what was said (the browser's best few, best first), the
        /// message for the first that reads as an order to a wingman; with none, the first one's,
        /// as ToChat makes it.</summary>
        public static string Choose(IList<string> guesses, IList<string> bots, string lastSpokenTo, out string to, out string corrected, IList<string> people = null)
        {
            to = null;
            corrected = null;
            if (guesses == null || guesses.Count == 0) return null;
            string first = null, firstTo = null, firstFix = null;
            foreach (var g in guesses)
            {
                string msg = ToChat(g, bots, lastSpokenTo, out string t, out string fix, people);
                if (msg == null) continue;
                if (first == null) { first = msg; firstTo = t; firstFix = fix; }
                if (IsOrder(msg, bots))
                {
                    to = t;
                    // (a guess other than the first: said, as it was heard otherwise)
                    corrected = fix ?? (g == guesses[0] ? null : $"\"{Clean(guesses[0])}\" as \"{Clean(g)}\"");
                    return msg;
                }
            }
            to = firstTo;
            corrected = firstFix;
            return first;
        }

        /// <summary>Is a message (as ToChat makes it) an order the wingman it's to understands?</summary>
        public static bool IsOrder(string message, IList<string> bots)
        {
            if (string.IsNullOrEmpty(message) || bots == null) return false;
            foreach (var name in bots.Concat(Everyone))
            {
                string rest = CoopBot.AddressedPart(message, name);
                if (rest != null) return IsOrderWords(rest);
            }
            return false;
        }

        static bool IsOrderWords(string words) =>
            !string.IsNullOrWhiteSpace(words) && (CoopBot.Clauses(CoopBot.Plain(words)) != null || CoopBot.ParseOrder(words) != null);

        // ------------------------------------------------------------ mishearings

        // What a recogniser has heard for the words of an order (from the co-op sessions), and the
        // order meant. The longest first; whole words at the start of the order only. `more`: only
        // with words after it ("Wingman, morning" alone is talk; "morning at the Inner Belt" isn't).
        static readonly (string heard, string meant, bool more)[] Mishearings =
        {
            // ("sell the or" is an order already: "sell", the rest left)
            ("cell or", "sell ore", false),
            ("cell ore", "sell ore", false), ("cellar", "sell ore", false), ("seller", "sell ore", false),
            ("at tack", "attack", false), ("a tack", "attack", false), ("attach", "attack", false), ("pack", "attack", false), ("tack", "attack", false),
            ("morning", "mine", true), ("mourning", "mine", true), ("mind", "mine", true), ("main", "mine", true),
            ("doc", "dock", false), ("duck", "dock", false),
            ("state us", "status", false), ("statues", "status", false), ("stats", "status", false),
            ("war to", "warp to", false), ("wharf to", "warp to", false), ("warp two", "warp to", false),
            ("fallow me", "follow me", false), ("hollow me", "follow me", false),
        };

        /// <summary>An order's words with a known mishearing at the start put right, if what was
        /// heard isn't an order as it is and is one put right; else as they were. `corrected`
        /// says what was changed (null: nothing).</summary>
        public static string FixOrder(string words, out string corrected)
        {
            corrected = null;
            string w = (words ?? "").Trim();
            if (w.Length == 0 || IsOrderWords(w)) return words;
            string lw = w.ToLowerInvariant();
            foreach (var (heard, meant, more) in Mishearings.OrderByDescending(m => m.heard.Length))
            {
                if (!(lw == heard || lw.StartsWith(heard + " ") || lw.StartsWith(heard + ","))) continue;
                string rest = w.Substring(heard.Length);
                if (more && rest.Trim(' ', ',').Length == 0) continue;
                string put = meant + rest;
                if (!IsOrderWords(put)) continue;
                corrected = $"\"{w.Substring(0, heard.Length)}\" as \"{meant}\"";
                return put;
            }
            return words;
        }

        static readonly HashSet<string> Greetings = new HashSet<string>(CoopWire.Greetings);

        /// <summary>The wingman a message starts with a mishearing of (null: none), by sound: its
        /// first word or two after any greeting ("women", "amen", "only man", "wing man"), or after
        /// a greeting the end of its name ("hey man"). `after`: the rest of the message; `asHeard`:
        /// the words taken for the name. An order's own word ("mine") is never taken for a name, and
        /// a name with nothing after it isn't either.</summary>
        public static string NameHeardAs(string said, IList<string> bots, out string after, out string asHeard)
        {
            after = null;
            asHeard = null;
            var words = (said ?? "").Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries).ToList();
            int i = 0;
            bool greeted = words.Count > 0 && Greetings.Contains(words[0].Trim(',', '!').ToLowerInvariant());
            if (greeted) i = 1;
            // (one word first: "women mine at the Inner Belt" is "women", not "women mine")
            foreach (int n in new[] { 1, 2 })
            {
                if (i + n >= words.Count) continue;          // (the name and something after it)
                string heard = string.Join(" ", words.Skip(i).Take(n)).Trim(',', '!', ':');
                string joined = heard.Replace(" ", "");
                if (IsOrderWords(heard) || OrderWords.Contains(heard.ToLowerInvariant())) continue;
                foreach (var bot in bots)
                {
                    if (string.IsNullOrWhiteSpace(bot)) continue;
                    string name = bot.Replace(" ", "").ToLowerInvariant();
                    bool tail = greeted && n == 1 && joined.Length >= 3 && name.Length > joined.Length && name.EndsWith(joined.ToLowerInvariant());
                    if (!tail && !SoundsLike(joined, name)) continue;
                    after = string.Join(" ", words.Skip(i + n)).TrimStart(',', ' ');
                    asHeard = string.Join(" ", words.Take(i + n)).Trim(',', '!', ':');
                    return bot;
                }
            }
            return null;
        }

        // (the orders' own words: never a wingman's name, however they sound)
        static readonly HashSet<string> OrderWords = new HashSet<string>
        {
            "mine", "mining", "sell", "dock", "attack", "follow", "hold", "stop", "status", "cargo", "help", "warp", "regroup", "report",
            "give", "go", "come", "salvage", "scavenge", "defend", "cover", "engage", "kill", "wait", "stay", "please", "now",
            "where", "protect", "shoot", "take",
        };

        /// <summary>Does a heard word sound like a name? Their sound keys (SoundKey) end alike and
        /// differ by a few sounds at most, and the word has most of the name's sounds.</summary>
        public static bool SoundsLike(string heard, string name)
        {
            string a = SoundKey(heard), b = SoundKey(name);
            if (a.Length == 0 || b.Length == 0) return false;
            if (a == b) return true;
            if (a.Length < b.Length - 2 || a.Length < 2) return false;
            int tail = System.Math.Min(2, System.Math.Min(a.Length, b.Length));
            if (a.Substring(a.Length - tail) != b.Substring(b.Length - tail)) return false;
            return Edits(a, b) <= (b.Length * 3 + 4) / 5;
        }

        /// <summary>A word's sounds, roughly (a small Metaphone): the consonants as they sound, the
        /// vowels left out but for a first one (as "A"), doubles as one. "Wingman" is WNKMN,
        /// "women" WMN, "amen" AMN.</summary>
        public static string SoundKey(string word)
        {
            var w = new string((word ?? "").ToLowerInvariant().Where(char.IsLetter).ToArray());
            if (w.Length == 0) return "";
            if (w.StartsWith("kn") || w.StartsWith("gn") || w.StartsWith("wr") || w.StartsWith("ps")) w = w.Substring(1);
            w = w.Replace("ph", "f").Replace("ck", "k").Replace("qu", "kw").Replace("tch", "ch").Replace("sch", "sk").Replace("dg", "j").Replace("wh", "w");
            var sb = new StringBuilder();
            for (int i = 0; i < w.Length; i++)
            {
                char c = w[i], next = i + 1 < w.Length ? w[i + 1] : '\0';
                char s;
                switch (c)
                {
                    case 'a': case 'e': case 'i': case 'o': case 'u': case 'y': s = i == 0 ? 'A' : '\0'; break;
                    case 'c': s = next == 'e' || next == 'i' || next == 'y' ? 'S' : 'K'; break;
                    case 'g': if (next == 'h') { i++; s = '\0'; } else s = 'K'; break;
                    case 'q': s = 'K'; break;
                    case 'x': sb.Append('K'); s = 'S'; break;
                    case 'z': s = 'S'; break;
                    case 'v': s = 'F'; break;
                    case 'b': s = 'P'; break;
                    case 'd': s = 'T'; break;
                    case 'h': s = i == 0 ? 'H' : '\0'; break;
                    case 'w': s = i == 0 ? 'W' : '\0'; break;
                    default: s = char.ToUpperInvariant(c); break;
                }
                if (s != '\0' && (sb.Length == 0 || sb[sb.Length - 1] != s)) sb.Append(s);
            }
            return sb.ToString();
        }

        /// <summary>How many letters must change, go in or come out to make one word the other
        /// (the edit distance).</summary>
        public static int Edits(string a, string b)
        {
            a ??= "";
            b ??= "";
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = System.Math.Min(System.Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
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
            return string.Join(" ", words).TrimEnd('.', '!', '?', ',', ' ');
        }

        /// <summary>In an order to hand over credits or ore (`order`, the words after any name,
        /// starts with give, transfer, pay ..., or take but for "take out", an attack), the spoken
        /// amount as figures; anything else as it is.</summary>
        static string MoneyFigures(string said, string order)
        {
            var words = (order ?? "").TrimStart().Split(' ');
            string first = words.FirstOrDefault()?.ToLowerInvariant() ?? "";
            bool take = first == "take" && !(words.Length > 1 && words[1].ToLowerInvariant().Trim(',') == "out");
            return MoneyVerbs.Contains(first) || take ? Numbers(said) : said;
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
                                            "cargo", "status", "report", "regroup", "form up", "help", "what's in your hold",
                                            "attack my target", "my target", "defend me", "cover me", "help me", "I'm under attack", "where are you",
                                            // (the rocks to go for: the minerals worth going after, and the most valuable)
                                            "mine platinum", "mine gold", "mine silver", "mine iron", "mine palladium", "mine iridium",
                                            "mine diamonds", "mine helium three", "mine deuterium", "mine uranium", "mine water",
                                            "mine the most valuable ore", "mine the best rocks",
                                            // (ore handed over, each way: CoopCargo)
                                            "give me your ore", "give me all your ore", "give me the platinum", "give me the gold", "give me the iron",
                                            "take my ore", "take all my ore", "take the iron" };

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
