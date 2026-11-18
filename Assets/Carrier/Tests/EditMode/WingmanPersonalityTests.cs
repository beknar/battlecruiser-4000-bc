using System.Collections.Generic;
using System.Linq;
using CarrierGame.Demo;
using NUnit.Framework;
using UnityEngine;

namespace CarrierGame.Tests
{
    /// <summary>The AI wingman's own casual talk (WingmanPersonality) and its chat mode switch
    /// (CoopBot): topics found by keywords, branches picked by number or keyword, nested branches
    /// and the flags they set, a conversation forgotten after its timeout, the live facts in the
    /// lines, no line said twice running, and the dialogue file itself.</summary>
    public class WingmanPersonalityTests
    {
        static readonly Dictionary<string, string> NoFacts = new Dictionary<string, string>();

        static WingmanDialogue.Variant V(string when, params string[] lines) => new WingmanDialogue.Variant { when = when, lines = lines };
        static WingmanDialogue.Choice C(string label, string next, params string[] keys) => new WingmanDialogue.Choice { label = label, next = next, keys = keys };
        static WingmanDialogue.Topic T(string id, string node, params string[] keywords) => new WingmanDialogue.Topic { id = id, node = node, keywords = keywords };

        /// <summary>A small dialogue: a Vrask conversation three deep that sets a flag, a fallback,
        /// a line of facts, and a topic with several lines.</summary>
        static WingmanPersonality Small(int seed = 1)
        {
            var d = new WingmanDialogue
            {
                reprompt = new[] { "Say again?" },
                paused = new[] { "Standing by.", "Quiet, standing by." },
                topics = new[]
                {
                    T("vrask", "vrask", "vrask", "aliens"),
                    T("ship", "ship", "ship", "hull"),
                    T("how", "how", "how are you", "you"),
                    T("hello", "hello", "hello", "hi"),
                    T("pirates", "pirates", "pirate*"),
                },
                nodes = new[]
                {
                    new WingmanDialogue.Node { id = "fallback", say = new[] { V("", "Copy.") } },
                    new WingmanDialogue.Node { id = "hail", say = new[] { V("", "Go ahead, {player}.") } },
                    new WingmanDialogue.Node
                    {
                        id = "vrask",
                        say = new[] { V("flag:asked_about_vrask", "Vrask again. Long or short?"), V("", "The Vrask. Long or short?") },
                        choices = new[] { C("long", "vrask_long", "full"), C("short", "vrask_short", "brief") },
                    },
                    new WingmanDialogue.Node { id = "vrask_short", sets = new[] { "asked_about_vrask" }, say = new[] { V("", "They board. Shoot first.") } },
                    new WingmanDialogue.Node
                    {
                        id = "vrask_long", sets = new[] { "asked_about_vrask" },
                        say = new[] { V("", "They take crews. Ships or tactics?") },
                        choices = new[] { C("ships", "vrask_ships"), C("tactics", "vrask_tactics") },
                    },
                    new WingmanDialogue.Node
                    {
                        id = "vrask_ships",
                        say = new[] { V("", "Snare, Fang or Maw?") },
                        choices = new[] { C("snare", "snare"), C("fang", "fang"), C("maw", "maw", "gun") },
                    },
                    new WingmanDialogue.Node { id = "vrask_tactics", say = new[] { V("", "Keep moving.") } },
                    new WingmanDialogue.Node { id = "snare", say = new[] { V("", "Snare: stay out of 2.5 km.") } },
                    new WingmanDialogue.Node { id = "fang", say = new[] { V("", "Fang: shoot it first.") } },
                    new WingmanDialogue.Node { id = "maw", sets = new[] { "maw" }, say = new[] { V("", "Maw: get under its guns.") } },
                    new WingmanDialogue.Node { id = "ship", say = new[] { V("", "Hull {hull}, shield {shield}.", "Leader {leader}, hull {hull}.") } },
                    new WingmanDialogue.Node { id = "how", say = new[] { V("", "Fine.", "Flying.", "Can't complain.") } },
                    new WingmanDialogue.Node { id = "hello", say = new[] { V("", "Hey, {player}.") } },
                    new WingmanDialogue.Node { id = "pirates", say = new[] { V("", "Pirates, ugh.") } },
                },
            };
            return new WingmanPersonality(d, seed);
        }

        // ------------------------------------------------------------ topics

