using System.Collections.Generic;
using System.Linq;
using CarrierGame.Demo;
using NUnit.Framework;
using UnityEngine;

namespace CarrierGame.Tests
{
    /// <summary>The system map's choice of a planet over its moons: at the whole system's view a
    /// moon is drawn on its planet, so a click there, Tab and the arrows take the planet; zoomed in
    /// far enough to draw it apart, the moon can be chosen. And what is listed round a planet
    /// (Places.Around): its moons, its own points of interest, then each moon's.</summary>
    public class SystemMapTests
    {
        readonly List<GameObject> made = new List<GameObject>();
        WarpPoint star, planet, moon, outerMoon, other;
        SystemMap map;

        [TearDown]
        public void TearDown()
        {
            foreach (var go in made)
            {
                if (go == null) continue;
                foreach (var w in go.GetComponents<WarpPoint>()) WarpPoint.All.Remove(w);
                Object.DestroyImmediate(go);
            }
            made.Clear();
        }

        WarpPoint Point(string name, WarpPoint.Kind kind, Vector3 at, float radius, WarpPoint parent = null)
        {
            var go = new GameObject(name);
            made.Add(go);
            go.transform.position = at;
            var w = go.AddComponent<WarpPoint>();
            w.displayName = name;
            w.kind = kind;
            w.bodyRadius = radius;
            w.arrivalDistance = Mathf.Max(5000f, radius * 1.5f);
            w.orbitParent = parent;
            if (!WarpPoint.All.Contains(w)) WarpPoint.All.Add(w);
            return w;
        }

        /// <summary>A system laid out as the Kestrel system is: a planet 4 million km out, its moons
        /// 1,500 and 2,200 km from it (so at the whole system's view, a fraction of a pixel).</summary>
        void MakeSystem()
        {
            star = Point("Test Star", WarpPoint.Kind.Star, Vector3.zero, 0f);
            planet = Point("Testa", WarpPoint.Kind.Planet, new Vector3(4.0e9f, 0f, 0f), 320000f);
            moon = Point("Testa Minor", WarpPoint.Kind.Moon, planet.Position + new Vector3(1.5e6f, 0f, 0f), 70000f, planet);
            outerMoon = Point("Testa Major", WarpPoint.Kind.Moon, planet.Position + new Vector3(0f, 0f, -2.2e6f), 45000f, planet);
            other = Point("Othera", WarpPoint.Kind.Planet, new Vector3(0f, 0f, 2.0e9f), 240000f);
            var go = new GameObject("Test map");
            made.Add(go);
            map = go.AddComponent<SystemMap>();
            map.screenSize = new Vector2(1920f, 1080f);
            map.RefreshOrder();
            map.ResetView();
            map.Layout();
        }

        [Test]
        public void AClickOnAPlanetTakesThePlanetNotAMoon()
        {
            MakeSystem();
            Vector2 at = map.ScreenPosOf(moon);
            Assert.That(Vector2.Distance(at, map.ScreenPosOf(planet)), Is.LessThan(1f), "at the whole system's view the moon is drawn on its planet");
            Assert.That(map.PassedOver(moon), Is.True);
            Assert.That(map.PassedOver(planet), Is.False, "a planet is never passed over");
            Assert.That(map.PointAt(at), Is.SameAs(planet), "a click on the moon's spot takes the planet");
            Assert.That(map.PointAt(map.ScreenPosOf(outerMoon)), Is.SameAs(planet));
            Assert.That(map.PointAt(map.ScreenPosOf(other)), Is.SameAs(other));
        }

        [Test]
        public void ZoomedInTheMoonCanBeChosen()
        {
            MakeSystem();
            map.ZoomAt(map.ScreenPosOf(planet), 1e6f);
            Assert.That(map.Zoom, Is.EqualTo(map.MaxZoom).Within(0.01f), "zoomed all the way in");
            Assert.That(Vector2.Distance(map.ScreenPosOf(moon), map.ScreenPosOf(planet)), Is.GreaterThan(SystemMap.MoonApartPx * Hud.S));
            Assert.That(map.PassedOver(moon), Is.False);
            Assert.That(map.PointAt(map.ScreenPosOf(moon)), Is.SameAs(moon), "drawn apart, a click on the moon takes the moon");
            Assert.That(map.PointAt(map.ScreenPosOf(planet)), Is.SameAs(planet), "and one on the planet the planet");
        }

        [Test]
        public void TabAndTheArrowsPassOverMoonsUntilZoomedIn()
        {
            MakeSystem();
            var seen = new HashSet<WarpPoint>();
            for (int i = 0; i < 10; i++) { map.Step(1); seen.Add(map.Selected); }
            Assert.That(seen, Has.Member(planet).And.Member(other).And.Member(star));
            Assert.That(seen, Has.No.Member(moon).And.No.Member(outerMoon), "at the whole system's view Tab never stops on a moon");
            for (int i = 0; i < 10; i++) { map.Step(-1); seen.Add(map.Selected); }
            Assert.That(seen, Has.No.Member(moon).And.No.Member(outerMoon), "nor does Shift+Tab");
            map.Select(planet);
            foreach (var dir in new[] { Vector2.left, Vector2.right, Vector2.up, Vector2.down })
            {
                map.Select(planet);
                map.StepToward(dir);
                Assert.That(map.Selected, Is.Not.SameAs(moon).And.Not.SameAs(outerMoon), $"an arrow ({dir}) doesn't step onto a moon either");
            }

            map.ZoomAt(map.ScreenPosOf(planet), 1e6f);
            seen.Clear();
            for (int i = 0; i < 10; i++) { map.Step(1); seen.Add(map.Selected); }
            Assert.That(seen, Has.Member(moon).And.Member(outerMoon), "zoomed in, Tab steps onto the moons");
            map.Select(planet);
            map.StepToward(Vector2.right);
            Assert.That(map.Selected, Is.SameAs(moon), "and the arrow toward a moon drawn apart chooses it");
        }

