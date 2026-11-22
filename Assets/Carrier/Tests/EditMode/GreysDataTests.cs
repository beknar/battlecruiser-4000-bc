using System;
using System.IO;
using NUnit.Framework;

namespace CarrierGame.Tests
{
    /// <summary>The Greys' numbers (Data/greys.json): their four ships, each with what it needs to
    /// be built and to fight, quiet on the sensors and without armour, each with its role's gear,
    /// and the cell made of them.</summary>
    public class GreysDataTests
    {
        const string Path = "Assets/Carrier/Data/greys.json";
        static FactionTable Table() => FactionTable.Parse(File.ReadAllText(Path));

        [Test]
        public void TheFourShipsHaveTheirNumbers()
        {
            var t = Table();
            Assert.That(t.Name, Is.EqualTo(GreysShip.Faction));
            Assert.That(t.Prefix, Is.EqualTo("Greys"));
            foreach (var id in Enum.GetNames(typeof(GreysShip.Role)))
            {
                var s = t.Find(id);
                Assert.That(s, Is.Not.Null, id);
                Assert.That(s.prefab, Is.EqualTo("Greys" + id), id);
                Assert.That(File.Exists($"Assets/Carrier/Models/{s.model}.fbx"), Is.True, $"{id}: its model");
                Assert.That(File.Exists($"Assets/Carrier/Data/{s.hardpoints}"), Is.True, $"{id}: its hardpoints");
                Assert.That(s.hull, Is.GreaterThan(0f), id);
                Assert.That(s.shield, Is.GreaterThan(0f), id + ": the Greys are shielded");
                Assert.That(s.armor, Is.EqualTo(0f), id + ": no armour plating");
                Assert.That(s.signature, Is.InRange(0.3f, 0.95f), id + ": quiet on the sensors");
                Assert.That(s.sensorRange, Is.GreaterThan(0f), id + ": it hunts by its sensors");
                Assert.That(s.respawn, Is.GreaterThan(0f), id);
                Assert.That(s.guns, Is.Not.Empty, id);
                foreach (var g in s.guns) Assert.That(t.Bolt(g.bolt), Is.Not.Null, $"{id} {g.label}: bolt {g.bolt}");
            }
        }

        [Test]
        public void EachRoleHasItsGear()
        {
            var t = Table();
            var scout = t.Find("Scout");
            Assert.That(scout.cloak, Is.InRange(0.05f, 0.3f), "the Scout cloaks");
            Assert.That(scout.designatorRange, Is.GreaterThan(5000f), "it paints");
            Assert.That(scout.drones.stock, Is.GreaterThan(0), "it has drones");
            var shroud = t.Find("Shroud");
            Assert.That(shroud.ecm.radius, Is.GreaterThan(0f));
            Assert.That(shroud.ecm.ghosts, Is.GreaterThan(0));
            Assert.That(shroud.emp.radius, Is.GreaterThan(0f));
            Assert.That(Array.Exists(shroud.guns, g => t.Bolt(g.bolt).Kind == HitKind.Emp), Is.True, "an EMP projector");
            var lance = t.Find("Lance");
            Assert.That(lance.style, Is.EqualTo("Fighter"), "the Lance makes runs");
            Assert.That(lance.attackSpeed, Is.GreaterThan(t.Find("Reaper").attackSpeed * 2f), "and is fast");
            Assert.That(t.Find("Reaper").tractorRange, Is.GreaterThan(0f), "the Reaper holds ships");
        }

        [Test]
        public void TheCellIsMadeOfThem()
        {
            var t = Table();
            Assert.That(t.group.slots, Is.Not.Empty);
            foreach (var slot in t.group.slots) Assert.That(t.Find(slot.ship), Is.Not.Null, slot.ship);
            Assert.That(Array.Exists(t.group.slots, sl => sl.ship == "Reaper"), Is.True);
            Assert.That(Array.Exists(t.group.slots, sl => sl.ship == "Shroud"), Is.True);
        }
    }
}
