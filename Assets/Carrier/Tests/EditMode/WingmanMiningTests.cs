using System.Collections.Generic;
using System.Linq;
using CarrierGame.Demo;
using NUnit.Framework;
using UnityEngine;

namespace CarrierGame.Tests
{
    /// <summary>The AI wingman's mining: reaching a field that lies near a world, a fight near
    /// the field that doesn't start and stop every second, and the rocks it chooses when told
    /// what to go after.</summary>
    public class WingmanMiningTests
    {
        readonly List<WarpPoint> made = new List<WarpPoint>();

        [TearDown]
        public void TearDown()
        {
            foreach (var w in made) { WarpPoint.All.Remove(w); if (w != null) Object.DestroyImmediate(w.gameObject); }
            made.Clear();
        }

        WarpPoint Point(string name, WarpPoint.Kind kind, Vector3 at, float radius)
        {
            var go = new GameObject(name);
            go.transform.position = at;
            var w = go.AddComponent<WarpPoint>();
            w.displayName = name;
            w.kind = kind;
            w.bodyRadius = radius;
            if (!WarpPoint.All.Contains(w)) WarpPoint.All.Add(w);
            made.Add(w);
            return w;
        }

        [Test]
        public void AFieldNearAMoonIsReachedAtTheField()
        {
            var moon = Point("Brontes", WarpPoint.Kind.Moon, Vector3.zero, 20000f);
            var field = Point("Brontes Rocks", WarpPoint.Kind.AsteroidField, new Vector3(50000f, 0f, 0f), 15000f);
            Vector3 there = field.Position + new Vector3(2000f, 0f, 0f);
            Assert.That(Places.At(there), Is.SameAs(moon), "the place named there is the moon (why the old check never arrived)");
            Assert.That(Wingman.AtField(field, there), Is.True, "but the bot is at the field");
            Assert.That(Wingman.AtField(field, new Vector3(400000f, 0f, 0f)), Is.False, "far off: not there");
            Assert.That(Wingman.AtField(null, there), Is.False);
        }

        [Test]
        public void AFightWhileMiningIsOverOnlyAfterTheCalm()
        {
            Assert.That(Wingman.StillDefending(true, 1f), Is.True, "an enemy gone a second ago (out of the sensors' reach, say): still on");
            Assert.That(Wingman.StillDefending(true, Wingman.DefendCalm - 0.1f), Is.True);
            Assert.That(Wingman.StillDefending(true, Wingman.DefendCalm), Is.False, "the calm out: back to mining");
            Assert.That(Wingman.StillDefending(false, 1f), Is.False, "following or holding: carries on at once, not to lose its leader");
            Assert.That(Wingman.DefendKeep, Is.GreaterThan(1f), "once started, a fight goes on a little further out than it starts");
        }

        /// <summary>A rock for the tests: a mix, tonnes left, and how far off it is.</summary>
        class Rock : IMineralSource
        {
            public string Label { get; set; }
            public Share[] Mix { get; set; }
            public float Remaining { get; set; }
            public float Initial => Remaining;
            public bool Depleted => Remaining <= 0f;
            public Vector3 Position => Vector3.zero;
            public float Radius => 50f;
            public bool Valid => true;
            public bool Discovered { get; set; }
            public float Take(float tonnes) => 0f;
            public float Off;
        }

        static Rock R(string label, float off, float tonnes, params (Mineral m, float f)[] mix) =>
            new Rock { Label = label, Off = off, Remaining = tonnes, Mix = mix.Select(x => new Share(x.m, x.f)).ToArray() };

        static string Order(IEnumerable<IMineralSource> rocks, Wingman.OreChoice choice, out bool missing) =>
            string.Join(" ", Wingman.Ranked(rocks, choice, r => ((Rock)r).Off, out missing).Select(r => r.Label));

        [Test]
        public void TheRocksAreChosenByWhatItWasToldToGoAfter()
        {
            var rocks = new List<IMineralSource>
            {
                R("stony", 100f, 5000f, (Mineral.Silicates, 0.5f), (Mineral.Iron, 0.3f), (Mineral.Gold, 0.001f)),
                R("iron", 10f, 5000f, (Mineral.Iron, 0.6f), (Mineral.Nickel, 0.3f), (Mineral.Silicates, 0.1f)),
                R("metalA", 300f, 3000f, (Mineral.Iron, 0.6f), (Mineral.Nickel, 0.2f), (Mineral.Platinum, 0.012f), (Mineral.Iridium, 0.004f), (Mineral.Gold, 0.003f)),
                R("metalB", 200f, 9000f, (Mineral.Iron, 0.6f), (Mineral.Nickel, 0.2f), (Mineral.Platinum, 0.012f)),
                R("metalC", 50f, 9000f, (Mineral.Iron, 0.65f), (Mineral.Nickel, 0.2f), (Mineral.Platinum, 0.005f)),
            };
            Assert.That(Order(rocks, Wingman.OreChoice.Nearest, out bool missing), Is.EqualTo("iron metalC stony metalB metalA"), "told nothing: the nearest first");
            Assert.That(missing, Is.False);
            // (the richest in platinum; two as rich, the one with more tonnes in it first; then the
            // less rich, and those with none last)
            Assert.That(Order(rocks, Wingman.OreChoice.For(Mineral.Platinum), out missing), Does.StartWith("metalB metalA metalC"));
            Assert.That(missing, Is.False);
            Assert.That(Order(rocks, Wingman.OreChoice.For(Mineral.Gold), out _), Does.StartWith("metalA stony"));
            Assert.That(Order(rocks, Wingman.OreChoice.For(Mineral.Iron), out _), Does.StartWith("metalC"));
            // (worth most a tonne: the platinum and iridium rock, over the platinum alone)
            Assert.That(Order(rocks, Wingman.OreChoice.MostValuable, out missing), Does.StartWith("metalA metalB metalC"));
            Assert.That(Wingman.Ranked(rocks, Wingman.OreChoice.MostValuable, r => ((Rock)r).Off, out _).Last().Label, Is.EqualTo("iron").Or.EqualTo("stony"));
            Assert.That(missing, Is.False);
            // none of it here: the most valuable instead, and it says so
            Assert.That(Order(rocks, Wingman.OreChoice.For(Mineral.Deuterium), out missing), Is.EqualTo(Order(rocks, Wingman.OreChoice.MostValuable, out _)));
            Assert.That(missing, Is.True);
            // (nothing found yet: nothing missing)
            Assert.That(Wingman.Ranked(new IMineralSource[0], Wingman.OreChoice.For(Mineral.Deuterium), r => 0f, out missing), Is.Empty);
            Assert.That(missing, Is.False, "no rocks scanned yet isn't none of it");
        }

        [Test]
        public void RocksAsGoodAsEachOtherAreTakenBiggestThenNearest()
        {
            var rocks = new List<IMineralSource>
            {
                R("far", 900f, 4000f, (Mineral.Iron, 0.6f), (Mineral.Gold, 0.002f)),
                R("near", 100f, 4000f, (Mineral.Iron, 0.6f), (Mineral.Gold, 0.002f)),
                R("big", 1500f, 8000f, (Mineral.Iron, 0.6f), (Mineral.Gold, 0.002f)),
            };
            Assert.That(Order(rocks, Wingman.OreChoice.For(Mineral.Gold), out _), Is.EqualTo("big near far"));
            Assert.That(Order(rocks, Wingman.OreChoice.MostValuable, out _), Is.EqualTo("big near far"));
        }
    }
}
