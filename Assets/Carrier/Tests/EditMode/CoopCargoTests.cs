using System.Collections.Generic;
using System.Linq;
using CarrierGame.Demo;
using NUnit.Framework;
using UnityEngine;

namespace CarrierGame.Tests
{
    /// <summary>Ore passed between co-op games (CoopCargo): the orders ("give me your ore", "take
    /// my ore") read, what an order takes out of a hold, the hold's arithmetic (a partial
    /// receipt, cargo coming back, a receipt that says too much), the refund when no receipt
    /// comes, and who may hand cargo to whom (a person and an AI wingman only, in one level,
    /// within 50 km, and no message older than 6 s).</summary>
    public class CoopCargoTests
    {
        readonly List<GameObject> made = new List<GameObject>();

        [SetUp]
        public void Begin() => CoopCargo.ResetStatics();

        [TearDown]
        public void Clear()
        {
            foreach (var go in made) if (go != null) Object.DestroyImmediate(go);
            made.Clear();
            CoopCargo.ResetStatics();
        }

        CargoHold Hold(float capacity = 600f)
        {
            var go = new GameObject("hold");
            made.Add(go);
            var hold = go.AddComponent<CargoHold>();
            hold.capacity = capacity;
            return hold;
        }

        static CoopItem Item(string id, float a) => new CoopItem { id = id, a = a };
        static float Of(List<CoopItem> list, string id) => list.Where(x => x.id == id).Sum(x => x.a);

        // ------------------------------------------------------------ the orders

        [TestCase("give me your ore", "all ore")]
        [TestCase("give me all your ore", "all ore")]
        [TestCase("give me the platinum", "all Platinum")]
        [TestCase("give me 100 t of iron", "100 Iron")]
        [TestCase("give me 100t of iron ore", "100 Iron")]
        [TestCase("Give me 1,500 tonnes of nickel.", "1500 Nickel")]
        [TestCase("hand over half your gold", "half Gold")]
        [TestCase("give me some iron", "? Iron")]
        [TestCase("give me everything", "all ore")]
        [TestCase("transfer the ore", "all ore")]
        [TestCase("send your helium 3 to Ann", "all Helium3 to Ann")]
        [TestCase("give Ann the iron", "all Iron to Ann")]
        [TestCase("give us 50 t of Pt", "50 Platinum")]
        public void OrdersToHandOreOverAreRead(string said, string args)
        {
            Assert.That(CoopBot.CargoArgs(said, false), Is.EqualTo(args), said);
            var p = CoopBot.ParseOrder(said);
            Assert.That(p.HasValue && p.Value.kind == "giveore" && p.Value.arg == args, Is.True, $"{said}: {p}");
        }

        [TestCase("take my ore", "all ore")]
        [TestCase("take the iron", "all Iron")]
        [TestCase("take 50 t of nickel", "50 Nickel")]
        [TestCase("take all my platinum please", "all Platinum")]
        [TestCase("take half my cargo", "half ore")]
        public void OrdersToTakeOreAreRead(string said, string args)
        {
            var p = CoopBot.ParseOrder(said);
            Assert.That(p.HasValue && p.Value.kind == "take" && p.Value.arg == args, Is.True, $"{said}: {p}");
        }

        [Test]
        public void OtherThingsAreNotOreOrders()
        {
            foreach (var said in new[] { "give me a hand", "send help", "give me your earnings report", "take a break", "take it easy", "give me all", "transfer" })
            {
                Assert.That(CoopBot.CargoArgs(said, false), Is.Null, said);
                Assert.That(CoopBot.CargoArgs(said, true), Is.Null, said);
            }
            // (still what they were)
            Assert.That(CoopBot.ParseOrder("take out the Scavenger").Value, Is.EqualTo(("attack", "Scavenger")));
            Assert.That(CoopBot.ParseOrder("give me 500 credits").Value, Is.EqualTo(("give", "500")));
            Assert.That(CoopBot.ParseOrder("transfer 2k credits to Ann").Value, Is.EqualTo(("give", "2000 to Ann")));
            Assert.That(CoopBot.ParseOrder("what's in your hold").Value.kind, Is.EqualTo("cargo"));
            Assert.That(string.Join(" | ", CoopBot.Clauses("take my ore, then mine").Select(c => c.kind)), Is.EqualTo("take | mine"));
        }

        [Test]
        public void WithoutAShipTheOreOrdersSaySo()
        {
            var bot = new GameObject("bot").AddComponent<CoopBot>();
            made.Add(bot.gameObject);
            Assert.That(bot.Command("give me your ore", 2), Is.EqualTo("not ready yet"));
            Assert.That(bot.Command("take my ore", 2), Is.EqualTo("not ready yet"));
            Assert.That(bot.Command("help", 0), Does.Contain("give me [N t of] your ore").And.Contain("take my ore"));
            Assert.That(bot.Orders, Is.Zero);
        }

