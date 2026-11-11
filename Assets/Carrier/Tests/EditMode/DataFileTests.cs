using System.Linq;
using Carrier.EditorTools;
using NUnit.Framework;

namespace CarrierGame.Tests
{
    /// <summary>The data files: the ground units' numbers (Resources/Data/ground_units.json) and
    /// the star system's planets, moons and fields (Data/star_system.json). A mistake in either
    /// shows here rather than as a broken level.</summary>
    public class DataFileTests
    {
        // ------------------------------------------------------------ ground units

        [Test]
        public void EveryGroundUnitHasItsNumbers()
        {
            var t = GroundUnitTable.Get();
            foreach (var (kind, s) in new[] { ("battery", t.battery), ("missile site", t.missileSite), ("tank", t.tank) })
            {
                Assert.That(s, Is.Not.Null, kind);
                Assert.That(s.hull, Is.GreaterThan(0f), kind);
                Assert.That(s.explosion, Is.GreaterThan(0f), kind);
                Assert.That(s.name, Is.Not.Empty, kind);
                Assert.That(s.shipClass, Is.Not.Empty, kind);
            }
            Assert.That(t.battery.range, Is.GreaterThan(0f));
            Assert.That(t.battery.minPitch, Is.LessThan(t.battery.maxPitch));
            Assert.That(t.missileSite.salvoSize, Is.GreaterThan(0));
            Assert.That(t.tank.burstShots, Is.GreaterThan(0));
        }

        [Test]
        public void ThePlanetsFightersAndGunshipsCanFly()
        {
            var t = GroundUnitTable.Get();
            foreach (var (kind, s) in new[] { ("fighter", t.fighter), ("gunship", t.gunship) })
            {
                Assert.That(s, Is.Not.Null, kind);
                Assert.That(s.cruiseSpeed, Is.GreaterThan(0f), kind);
                Assert.That(s.attackSpeed, Is.GreaterThanOrEqualTo(s.cruiseSpeed), kind);
                Assert.That(s.turnRate, Is.GreaterThan(0f), kind);
                Assert.That(s.acceleration, Is.GreaterThan(0f), kind);
                Assert.That(s.leashDistance, Is.GreaterThan(0f), kind);
                Assert.That(s.climb, Is.GreaterThan(0f), kind);
            }
            Assert.That(t.fighter.patrolHeight, Is.GreaterThan(0f));
            Assert.That(t.gunship.standoff, Is.GreaterThan(0f));
        }

        [Test]
        public void TheSeaForcesFitInTheWater()
        {
            var t = GroundUnitTable.Get();
            foreach (var (kind, s) in new[] { ("sea base", t.seaBase), ("submarine", t.submarine) })
            {
                Assert.That(s, Is.Not.Null, kind);
                Assert.That(s.hull, Is.GreaterThan(0f), kind);
                Assert.That(s.name, Is.Not.Empty, kind);
                Assert.That(s.missileRange, Is.GreaterThan(0f), kind + ": it has torpedoes");
                Assert.That(s.salvoSize, Is.GreaterThan(0), kind);
                Assert.That(s.salvoInterval, Is.GreaterThan(0f), kind);
                Assert.That(s.minDepth, Is.GreaterThan(0f), kind + ": built only in deep enough water");
            }
            var sub = t.submarine;
            Assert.That(sub.attackSpeed, Is.GreaterThanOrEqualTo(sub.cruiseSpeed).And.GreaterThan(0f));
            Assert.That(sub.turnRate, Is.GreaterThan(0f));
            Assert.That(sub.standoff, Is.GreaterThan(0f).And.LessThan(sub.missileRange), "it fires from its stand-off");
            Assert.That(sub.keelClearance, Is.GreaterThan(0f).And.LessThan(sub.minDepth), "room under its keel in the shallowest water it is built in");
        }

        [Test]
        public void AGroundUnitLeavesOutWhatItHasntGot()
        {
            var t = GroundUnitTable.Parse("{ \"tank\": { \"hull\": 380, \"gunRange\": 3200 } }");
            Assert.That(t.tank.hull, Is.EqualTo(380f));
            Assert.That(t.tank.missileRange, Is.EqualTo(0f));
        }

        // ------------------------------------------------------------ the star system

        [Test]
        public void TheStarSystemsBodiesAreAllThere()
        {
            var d = StarSystemData.Load();
            var names = d.planets.Select(p => p.name).Concat(d.moons.Select(m => m.name)).Concat(d.fields.Select(f => f.name)).ToArray();
            Assert.That(names.Distinct().Count(), Is.EqualTo(names.Length), "every name once");
            // what the builder builds the rest of the level round
            foreach (var n in new[] { "Cinder", "Ochre", "Thalassa", "Pelagia", "Jovara", "Glacis", "Selene", "Brontes", "Inner Belt", "Jovara Trojans", "Outer Drift" })
                Assert.That(names, Does.Contain(n));
        }

        [Test]
        public void EveryPlanetAndMoonIsSound()
        {
            var d = StarSystemData.Load();
            var planetKinds = new[] { SurfaceKind.Volcanic, SurfaceKind.Desert, SurfaceKind.Ocean, SurfaceKind.GasGiant, SurfaceKind.IceGiant };
            Assert.That(d.planets.Count(p => p.yearSetsStarMass), Is.EqualTo(1), "one planet's year sets the star's mass");
            foreach (var p in d.planets)
            {
                Assert.That(planetKinds, Does.Contain(p.Surface), p.name);
                Assert.That(p.radius, Is.GreaterThan(0f), p.name);
                Assert.That(p.arrival, Is.GreaterThan(p.radius), p.name + ": arrives outside it");
                Assert.That(p.mass, Is.GreaterThan(0f).And.LessThan(1f / 26f), p.name + ": a planet's Trojans need it under 1/26 of the star");
                Assert.That(p.mapColor, Has.Length.EqualTo(3), p.name);
                Assert.That(p.description, Is.Not.Empty, p.name);
            }
            foreach (var m in d.moons)
            {
                Assert.That(d.planets.Any(p => p.name == m.planet), Is.True, $"{m.name} goes round {m.planet}, which isn't a planet here");
                Assert.That(m.radius, Is.GreaterThan(0f), m.name);
                Assert.That(m.mass, Is.GreaterThan(0f), m.name);
            }
            foreach (var f in d.fields)
            {
                Assert.That(f.rocks, Is.GreaterThan(0), f.name);
                Assert.That(f.mapColor, Has.Length.EqualTo(3), f.name);
            }
        }

        [Test]
        public void APositionIsRoundTheStar()
        {
            var p = new StarSystemData.Placed { angle = 90f, distanceKm = 10f, heightKm = 1f };
            Assert.That(p.Position.x, Is.EqualTo(0f).Within(1e-2f));
            Assert.That(p.Position.y, Is.EqualTo(1000f));
            Assert.That(p.Position.z, Is.EqualTo(10000f).Within(1e-2f));
        }
    }
}