        [TestCase("tell me about the vrask", "vrask")]
        [TestCase("Any ALIENS out there?", "vrask")]
        [TestCase("how's the ship?", "ship")]
        [TestCase("how are you?", "how")]
        [TestCase("hi there", "hello")]
        [TestCase("seen any pirates?", "pirates")]
        [TestCase("nice weather", null)]
        [TestCase("", null)]
        public void ATopicIsFoundByItsKeywords(string said, string topic)
        {
            Assert.That(Small().TopicOf(said), Is.EqualTo(topic));
        }

        [Test]
        public void KeywordsAreWholeWordsUnlessStarred()
        {
            var p = Small();
            Assert.That(p.TopicOf("this is shipshape"), Is.Null, "'ship' isn't in 'shipshape'");
            Assert.That(p.TopicOf("piratey types"), Is.EqualTo("pirates"), "'pirate*' is any word starting so");
            Assert.That(p.TopicOf("how are you hi"), Is.EqualTo("how"), "the longest match wins");
        }

        // ------------------------------------------------------------ branching

        [Test]
        public void AQuestionListsItsChoicesAndANumberPicksOne()
        {
            var p = Small();
            string q = p.Reply("Ann", "tell me about the vrask", 0f, NoFacts);
            Assert.That(q, Is.EqualTo("The Vrask. Long or short? (1) long (2) short"));
            Assert.That(p.Pending("Ann", 0f), Is.EqualTo("vrask"));
            Assert.That(p.Reply("Ann", "1", 1f, NoFacts), Is.EqualTo("They take crews. Ships or tactics? (1) ships (2) tactics"));
            Assert.That(p.Pending("Ann", 1f), Is.EqualTo("vrask_long"));
            Assert.That(p.Reply("Ann", "option 2", 2f, NoFacts), Is.EqualTo("Keep moving."));
            Assert.That(p.Pending("Ann", 2f), Is.Null, "a line with no question ends the branch");
        }

        [TestCase("short")]
        [TestCase("the short one please")]
        [TestCase("brief")]
        [TestCase("two")]
        [TestCase("(2)")]
        [TestCase("the second one")]
        public void AKeywordOrANumberWordPicksABranch(string answer)
        {
            var p = Small();
            p.Reply("Ann", "vrask?", 0f, NoFacts);
            Assert.That(p.Reply("Ann", answer, 1f, NoFacts), Is.EqualTo("They board. Shoot first."));
        }

        [Test]
        public void BranchesNestThreeDeepAndSetFlagsThatChangeLaterLines()
        {
            var p = Small();
            p.Reply("Ann", "the vrask", 0f, NoFacts);
            p.Reply("Ann", "long", 1f, NoFacts);
            Assert.That(p.Reply("Ann", "ships", 2f, NoFacts), Does.StartWith("Snare, Fang or Maw?"));
            Assert.That(p.Reply("Ann", "the big gun", 3f, NoFacts), Is.EqualTo("Maw: get under its guns."), "three deep, by a keyword");
            Assert.That(p.HasFlag("Ann", "asked_about_vrask", 3f), Is.True);
            Assert.That(p.HasFlag("Ann", "maw", 3f), Is.True);
            Assert.That(p.Reply("Ann", "aliens", 4f, NoFacts), Does.StartWith("Vrask again."), "the flag changes the line");
            Assert.That(p.HasFlag("Bob", "asked_about_vrask", 4f), Is.False, "each player has a session of their own");
        }

        [Test]
        public void ANonAnswerAsksAgainAndANewTopicMovesOn()
        {
            var p = Small();
            p.Reply("Ann", "vrask", 0f, NoFacts);
            Assert.That(p.Reply("Ann", "bananas", 1f, NoFacts), Is.EqualTo("Say again? (1) long (2) short"));
            Assert.That(p.Pending("Ann", 1f), Is.EqualTo("vrask"));
            Assert.That(p.Reply("Ann", "hello", 2f, NoFacts), Is.EqualTo("Hey, unknown."), "a new topic: the question is dropped");
            Assert.That(p.Pending("Ann", 2f), Is.Null);
        }

