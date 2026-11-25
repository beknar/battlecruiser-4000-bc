using System.Collections.Generic;
using System.Linq;
using CarrierGame.Demo;
using NUnit.Framework;
using UnityEngine;

namespace CarrierGame.Tests
{
    /// <summary>The AI wingman's Salvage order, out of play: the orders read ("salvage",
    /// "scavenge", "salvage the wreck of the Fang Hunter", "give me the parts"), the wreck's name
    /// taken out of what was said, the doing line, the sale everything aboard goes in
    /// (IndustryPanel.SellCargo, with the parts kept for a player), and the parts an order takes.
    /// The loop itself, against wrecks, is WingmanSalvagePlayTests' (PlayMode).</summary>
    public class WingmanSalvageTests
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

        // ------------------------------------------------------------ the orders

        [TestCase("salvage", "")]
        [TestCase("Scavenge!", "")]
        [TestCase("salvage the wrecks", "")]
        [TestCase("salvage that wreck", "")]
        [TestCase("salvage the wreckage", "")]
        [TestCase("start salvaging", "")]
        [TestCase("go salvaging", "")]
        [TestCase("can you salvage what's left please", "")]
        [TestCase("scavenge for parts", "")]
        [TestCase("strip the wreck", "")]
        [TestCase("salvage the wreck of the Fang Hunter", "Fang Hunter")]
        [TestCase("salvage the Pirate Cruiser", "Pirate Cruiser")]
        [TestCase("salvage the Skink's wreck", "Skink")]
        [TestCase("scavenge the Fang Hunter wreck", "Fang Hunter")]
        public void TheSalvageOrdersAreRead(string said, string wreck)
        {
            var p = CoopBot.ParseOrder(said);
            Assert.That(p.HasValue, Is.True, said);
            Assert.That(p.Value, Is.EqualTo(("salvage", wreck)), said);
        }

        [Test]
        public void OtherThingsAreNotSalvage()
        {
            Assert.That(CoopBot.ParseOrder("attack the Scavenger").Value, Is.EqualTo(("attack", "Scavenger")), "an enemy called a scavenger");
            Assert.That(CoopBot.ParseOrder("scavenger"), Is.Null, "a whole word only");
            Assert.That(CoopBot.ParseOrder("salvaged anything?"), Is.Null);
            Assert.That(CoopBot.ParseOrder("mine").Value.kind, Is.EqualTo("mine"));
        }

        [TestCase("give me the parts", "all parts")]
        [TestCase("give me your parts", "all parts")]
        [TestCase("hand over the ship parts", "all parts")]
        [TestCase("give Ann the parts", "all parts to Ann")]
        [TestCase("give me 2 parts", "2 parts")]
        public void GiveMeThePartsIsACargoOrder(string said, string args)
        {
            Assert.That(CoopBot.CargoArgs(said, false), Is.EqualTo(args), said);
            Assert.That(CoopBot.ParseOrder(said).Value, Is.EqualTo(("giveore", args)), said);
        }

        [Test]
        public void SalvageIsAnOrderOfAChainAndSaysItWithoutAShip()
        {
            Assert.That(string.Join(" | ", CoopBot.Clauses("salvage, then follow me").Select(c => c.kind)), Is.EqualTo("salvage | follow"));
            var bot = new GameObject("bot").AddComponent<CoopBot>();
            made.Add(bot.gameObject);
            Assert.That(bot.Command("salvage", 2), Is.EqualTo("not ready yet"));
            Assert.That(bot.Command("salvage the wrecks and sell", 2), Is.EqualTo("not ready yet"), "salvaging (it sells when full)");
            Assert.That(bot.Command("give me the parts", 2), Is.EqualTo("not ready yet"));
            Assert.That(bot.Command("help", 0), Does.Contain("salvage").And.Contain("the parts"), "the help lists them");
            Assert.That(WingmanReplies.Get("help").Length + "Wingman: ".Length, Is.LessThanOrEqualTo(CoopChat.MaxLength), "the help fits one message");
            Assert.That(bot.Orders, Is.Zero);
            Assert.That(bot.Pending, Is.Zero);
        }

