using System.Collections.Generic;
using CarrierGame.Demo;
using NUnit.Framework;
using UnityEngine;

namespace CarrierGame.Tests
{
    /// <summary>The AI wingman's mining: reaching a field that lies near a world, and a fight
    /// near the field that doesn't start and stop every second.</summary>
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
    }
}