        [Test]
        public void AConversationIsForgottenAfterItsTimeout()
        {
            var p = Small();
            p.Reply("Ann", "vrask", 0f, NoFacts);
            p.Reply("Ann", "short", 1f, NoFacts);
            p.Reply("Ann", "vrask", 2f, NoFacts);
            float later = 2f + WingmanPersonality.SessionTimeout + 1f;
            Assert.That(p.Pending("Ann", later), Is.Null);
            Assert.That(p.Reply("Ann", "1", later, NoFacts), Is.EqualTo("Copy."), "the question has gone: '1' is just talk");
            Assert.That(p.HasFlag("Ann", "asked_about_vrask", later), Is.False, "and so have the flags");
            Assert.That(p.Reply("Ann", "vrask", later + 1f, NoFacts), Does.StartWith("The Vrask."));
        }

        // ------------------------------------------------------------ facts and variety

        [Test]
        public void TheBlanksAreFilledFromTheFacts()
        {
            var facts = new Dictionary<string, string> { ["hull"] = "80%", ["shield"] = "65%", ["player"] = "Ann" };
            Assert.That(WingmanPersonality.Fill("Hull {hull}, shield {shield}, near {near}.", facts), Is.EqualTo("Hull 80%, shield 65%, near unknown."));
            var p = Small();
            // (a line whose blanks aren't all known is passed over while another can be said)
            for (int i = 0; i < 10; i++)
                Assert.That(p.Reply("Ann", "ship", i, facts), Is.EqualTo("Hull 80%, shield 65%."));
            Assert.That(p.Reply("Ann", "hi", 20f, facts), Is.EqualTo("Hey, Ann."));
        }

        [Test]
        public void NoLineIsSaidTwiceRunningToAPlayer()
        {
            for (int seed = 0; seed < 5; seed++)
            {
                var p = Small(seed);
                string last = null;
                var seen = new HashSet<string>();
                for (int i = 0; i < 60; i++)
                {
                    string said = p.Reply("Ann", "how are you", i, NoFacts);
                    Assert.That(said, Is.Not.EqualTo(last), $"seed {seed}, line {i}");
                    seen.Add(said);
                    last = said;
                }
                Assert.That(seen.Count, Is.EqualTo(3), "every variant is used");
            }
        }

        [Test]
        public void ThePausedLineIsNeverTheSameTwiceRunning()
        {
            var p = Small();
            string last = null;
            for (int i = 0; i < 20; i++)
            {
                string said = p.PausedLine(NoFacts);
                Assert.That(said, Does.Contain("tanding by"));
                Assert.That(said, Is.Not.EqualTo(last));
                last = said;
            }
        }

        [Test]
        public void ItsNameAloneIsAHailAndUnknownTalkTheFallback()
        {
            var p = Small();
            Assert.That(p.Reply("Ann", "", 0f, new Dictionary<string, string> { ["player"] = "Ann" }), Is.EqualTo("Go ahead, Ann."));
            Assert.That(p.Reply("Ann", "nice weather", 1f, NoFacts), Is.EqualTo("Copy."));
        }

        // ------------------------------------------------------------ the dialogue file

        [Test]
        public void TheDialogueFileHoldsTogether()
        {
            var text = Resources.Load<TextAsset>(WingmanPersonality.Resource);
            Assert.That(text, Is.Not.Null, "Resources/Data/wingman_dialogue.json");
            var d = WingmanPersonality.Parse(text.text);
            var p = new WingmanPersonality(d, 3);
            var nodes = d.nodes.ToDictionary(n => n.id);
            Assert.That(d.topics.Length, Is.GreaterThanOrEqualTo(15));
            Assert.That(p.HasNode(d.fallback) && p.HasNode(d.hail), "a fallback and a hail");
            Assert.That(d.paused.Length, Is.GreaterThan(0));
            foreach (var t in d.topics) Assert.That(p.HasNode(t.node), $"topic {t.id}'s node {t.node}");
            foreach (var n in d.nodes)
            {
                Assert.That(n.say != null && n.say.Any(v => v.lines != null && v.lines.Length > 0 && string.IsNullOrEmpty(v.when)),
                    $"node {n.id} always has something to say");
                foreach (var c in n.choices ?? new WingmanDialogue.Choice[0])
                    Assert.That(p.HasNode(c.next), $"node {n.id}'s choice {c.label} goes to {c.next}");
            }
            int Depth(string id, int guard)
            {
                if (guard > 6 || !nodes.TryGetValue(id, out var n) || n.choices == null || n.choices.Length == 0) return 0;
                return 1 + n.choices.Max(c => c.next == id ? 0 : Depth(c.next, guard + 1));
            }
            var deep = d.topics.Where(t => Depth(t.node, 0) >= 2).Select(t => t.id).ToList();
            Assert.That(deep.Count, Is.GreaterThanOrEqualTo(4), "branching conversations two deep: " + string.Join(", ", deep));
            Assert.That(d.topics.Any(t => Depth(t.node, 0) >= 3), "one three deep");
        }