        [Test]
        public void TheSalvageOrdersCanBeSpokenAndAreInTheKeywordList()
        {
            var bots = new List<string> { "Wingman" };
            var phrases = VoiceCommand.Phrases(bots, new string[0]);
            foreach (var said in new[] { "salvage", "scavenge", "salvage the wrecks", "salvage that wreck", "give me the parts" })
            {
                string sent = VoiceCommand.ToChat(said, bots);
                Assert.That(VoiceCommand.IsOrder(sent, bots), Is.True, $"\"{said}\" sent as \"{sent}\"");
                Assert.That(phrases, Does.Contain("Wingman " + said));
            }
        }

        [TestCase("the wreck of the Fang Hunter", "Fang Hunter")]
        [TestCase("Wreck of Pirate Cruiser", "Pirate Cruiser")]
        [TestCase("the Skink's wreck", "Skink")]
        [TestCase("Fang Hunter wreckage", "Fang Hunter")]
        [TestCase("the Corsair", "Corsair")]
        [TestCase("", "")]
        public void AWrecksNameIsWhatItWas(string said, string name)
        {
            Assert.That(Wingman.WreckName(said), Is.EqualTo(name));
        }

        [Test]
        public void TheLineSaysWhichWreckOrWhereItSells()
        {
            Assert.That(CoopBot.SalvageLine(Wingman.SalvageStep.Strip, "Pirate Cruiser", null, 64.4f), Is.EqualTo("salvaging the wreck of Pirate Cruiser: 64 t aboard"));
            Assert.That(CoopBot.SalvageLine(Wingman.SalvageStep.ToWreck, "Fang Hunter", null, 0f), Is.EqualTo("on the way to the wreck of Fang Hunter: 0 t aboard"));
            Assert.That(CoopBot.SalvageLine(Wingman.SalvageStep.Find, null, null, -1f), Is.EqualTo("looking for wrecks to salvage"));
            Assert.That(CoopBot.SalvageLine(Wingman.SalvageStep.Docking, "Fang Hunter", "Kestrel Station", 600f), Is.EqualTo("selling salvage at Kestrel Station: 600 t aboard"));
            Assert.That(CoopBot.SalvageLine(Wingman.SalvageStep.ToMarket, null, null, 600f), Is.EqualTo("selling salvage at the nearest market: 600 t aboard"));
        }

        // ------------------------------------------------------------ the sale

        [Test]
        public void TheSaleSellsEverythingAboardButThePartsKept()
        {
            var table = IndustryTable.Get();
            var hold = Hold();
            hold.Load("SteelPlate", 30f);
            hold.Load("CopperWire", 4f);
            hold.Load("ArmourPlating", 2f);
            hold.Add(Mineral.Iron, 10f);
            float credits0 = hold.Credits;
            float worth = 30f * table.Value("SteelPlate") + 4f * table.Value("CopperWire") + 2f * table.Value("ArmourPlating") + 10f * Minerals.Value(Mineral.Iron);
            float credits = IndustryPanel.SellCargo(hold, 0f, out string sold, out float tonnes);
            Assert.That(credits, Is.EqualTo(worth).Within(0.5f));
            Assert.That(hold.Credits - credits0, Is.EqualTo(worth).Within(0.5f), "earned into the hold");
            Assert.That(hold.Used, Is.EqualTo(0f).Within(1e-3f), "nothing kept");
            Assert.That(tonnes, Is.EqualTo(30f + 4f + 2f * table.Mass("ArmourPlating", 1f) + 10f).Within(0.01f));
            Assert.That(sold, Is.EqualTo("10 t of ore, 34 t of goods and 2 parts"));

            // (parts kept for a player: as many whole ones as the tonnes kept back allow)
            hold.Load("ArmourPlating", 3f);
            hold.Load("SteelPlate", 5f);
            IndustryPanel.SellCargo(hold, table.Mass("ArmourPlating", 1f) * 2.5f, out sold, out _);
            Assert.That(hold.Amount("ArmourPlating"), Is.EqualTo(2f), "two whole parts in two and a half parts' tonnes");
            Assert.That(hold.Amount("SteelPlate"), Is.Zero);
            Assert.That(sold, Is.EqualTo("5 t of goods and 1 part"));

            Assert.That(IndustryPanel.SellCargo(Hold(), 0f, out sold, out tonnes), Is.Zero);
            Assert.That(sold, Is.EqualTo(""));
            Assert.That(tonnes, Is.Zero);
        }