        [Test]
        public void AroundAPlanetAreItsMoonsThenItsOwnThenTheMoons()
        {
            MakeSystem();
            var high = Landmark("Testa High Orbit", planet, Vector3.up * 500000f);
            var north = Landmark("Testa Northern Reach", planet, new Vector3(0f, 240000f, 210000f));
            // (a station in orbit: its place is the body it is within reach of)
            var station = Point("Testa Highport", WarpPoint.Kind.Station, planet.Position + new Vector3(0f, 0f, 400000f), 0f);
            var moonHigh = Landmark("Testa Minor High Orbit", moon, Vector3.up * 120000f);
            var outerHigh = Landmark("Testa Major High Orbit", outerMoon, Vector3.up * 80000f);

            var around = Places.Around(planet);
            Assert.That(around.Take(2), Is.EqualTo(new[] { moon, outerMoon }), "the moons first, the nearer first");
            Assert.That(around.Skip(2).Take(3), Is.EquivalentTo(new[] { high, north, station }), "then the planet's own points of interest");
            Assert.That(around.Skip(5), Is.EqualTo(new[] { moonHigh, outerHigh }), "then each moon's, moon by moon");
            Assert.That(around, Is.Unique);
            Assert.That(Places.PointsOfInterest(planet, planet.Position), Has.No.Member(moon), "a moon is still a place of its own");

            Assert.That(Places.Around(moon), Is.EqualTo(new[] { moonHigh }), "round a moon: only its own");
            Assert.That(Places.Around(other), Is.Empty, "a planet with nothing round it");
            Assert.That(Places.Around(null), Is.Empty);
        }

        WarpPoint Landmark(string name, WarpPoint body, Vector3 offset)
        {
            var w = Point(name, WarpPoint.Kind.Landmark, body.Position + offset, 0f);
            w.follow = body;
            w.followOffset = offset;
            return w;
        }

        [Test]
        public void TheLocalMapShowsThePlanetItsMoonsAndWhatIsRoundThem()
        {
            MakeSystem();
            var high = Landmark("Testa High Orbit", planet, Vector3.up * 500000f);
            var moonHigh = Landmark("Testa Minor High Orbit", moon, Vector3.up * 120000f);
            Assert.That(map.OpenLocal(), Is.False, "not with the map closed");
            Assert.That(InputFocus.Take(InputFocus.Panel.SystemMap), Is.True);
            try
            {
                map.Select(other);
                Assert.That(map.LocalTarget, Is.SameAs(other), "a planet chosen: its local map");
                map.Select(star);
                Assert.That(map.LocalTarget, Is.Null, "the star (and the ship nowhere): no local map, and no button");
                Assert.That(map.LocalButtonRect.width, Is.Zero);
                map.Select(moon);
                Assert.That(map.LocalTarget, Is.SameAs(planet), "a moon chosen: its planet's");

                Assert.That(map.OpenLocal(), Is.True);
                Assert.That(map.Local, Is.True);
                Assert.That(map.LocalPlanet, Is.SameAs(planet));
                Assert.That(map.LocalPoints, Is.EqualTo(new[] { planet, moon, outerMoon, high, moonHigh }), "the planet, its moons, then what is round each");
                Assert.That(map.Selected, Is.SameAs(moon), "the choice kept: it is on the local map");
                Assert.That(map.LocalButtonRect.width, Is.GreaterThan(0f), "BACK beside WARP");
                Assert.That(map.PassedOver(moon), Is.False, "every moon is drawn apart");
                Assert.That(map.PointAt(map.ScreenPosOf(moon)), Is.SameAs(moon));
                Assert.That(map.PointAt(map.ScreenPosOf(outerMoon)), Is.SameAs(outerMoon));
                Assert.That(map.PointAt(map.ScreenPosOf(planet)), Is.SameAs(planet));
                float off = Vector2.Distance(map.ScreenPosOf(high), map.ScreenPosOf(planet));
                Assert.That(off, Is.GreaterThan(11f * Hud.S), "the high orbit, straight up, is drawn off the planet's disc, not on its middle");
                Assert.That(map.PointAt(map.ScreenPosOf(high)), Is.SameAs(high), "and can be clicked");
                var seen = new HashSet<WarpPoint>();
                for (int i = 0; i < 6; i++) { map.Step(1); seen.Add(map.Selected); }
                Assert.That(seen, Is.EquivalentTo(map.LocalPoints), "Tab steps through all of it");
                Assert.That(map.CanFlyTo(high), Is.False, "the ship isn't at the planet: a warp, not a flight");

                map.Select(moonHigh);
                map.CloseLocal();
                Assert.That(map.Local, Is.False);
                Assert.That(map.LocalPoints, Is.Empty);
                Assert.That(map.Selected, Is.SameAs(moon), "back on the system map, a point of interest gives way to its place");
            }
            finally
            {
                InputFocus.Release(InputFocus.Panel.SystemMap);
            }
        }
    }
}