        [Test]
        public void ACargoTargetIsTakenApart()
        {
            CoopCargo.SplitCargo("100 Iron", out string a, out string item, out string to);
            Assert.That((a, item, to), Is.EqualTo(("100", "Iron", "")));
            CoopCargo.SplitCargo("all ore to Ann the Bold", out a, out item, out to);
            Assert.That((a, item, to), Is.EqualTo(("all", "ore", "Ann the Bold")));
            CoopCargo.SplitCargo("helium 3", out a, out item, out to);
            Assert.That((a, item, to), Is.EqualTo(("all", "helium 3", "")), "as the API may give it");
            CoopCargo.SplitCargo("", out a, out item, out to);
            Assert.That((a, item, to), Is.EqualTo(("all", "ore", "")));
        }

        [Test]
        public void TheOrderToTakeGoesToTheWingmanNamed()
        {
            var w1 = new CoopPlayer { id = 3, name = "Wingman", client = CoopBot.ClientName };
            var w2 = new CoopPlayer { id = 4, name = "Wingman 2", client = CoopBot.ClientName };
            var both = new List<CoopPlayer> { w1, w2 };
            var to = CoopCargo.TakeOrderTo("Wingman 2, take my ore", both, out string arg);
            Assert.That(to.Select(p => p.id), Is.EqualTo(new[] { 4 }), "not \"Wingman\", whose order would be \"2, take my ore\"");
            Assert.That(arg, Is.EqualTo("all ore"));
            to = CoopCargo.TakeOrderTo("Hey Wingman, take 50 t of iron and then mine", both, out arg);
            Assert.That(to.Select(p => p.id), Is.EqualTo(new[] { 3 }));
            Assert.That(arg, Is.EqualTo("50 Iron"));
            Assert.That(CoopCargo.TakeOrderTo("all ships, take the iron", both, out arg).Count, Is.EqualTo(2), "all of them (the nearest is chosen)");
            Assert.That(CoopCargo.TakeOrderTo("Wingman, mine", both, out _), Is.Null);
            Assert.That(CoopCargo.TakeOrderTo("Wingman, take out the Skink", both, out _), Is.Null);
            Assert.That(CoopCargo.TakeOrderTo("take my ore", both, out _), Is.Null, "to nobody by name");
        }

        // ------------------------------------------------------------ what an order takes

        [Test]
        public void AnOrderTakesWhatIsAboard()
        {
            var hold = Hold();
            hold.Add(Mineral.Iron, 120f);
            hold.Add(Mineral.Nickel, 40f);
            hold.Add(Mineral.Gold, 2f);
            var all = CoopCargo.Wanted(hold, "all", "ore", out _);
            Assert.That(CoopCargo.Total(all), Is.EqualTo(162f).Within(0.01f));
            Assert.That(all.Select(x => x.id), Is.EqualTo(new[] { "Gold", "Iron", "Nickel" }), "the most valuable first");
            // (so many tonnes of ore: the most valuable first, up to that)
            var some = CoopCargo.Wanted(hold, "100", "ore", out _);
            Assert.That(CoopCargo.Total(some), Is.EqualTo(100f).Within(0.01f));
            Assert.That(Of(some, "Gold"), Is.EqualTo(2f).Within(0.01f));
            Assert.That(Of(some, "Iron"), Is.EqualTo(98f).Within(0.01f));
            Assert.That(Of(CoopCargo.Wanted(hold, "half", "iron", out _), "Iron"), Is.EqualTo(60f).Within(0.01f));
            Assert.That(Of(CoopCargo.Wanted(hold, "50", "Fe", out _), "Iron"), Is.EqualTo(50f).Within(0.01f));
            Assert.That(Of(CoopCargo.Wanted(hold, "500", "Iron", out _), "Iron"), Is.EqualTo(120f).Within(0.01f), "no more than there is");
            Assert.That(CoopCargo.Wanted(hold, "all", "platinum", out string why), Is.Null);
            Assert.That(why, Is.EqualTo("no platinum aboard"));
            Assert.That(CoopCargo.Wanted(hold, "?", "iron", out why), Is.Null);
            Assert.That(why, Is.EqualTo("how much?"));
            Assert.That(CoopCargo.Wanted(hold, "-5", "iron", out why), Is.Null);
            Assert.That(CoopCargo.Wanted(hold, "all", "dilithium", out why), Is.Null);
            Assert.That(CoopCargo.Wanted(Hold(), "all", "ore", out why), Is.Null);
            Assert.That(why, Is.EqualTo("no ore aboard"));
        }

