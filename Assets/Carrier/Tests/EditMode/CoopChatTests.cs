using System.Collections.Generic;
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
            Assert.That(bot.Orders, Is.Zero);
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
