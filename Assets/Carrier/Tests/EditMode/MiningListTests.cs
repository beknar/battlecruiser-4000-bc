using System.Collections.Generic;
using System.Linq;
using CarrierGame.Demo;
using NUnit.Framework;
using UnityEngine;

namespace CarrierGame.Tests
{
    /// <summary>The mining list's order (O) and mineral filter (F): MiningMode.Arrange and
    /// MiningMode.MineralsIn, applied before the list is cut to its first 20.</summary>
    public class MiningListTests
    {
        /// <summary>A deposit for the tests: `at` metres from the laser.</summary>
        sealed class Rock : IMineralSource
        {
            public string Label { get; set; } = "rock";
            public Share[] Mix { get; set; }
            public float Remaining { get; set; }
            public float Initial { get; set; }
            public bool Depleted => Remaining <= 0f;
            public Vector3 Position { get; set; }
            public float Radius => 0f;
            public bool Valid { get; set; } = true;
            public bool Discovered { get; set; } = true;
            public float Take(float tonnes) { float t = Mathf.Min(tonnes, Remaining); Remaining -= t; return t; }
        }

        static Rock R(string name, float at, float initial, float remaining, params (Mineral m, float f)[] mix) => new Rock
        {
            Label = name,
            Position = new Vector3(at, 0f, 0f),
            Initial = initial,
            Remaining = remaining,
            Mix = mix.Select(x => new Share(x.m, x.f)).ToArray(),
        };

        static float Reach(IMineralSource s) => s.Position.magnitude;

        static string Names(IEnumerable<IMineralSource> list) => string.Join(", ", list.Select(s => s.Label));

        [TearDown]
        public void Restore() => LaunchArgs.TestArgs = null;

        [Test]
        public void NearestFirstIsByTheLasersReach()
        {
            var finds = new IMineralSource[] { R("far", 3000f, 100f, 100f, (Mineral.Iron, 1f)), R("near", 500f, 100f, 100f, (Mineral.Iron, 1f)),
                                               R("middle", 1500f, 100f, 100f, (Mineral.Iron, 1f)) };
            Assert.That(Names(MiningMode.Arrange(finds, Reach, DepositOrder.Nearest, null)), Is.EqualTo("near, middle, far"));
        }

        [Test]
        public void MostValuableIsCreditsATonneTimesTheTonnesItStartedWith()
        {
            // a big iron rock nearly mined out (12 cr/t x 10 000 t it started with: 120 000 cr)
            // against a small rich one (gold 10%: about 600 cr/t x 100 t: 60 000 cr)
            var iron = R("iron", 900f, 10000f, 10f, (Mineral.Iron, 1f));
            var gold = R("gold", 300f, 100f, 100f, (Mineral.Silicates, 0.9f), (Mineral.Gold, 0.1f));
            var finds = new IMineralSource[] { gold, iron };
            Assert.That(Names(MiningMode.Arrange(finds, Reach, DepositOrder.MostValuable, null)), Is.EqualTo("iron, gold"),
                        "by the tonnes it started with, not what is left (the rows don't move while the laser cuts)");
            Assert.That(Names(MiningMode.Arrange(finds, Reach, DepositOrder.Richest, null)), Is.EqualTo("gold, iron"), "richest: credits a tonne");
            Assert.That(MiningMode.PerTonne(gold.Mix, null), Is.EqualTo(Minerals.ValuePerTonne(gold.Mix)).Within(1e-3f));
        }

        [Test]
        public void TheOrderComesBeforeTheCutToTwenty()
        {
            // thirty cheap rocks close by, and one rich rock beyond them all
            var finds = new List<IMineralSource>();
            for (int i = 0; i < 30; i++) finds.Add(R($"stone {i}", 200f + i * 10f, 500f, 500f, (Mineral.Silicates, 1f)));
            var prize = R("prize", 20000f, 500f, 500f, (Mineral.Silicates, 0.5f), (Mineral.Platinum, 0.5f));
            finds.Add(prize);
            Assert.That(MiningMode.Arrange(finds, Reach, DepositOrder.Nearest, null).Take(MiningMode.MaxListed), Has.No.Member(prize),
                        "nearest first, the far rock is past the cut");
            Assert.That(MiningMode.Arrange(finds, Reach, DepositOrder.MostValuable, null).First(), Is.SameAs(prize), "most valuable first, it heads the list");
            Assert.That(MiningMode.Arrange(finds, Reach, DepositOrder.Richest, null).First(), Is.SameAs(prize), "and richest first");
        }

        [Test]
        public void EqualWorthGoesNearestFirst()
        {
            var finds = new IMineralSource[] { R("b", 800f, 100f, 100f, (Mineral.Nickel, 1f)), R("a", 400f, 100f, 100f, (Mineral.Nickel, 1f)),
                                               R("c", 1200f, 100f, 100f, (Mineral.Nickel, 1f)) };
            Assert.That(Names(MiningMode.Arrange(finds, Reach, DepositOrder.MostValuable, null)), Is.EqualTo("a, b, c"));
            Assert.That(Names(MiningMode.Arrange(finds, Reach, DepositOrder.Richest, null)), Is.EqualTo("a, b, c"));
        }

