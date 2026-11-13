using System.Linq;
using NUnit.Framework;

namespace CarrierGame.Tests
{
    /// <summary>The world going on whether or not it is seen (WorldState), and what is loaded when
    /// (StreamedVisual).</summary>
    public class WorldStateTests
    {
        [SetUp]
        public void SetUp() { WorldState.Clear(); IndustryLedger.SpeedUp = 1f; }

        [TearDown]
        public void TearDown() { WorldState.Clear(); IndustryLedger.SpeedUp = 1f; }

        [Test]
        public void ADestroyedUnitIsRebuiltInItsTimeWhetherOrNotAnyoneIsThere()
        {
            var d = WorldState.Body("Cinder").destroyed;
            d.Add(7L, 20f);
            d.Add(8L, 0f);       // never rebuilt
            Assert.That(d.Contains(7L), Is.True);
            WorldState.Advance(19f * 60f);
            Assert.That(d.Contains(7L), Is.True, "not yet");
            WorldState.Advance(61f);
            Assert.That(d.Contains(7L), Is.False, "twenty minutes on: rebuilt");
            Assert.That(d.Contains(8L), Is.True, "one that's never rebuilt stays destroyed");
            Assert.That(d.Contains(9L), Is.False);
            Assert.That(d.Count, Is.EqualTo(1), "the rebuilt one no longer counts (nor is saved)");
        }

        [Test]
        public void TheClockRunsAsFastAsTheIndustrys()
        {
            IndustryLedger.SpeedUp = 60f;
            WorldState.Advance(1f);
            Assert.That(WorldState.Clock, Is.EqualTo(60.0).Within(1e-6));
        }

        [Test]
        public void TheWorldIsSavedAndComesBack()
        {
            WorldState.Advance(100f);
            var m = WorldState.Body("Glacis");
            m.explored.Add(41);
            m.destroyed.Add(123L, 15f);
            var json = IndustrySave.ToJson(new IndustrySave.Data { world = WorldState.Snapshot() });
            WorldState.Clear();
            WorldState.Restore(IndustrySave.FromJson(json).world);
            Assert.That(WorldState.Clock, Is.EqualTo(100.0).Within(1e-6));
            var back = WorldState.Body("Glacis");
            Assert.That(back.explored.Contains(41), Is.True);
            Assert.That(back.destroyed.Contains(123L), Is.True);
            Assert.That(back.destroyed.RebuiltAt(123L), Is.EqualTo(100.0 + 15 * 60.0).Within(1e-6));
            Assert.That(WorldState.Bodies.Count(), Is.EqualTo(1));
        }

        [Test]
        public void AnOldSaveWithNoWorldStartsAFreshOne()
        {
            var d = IndustrySave.FromJson("{ \"credits\": 5 }");
            Assert.That(d.world, Is.Not.Null);
            WorldState.Restore(d.world);
            Assert.That(WorldState.Clock, Is.EqualTo(0.0));
        }

        [Test]
        public void ALookIsLoadedNearAndLetGoFarWithRoomBetween()
        {
            const float load = 25000f, unload = 32000f;
            Assert.That(StreamedVisual.Want(24000f, false, load, unload), Is.True, "near: loaded");
            Assert.That(StreamedVisual.Want(28000f, false, load, unload), Is.False, "not loaded till within the load distance");
            Assert.That(StreamedVisual.Want(28000f, true, load, unload), Is.True, "once loaded, kept till beyond the unload distance");
            Assert.That(StreamedVisual.Want(33000f, true, load, unload), Is.False);
        }

        [Test]
        public void CratersAndTheSatellitesClocksAreSaved()
        {
            WorldState.AddScar("Cinder", new WorldState.Scar { u = 1234.5, v = -678.9, radius = 360f, depth = 60f, at = 5.0 });
            WorldState.SetTimer("strike:Corsair Ion Satellite", 1800.0);
            var json = IndustrySave.ToJson(new IndustrySave.Data { world = WorldState.Snapshot() });
            WorldState.Clear();
            WorldState.Restore(IndustrySave.FromJson(json).world);
            var s = WorldState.Body("Cinder").scars;
            Assert.That(s.Count, Is.EqualTo(1));
            Assert.That(s[0].u, Is.EqualTo(1234.5).Within(1e-9));
            Assert.That(s[0].radius, Is.EqualTo(360f));
            Assert.That(WorldState.TryTimer("strike:Corsair Ion Satellite", out double at) && at == 1800.0, Is.True);
        }

        [Test]
        public void ABuildingsKeyIsNeverAGroundUnitsKey()
        {
            long b = WorldState.BuildingKey("Port Ochre", 7);
            Assert.That(b, Is.Not.EqualTo(WorldState.BuildingKey("Port Ochre", 8)));
            Assert.That(b, Is.Not.EqualTo(WorldState.BuildingKey("Pyrois Domes", 7)));
            // ground units: (area << 8) | id, the area well under 2^40: never the top bit
            long unit = ((long)500000 << 8) | 255u;
            Assert.That((b >> 62) & 1L, Is.EqualTo(1L));
            Assert.That((unit >> 62) & 1L, Is.EqualTo(0L));
        }

        [Test]
        public void ACraterLowersAndScorchesTheGround()
        {
            var w = new SurfaceWorld(SurfaceKind.Desert, "Test", 200000.0, false);
            float m = 0f;
            var ctx = System.Array.Empty<TileFeatures>();
            float before = w.Height(1000.0, 500.0, w.Region(1000.0, 500.0), ctx, false, ref m);
            w.scars = new[] { new WorldState.Scar { u = 1000.0, v = 500.0, radius = 400f, depth = 50f } };
            float after = w.Height(1000.0, 500.0, w.Region(1000.0, 500.0), ctx, false, ref m);
            Assert.That(after, Is.LessThan(before - 40f), "the crater's floor");
            w.ScarAt(1000.0, 500.0, out float scorch);
            Assert.That(scorch, Is.GreaterThan(0.9f));
            w.ScarAt(1000.0 + 3000.0, 500.0, out float away);
            Assert.That(away, Is.EqualTo(0f), "far from it, untouched");
        }

        [Test]
        public void EveryGroundUnitKindIsRebuiltInTime()
        {
            var t = GroundUnitTable.Get();
            foreach (var (kind, s) in new[] { ("battery", t.battery), ("missile site", t.missileSite), ("tank", t.tank), ("fighter", t.fighter),
                                              ("gunship", t.gunship), ("sea base", t.seaBase), ("submarine", t.submarine) })
                Assert.That(s.rebuildMinutes, Is.GreaterThan(0f), kind);
        }
    }
}
