using System.Collections.Generic;
using System.Linq;
using CarrierGame.Demo;
using NUnit.Framework;
using UnityEngine;

namespace CarrierGame.Tests
{
    /// <summary>The co-op comms channel and the AI wingman: the chat message on the wire, how the
    /// bot tells an order to it from talk, what it answers before it has a ship, how it picks
    /// targets, and the comms line taking the keys from the game.</summary>
    public class CoopChatTests
    {
        readonly List<GameObject> made = new List<GameObject>();

        [TearDown]
        public void Clear()
        {
            foreach (var go in made) if (go != null) Object.DestroyImmediate(go);
            made.Clear();
            Targets.Reset(null);
            InputFocus.Release(InputFocus.Panel.Chat);
        }

        [Test]
        public void AChatMessageGoesOutAsTheServerPassesItOn()
        {
            string json = JsonUtility.ToJson(new ChatMsg { text = "Wingman, follow me" });
            StringAssert.Contains("\"t\":\"chat\"", json);
            StringAssert.Contains("\"text\":\"Wingman, follow me\"", json);
            // and comes in (with who sent it, added by the server) as a message the game reads
            var m = JsonUtility.FromJson<CoopMsg>("{\"t\":\"chat\",\"text\":\"hello\",\"from\":3}");
            Assert.That(m.t, Is.EqualTo(CoopWire.Chat));
            Assert.That(m.text, Is.EqualTo("hello"));
            Assert.That(m.from, Is.EqualTo(3));
            CollectionAssert.DoesNotContain(CoopWire.ServerOnly, CoopWire.Chat, "games may send it");
        }

        [Test]
        public void AMessageIsCleanedAndCut()
        {
            Assert.That(CoopChat.Clean("  hi\nthere\t "), Is.EqualTo("hithere"));
            Assert.That(CoopChat.Clean(null), Is.EqualTo(""));
            Assert.That(CoopChat.Clean(new string('x', 500)).Length, Is.EqualTo(CoopChat.MaxLength));
        }

        [Test]
        public void ALongReplyIsKeptWhole()
        {
            // (a full answer from the wingman's LLM: more than the old 240 characters)
            string reply = "Wingman: " + new string('y', 330);
            Assert.That(CoopChat.Clean(reply), Is.EqualTo(reply));
            Assert.That(CoopChat.MaxLength, Is.GreaterThanOrEqualTo(400));
            // (line breaks and the space before it aren't counted against the limit)
            string lines = "   " + string.Join("\n", System.Linq.Enumerable.Repeat(new string('z', 39), 10));
            Assert.That(CoopChat.Clean(lines).Length, Is.EqualTo(390));
            Assert.That(CoopChat.Clean(new string('x', 500), int.MaxValue).Length, Is.EqualTo(500), "uncut, to tell whether it was cut");
        }

        [Test]
        public void TheSendersNameIsShownOnce()
        {
            var bot = new CoopChat.Line { name = "Wingman", text = "Wingman: on your wing." };
            Assert.That(CoopChat.Shown(bot), Is.EqualTo("Wingman: on your wing."));
            var player = new CoopChat.Line { name = "web_user", text = "wingman, status" };
            Assert.That(CoopChat.Shown(player), Is.EqualTo("web_user: wingman, status"));
        }

        [Test]
        public void ALandedLeaderIsWaitedForInHighOrbit()
        {
            WarpPoint Point(string name, WarpPoint.Kind kind)
            {
                var go = new GameObject(name);
                made.Add(go);
                var w = go.AddComponent<WarpPoint>();
                w.displayName = name;
                w.kind = kind;
                if (!WarpPoint.All.Contains(w)) WarpPoint.All.Add(w);
                return w;
            }
            var forge = Point("The Forge", WarpPoint.Kind.Planet);
            var orbit = Point("The Forge High Orbit", WarpPoint.Kind.Landmark);
            var bare = Point("Seraph", WarpPoint.Kind.Moon);
            try
            {
                Assert.That(Wingman.WaitingPlace(forge), Is.SameAs(orbit));
                Assert.That(Wingman.WaitingPlace(bare), Is.SameAs(bare), "no high orbit: over the world itself");
                Assert.That(Wingman.WaitingPlace(null), Is.Null);
            }
            finally
            {
                WarpPoint.All.Remove(forge);
                WarpPoint.All.Remove(orbit);
                WarpPoint.All.Remove(bare);
            }
        }

        [Test]
        public void SayingNothingOrUnconnectedSendsNothing()
        {
            Assert.That(CoopChat.Say("hello"), Is.False, "not connected");
            Assert.That(CoopChat.Say("   "), Is.False);
        }

