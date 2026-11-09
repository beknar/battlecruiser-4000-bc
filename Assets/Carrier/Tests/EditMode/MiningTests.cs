using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CarrierGame.Tests
{
    /// <summary>The minerals: the table, what bodies are made of, and pricing a mix.</summary>
    public class MineralsTests
    {
        static void AssertGoodMix(Share[] mix, string what)
        {
            Assert.That(mix, Is.Not.Empty, what);
            Assert.That(mix.Sum(s => s.fraction), Is.EqualTo(1f).Within(1e-4f), $"{what}: the shares add up to the whole");
            Assert.That(mix.All(s => s.fraction > 0f), what);
            for (int i = 1; i < mix.Length; i++)
                Assert.That(mix[i].fraction, Is.LessThanOrEqualTo(mix[i - 1].fraction), $"{what}: biggest share first");
            Assert.That(mix.Select(s => s.mineral).Distinct().Count(), Is.EqualTo(mix.Length), $"{what}: each mineral once");
        }

        [Test]
        public void EveryMineralHasANameASymbolAndAPrice()
        {
            var all = (Mineral[])Enum.GetValues(typeof(Mineral));
            Assert.That(Minerals.Count, Is.EqualTo(all.Length));
            foreach (var m in all)
            {
                Assert.That(Minerals.Name(m), Is.Not.Empty);
                Assert.That(Minerals.Value(m), Is.GreaterThan(0f), m.ToString());
                StringAssert.StartsWith("#", Minerals.Hex(m));
            }
            Assert.That(all.Select(Minerals.Symbol).Distinct().Count(), Is.EqualTo(all.Length), "symbols are unique");
        }

        [Test]
        public void EveryKindOfBodyIsAWholeMix([Values] BodyClass body)
        {
            for (int seed = 1; seed <= 50; seed++)
            {
                var mix = Minerals.ForBody(body, seed, out string kind);
                Assert.That(kind, Is.Not.Empty);
                AssertGoodMix(mix, $"{body} seed {seed}");
            }
        }

        [Test]
        public void EveryKindOfSurfaceIsAWholeMix([Values] SurfaceKind surface, [Values] bool wet)
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                var mix = Minerals.ForSurface(surface, seed, wet, out string what);
                Assert.That(what, Is.Not.Empty);
                AssertGoodMix(mix, $"{surface} seed {seed}");
            }
        }

        [Test]
        public void TheSameSeedMakesTheSameMix()
        {
            var a = Minerals.ForBody(BodyClass.Asteroid, 1234, out string ka);
            var b = Minerals.ForBody(BodyClass.Asteroid, 1234, out string kb);
            Assert.That(ka, Is.EqualTo(kb));
            Assert.That(a.Select(s => (s.mineral, s.fraction)), Is.EqualTo(b.Select(s => (s.mineral, s.fraction))));
        }

        [Test]
        public void AsteroidsComeInAllThreeTypesInRoughlyTheRightNumbers()
        {
            var counts = new Dictionary<string, int>();
            const int n = 2000;
            for (int seed = 1; seed <= n; seed++)
            {
                Minerals.ForBody(BodyClass.Asteroid, seed, out string kind);
                counts[kind] = counts.TryGetValue(kind, out int c) ? c + 1 : 1;
            }
            Assert.That(counts.Keys, Is.EquivalentTo(new[] { "C-type asteroid (carbonaceous)", "S-type asteroid (stony)", "M-type asteroid (metallic)" }));
            Assert.That(counts["C-type asteroid (carbonaceous)"] / (float)n, Is.EqualTo(0.45f).Within(0.05f));
            Assert.That(counts["S-type asteroid (stony)"] / (float)n, Is.EqualTo(0.40f).Within(0.05f));
            Assert.That(counts["M-type asteroid (metallic)"] / (float)n, Is.EqualTo(0.15f).Within(0.05f));
        }

        [Test]
        public void ARockOnASurfaceIsCalledARock()
        {
            Minerals.ForSurface(SurfaceKind.Asteroid, 7, false, out string what);
            StringAssert.Contains("rock", what);
            StringAssert.DoesNotContain("asteroid", what);
        }

        [Test]
        public void ASeaBedIsBrineAndAnIslandIsOre()
        {
            Minerals.ForSurface(SurfaceKind.Ocean, 3, true, out string wet);
            Minerals.ForSurface(SurfaceKind.Ocean, 3, false, out string dry);
            Assert.That(wet, Is.EqualTo("heavy-water brine"));
            Assert.That(dry, Is.EqualTo("island ore body"));
        }

        [Test]
        public void ThePrizeIsTheMostValuableMineralThatIsReallyThere()
        {
            var mix = new[] { new Share(Mineral.Iron, 0.6f), new Share(Mineral.Platinum, 0.01f), new Share(Mineral.Diamonds, 0.001f) };
            Assert.That(Minerals.Prize(mix), Is.EqualTo(Mineral.Platinum), "a trace of diamonds below the cut-off doesn't count");
            Assert.That(Minerals.Prize(mix, 0.0005f), Is.EqualTo(Mineral.Diamonds));
            Assert.That(Minerals.Prize(Array.Empty<Share>()), Is.EqualTo(Mineral.Silicates));
        }

        [Test]
        public void AMixIsWorthItsSharesPrices()
        {
            var mix = new[] { new Share(Mineral.Iron, 0.5f), new Share(Mineral.Gold, 0.5f) };
            Assert.That(Minerals.ValuePerTonne(mix), Is.EqualTo(0.5f * Minerals.Value(Mineral.Iron) + 0.5f * Minerals.Value(Mineral.Gold)).Within(1e-3f));
        }

        [Test]
        public void TheSummaryShowsTheBiggestSharesThenThePreciousOnes()
        {
            var mix = new[] { new Share(Mineral.Iron, 0.55f), new Share(Mineral.Nickel, 0.3f), new Share(Mineral.Silicates, 0.144f), new Share(Mineral.Platinum, 0.006f) };
            Assert.That(Minerals.Summary(mix, 3, false), Is.EqualTo("Fe 55%  Ni 30%  Si 14%  Pt 0.6%"));
            Assert.That(Minerals.Summary(mix, 1, false), Is.EqualTo("Fe 55%  Pt 0.6%"));
            StringAssert.Contains("<color=#", Minerals.Summary(mix));
        }

        [Test]
        public void ADepositsSeedComesFromWhereItIs()
        {
            var p = new Vector3(1234.4f, -50f, 99999f);
            int seed = MineralDeposit.PositionSeed(p);
            Assert.That(seed, Is.EqualTo(MineralDeposit.PositionSeed(p)));
            Assert.That(seed, Is.InRange(1, 1000003));
            var seeds = new HashSet<int>();
            for (int i = 0; i < 200; i++) seeds.Add(MineralDeposit.PositionSeed(new Vector3(i * 37f, i * -11f, i * 101f)));
            Assert.That(seeds.Count, Is.GreaterThan(190), "different places, different seeds");
            Assert.That(seeds.All(s => s >= 1 && s <= 1000003));
        }
    }

    /// <summary>The ore hold: loading, jettisoning and selling.</summary>
    public class CargoHoldTests
    {
        GameObject go;
        CargoHold hold;
        int changes;

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("Hold");
            hold = go.AddComponent<CargoHold>();
            hold.capacity = 100f;
            changes = 0;
            hold.Changed += () => changes++;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(go);

        [Test]
        public void ItLoadsUpToItsCapacity()
        {
            Assert.That(hold.Add(Mineral.Iron, 60f), Is.EqualTo(60f));
            Assert.That(hold.Add(Mineral.Gold, 60f), Is.EqualTo(40f), "only what there is room for");
            Assert.That(hold.Full, Is.True);
            Assert.That(hold.Free, Is.EqualTo(0f));
            Assert.That(hold.Add(Mineral.Iron, 5f), Is.EqualTo(0f));
            Assert.That(hold[Mineral.Iron], Is.EqualTo(60f));
            Assert.That(hold[Mineral.Gold], Is.EqualTo(40f));
            Assert.That(hold.TotalMined, Is.EqualTo(100f));
            Assert.That(changes, Is.EqualTo(2));
        }

        [Test]
        public void NothingNegativeGoesIn()
        {
            Assert.That(hold.Add(Mineral.Iron, -5f), Is.EqualTo(0f));
            Assert.That(hold.Used, Is.EqualTo(0f));
            Assert.That(changes, Is.EqualTo(0));
        }

        [Test]
        public void AMixGoesInInProportion()
        {
            var mix = new[] { new Share(Mineral.Iron, 0.75f), new Share(Mineral.Nickel, 0.25f) };
            Assert.That(hold.AddMix(mix, 40f), Is.EqualTo(40f));
            Assert.That(hold[Mineral.Iron], Is.EqualTo(30f).Within(1e-4f));
            Assert.That(hold[Mineral.Nickel], Is.EqualTo(10f).Within(1e-4f));
            Assert.That(hold.AddMix(mix, 500f), Is.EqualTo(60f), "capped at the room left");
            Assert.That(hold.Full, Is.True);
        }

        [Test]
        public void TheManifestPutsTheMostValuableCargoFirst()
        {
            hold.Add(Mineral.Iron, 50f);        // 600 cr
            hold.Add(Mineral.Gold, 1f);         // 6000 cr
            hold.Add(Mineral.Silicates, 10f);   // 20 cr
            var manifest = hold.Manifest();
            Assert.That(manifest.Select(m => m.mineral), Is.EqualTo(new[] { Mineral.Gold, Mineral.Iron, Mineral.Silicates }));
            Assert.That(hold.Value, Is.EqualTo(50f * 12f + 6000f + 20f).Within(1e-2f));
        }

        [Test]
        public void TheCheapestIsJettisoned()
        {
            Assert.That(hold.Cheapest(), Is.Null);
            hold.Add(Mineral.Iron, 50f);
            hold.Add(Mineral.Silicates, 30f);
            Assert.That(hold.Cheapest(), Is.EqualTo(Mineral.Silicates));
            Assert.That(hold.Jettison(Mineral.Silicates), Is.EqualTo(30f));
            Assert.That(hold.Used, Is.EqualTo(50f));
            Assert.That(hold.TotalJettisoned, Is.EqualTo(30f));
            Assert.That(hold.Cheapest(), Is.EqualTo(Mineral.Iron));
            Assert.That(hold.Jettison(Mineral.Gold), Is.EqualTo(0f), "nothing of it aboard");
        }

        [Test]
        public void SellingEmptiesTheHoldForItsWorth()
        {
            hold.Add(Mineral.Iron, 50f);
            hold.Add(Mineral.Gold, 2f);
            float worth = hold.Value;
            Assert.That(hold.SellAll(out float sold), Is.EqualTo(worth));
            Assert.That(sold, Is.EqualTo(52f));
            Assert.That(hold.Used, Is.EqualTo(0f));
            Assert.That(hold[Mineral.Gold], Is.EqualTo(0f));
            Assert.That(hold.Credits, Is.EqualTo(worth));
            hold.Add(Mineral.Iron, 10f);
            hold.SellAll(out _);
            Assert.That(hold.Credits, Is.EqualTo(worth + 120f).Within(1e-2f), "credits add up");
            Assert.That(hold.TotalSold, Is.EqualTo(62f));
            Assert.That(hold.SellAll(out float none), Is.EqualTo(0f));
            Assert.That(none, Is.EqualTo(0f));
        }
    }
}
