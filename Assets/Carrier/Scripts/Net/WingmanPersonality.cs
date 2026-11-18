using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The wingman's dialogue (Resources/Data/wingman_dialogue.json): the casual topics it knows,
    /// what it says on each, and the conversations that branch from them. Read with JsonUtility,
    /// so it is flat: nodes refer to each other by id.
    /// </summary>
    [Serializable]
    public class WingmanDialogue
    {
        /// <summary>A casual topic: the words that bring it up, and the node it starts at.</summary>
        [Serializable]
        public class Topic
        {
            public string id;
            /// <summary>Words or phrases, matched as whole words; "corsair*" matches any word starting "corsair".</summary>
            public string[] keywords;
            public string node;
        }

        /// <summary>What to say at a point in a conversation.</summary>
        [Serializable]
        public class Node
        {
            public string id;
            /// <summary>The lines it may say: the first set whose condition holds, a line at random from it.</summary>
            public Variant[] say;
            /// <summary>Session flags set once this is said (they change later lines).</summary>
            public string[] sets;
            /// <summary>Where the player may take it next: the question's answers (its line asks it).</summary>
            public Choice[] choices;
        }

        [Serializable]
        public class Variant
        {
            /// <summary>When these lines are said: "" always; otherwise conditions joined by ",", each
            /// "flag:name" (a session flag is set), "fact:name" (a fact is known), or either with
            /// "!" in front (not set, not known).</summary>
            public string when;
            public string[] lines;
        }

        [Serializable]
        public class Choice
        {
            /// <summary>Shown after the question: "(1) label".</summary>
            public string label;
            /// <summary>Words that pick it, besides its number and its label.</summary>
            public string[] keys;
            public string next;
        }

        public Topic[] topics;
        public Node[] nodes;
        /// <summary>The node for talk on no topic it knows.</summary>
        public string fallback = "fallback";
        /// <summary>The node for its name alone ("Wingman?").</summary>
        public string hail = "hail";
        /// <summary>Said when a question is pending and the answer matches nothing: the choices follow.</summary>
        public string[] reprompt;
        /// <summary>Said while casual chat is paused (at most once in a while).</summary>
        public string[] paused;
    }

    /// <summary>
    /// The wingman's own casual conversation, for when no LLM answers the talk (CoopBot's chat
    /// mode "personality", or "llm" with no listener). It finds the topic of what a player said
    /// by keywords, answers in the wingman's voice with its live facts filled in ({hull},
    /// {shield}, {near}, {hostiles}, {credits}, {hold}, {leader}, {order}...), and can ask a
    /// question whose answer (a number or a keyword) picks the next branch. Each player has a
    /// session of their own: the question pending, the flags set, the last line said (never said
    /// twice running); it is forgotten after two minutes of quiet. It never carries out orders:
    /// the bot reads those first. A plain class (no Unity objects) so it can be tested.
    /// </summary>
    public class WingmanPersonality
    {
        public const string Resource = "Data/wingman_dialogue";
        /// <summary>Seconds of quiet after which a player's conversation starts again.</summary>
        public const float SessionTimeout = 120f;

        class Session
        {
            public string pending;
            public float lastSeen;
            public string lastLine;
            public readonly HashSet<string> flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        readonly WingmanDialogue data;
        readonly Dictionary<string, WingmanDialogue.Node> nodes = new Dictionary<string, WingmanDialogue.Node>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Session> sessions = new Dictionary<string, Session>(StringComparer.OrdinalIgnoreCase);
        readonly System.Random rng;
        string lastPaused;

        static readonly Regex Blank = new Regex(@"\{(?<k>[a-z_]+)\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly string[] NumberWords = { "one", "two", "three", "four", "five", "six" };
        static readonly string[] OrdinalWords = { "first", "second", "third", "fourth", "fifth", "sixth" };

        public WingmanPersonality(WingmanDialogue dialogue, int? seed = null)
        {
            data = dialogue ?? new WingmanDialogue();
            data.topics ??= new WingmanDialogue.Topic[0];
            data.nodes ??= new WingmanDialogue.Node[0];
            data.reprompt ??= new string[0];
            data.paused ??= new string[0];
            foreach (var n in data.nodes)
            {
                if (n == null || string.IsNullOrEmpty(n.id)) continue;
                n.say ??= new WingmanDialogue.Variant[0];
                n.sets ??= new string[0];
                n.choices ??= new WingmanDialogue.Choice[0];
                nodes[n.id] = n;
            }
            rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
        }

        public static WingmanDialogue Parse(string json) => JsonUtility.FromJson<WingmanDialogue>(json);

        /// <summary>The wingman's personality from Resources/Data/wingman_dialogue.json (null if it's missing).</summary>
        public static WingmanPersonality Load()
        {
            var text = Resources.Load<TextAsset>(Resource);
            if (text == null) { Debug.LogWarning($"[CoopBot] Resources/{Resource}.json is missing: no built-in chatter"); return null; }
            try { return new WingmanPersonality(Parse(text.text)); }
            catch (Exception e) { Debug.LogWarning($"[CoopBot] {Resource}.json: {e.Message}"); return null; }
        }

        public WingmanDialogue Data => data;
        public bool HasNode(string id) => id != null && nodes.ContainsKey(id);

        // ------------------------------------------------------------ the conversation

        /// <summary>The answer to a player's casual line (what followed the wingman's name), or null
        /// if there is nothing to say. `now`: seconds on any steady clock; `facts`: the live facts
        /// for the blanks (a missing or empty one is unknown).</summary>
        public string Reply(string player, string text, float now, IReadOnlyDictionary<string, string> facts)
        {
            var s = SessionOf(player, now);
            string t = Normalize(text);
            WingmanDialogue.Node next = null;
            // an answer to the question pending
            if (s.pending != null && nodes.TryGetValue(s.pending, out var asked))
            {
                int pick = ChoiceIndex(asked, t);
                if (pick >= 0) nodes.TryGetValue(asked.choices[pick].next ?? "", out next);
            }
            if (next == null)
            {
                string topic = TopicOf(t);
                if (topic != null) next = NodeOfTopic(topic);
                else if (s.pending != null && nodes.TryGetValue(s.pending, out var still) && data.reprompt.Length > 0)
                {
                    // (not an answer, nor a new topic: the question again)
                    string again = PickLine(data.reprompt, s.lastLine, facts, out string tmpl);
                    s.lastLine = tmpl;
                    return again + ChoicesSuffix(still);
                }
                else nodes.TryGetValue(t.Length == 0 ? data.hail ?? "" : data.fallback ?? "", out next);
            }
            if (next == null) { s.pending = null; return null; }
            return Speak(s, next, facts);
        }

        /// <summary>A "comms quiet, standing by" line, never the same twice running (null if there are none).</summary>
        public string PausedLine(IReadOnlyDictionary<string, string> facts)
        {
            if (data.paused.Length == 0) return null;
            string said = PickLine(data.paused, lastPaused, facts, out lastPaused);
            return said;
        }

        /// <summary>The topic a line is on (the one whose keywords match the most of it), or null.</summary>
        public string TopicOf(string text)
        {
            string t = " " + Normalize(text) + " ";
            if (t.Trim().Length == 0) return null;
            string best = null;
            int bestScore = 0;
            foreach (var topic in data.topics)
            {
                if (topic == null || topic.keywords == null) continue;
                int score = 0;
                foreach (var kw in topic.keywords)
                    if (Matches(t, kw)) score += Normalize(kw).Length;
                if (score > bestScore) { bestScore = score; best = topic.id; }
            }
            return best;
        }

        /// <summary>The question pending for a player (the node that asked it), or null.</summary>
        public string Pending(string player, float now)
        {
            return sessions.TryGetValue(player ?? "", out var s) && now - s.lastSeen <= SessionTimeout ? s.pending : null;
        }

        /// <summary>Is a session flag set for a player (and their conversation not yet forgotten)?</summary>
        public bool HasFlag(string player, string flag, float now)
        {
            return sessions.TryGetValue(player ?? "", out var s) && now - s.lastSeen <= SessionTimeout && s.flags.Contains(flag);
        }

        /// <summary>Forget a player's conversation.</summary>
        public void Forget(string player) => sessions.Remove(player ?? "");

        Session SessionOf(string player, float now)
        {
            string key = player ?? "";
            if (!sessions.TryGetValue(key, out var s)) sessions[key] = s = new Session { lastSeen = now };
            else if (now - s.lastSeen > SessionTimeout)
            {
                // (a while since: a new conversation; the last line is kept so it isn't said again at once)
                s.pending = null;
                s.flags.Clear();
            }
            s.lastSeen = now;
            // (players who left long ago are let go)
            if (sessions.Count > 64)
            {
                var old = new List<string>();
                foreach (var kv in sessions) if (now - kv.Value.lastSeen > SessionTimeout) old.Add(kv.Key);
                foreach (var k in old) sessions.Remove(k);
            }
            return s;
        }

        WingmanDialogue.Node NodeOfTopic(string topicId)
        {
            foreach (var topic in data.topics)
                if (topic != null && topic.id == topicId && nodes.TryGetValue(topic.node ?? "", out var n)) return n;
            return null;
        }

        string Speak(Session s, WingmanDialogue.Node node, IReadOnlyDictionary<string, string> facts)
        {
            string[] lines = null;
            foreach (var v in node.say)
                if (v != null && v.lines != null && v.lines.Length > 0 && Holds(v.when, s, facts)) { lines = v.lines; break; }
            foreach (var f in node.sets) if (!string.IsNullOrEmpty(f)) s.flags.Add(f);
            if (lines == null) { s.pending = null; return null; }
            string said = PickLine(lines, s.lastLine, facts, out string tmpl);
            s.lastLine = tmpl;
            s.pending = node.choices.Length > 0 ? node.id : null;
            return said + ChoicesSuffix(node);
        }

        static string ChoicesSuffix(WingmanDialogue.Node node)
        {
            if (node.choices == null || node.choices.Length == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < node.choices.Length; i++) sb.Append($" ({i + 1}) {node.choices[i].label}");
            return sb.ToString();
        }

        /// <summary>Which of a node's choices a reply picks: its number ("2", "(2)", "two",
        /// "option 2", "the second"), or one of its words; -1 if none.</summary>
        static int ChoiceIndex(WingmanDialogue.Node node, string t)
        {
            if (node.choices.Length == 0 || t.Length == 0) return -1;
            // a number, alone or nearly
            string n = t;
            foreach (var lead in new[] { "i pick ", "pick ", "go with ", "option ", "number ", "choice ", "the " })
                if (n.StartsWith(lead)) n = n.Substring(lead.Length);
            if (n.EndsWith(" one") && n != "one") n = n.Substring(0, n.Length - 4);   // ("the second one")
            if (n.EndsWith(" please")) n = n.Substring(0, n.Length - 7);
            for (int i = 0; i < node.choices.Length; i++)
                if (n == (i + 1).ToString() || (i < NumberWords.Length && (n == NumberWords[i] || n == OrdinalWords[i]))) return i;
            // a word of one of them (the first that matches)
            string padded = " " + t + " ";
            for (int i = 0; i < node.choices.Length; i++)
            {
                var c = node.choices[i];
                if (!string.IsNullOrEmpty(c.label) && Matches(padded, c.label)) return i;
                if (c.keys != null)
                    foreach (var k in c.keys)
                        if (Matches(padded, k)) return i;
            }
            return -1;
        }

        /// <summary>Does a keyword (a whole word or phrase; "stem*" any word starting so) occur in
        /// a normalized line padded with spaces?</summary>
        static bool Matches(string padded, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return false;
            bool stem = keyword.TrimEnd().EndsWith("*");
            string k = Normalize(keyword.TrimEnd().TrimEnd('*'));
            if (k.Length == 0) return false;
            return padded.Contains(" " + k + (stem ? "" : " "));
        }

        static bool Holds(string when, Session s, IReadOnlyDictionary<string, string> facts)
        {
            if (string.IsNullOrWhiteSpace(when)) return true;
            foreach (var raw in when.Split(','))
            {
                string c = raw.Trim();
                if (c.Length == 0) continue;
                bool not = c.StartsWith("!");
                if (not) c = c.Substring(1).Trim();
                bool yes;
                if (c.StartsWith("flag:")) yes = s.flags.Contains(c.Substring(5).Trim());
                else if (c.StartsWith("fact:")) yes = Known(facts, c.Substring(5).Trim());
                else yes = false;
                if (yes == not) return false;
            }
            return true;
        }

        static bool Known(IReadOnlyDictionary<string, string> facts, string key) =>
            facts != null && facts.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v);

        /// <summary>A line at random, not `last` (when there is another), preferring those whose
        /// blanks can all be filled; filled in. `template`: the line chosen, before filling.</summary>
        string PickLine(string[] lines, string last, IReadOnlyDictionary<string, string> facts, out string template)
        {
            var fillable = new List<string>();
            foreach (var l in lines) if (!string.IsNullOrEmpty(l) && CanFill(l, facts)) fillable.Add(l);
            var pool = fillable.Count > 0 ? fillable : new List<string>(Array.FindAll(lines, l => !string.IsNullOrEmpty(l)));
            if (pool.Count == 0) { template = last; return ""; }
            if (pool.Count > 1 && last != null) pool.Remove(last);
            template = pool[rng.Next(pool.Count)];
            return Fill(template, facts);
        }

        static bool CanFill(string line, IReadOnlyDictionary<string, string> facts)
        {
            foreach (Match m in Blank.Matches(line))
                if (!Known(facts, m.Groups["k"].Value.ToLowerInvariant())) return false;
            return true;
        }

        /// <summary>A line with its blanks filled from the facts ("unknown" for one not known).</summary>
        public static string Fill(string line, IReadOnlyDictionary<string, string> facts)
        {
            return Blank.Replace(line ?? "", m =>
            {
                string k = m.Groups["k"].Value.ToLowerInvariant();
                return Known(facts, k) ? facts[k] : "unknown";
            });
        }

        /// <summary>A line as it is matched: lower case, apostrophes dropped ("how's" is "hows"),
        /// anything but letters and digits a space, spaces single.</summary>
        public static string Normalize(string text)
        {
            var sb = new StringBuilder();
            bool space = true;
            foreach (char ch in (text ?? "").ToLowerInvariant())
            {
                if (ch == '\'' || ch == '\u2019') continue;
                if (char.IsLetterOrDigit(ch)) { sb.Append(ch); space = false; }
                else if (!space) { sb.Append(' '); space = true; }
            }
            return sb.ToString().Trim();
        }
    }
}