        [TestCase("Wingman, follow me", "follow me")]
        [TestCase("@wingman attack the corsair", "attack the corsair")]
        [TestCase("WINGMAN: hold!", "hold!")]
        [TestCase("all ships, regroup", "regroup")]
        [TestCase("Everyone - status", "status")]
        [TestCase("Wingman", "")]
        public void AnOrderStartsWithItsName(string said, string order)
        {
            Assert.That(CoopBot.AddressedPart(said, "Wingman"), Is.EqualTo(order));
        }

        [TestCase("follow me, wingman")]
        [TestCase("Wingmanly behaviour")]
        [TestCase("nice shooting")]
        [TestCase("")]
        public void TalkIsNotAnOrder(string said)
        {
            Assert.That(CoopBot.AddressedPart(said, "Wingman"), Is.Null);
        }

        [Test]
        public void WithoutAShipItSaysSoAndKnowsTalkFromOrders()
        {
            var bot = new GameObject("bot").AddComponent<CoopBot>();
            made.Add(bot.gameObject);
            Assert.That(bot.Command("hold", 0), Is.EqualTo("not ready yet"));
            Assert.That(bot.Command("follow me", 2), Is.EqualTo("not ready yet"));
            Assert.That(bot.Command("help", 0), Does.Contain("follow me"));
            Assert.That(bot.Command("nice weather out here", 0), Is.Null, "talk, left for whoever reads the channel");
            Assert.That(bot.Command("holdup", 0), Is.Null, "a whole word only");
            Assert.That(bot.Command("what's in your hold?", 0), Is.EqualTo("not ready yet"), "a cargo report, not 'hold'");
            Assert.That(bot.Command("give me 500 credits", 2), Is.EqualTo("not ready yet"));
            Assert.That(bot.Command("give me a hand", 2), Is.Null, "giving, but not credits: talk");
            Assert.That(bot.Command("mine and then sell at the nearest station", 2), Is.EqualTo("not ready yet"), "mining (it sells when full), not a field's name");
            Assert.That(bot.Command("warp to Heaven, then dock", 2), Is.EqualTo("not ready yet"));
            Assert.That(bot.Pending, Is.Zero);
            Assert.That(bot.Orders, Is.Zero);
        }

        [Test]
        public void SeveralOrdersInOneMessageAreSplitIntoThem()
        {
            string Kinds(string said) => string.Join(" | ", CoopBot.Clauses(said).Select(c => c.arg.Length > 0 ? $"{c.kind} {c.arg}" : c.kind));
            Assert.That(Kinds("mine and then sell at nearest station"), Is.EqualTo("mine | sell"));
            Assert.That(Kinds("warp to Heaven, then dock"), Is.EqualTo("goto Heaven | dock"));
            Assert.That(Kinds("mine at the Inner Belt; status"), Is.EqualTo("mine Inner Belt | status"));
            Assert.That(Kinds("attack the Scavenger and then follow me"), Is.EqualTo("attack Scavenger | follow"));
            Assert.That(Kinds("dock, sell, cargo"), Is.EqualTo("dock | sell | cargo"));
            // one order, or not all orders: taken whole
            Assert.That(CoopBot.Clauses("mine"), Is.Null);
            Assert.That(CoopBot.Clauses("give me 1,000 credits"), Is.Null, "a comma in a number isn't a break");
            Assert.That(CoopBot.Clauses("hold on, that was close"), Is.Null, "talk");
            Assert.That(CoopBot.Clauses("warp to Heaven and say hello"), Is.Null);
        }

        [Test]
        public void AStandingOrderCanOnlyComeLast()
        {
            var bot = new GameObject("bot").AddComponent<CoopBot>();
            made.Add(bot.gameObject);
            Assert.That(bot.Command("follow me and then dock", 2), Does.Contain("give it last"));
            Assert.That(bot.Command("form up, then status", 2), Is.EqualTo("not ready yet"), "a report after it is fine");
            Assert.That(bot.Orders, Is.Zero);
        }

        [TestCase("give me 500 credits", "500")]
        [TestCase("Give me 1,500 cr.", "1500")]
        [TestCase("transfer 2k credits to Ann", "2000 to Ann")]
        [TestCase("send your credits to Bob the Bold", "all to Bob the Bold")]
        [TestCase("transfer credits", "?")]
        [TestCase("give me some credits", "?")]
        [TestCase("give me half your credits", "half")]
        [TestCase("give me all your credits", "all")]
        [TestCase("pay me", "?")]
        [TestCase("pay up", "?")]
        [TestCase("please pay me 300", "300")]
        [TestCase("hand over the money to me", "?")]
        [TestCase("give us 12.9 credits", "12")]
        [TestCase("give Ann 500 credits", "500 to Ann")]
        [TestCase("give them all your credits", "all")]
        public void CreditOrdersAreRead(string said, string args)
        {
            Assert.That(CoopBot.GiveArgs(said), Is.EqualTo(args));
        }

