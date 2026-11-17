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
            Assert.That(Kinds("Maw"), Does.Contain(HitKind.Penetrator));
            Assert.That(t.Find("Maw").guns.Where(g => t.Bolt(g.bolt).Kind == HitKind.Penetrator).Max(g => g.range),
                        Is.GreaterThan(t.Find("Snare").guns.Max(g => g.range)), "the spinal gun outranges the Snare");
            Assert.That(t.Find("Skink").guns.Min(g => g.interval), Is.LessThan(1f), "the Skink's guns fire fast");
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
