using CarrierGame.Demo;
using NUnit.Framework;
using UnityEngine;

namespace CarrierGame.Tests
{
    /// <summary>Ships flown by an autopilot steering round each other (ShipAvoidance): the dodge
    /// for a ship in the way, worked out by each of the two ships from its own side, and the
    /// backstop that keeps two hulls apart.</summary>
    public class ShipAvoidanceTests
    {
        const float R = 90f, Spd = 100f, H = 10f;

        /// <summary>The push for a ship at p0 flying along v0 (speed Spd), the other at pj with vj.</summary>
        static Vector3 PushFor(Vector3 p0, Vector3 v0, Vector3 pj, Vector3 vj, out float k, float goal = 20000f) =>
            ShipAvoidance.Push(p0, v0, R, Spd, goal, H, Vector3.up, v0.normalized, pj, vj, R, null, out _, out k);

        [Test]
        public void HeadOnTheTwoTurnOppositeWays()
        {
            Vector3 a = Vector3.zero, b = new Vector3(0f, 0f, 1500f);
            Vector3 va = Vector3.forward * Spd, vb = Vector3.back * Spd;
            var pa = PushFor(a, va, b, vb, out float ka);
            var pb = PushFor(b, vb, a, va, out float kb);
            Assert.That(pa.magnitude, Is.GreaterThan(0.1f), "A dodges");
            Assert.That(pb.magnitude, Is.GreaterThan(0.1f), "B dodges");
            Assert.That(Vector3.Dot(pa.normalized, pb.normalized), Is.LessThan(-0.9f), "to opposite sides: never into each other");
            Assert.That(ka, Is.LessThan(1f), "A eases off for a ship ahead");
        }

        [Test]
        public void ANearMissIsWidenedAwayFromTheOther()
        {
            // B passes 60 m to A's starboard: A goes to port
            Vector3 a = Vector3.zero, b = new Vector3(60f, 0f, 1500f);
            var pa = PushFor(a, Vector3.forward * Spd, b, Vector3.back * Spd, out _);
            Assert.That(pa.x, Is.LessThan(0f));
            var pb = PushFor(b, Vector3.back * Spd, a, Vector3.forward * Spd, out _);
            Assert.That(pb.x, Is.GreaterThan(0f), "and B to its own other side");
        }

        [Test]
        public void OvertakingASlowerShipGoesRoundIt()
        {
            Vector3 a = Vector3.zero, b = new Vector3(0f, 0f, 500f);
            var pa = PushFor(a, Vector3.forward * Spd, b, Vector3.forward * 30f, out float k);
            Assert.That(pa.magnitude, Is.GreaterThan(0.05f));
            Assert.That(k, Is.LessThan(1f));
        }

        [Test]
        public void ShipsThatWillPassWideOrAreGoingAwayAreLeftAlone()
        {
            // a kilometre to the side, on a parallel course
            Assert.That(PushFor(Vector3.zero, Vector3.forward * Spd, new Vector3(1000f, 0f, 2000f), Vector3.back * Spd, out _), Is.EqualTo(Vector3.zero));
            // behind and going the other way
            Assert.That(PushFor(Vector3.zero, Vector3.forward * Spd, new Vector3(0f, 0f, -1500f), Vector3.back * Spd, out _), Is.EqualTo(Vector3.zero));
            // a ship beyond where it is going (it would meet it in 15 s, but is there in 10): there first
            Assert.That(ShipAvoidance.Push(Vector3.zero, Vector3.forward * Spd, R, Spd, 1000f, 20f, Vector3.up, Vector3.forward,
                                           new Vector3(0f, 0f, 1500f), Vector3.zero, R, null, out _, out _), Is.EqualTo(Vector3.zero));
            // (the same ship, the goal beyond it: in the way)
            Assert.That(ShipAvoidance.Push(Vector3.zero, Vector3.forward * Spd, R, Spd, 5000f, 20f, Vector3.up, Vector3.forward,
                                           new Vector3(0f, 0f, 1500f), Vector3.zero, R, null, out _, out _), Is.Not.EqualTo(Vector3.zero));
        }