        // ------------------------------------------------------------ the hold's arithmetic

        [Test]
        public void WhatThereIsNoRoomForIsLeft()
        {
            var hold = Hold(100f);
            hold.Add(Mineral.Silicates, 70f);
            var loaded = CoopCargo.PutIn(hold, new[] { Item("Iron", 50f) });
            Assert.That(Of(loaded, "Iron"), Is.EqualTo(30f).Within(0.01f), "as much as there's room for");
            Assert.That(hold.Full, Is.True);
            var left = CoopCargo.Left(new[] { Item("Iron", 50f) }, loaded);
            Assert.That(Of(left, "Iron"), Is.EqualTo(20f).Within(0.01f));
            // (an id it doesn't know, nothing, or a nonsense amount: loads nothing)
            Assert.That(CoopCargo.PutIn(Hold(), new[] { Item("Dilithium", 5f), Item("Iron", float.NaN), Item("Iron", -3f), null }), Is.Empty);
            // (a receipt that says more than was sent: all of it taken, none back)
            Assert.That(CoopCargo.Left(new[] { Item("Iron", 50f) }, new[] { Item("Iron", 80f) }), Is.Empty);
            Assert.That(Of(CoopCargo.Left(new[] { Item("Iron", 50f), Item("Gold", 1f) }, new[] { Item("Gold", 1f) }), "Iron"), Is.EqualTo(50f));
        }

        [Test]
        public void APartialReceiptPutsTheRestBack()
        {
            var hold = Hold();
            hold.Add(Mineral.Iron, 100f);
            var sent = CoopCargo.TakeOut(hold, new List<CoopItem> { Item("Iron", 60f), Item("Gold", 5f) });
            Assert.That(sent.Select(x => x.id), Is.EqualTo(new[] { "Iron" }), "none of what wasn't aboard");
            Assert.That(hold[Mineral.Iron], Is.EqualTo(40f).Within(0.01f));
            var g = CoopCargo.Hold(hold, 7, sent, 0f);
            Assert.That(CoopCargo.Outstanding, Is.EqualTo(1));
            Assert.That(CoopCargo.Receipt(8, g.id, new[] { Item("Iron", 40f) }, ""), Is.False, "only from whom it went to");
            List<CoopItem> taken = null, back = null;
            CoopCargo.Returned += (to, t, b, why) => { taken = t; back = b; };
            Assert.That(CoopCargo.Receipt(7, g.id, new[] { Item("Iron", 40f) }, ""), Is.True);
            Assert.That(Of(taken, "Iron"), Is.EqualTo(40f).Within(0.01f));
            Assert.That(Of(back, "Iron"), Is.EqualTo(20f).Within(0.01f));
            Assert.That(hold[Mineral.Iron], Is.EqualTo(60f).Within(0.01f), "the 20 t they had no room for, back");
            Assert.That(CoopCargo.Outstanding, Is.Zero);
            Assert.That(CoopCargo.Receipt(7, g.id, new CoopItem[0], ""), Is.False, "answered once");
            Assert.That(hold[Mineral.Iron], Is.EqualTo(60f).Within(0.01f));
        }

        [Test]
        public void ARefusalOrNoReceiptPutsItAllBack()
        {
            var hold = Hold();
            hold.Add(Mineral.Iron, 100f);
            var g = CoopCargo.Hold(hold, 7, CoopCargo.TakeOut(hold, new[] { Item("Iron", 30f) }), 0f);
            Assert.That(CoopCargo.Receipt(7, g.id, null, "it came 7.2 s late"), Is.True);
            Assert.That(hold[Mineral.Iron], Is.EqualTo(100f).Within(0.01f), "refused: all back");
            Assert.That(Notices.Last, Does.Contain("refused").And.Contain("back in the hold"));
            // no receipt: back after ReceiptWait, not before
            CoopCargo.Hold(hold, 7, CoopCargo.TakeOut(hold, new[] { Item("Iron", 30f) }), 0f);
            CoopCargo.Tick(CoopCargo.ReceiptWait - 1f);
            Assert.That(CoopCargo.Outstanding, Is.EqualTo(1));
            Assert.That(hold[Mineral.Iron], Is.EqualTo(70f).Within(0.01f));
            CoopCargo.Tick(CoopCargo.ReceiptWait + 0.1f);
            Assert.That(CoopCargo.Outstanding, Is.Zero);
            Assert.That(hold[Mineral.Iron], Is.EqualTo(100f).Within(0.01f), "back when no receipt came");
            Assert.That(Notices.Last, Does.Contain("No answer"));
            // (a refund can't be received too: the receiver refuses what's older than this)
            Assert.That(CoopCargo.TakeWithin, Is.LessThan(CoopCargo.ReceiptWait));
        }

