using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CarrierGame.Tests
{
    /// <summary>The frigates a player can fly: the roster the start menu offers, and that every one
    /// has what every frigate must (its turrets, a CIWS mount or more, one main gun).</summary>
    public class FrigateTests
    {
        static FrigateRoster Roster
        {
            get
            {
                var r = FrigateRoster.Get();
                Assert.That(r, Is.Not.Null, "Resources/FrigateRoster.asset (made by Tools > Carrier > Build Everything)");
                return r;
            }
        }

        [System.Serializable] class Spec { public string id; }
        [System.Serializable] class Specs { public Spec[] frigates; }
        [System.Serializable] class Turret { public string name; }
        [System.Serializable] class Hardpoints { public Turret[] turrets; }

        [Test]
        public void TheRosterHasEveryFrigateStartingWithTheSentinel()
        {
            var specs = JsonUtility.FromJson<Specs>(File.ReadAllText(Path.Combine(Application.dataPath, "Carrier", "Data", "frigates.json")));
            Assert.That(Roster.frigates.Select(e => e.id), Is.EqualTo(specs.frigates.Select(s => s.id)));
            Assert.That(Roster.Default.id, Is.EqualTo("Sentinel"));
            Assert.That(Roster.frigates.Select(e => e.id).Distinct().Count(), Is.EqualTo(Roster.frigates.Length));
            foreach (var e in Roster.frigates)
            {
                Assert.That(e.prefab, Is.Not.Null, e.id);
                Assert.That(FrigateClass.Of(e.prefab.transform), Is.EqualTo(e.id), e.id + ": its prefab knows its class");
                Assert.That(e.displayName, Is.Not.Empty, e.id);
                Assert.That(e.description, Is.Not.Empty, e.id);
            }
        }

        [Test]
        public void EveryFrigateHasItsTurretsACiwsAndAMainGun()
        {
            foreach (var e in Roster.frigates)
            {
                var s = FrigateStats.Of(e.prefab);
                Assert.That(s.turrets, Is.GreaterThanOrEqualTo(2), e.id);
                Assert.That(s.ciws, Is.GreaterThanOrEqualTo(1), e.id + ": at least one CIWS mount");
                Assert.That(s.mainGuns, Is.EqualTo(1), e.id + ": one main gun");
                foreach (var t in e.prefab.GetComponent<CarrierController>().mainTurrets)
                {
                    Assert.That(t, Is.Not.Null, e.id);
                    Assert.That(t.muzzles, Is.Not.Empty, $"{e.id} {t.Label}: muzzles");
                    Assert.That(t.projectilePrefab, Is.Not.Null, $"{e.id} {t.Label}: what it fires");
                    Assert.That(t.checkHullClearance, Is.True, $"{e.id} {t.Label}: never fires through its own hull");
                }
                Assert.That(s.hull, Is.GreaterThan(0f), e.id);
                Assert.That(s.shield, Is.GreaterThan(0f), e.id);
                Assert.That(s.speed, Is.GreaterThan(0f), e.id);
                Assert.That(s.cargo, Is.GreaterThan(0f), e.id);
                Assert.That(s.slots, Is.GreaterThan(0), e.id + ": slots for ship parts");
            }
            // the frigates differ in what they can carry and fit
            var stats = Roster.frigates.Select(f => FrigateStats.Of(f.prefab)).ToArray();
            Assert.That(stats.Select(x => x.cargo).Distinct().Count(), Is.GreaterThan(1), "holds of different sizes");
            Assert.That(stats.Select(x => x.slots).Distinct().Count(), Is.GreaterThan(1), "different numbers of part slots");
        }

        [Test]
        public void TheTurretsAreTheOnesModelledOnTheShip()
        {
            // each frigate with turrets modelled on it has one turret for each in its hardpoints
            foreach (var e in Roster.frigates.Skip(1))
            {
                var path = Path.Combine(Application.dataPath, "Carrier", "Data", $"frigate_{e.id.ToLowerInvariant()}_hardpoints.json");
                Assert.That(File.Exists(path), Is.True, path);
                var hp = JsonUtility.FromJson<Hardpoints>(File.ReadAllText(path));
                var turrets = e.prefab.GetComponent<CarrierController>().mainTurrets;
                Assert.That(turrets.Select(t => t.Label), Is.EquivalentTo(hp.turrets.Select(t => t.name)), e.id);
                foreach (var t in turrets)
                    Assert.That(t.yaw.GetComponentsInChildren<MeshRenderer>(), Is.Not.Empty, $"{e.id} {t.Label}: the model's turret turns with it");
            }
        }

        [Test]
        public void TheFrigatesAreNotAllTheSame()
        {
            var stats = Roster.frigates.Select(e => FrigateStats.Of(e.prefab)).ToArray();
            Assert.That(stats.Select(s => s.speed).Distinct().Count(), Is.EqualTo(stats.Length), "speeds");
            Assert.That(stats.Select(s => s.hull).Distinct().Count(), Is.EqualTo(stats.Length), "hulls");
            Assert.That(stats.Select(s => s.gunDps).Distinct().Count(), Is.EqualTo(stats.Length), "firepower");
        }

        [Test]
        public void TheStartingFrigateKeepsItsName()
        {
            // the autotest's and the captain's lines say "Frigate"
            Assert.That(Roster.Default.prefab.GetComponent<CarrierController>().shipName, Is.EqualTo("Frigate"));
        }
    }
}