        [Test]
        public void AFormationSlotNeighbourDoesntPush()
        {
            // flying alongside, the same way at the same speed, 200 m apart (inside the margin): no closing at all
            Assert.That(PushFor(Vector3.zero, Vector3.forward * Spd, new Vector3(200f, 0f, 0f), Vector3.forward * Spd, out _), Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void NearlyDeadOnTheTwoStillGoOppositeWays()
        {
            // every small offset either side of dead on: the two sides are always opposite
            foreach (float x in new[] { -40f, -25f, -10f, 0f, 10f, 25f, 40f })
            {
                Vector3 a = Vector3.zero, b = new Vector3(x, 0f, 1500f);
                ShipAvoidance.Push(a, Vector3.forward * Spd, R, Spd, 20000f, H, Vector3.up, Vector3.forward, b, Vector3.back * Spd, R, null, out var sa, out _);
                ShipAvoidance.Push(b, Vector3.back * Spd, R, Spd, 20000f, H, Vector3.up, Vector3.back, a, Vector3.forward * Spd, R, null, out var sb, out _);
                Assert.That(Vector3.Dot(sa, sb), Is.LessThan(-0.9f), $"offset {x}");
            }
        }

        [Test]
        public void TheSideOnceChosenIsKept()
        {
            Vector3 kept = Vector3.left;
            var p = ShipAvoidance.Push(Vector3.zero, Vector3.forward * Spd, R, Spd, 20000f, H, Vector3.up, Vector3.forward,
                                       new Vector3(40f, 0f, 1500f), Vector3.back * Spd, R, kept, out var side, out _);
            Assert.That(side, Is.EqualTo(kept));
            Assert.That(p.x, Is.LessThan(0f));
        }

        [Test]
        public void ClosestPointsOfTwoSegments()
        {
            ShipAvoidance.ClosestPoints(new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f), new Vector3(0f, 5f, -10f), new Vector3(0f, 5f, 10f), out var pa, out var pb);
            Assert.That(Vector3.Distance(pa, Vector3.zero), Is.LessThan(1e-3f));
            Assert.That(Vector3.Distance(pb, new Vector3(0f, 5f, 0f)), Is.LessThan(1e-3f));
            // parallel, overlapping
            ShipAvoidance.ClosestPoints(Vector3.zero, Vector3.forward * 10f, new Vector3(3f, 0f, 5f), new Vector3(3f, 0f, 20f), out pa, out pb);
            Assert.That(Vector3.Distance(pa, pb), Is.EqualTo(3f).Within(1e-3f));
        }

        [Test]
        public void OverlappingHullsArePushedApartAndClearOnesNot()
        {
            // two 150 m hulls side by side, 40 m between their centre lines, 30 m radius each: 20 m too close
            var push = ShipAvoidance.Separation(Vector3.zero, Vector3.forward, 45f, 30f, new Vector3(40f, 0f, 0f), Vector3.forward, 45f, 30f, Vector3.up);
            Assert.That(push.x, Is.LessThan(0f));
            Assert.That(push.magnitude, Is.EqualTo(20f).Within(0.01f));
            Assert.That(ShipAvoidance.Separation(Vector3.zero, Vector3.forward, 45f, 30f, new Vector3(100f, 0f, 0f), Vector3.forward, 45f, 30f, Vector3.up),
                        Is.EqualTo(Vector3.zero));
            // nose to tail, end on
            push = ShipAvoidance.Separation(Vector3.zero, Vector3.forward, 45f, 30f, new Vector3(0f, 0f, 120f), Vector3.forward, 45f, 30f, Vector3.up);
            Assert.That(push.z, Is.LessThan(0f));
        }
    }
}
