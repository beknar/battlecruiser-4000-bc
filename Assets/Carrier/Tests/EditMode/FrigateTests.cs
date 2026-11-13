using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CarrierGame.Tests
{
    /// <summary>The ships a player can fly: the roster the start menu offers, that every one has
    /// what it must (its turrets, a CIWS mount or more, a main gun unless it is a destroyer), and
    /// that the classes are what they should be against each other: destroyers tougher, slower
    /// and more crewed than frigates, with more turrets, fewer CIWS, no main gun and a limited
    /// stock of missiles; the cruiser above the destroyers in all but speed, with a main gun.</summary>
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

        [System.Serializable] class Spec { public string id, hardpoints; }
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
                Assert.That(s.mainGuns, Is.EqualTo(e.category == FrigateRoster.Destroyer ? 0 : 1), e.id + ": one main gun (none on a destroyer)");
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
            // each ship with turrets modelled on it has one turret for each in its hardpoints (and,
            // a destroyer or cruiser, ball mounts besides)
            var specs = JsonUtility.FromJson<Specs>(File.ReadAllText(Path.Combine(Application.dataPath, "Carrier", "Data", "frigates.json")));
            foreach (var e in Roster.frigates.Skip(1))
            {
                var spec = specs.frigates.First(f => f.id == e.id);
                var path = Path.Combine(Application.dataPath, "Carrier", "Data", spec.hardpoints);
                Assert.That(File.Exists(path), Is.True, path);
                var hp = JsonUtility.FromJson<Hardpoints>(File.ReadAllText(path));
                var turrets = e.prefab.GetComponent<CarrierController>().mainTurrets.Where(t => !t.ballMount).ToArray();
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
        public void DestroyersAndTheCruiserAreAboveTheFrigates()
        {
            FrigateStats[] Of(string category) => Roster.frigates.Where(e => e.category == category).Select(e => FrigateStats.Of(e.prefab)).ToArray();
            var frigates = Of(FrigateRoster.Frigate);
            var destroyers = Of(FrigateRoster.Destroyer);
            var cruisers = Of(FrigateRoster.Cruiser);
            Assert.That(frigates.Length, Is.EqualTo(5), "frigates");
            Assert.That(destroyers.Length, Is.EqualTo(5), "destroyers");
            Assert.That(cruisers.Length, Is.EqualTo(1), "cruisers");
            Assert.That(Roster.frigates.Where(e => e.category != FrigateRoster.Frigate).All(e => e.buildable), Is.True, "destroyers and cruisers are built at a shipyard");
            Assert.That(Roster.frigates.Where(e => e.category == FrigateRoster.Frigate).Any(e => e.buildable), Is.False, "frigates aren't");
            foreach (var d in destroyers)
            {
                Assert.That(d.hull, Is.GreaterThan(frigates.Max(f => f.hull)), "a destroyer is tougher than any frigate");
                Assert.That(d.speed, Is.LessThan(frigates.Min(f => f.speed)), "but slower");
                Assert.That(d.mainGuns, Is.EqualTo(0), "no main gun");
                Assert.That(d.turrets, Is.GreaterThan(frigates.Max(f => f.turrets)), "more turrets than any frigate");
                Assert.That(d.ciws, Is.LessThan(frigates.Max(f => f.ciws)), "fewer CIWS");
                Assert.That(d.ciws, Is.GreaterThanOrEqualTo(1), "a CIWS mount");
                Assert.That(d.missiles, Is.GreaterThan(0).And.LessThan(30), "a limited stock of missiles");
                Assert.That(d.missileCells, Is.GreaterThan(0), "cells to fire them from");
                Assert.That(d.crew, Is.GreaterThan(frigates.Max(f => f.crew)), "a bigger crew than any frigate");
            }
            foreach (var c in cruisers)
            {
                Assert.That(c.hull, Is.GreaterThan(destroyers.Max(d => d.hull)), "a cruiser is tougher than any destroyer");
                Assert.That(c.turrets, Is.GreaterThan(destroyers.Max(d => d.turrets)), "with more turrets");
                Assert.That(c.mainGuns, Is.EqualTo(1), "a main gun");
                Assert.That(c.ciws, Is.GreaterThan(destroyers.Max(d => d.ciws)), "CIWS");
                Assert.That(c.missiles, Is.GreaterThan(0), "missiles");
                Assert.That(c.crew, Is.GreaterThan(destroyers.Max(d => d.crew)), "and a bigger crew than any destroyer");
            }
        }

        [Test]
        public void EveryBuildableShipHasARecipeAndAShipyard()
        {
            var t = IndustryTable.Get();
            foreach (var e in Roster.frigates.Where(e => e.buildable))
            {
                var r = t.Ship(e.id);
                Assert.That(r, Is.Not.Null, e.id + ": a shipyard recipe");
                Assert.That(r.seconds, Is.GreaterThan(0f), e.id);
                foreach (var i in r.inputs) Assert.That(t.Kind(i.item), Is.Not.EqualTo(ItemKind.None), $"{e.id}: {i.item}");
            }
            foreach (var r in t.ships) Assert.That(Roster.Find(r.id)?.buildable ?? false, Is.True, r.id + ": a buildable ship in the roster");
            // shipyards in space and on bodies: deep space, orbit, a planet, a moon and an asteroid
            foreach (var where in new[] { "deepspace", "orbit", "surface", "moon", "asteroid" })
                Assert.That(t.sites.Any(x => x.shipyard && x.where == where), Is.True, "a shipyard in " + where);
        }

        [Test]
        public void TheStartingFrigateKeepsItsName()
        {
            // the autotest's and the captain's lines say "Frigate"
            Assert.That(Roster.Default.prefab.GetComponent<CarrierController>().shipName, Is.EqualTo("Frigate"));
        }
    }
}
