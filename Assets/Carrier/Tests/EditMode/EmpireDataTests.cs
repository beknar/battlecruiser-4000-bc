using System;
using System.IO;
using NUnit.Framework;

namespace CarrierGame.Tests
{
    /// <summary>The Empire's numbers (Data/empire.json): their four ships, each with what it needs
    /// to be built and to fight, armoured, each with its role's gear, and the task force made of them.</summary>
    public class EmpireDataTests
    {
        const string Path = "Assets/Carrier/Data/empire.json";
        static FactionTable Table() => FactionTable.Parse(File.ReadAllText(Path));

        [Test]
        public void TheFourShipsHaveTheirNumbers()
        {
            var t = Table();
            Assert.That(t.Name, Is.EqualTo(EmpireShip.Faction));
            foreach (var id in Enum.GetNames(typeof(EmpireShip.Role)))
            {
                var s = t.Find(id);
                Assert.That(s, Is.Not.Null, id);
                Assert.That(s.prefab, Is.EqualTo("Empire" + id), id);
                Assert.That(File.Exists($"Assets/Carrier/Models/{s.model}.fbx"), Is.True, $"{id}: its model");
                Assert.That(File.Exists($"Assets/Carrier/Data/{s.hardpoints}"), Is.True, $"{id}: its hardpoints");
                Assert.That(s.hull, Is.GreaterThan(0f), id);
                Assert.That(s.armor, Is.GreaterThan(0f), id + ": ablative plating");
                Assert.That(s.sensorRange, Is.GreaterThan(0f), id);
                Assert.That(s.guns, Is.Not.Empty, id);
                foreach (var g in s.guns) Assert.That(t.Bolt(g.bolt), Is.Not.Null, $"{id} {g.label}: bolt {g.bolt}");
            }
        }

        [Test]
        public void EachRoleHasItsGear()
        {
            var t = Table();
            var assault = t.Find("Assault");
            Assert.That(assault.armor, Is.GreaterThanOrEqualTo(0.4f), "the line is the best armoured");
            Assert.That(assault.drones.stock, Is.GreaterThan(0), "drones");
            Assert.That(assault.salvoSize, Is.GreaterThan(0), "missiles");
            Assert.That(Array.Exists(assault.guns, g => g.on == "ciws"), Is.True, "CIWS");
            var patrol = t.Find("Patrol");
            Assert.That(patrol.pods, Is.GreaterThan(0), "marines");
            Assert.That(patrol.interdict, Is.GreaterThan(0f), "a warp inhibitor");
            Assert.That(Array.Exists(patrol.guns, g => g.on == "ciws"), Is.True, "CIWS");
            var raider = t.Find("Raider");
            Assert.That(raider.style, Is.EqualTo("Fighter"));
            Assert.That(raider.pdMissileScale, Is.GreaterThan(0f), "its guns double as point defence");
            var recon = t.Find("Recon");
            Assert.That(recon.designatorRange, Is.GreaterThan(10000f), "a targeting laser");
            Assert.That(recon.sensorRange, Is.GreaterThan(t.Find("Assault").sensorRange), "it sees furthest");
        }

        [Test]
        public void TheTaskForceIsMadeOfThem()
        {
            var t = Table();
            foreach (var slot in t.group.slots) Assert.That(t.Find(slot.ship), Is.Not.Null, slot.ship);
            foreach (var id in Enum.GetNames(typeof(EmpireShip.Role)))
                Assert.That(Array.Exists(t.group.slots, sl => sl.ship == id), Is.True, $"a {id} in the task force");
        }
    }
}
