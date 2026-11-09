using CarrierGame.Demo;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CarrierGame.Tests
{
    /// <summary>Orbits: Kepler's laws and the Lagrange points.</summary>
    public class OrbitTests
    {
        GameObject go;
        double savedStarGM;

        [SetUp]
        public void SetUp()
        {
            savedStarGM = OrbitalSystem.StarGM;
            OrbitalSystem.StarGM = 2.839e9;
            go = new GameObject("Orbits");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(go);
            OrbitalSystem.StarGM = savedStarGM;
        }

        Orbit Make(double a, double e = 0.0, Orbit parent = null, float massRatio = 0f)
        {
            var o = new GameObject("orbit").AddComponent<Orbit>();
            o.transform.SetParent(go.transform);
            o.semiMajor = a;
            o.eccentricity = e;
            o.parent = parent;
            o.massRatio = massRatio;
            return o;
        }

        static void Near(Vector3 got, Vector3 want, float tolerance, string what = "") =>
            Assert.That(Vector3.Distance(got, want), Is.LessThan(tolerance), $"{what} {got} vs {want}");

        [Test]
        public void ThePeriodFollowsKeplersThirdLaw()
        {
            var o = Make(200000.0);
            double want = 2.0 * System.Math.PI * System.Math.Sqrt(200000.0 * 200000.0 * 200000.0 / OrbitalSystem.StarGM);
            Assert.That(o.Period, Is.EqualTo(want).Within(want * 1e-9));
            Assert.That(o.MeanMotion, Is.EqualTo(2.0 * System.Math.PI / want).Within(1e-12));
            // four times as far out: eight times as long
            Assert.That(Make(800000.0).Period / o.Period, Is.EqualTo(8.0).Within(1e-6));
        }

        [Test]
        public void ACircularOrbitStaysAtItsRadiusAndComesRound()
        {
            var o = Make(100000.0);
            double T = o.Period;
            for (int i = 0; i <= 8; i++)
                Assert.That(o.Offset(T * i / 8.0).magnitude, Is.EqualTo(100000f).Within(1f));
            Near(o.Offset(0.0), new Vector3(100000f, 0f, 0f), 1f, "starts at periapsis");
            Near(o.Offset(T / 4.0), new Vector3(0f, 0f, 100000f), 5f, "a quarter round: ahead");
            Near(o.Offset(T / 2.0), new Vector3(-100000f, 0f, 0f), 5f, "half way round");
            Near(o.Offset(T), o.Offset(0.0), 5f, "back after one period");
        }

        [Test]
        public void AnEllipseIsNearestAtPeriapsisAndFarthestHalfARoundLater()
        {
            var o = Make(100000.0, 0.5);
            Assert.That(o.Offset(0.0).magnitude, Is.EqualTo(50000f).Within(1f));
            Assert.That(o.Offset(o.Period / 2.0).magnitude, Is.EqualTo(150000f).Within(2f));
            Assert.That(o.Velocity(0.0).magnitude, Is.GreaterThan(o.Velocity(o.Period / 2.0).magnitude * 2.5f), "fast near the star, slow far out");
        }

        [Test]
        public void AnEllipseSweepsEqualAreasInEqualTimes()
        {
            var o = Make(150000.0, 0.6);
            float h0 = Vector3.Cross(o.Offset(0.0), o.Velocity(0.0)).magnitude;
            for (int i = 1; i < 10; i++)
            {
                double t = o.Period * i / 10.0;
                float h = Vector3.Cross(o.Offset(t), o.Velocity(t)).magnitude;
                Assert.That(h, Is.EqualTo(h0).Within(h0 * 0.01f), $"at {i}/10 of the orbit");
            }
        }

        [Test]
        public void TheEllipsePointsAreOnTheOrbit()
        {
            var o = Make(100000.0, 0.3);
            Near(o.EllipsePoint(0.0), o.Offset(0.0), 1f);
            Near(o.EllipsePoint(System.Math.PI), o.Offset(o.Period / 2.0), 2f);
        }

        [Test]
        public void AMoonGoesRoundItsPlanetsMass()
        {
            var planet = Make(1e6, massRatio: 0.001f);
            var moon = Make(5000.0, parent: planet);
            Assert.That(planet.BodyGM, Is.EqualTo((double)0.001f * OrbitalSystem.StarGM).Within(1e-3));
            Assert.That(moon.PrimaryGM, Is.EqualTo(planet.BodyGM));
            double want = 2.0 * System.Math.PI * System.Math.Sqrt(5000.0 * 5000.0 * 5000.0 / planet.BodyGM);
            Assert.That(moon.Period, Is.EqualTo(want).Within(want * 1e-9));
        }

        [Test]
        public void TheHillAndLibrationFractions()
        {
            Assert.That(Orbit.HillFraction(0.003), Is.EqualTo(System.Math.Pow(0.001, 1.0 / 3.0)).Within(1e-12));
            Assert.That(Orbit.HillFraction(-1.0), Is.EqualTo(0.0));
            Assert.That(Orbit.LibrationFraction(4.0 / 27.0), Is.EqualTo(1.0).Within(1e-12));
        }

        [Test]
        public void TheFiveLagrangePoints()
        {
            var s = new Vector3(100000f, 0f, 0f);
            double mu = 0.003;
            float k = (float)Orbit.HillFraction(mu);
            Near(Orbit.LagrangePoint(s, Vector3.right, Vector3.forward, mu, 1), s * (1f - k), 1f, "L1 inside, on the line");
            Near(Orbit.LagrangePoint(s, Vector3.right, Vector3.forward, mu, 2), s * (1f + k), 1f, "L2 outside, on the line");
            Assert.That(Vector3.Dot(Orbit.LagrangePoint(s, Vector3.right, Vector3.forward, mu, 3).normalized, s.normalized), Is.EqualTo(-1f).Within(1e-5f), "L3 opposite");
            var l4 = Orbit.LagrangePoint(s, Vector3.right, Vector3.forward, mu, 4);
            var l5 = Orbit.LagrangePoint(s, Vector3.right, Vector3.forward, mu, 5);
            foreach (var l in new[] { l4, l5 })
            {
                // the star, the planet and the point make an equilateral triangle
                Assert.That(l.magnitude, Is.EqualTo(s.magnitude).Within(1f));
                Assert.That(Vector3.Distance(l, s), Is.EqualTo(s.magnitude).Within(1f));
            }
            Assert.That(l4.z, Is.GreaterThan(0f), "L4 ahead of the planet");
            Assert.That(l5.z, Is.LessThan(0f), "L5 behind it");
            var swung = Orbit.LagrangePoint(s, Vector3.right, Vector3.forward, mu, 4, 10.0);
            Assert.That(Vector3.Angle(s, swung), Is.EqualTo(70f).Within(0.01f), "libration swings it along the orbit");
        }

        [Test]
        public void ALagrangeOrbitKeepsItsPlaceWithThePlanet()
        {
            var planet = Make(300000.0, massRatio: 0.001f);
            var l4 = Make(0.0);
            l4.kind = Orbit.Kind.Lagrange;
            l4.secondary = planet;
            l4.point = 4;
            Assert.That(l4.Period, Is.EqualTo(planet.Period));
            for (int i = 0; i < 6; i++)
            {
                double t = planet.Period * i / 6.0;
                Vector3 p = planet.Offset(t), q = l4.Offset(t);
                Assert.That(Vector3.Angle(p, q), Is.EqualTo(60f).Within(0.05f), $"60 degrees ahead at {i}/6");
                Assert.That(q.magnitude, Is.EqualTo(p.magnitude).Within(2f));
            }
        }
    }

    /// <summary>Comets' tails, noise for shapes, and place names.</summary>
    public class WorldMathTests
    {
        [Test]
        public void ACometsTailsPointAwayFromTheStarAndTheDustLags()
        {
            var comet = new Vector3(50000f, 2000f, -30000f);
            var travel = new Vector3(0.3f, 0f, 1f).normalized;
            var r = Comet.TailRotation(comet, Vector3.zero, travel);
            Assert.That(Vector3.Angle(r * Vector3.forward, comet), Is.LessThan(0.01f), "straight away from the star");
            Assert.That(Vector3.Dot(r * Vector3.right, -travel), Is.GreaterThan(0f), "the dust falls behind");
        }

        [Test]
        public void ACometsTailsAreSoundInOddCases()
        {
            // at the star, and moving straight out from it
            foreach (var r in new[] { Comet.TailRotation(Vector3.zero, Vector3.zero, Vector3.forward), Comet.TailRotation(new Vector3(0, 0, 1000), Vector3.zero, Vector3.forward) })
            {
                Vector3 f = r * Vector3.forward;
                Assert.That(float.IsNaN(f.x) || float.IsNaN(r.w), Is.False);
                Assert.That(f.magnitude, Is.EqualTo(1f).Within(1e-4f));
            }
        }

        [Test]
        public void TheDustTailBends()
        {
            Assert.That(Comet.DustCurve(0f, 0.01f), Is.EqualTo(Vector3.zero));
            Assert.That(Comet.DustCurve(100f, 0.01f), Is.EqualTo(new Vector3(100f, 0f, 100f)));
            Assert.That(Comet.DustCurve(200f, 0.01f).x, Is.EqualTo(4f * Comet.DustCurve(100f, 0.01f).x).Within(1e-3f));
        }

        [Test]
        public void TheNoiseIsTheSameEveryTimeAndInRange()
        {
            Assert.That(ProcNoise.Hash(1, 2, 3, 4), Is.EqualTo(ProcNoise.Hash(1, 2, 3, 4)));
            Assert.That(ProcNoise.Hash(1, 2, 3, 4), Is.Not.EqualTo(ProcNoise.Hash(1, 2, 3, 5)));
            var rng = new System.Random(1);
            for (int i = 0; i < 500; i++)
            {
                var p = new Vector3((float)rng.NextDouble() * 200f - 100f, (float)rng.NextDouble() * 200f - 100f, (float)rng.NextDouble() * 200f - 100f);
                Assert.That(ProcNoise.Hash(i, -i, i * 3, 7), Is.InRange(0f, 1f));
                Assert.That(ProcNoise.Value(p, 11), Is.InRange(0f, 1f));
                Assert.That(ProcNoise.Fbm(p, 11, 5), Is.InRange(-1f, 1f));
                Assert.That(ProcNoise.Ridged(p, 11, 5), Is.InRange(0f, 1f));
                Assert.That(ProcNoise.Value(p, 11), Is.EqualTo(ProcNoise.Value(p, 11)));
            }
        }

        [Test]
        public void ValueNoiseIsSmoothAndPassesThroughItsLattice()
        {
            Assert.That(ProcNoise.Value(new Vector3(3f, -2f, 5f), 9), Is.EqualTo(ProcNoise.Hash(3, -2, 5, 9)).Within(1e-6f));
            var p = new Vector3(1.3f, 2.7f, -0.4f);
            Assert.That(Mathf.Abs(ProcNoise.Value(p, 9) - ProcNoise.Value(p + new Vector3(0.001f, 0f, 0f), 9)), Is.LessThan(0.01f));
        }

        [TestCase(2.4f, -71f, "2°N 71°W")]
        [TestCase(-2f, 109f, "2°S 109°E")]
        [TestCase(0f, 0f, "0°N 0°E")]
        public void LatitudeAndLongitudeRead(float lat, float lon, string text) =>
            Assert.That(PlanetLanding.LatLonText(lat, lon), Is.EqualTo(text));
    }
}