        // ------------------------------------------------------------ who may

        [Test]
        public void OnlyAPersonAndAnAiWingmanInOneLevelNearEachOther()
        {
            Assert.That(CoopCargo.Refusal(false, true, true, 2000f), Is.Empty, "a person to the wingman");
            Assert.That(CoopCargo.Refusal(true, false, true, 2000f), Is.Empty, "the wingman to a person");
            Assert.That(CoopCargo.Refusal(false, false, true, 2000f), Does.Contain("AI wingman"), "person to person");
            Assert.That(CoopCargo.Refusal(true, true, true, 2000f), Does.Contain("among themselves"));
            Assert.That(CoopCargo.Refusal(false, true, false, 2000f), Does.Contain("part of space"));
            Assert.That(CoopCargo.Refusal(false, true, true, -1f), Does.Contain("can't see"));
            Assert.That(CoopCargo.Refusal(false, true, true, 49000f), Is.Empty);
            Assert.That(CoopCargo.Refusal(false, true, true, 51000f), Does.Contain("51 km").Or.Contain("51,000").Or.Contain("apart"), "beyond 50 km");
            // (the receiver allows a little more, the ships having moved meanwhile)
            Assert.That(CoopCargo.Refusal(false, true, true, 52000f, CoopCargo.ReachReceived, 0.3), Is.Empty);
            Assert.That(CoopCargo.Refusal(false, true, true, 2000f, CoopCargo.ReachReceived, 5.9), Is.Empty);
            Assert.That(CoopCargo.Refusal(false, true, true, 2000f, CoopCargo.ReachReceived, 6.5), Does.Contain("late"), "older than 6 s");
        }

        [Test]
        public void AnAiPlayerIsKeyedOrOnAnOpenServerSaysItIsABot()
        {
            var bot = new CoopPlayer { id = 3, name = "Wingman", client = CoopBot.ClientName };
            var person = new CoopPlayer { id = 2, name = "Ann", client = "game" };
            var keyed = new CoopPlayer { id = 4, name = "Ace", client = CoopBot.ClientName, ai = true };
            Assert.That(CoopCargo.IsAi(bot, new List<CoopPlayer> { bot, person }), Is.True, "an open server: it says so");
            Assert.That(CoopCargo.IsAi(person, new List<CoopPlayer> { bot, person }), Is.False);
            Assert.That(CoopCargo.IsAi(keyed, new List<CoopPlayer> { bot, person, keyed }), Is.True);
            Assert.That(CoopCargo.IsAi(bot, new List<CoopPlayer> { bot, person, keyed }), Is.False, "with keys in use, saying so isn't enough");
        }

        // ------------------------------------------------------------ on the wire

        [Test]
        public void CargoGoesOutByTheHoldsItemIds()
        {
            string json = JsonUtility.ToJson(new CargoMsg { t = CoopWire.Cargo, to = 3, gid = 9, ts = 12.5, items = new[] { Item("Iron", 50f), Item("SteelPlate", 4f) } });
            StringAssert.Contains("\"t\":\"cargo\"", json);
            StringAssert.Contains("\"id\":\"Iron\"", json);
            StringAssert.Contains("\"id\":\"SteelPlate\"", json);
            var m = JsonUtility.FromJson<CoopMsg>("{\"t\":\"cargook\",\"from\":3,\"gid\":9,\"items\":[{\"id\":\"Iron\",\"a\":30}],\"why\":\"\"}");
            Assert.That(m.t, Is.EqualTo(CoopWire.CargoReceipt));
            Assert.That(m.items.Length, Is.EqualTo(1));
            Assert.That(m.items[0].id, Is.EqualTo("Iron"));
            Assert.That(m.items[0].a, Is.EqualTo(30f));
            CollectionAssert.DoesNotContain(CoopWire.ServerOnly, CoopWire.Cargo, "games send it");
            CollectionAssert.DoesNotContain(CoopWire.ServerOnly, CoopWire.CargoReceipt);
        }

        [Test]
        public void CargoIsSaidPlainly()
        {
            Assert.That(CoopCargo.Describe(new[] { Item("Iron", 50f) }), Is.EqualTo("50 t of iron"));
            Assert.That(CoopCargo.Describe(new[] { Item("Iron", 80f), Item("Nickel", 40f) }), Is.EqualTo("120 t of ore (80 t of iron, 40 t of nickel)"));
            Assert.That(CoopCargo.Describe(new CoopItem[0]), Is.EqualTo("nothing"));
        }
    }
}