        [TestCase("hello there", "greeting")]
        [TestCase("how's it going?", "how_are_you")]
        [TestCase("how was the battle?", "battle")]
        [TestCase("what's in the hold?", "cargo")]
        [TestCase("where are we?", "where")]
        [TestCase("tell me about the vrask", "vrask")]
        [TestCase("any pirates about?", "corsairs")]
        [TestCase("do you miss home?", "home")]
        [TestCase("what do you think of the captain?", "captain")]
        [TestCase("tell me a joke", "joke")]
        [TestCase("thanks", "thanks")]
        [TestCase("bye for now", "goodbye")]
        public void TheDialogueFileKnowsTheCasualTopics(string said, string topic)
        {
            var p = new WingmanPersonality(WingmanPersonality.Parse(Resources.Load<TextAsset>(WingmanPersonality.Resource).text));
            Assert.That(p.TopicOf(said), Is.EqualTo(topic));
        }

        // ------------------------------------------------------------ the chat mode

        [TestCase("llm", CoopBot.ChatMode.Llm)]
        [TestCase("ON", CoopBot.ChatMode.Llm)]
        [TestCase("personality", CoopBot.ChatMode.Personality)]
        [TestCase("off", CoopBot.ChatMode.Personality)]
        [TestCase("pause", CoopBot.ChatMode.Paused)]
        [TestCase("paused", CoopBot.ChatMode.Paused)]
        [TestCase("resume", CoopBot.ChatMode.Personality)]
        [TestCase("unpause", CoopBot.ChatMode.Personality)]
        public void AChatModeByNameOrSwitchWord(string word, CoopBot.ChatMode mode)
        {
            Assert.That(CoopBot.ParseChatMode(word, CoopBot.ChatMode.Personality, out var m), Is.True);
            Assert.That(m, Is.EqualTo(mode));
            Assert.That(CoopBot.ParseChatMode("loud", CoopBot.ChatMode.Llm, out _), Is.False);
        }

        [TestCase("chat on", "on")]
        [TestCase("chat off", "off")]
        [TestCase("Chat pause!", "pause")]
        [TestCase("chat resume", "resume")]
        [TestCase("chat mode?", "")]
        [TestCase("chat mode personality", "personality")]
        [TestCase("comms mode llm", "llm")]
        [TestCase("chat with me", null)]
        [TestCase("chat", "")]
        [TestCase("chatty today", null)]
        [TestCase("comms check", null)]
        public void AChatModeOrderIsToldFromTalk(string said, string arg)
        {
            Assert.That(CoopBot.ChatOrderArg(said), Is.EqualTo(arg));
        }

        [Test]
        public void TheChatModeSwitchesByOrderAndResumesWhereItWas()
        {
            var bot = new GameObject("bot").AddComponent<CoopBot>();
            try
            {
                Assert.That(bot.Chat, Is.EqualTo(CoopBot.ChatMode.Llm));
                Assert.That(bot.Command("chat mode?", 2), Does.Contain("chat mode llm"));
                Assert.That(bot.Command("chat off", 2), Does.Contain("personality"));
                Assert.That(bot.Chat, Is.EqualTo(CoopBot.ChatMode.Personality));
                Assert.That(bot.Command("chat pause", 2), Does.Contain("paused"));
                Assert.That(bot.Chat, Is.EqualTo(CoopBot.ChatMode.Paused));
                Assert.That(bot.Command("chat resume", 2), Does.Contain("personality"));
                Assert.That(bot.Chat, Is.EqualTo(CoopBot.ChatMode.Personality), "back to what it was before the pause");
                Assert.That(bot.Command("chat resume", 2), Does.Contain("already"), "nothing paused: as it was");
                Assert.That(bot.Command("chat on", 2), Does.Contain("llm"));
                Assert.That(bot.Chat, Is.EqualTo(CoopBot.ChatMode.Llm));
                Assert.That(bot.Command("chat with me", 2), Is.Null, "talk");
                Assert.That(bot.Orders, Is.Zero, "switching the chat isn't an order to the pilot");
                Assert.That(bot.MayChangeChat(5), Is.True, "no leader and no host: anyone may");
            }
            finally { Object.DestroyImmediate(bot.gameObject); }
        }
    }
}
