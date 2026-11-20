using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

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

        [Test]
        public void TheSkinkScreensAndTheFleetHasItsOrder()
        {
            var t = Table();
            var skink = t.Find("Skink");
            Assert.That(t.fleet, Is.Not.Null, "the fleet's numbers");
            var f = t.fleet;
            Assert.That(skink.sensorRange, Is.GreaterThan(f.sensorRange), "the Skink sees further than the rest: the group's eyes");
            Assert.That(skink.cap, Has.Length.EqualTo(2));
            Assert.That(skink.cap[0], Is.InRange(1000f, 2000f), "a loose patrol orbit, 1.5 to 2.5 km");
            Assert.That(skink.cap[1], Is.InRange(skink.cap[0], 3000f));
            Assert.That(skink.interceptRange, Is.InRange(4000f, 8000f), "threats run down within about 6 km");
            float needle = t.Bolt(skink.guns[0].bolt).damage * skink.pdMissileScale;
            Assert.That(needle, Is.InRange(5f, 16f), "a needle does to a missile about what a CIWS bolt does (8)");
            Assert.That(t.ships.Count(s => s.interceptRange > 0f), Is.EqualTo(1), "only the Skink escorts");
            // held at the line, the Snares and the Fang can't fire on the target, nor launch pods
            // or missiles at it: the artillery opens first, and reaches past them
            float reach = t.ships.Where(s => s.id != "Maw").SelectMany(s => s.guns).Max(g => g.range);
            float launch = Math.Max(t.ships.Max(s => s.podRange), t.ships.Max(s => s.missileRange));
            Assert.That(f.holdLine, Is.GreaterThan(Math.Max(reach, launch)), "the hold line beyond the Snares' and the Fang's reach");
            Assert.That(f.holdLine, Is.LessThan(t.Find("Maw").artillery.range));
            Assert.That(f.reactRange, Is.LessThan(f.holdLine));
            Assert.That(f.picketStandoff, Is.GreaterThan(skink.guns.Max(g => g.range)), "on picket out of its own guns' reach");
            Assert.That(f.picketStandoff + f.picketLead, Is.LessThan(skink.sensorRange + f.holdLine), "and close enough to see");
            Assert.That(f.bombardTime, Is.InRange(15f, 120f));
            Assert.That(f.bombardShield, Is.InRange(0.1f, 0.9f));
            Assert.That(f.recoverTime, Is.GreaterThan(0f));
            Assert.That(f.engageRange, Is.GreaterThan(t.Find("Maw").artillery.range));
        }

        [Test]
        public void ArmourWearsWhereHitAndMendsOnlyWhenTold()
        {
            var go = new GameObject("armour");
            try
            {
                var a = go.AddComponent<Armor>();
                a.rating = 0.4f;
                a.plating = 1000f;
                a.Repair();
                float through = a.Reduce(500f, HitKind.Cannon, new Vector3(1f, 2f, 3f));
                Assert.That(through, Is.LessThan(500f), "it stops a share");
                Assert.That(a.Integrity, Is.LessThan(1f), "and wears");
                Assert.That(a.HasHitPoint && a.LastHitPoint == new Vector3(1f, 2f, 3f), Is.True, "where it was hit");
                float worn = a.Integrity;
                a.Mend(100f);
                Assert.That(a.Integrity, Is.EqualTo(Mathf.Min(1f, worn + 0.1f)).Within(1e-4f), "grown back by what it is given");
                a.Mend(1e6f);
                Assert.That(a.Integrity, Is.EqualTo(1f), "no more than whole");
                Assert.That(VraskShip.PlatesFor(VraskShip.Role.Maw), Is.GreaterThan(VraskShip.PlatesFor(VraskShip.Role.Skink)));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
