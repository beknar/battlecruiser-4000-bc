using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace CarrierGame.Tests
{
    /// <summary>The Vrask's numbers (Data/vrask.json): their four ships, each with what it needs to
    /// be built and to fight, the Maw the slowest and the best armoured, and the raiding group made
    /// of them. A mistake shows here rather than as a missing ship in the level.</summary>
    public class VraskDataTests
    {
        static VraskTable Table() => VraskTable.Parse(File.ReadAllText(VraskTable.DataPath));

        [Test]
        public void TheFourShipsHaveTheirNumbers()
        {
            var t = Table();
            foreach (var id in Enum.GetNames(typeof(VraskShip.Role)))
            {
                var s = t.Find(id);
                Assert.That(s, Is.Not.Null, id);
                Assert.That(s.displayName, Does.StartWith("Vrask "), id);
                Assert.That(s.hull, Is.GreaterThan(0f), id);
                Assert.That(s.shield, Is.GreaterThan(0f), id + ": the Vrask are shielded");
                Assert.That(s.armor, Is.InRange(0.3f, 0.4f), id);
                Assert.That(s.plating, Is.GreaterThan(0f), id);
                Assert.That(s.cruiseSpeed, Is.GreaterThan(0f), id);
                Assert.That(s.attackSpeed, Is.GreaterThanOrEqualTo(s.cruiseSpeed), id);
                Assert.That(s.turnRate, Is.GreaterThan(0f), id);
                Assert.That(s.acceleration, Is.GreaterThan(0f), id);
                Assert.That(s.respawn, Is.GreaterThan(0f), id + ": the spawns bring it back");
                Assert.That(s.guns, Is.Not.Empty, id);
                foreach (var g in s.guns)
                {
                    var b = t.Bolt(g.bolt);
                    Assert.That(b, Is.Not.Null, $"{id} {g.label}: bolt {g.bolt}");
                    Assert.That(Enum.TryParse(b.kind, out HitKind _), Is.True, $"{b.name}: kind {b.kind}");
                    Assert.That(g.interval, Is.GreaterThan(0f), $"{id} {g.label}");
                    Assert.That(g.range, Is.GreaterThan(0f), $"{id} {g.label}");
                }
            }
        }

        [Test]
        public void EachFiresItsOwnKindOfShot()
        {
            var t = Table();
            HitKind[] Kinds(string id) => t.Find(id).guns.Select(g => t.Bolt(g.bolt).Kind).ToArray();
            Assert.That(Kinds("Snare"), Does.Contain(HitKind.Ion));
            Assert.That(Kinds("Fang"), Does.Contain(HitKind.Cannon));
            Assert.That(t.Find("Fang").salvoSize, Is.GreaterThan(0), "the Fang's missile pods");
            var art = t.Find("Maw").artillery;
            Assert.That(art, Is.Not.Null, "the Maw's spinal artillery");
            Assert.That(art.range, Is.InRange(10000f, 14000f), "reaching 10 to 14 km");
            Assert.That(art.range, Is.GreaterThan(t.ships.SelectMany(s => s.guns).Max(g => g.range)), "further than any of their guns");
            Assert.That(art.interval, Is.InRange(12f, 20f), "and firing far less often");
            Assert.That(art.shellSpeed, Is.InRange(400f, 700f), "its shells slow");
            Assert.That(art.blastRadius, Is.InRange(400f, 600f), "and bursting wide");
            Assert.That(art.edge, Is.InRange(0.05f, 0.5f));
            Assert.That(t.ships.Count(s => s.artillery != null && s.artillery.range > 0f), Is.EqualTo(1), "only the Maw has artillery");
            var nukes = t.Find("Maw").nukes;
            Assert.That(nukes.stock, Is.EqualTo(2), "two nuclear missiles");
            Assert.That(nukes.blastRadius, Is.InRange(1000f, 1500f), "a blast over a kilometre wide in space");
            Assert.That(t.ships.Count(s => s.nukes != null && s.nukes.stock > 0), Is.EqualTo(1), "only the Maw has them");
            Assert.That(t.Find("Skink").guns.Min(g => g.interval), Is.LessThan(1f), "the Skink's guns fire fast");
            Assert.That(t.Find("Snare").tractorRange, Is.InRange(2000f, 3000f), "the Snare's tractor beam");
            Assert.That(t.ships.Count(s => s.tractorRange > 0f), Is.EqualTo(1), "only the Snare has one");
            Assert.That(t.Find("Fang").pods, Is.InRange(2, 4), "the Fang's boarding pods a wave");
            Assert.That(t.Find("Snare").pods, Is.GreaterThan(0), "the Snare boards what it holds");
            Assert.That(t.Find("Fang").podInterval, Is.LessThan(t.Find("Snare").podInterval), "the Fang's waves come faster");
            Assert.That(t.ships.Where(s => s.pods > 0).All(s => s.podRange >= 2000f && s.podInterval > 0f), Is.True);
        }

        [Serializable] class SysBody { public string name; public float atmosphereScale; }
        [Serializable] class Sys { public SysBody[] planets, moons; }

        [Test]
        public void TheSiegeIsOfACityOnAWorldWithAir()
        {
            var g = Table().siege;
            Assert.That(g, Is.Not.Null);
            Assert.That(g.at, Is.Not.Empty);
            var sys = UnityEngine.JsonUtility.FromJson<Sys>(File.ReadAllText("Assets/Carrier/Data/star_system.json"));
            var body = sys.planets.FirstOrDefault(p => p.name == g.body);
            Assert.That(body, Is.Not.Null, $"{g.at} is on a planet ({g.body}), not a moon");
            Assert.That(body.atmosphereScale, Is.GreaterThan(0f), $"{g.body} has air");
            Assert.That(sys.moons.Any(m => m.name == g.body), Is.False);
            Assert.That(Table().Find(g.ship), Is.Not.Null);
            Assert.That(Table().Find(g.escort), Is.Not.Null);
            Assert.That(g.escorts, Is.GreaterThan(0), "the Maw has escorts");
            Assert.That(g.intervalMinutes, Is.GreaterThan(0f));
            Assert.That(g.shells, Is.GreaterThan(0));
            Assert.That(g.nukeChance, Is.InRange(0.01f, 0.25f), "a nuclear strike only rarely");
            Assert.That(g.nukeRebuildMinutes, Is.GreaterThan(120f), "its ruins last far longer than a kinetic strike's hour");
            Assert.That(g.nukeRestockMinutes, Is.GreaterThan(g.intervalMinutes));
        }

        [Test]
        public void TheMawIsTheSlowestAndTheBestArmoured()
        {
            var t = Table();
            var maw = t.Find("Maw");
            foreach (var s in t.ships.Where(s => s.id != "Maw"))
            {
                Assert.That(maw.attackSpeed, Is.LessThan(s.attackSpeed), s.id);
                Assert.That(maw.cruiseSpeed, Is.LessThan(s.cruiseSpeed), s.id);
                Assert.That(maw.acceleration, Is.LessThan(s.acceleration), s.id);
                Assert.That(maw.armor, Is.GreaterThan(s.armor), s.id);
            }
        }

        [Test]
        public void TheirModelsAndHardpointsAreThere()
        {
            foreach (var s in Table().ships)
            {
                Assert.That(File.Exists($"Assets/Carrier/Models/{s.model}.fbx"), Is.True, s.model);
                Assert.That(File.Exists($"Assets/Carrier/Models/{s.texture}"), Is.True, s.texture);
                Assert.That(File.Exists($"Assets/Carrier/Data/{s.hardpoints}"), Is.True, s.hardpoints);
            }
        }

        [Test]
        public void TheRaidingGroupIsMadeOfThem()
        {
            var t = Table();
            Assert.That(t.group, Is.Not.Null);
            Assert.That(t.group.at, Is.Not.Empty);
            Assert.That(t.group.spotRange, Is.GreaterThan(0f));
            Assert.That(t.group.slots, Is.Not.Empty);
            foreach (var slot in t.group.slots) Assert.That(t.Find(slot.ship), Is.Not.Null, slot.ship);
            foreach (var id in Enum.GetNames(typeof(VraskShip.Role)))
                Assert.That(t.group.slots.Any(x => x.ship == id), Is.True, $"a {id} in the group");
        }
    }
}