        [Test]
        public void GoneAndMinedOutDepositsAreLeftOutAndEachIsListedOnce()
        {
            var good = R("good", 100f, 50f, 50f, (Mineral.Iron, 1f));
            var gone = R("gone", 200f, 50f, 50f, (Mineral.Iron, 1f));
            gone.Valid = false;
            var empty = R("empty", 300f, 50f, 0f, (Mineral.Iron, 1f));
            var finds = new IMineralSource[] { good, null, gone, empty, good };
            foreach (DepositOrder o in System.Enum.GetValues(typeof(DepositOrder)))
                Assert.That(Names(MiningMode.Arrange(finds, Reach, o, null)), Is.EqualTo("good"), o.ToString());
            Assert.That(MiningMode.MineralsIn(new IMineralSource[] { gone, empty, null }), Is.Empty);
        }

        [Test]
        public void AFilterListsOnlyTheDepositsWithThatMineral()
        {
            var a = R("a", 100f, 100f, 100f, (Mineral.Iron, 0.99f), (Mineral.Gold, 0.01f));
            var b = R("b", 200f, 100f, 100f, (Mineral.Iron, 1f));
            var c = R("c", 300f, 100f, 100f, (Mineral.Silicates, 0.95f), (Mineral.Gold, 0.05f));
            var finds = new IMineralSource[] { a, b, c };
            Assert.That(Names(MiningMode.Arrange(finds, Reach, DepositOrder.Nearest, Mineral.Gold)), Is.EqualTo("a, c"));
            Assert.That(MiningMode.Arrange(finds, Reach, DepositOrder.Nearest, Mineral.Platinum), Is.Empty, "none with platinum");
            Assert.That(MiningMode.Holds(b, Mineral.Gold), Is.False);
            Assert.That(MiningMode.Holds(c, Mineral.Gold), Is.True);
        }

        [Test]
        public void WithAFilterTheWorthIsThatMinerals()
        {
            // a little gold in a diamond-rich rock, against a rock with more gold and nothing else
            // of worth: richest by the whole mix is the diamonds, by gold it is the gold
            var diamonds = R("diamonds", 100f, 100f, 100f, (Mineral.Silicates, 0.94f), (Mineral.Diamonds, 0.05f), (Mineral.Gold, 0.01f));
            var gold = R("gold", 200f, 100f, 100f, (Mineral.Silicates, 0.95f), (Mineral.Gold, 0.05f));
            var finds = new IMineralSource[] { diamonds, gold };
            Assert.That(Names(MiningMode.Arrange(finds, Reach, DepositOrder.Richest, null)), Is.EqualTo("diamonds, gold"));
            Assert.That(Names(MiningMode.Arrange(finds, Reach, DepositOrder.Richest, Mineral.Gold)), Is.EqualTo("gold, diamonds"));
            Assert.That(Names(MiningMode.Arrange(finds, Reach, DepositOrder.MostValuable, Mineral.Gold)), Is.EqualTo("gold, diamonds"));
            Assert.That(MiningMode.PerTonne(gold.Mix, Mineral.Gold), Is.EqualTo(0.05f * Minerals.Value(Mineral.Gold)).Within(1e-3f));
            Assert.That(MiningMode.PerTonne(gold.Mix, Mineral.Platinum), Is.EqualTo(0f));
        }

        [Test]
        public void TheFilterGoesThroughTheMineralsFoundMostValuableFirst()
        {
            var finds = new IMineralSource[]
            {
                R("a", 100f, 100f, 100f, (Mineral.Iron, 0.7f), (Mineral.Nickel, 0.3f)),
                R("b", 200f, 100f, 100f, (Mineral.Silicates, 0.9f), (Mineral.Platinum, 0.1f)),
                R("c", 300f, 100f, 100f, (Mineral.Iron, 0.5f), (Mineral.Water, 0.5f)),
            };
            Assert.That(MiningMode.MineralsIn(finds),
                        Is.EqualTo(new[] { Mineral.Platinum, Mineral.Nickel, Mineral.Iron, Mineral.Water, Mineral.Silicates }),
                        "each mineral once, the most credits a tonne first");
        }

        [Test]
        public void TestsKeepTheirOwnSettings()
        {
            LaunchArgs.TestArgs = new[] { "Game.exe" };
            string orderKey = MiningMode.OrderKey, filterKey = MiningMode.FilterKey;
            LaunchArgs.TestArgs = new[] { "Game.exe", "-systemtest" };
            Assert.That(MiningMode.OrderKey, Is.Not.EqualTo(orderKey), "a test never picks up the player's order");
            Assert.That(MiningMode.FilterKey, Is.Not.EqualTo(filterKey), "nor the player's mineral");
        }
    }
}