        [TestCase("give me a hand")]
        [TestCase("send help")]
        [TestCase("transfer the ore")]
        [TestCase("credits please")]
        [TestCase("pay attention")]
        [TestCase("give me your earnings report")]
        [TestCase("")]
        [TestCase(null)]
        public void OtherThingsAreNotCreditOrders(string said)
        {
            Assert.That(CoopBot.GiveArgs(said), Is.Null);
        }

        [Test]
        public void TheCargoLineSaysHowFullAndWhatIsAboard()
        {
            var go = new GameObject("hold");
            made.Add(go);
            var hold = go.AddComponent<CargoHold>();
            hold.capacity = 600f;
            Assert.That(CoopBot.CargoLine(hold), Is.EqualTo("hold 0 of 600 t (0% full), empty; 0 cr aboard.").Or.Contain("empty"));
            hold.Add(Mineral.Iron, 120f);
            hold.Earn(918f);
            string line = CoopBot.CargoLine(hold);
            Assert.That(line, Does.StartWith("hold 120 of 600 t ("));
            Assert.That(line, Does.Contain("120 t "));
            Assert.That(line, Does.Contain("918 cr aboard"));
            Assert.That(CoopBot.CargoLine(null), Is.EqualTo("no cargo hold."));
            // the most first, whatever order it came aboard in; under ten tonnes to a tenth
            hold.Add(Mineral.Gold, 0.3f);
            hold.Add(Mineral.Nickel, 14f);
            hold.Add(Mineral.Silicates, 166f);
            hold.Add(Mineral.Titanium, 6.4f);
            hold.Add(Mineral.Magnesium, 27f);
            hold.Add(Mineral.Aluminium, 12f);
            hold.Add(Mineral.Copper, 2f);
            line = CoopBot.CargoLine(hold);
            Assert.That(line, Does.Contain(": 166 t Silicates, 120 t Iron, 27 t Magnesium, 14 t Nickel, 12 t Aluminium, 3 more (8.7 t)"), line);
            Assert.That(CoopBot.CargoLine(hold, 8), Does.Contain("6.4 t Titanium, 2 t Copper, 0.3 t Gold"));
        }

        [Test]
        public void JsonStringsAreEscaped()
        {
            Assert.That(CoopBot.Q("a \"b\"\\\n\u0001"), Is.EqualTo("\"a \\\"b\\\"\\\\\\n\\u0001\""));
            Assert.That(CoopBot.Q(null), Is.EqualTo("null"));
        }

        Transform Ship(string name, Vector3 at, string faction)
        {
            var go = new GameObject(name);
            made.Add(go);
            go.transform.position = at;
            var info = go.AddComponent<TargetInfo>();
            info.displayName = name;
            info.faction = faction;
            return go.transform;
        }

        [Test]
        public void ItPicksTheNearestLivingEnemyAndFindsTargetsByName()
        {
            var near = Ship("Corsair Raider", new Vector3(0f, 0f, 1000f), "Hostile");
            var far = Ship("Corsair Frigate", new Vector3(0f, 0f, 4000f), "Hostile");
            var friend = Ship("Ochre Freighter", new Vector3(0f, 0f, 500f), "Neutral");
            Targets.Reset(new[] { far, friend, near });
            Assert.That(Wingman.NearestEnemy(Vector3.zero, 6000f), Is.SameAs(near), "not the friendly one, however near");
            Assert.That(Wingman.NearestEnemy(Vector3.zero, 800f), Is.Null, "none in range");
            Assert.That(Wingman.FindTarget("frigate", Vector3.zero), Is.SameAs(far));
            Assert.That(Wingman.FindTarget("CORSAIR", Vector3.zero), Is.SameAs(near), "the nearest that matches");
            Assert.That(Wingman.FindTarget("dreadnought", Vector3.zero), Is.Null);
            Assert.That(Wingman.NameOf(friend), Is.EqualTo("Ochre Freighter"));
        }

        [Test]
        public void TheCommsLineTakesTheKeysFromTheGame()
        {
            Assert.That(GameMenu.IsOpen, Is.False);
            Assert.That(InputFocus.Take(InputFocus.Panel.Chat), Is.True);
            Assert.That(GameMenu.IsOpen, Is.True, "the ship's controls read no keys while typing");
            Assert.That(InputFocus.Take(InputFocus.Panel.PauseMenu), Is.False, "nor does the pause menu open");
            InputFocus.Release(InputFocus.Panel.Chat);
            Assert.That(GameMenu.JustClosed, Is.True, "the Enter or Esc that closed it isn't read by the game");
        }
    }
}