        [Test]
        public void AScreensSaleOfOneThingIsTheSame()
        {
            var table = IndustryTable.Get();
            var hold = Hold();
            hold.Load("AluminiumSheet", 12f);
            float credits = IndustryPanel.SellFrom(hold, "AluminiumSheet", 10f, out float sold);
            Assert.That(sold, Is.EqualTo(10f).Within(1e-3f));
            Assert.That(credits, Is.EqualTo(10f * table.Value("AluminiumSheet")).Within(0.01f));
            Assert.That(hold.Amount("AluminiumSheet"), Is.EqualTo(2f).Within(1e-3f));
            Assert.That(IndustryPanel.SellFrom(null, "AluminiumSheet", 10f, out sold), Is.Zero);
        }

        [Test]
        public void AnOrderForThePartsTakesTheWholeParts()
        {
            var hold = Hold();
            hold.Load("ArmourPlating", 3f);
            hold.Load("VectoringJets", 1f);
            hold.Load("SteelPlate", 20f);
            var all = CoopCargo.Wanted(hold, "all", "parts", out _);
            Assert.That(all.Select(x => (x.id, x.a)).OrderBy(x => x.id), Is.EqualTo(new[] { ("ArmourPlating", 3f), ("VectoringJets", 1f) }), "the parts, not the steel");
            Assert.That(CoopCargo.Total(CoopCargo.Wanted(hold, "2", "parts", out _)), Is.EqualTo(2f));
            Assert.That(CoopCargo.Total(CoopCargo.Wanted(hold, "half", "parts", out _)), Is.EqualTo(1f), "half of three, rounded down, and none of one");
            Assert.That(CoopCargo.Wanted(Hold(), "all", "parts", out string why), Is.Null);
            Assert.That(why, Is.EqualTo("no parts aboard"));
            Assert.That(Wingman.PartsTonnes(hold), Is.EqualTo(3f * IndustryTable.Get().Mass("ArmourPlating", 1f) + IndustryTable.Get().Mass("VectoringJets", 1f)).Within(0.01f));
        }

        [Test]
        public void TheSameSalvageOrderTwiceCarriesOn()
        {
            var go = new GameObject("wingman");
            made.Add(go);
            var w = go.AddComponent<Wingman>();
            w.SalvageWrecks(null);
            Assert.That(w.Current, Is.EqualTo(Wingman.Order.Salvage));
            Assert.That(w.SalvagingStep, Is.EqualTo(Wingman.SalvageStep.Find));
            typeof(Wingman).GetProperty(nameof(Wingman.SalvagingStep)).SetValue(w, Wingman.SalvageStep.Strip);
            w.SalvageWrecks(null);
            Assert.That(w.SalvagingStep, Is.EqualTo(Wingman.SalvageStep.Strip), "given again: carried on");
            w.SalvageWrecks(null, "Fang Hunter");
            Assert.That(w.SalvagingStep, Is.EqualTo(Wingman.SalvageStep.Find), "another wreck named: a new run");
            Assert.That(w.SalvageNamed, Is.EqualTo("Fang Hunter"));
            w.Hold();
            Assert.That(w.Current, Is.EqualTo(Wingman.Order.Hold));
        }
    }
}
