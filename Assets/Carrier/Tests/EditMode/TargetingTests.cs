using NUnit.Framework;
using UnityEngine;

namespace CarrierGame.Tests
{
    /// <summary>Aiming ahead of a target, and which player's ship a hostile goes for.</summary>
    public class TargetingTests
    {
        [Test]
        public void AStillTargetIsAimedAtWhereItIs()
        {
            var p = new Vector3(100f, 20f, 300f);
            Assert.That(Ballistics.Lead(Vector3.zero, p, Vector3.zero, 500f, refine: 2), Is.EqualTo(p));
        }

        [Test]
        public void AShotWithNoSpeedAimsAtTheTarget()
        {
            var p = new Vector3(0f, 0f, 1000f);
            Assert.That(Ballistics.Lead(Vector3.zero, p, new Vector3(50f, 0f, 0f), 0f), Is.EqualTo(p));
        }

        [Test]
        public void OnePassLeadsByTheTimeToWhereItIsNow()
        {
            // 1000 m away at 500 m/s: 2 s, in which it moves 100 m sideways
            var aim = Ballistics.Lead(Vector3.zero, new Vector3(0f, 0f, 1000f), new Vector3(50f, 0f, 0f), 500f);
            Assert.That(aim.x, Is.EqualTo(100f).Within(1e-3f));
            Assert.That(aim.z, Is.EqualTo(1000f).Within(1e-3f));
        }

        [Test]
        public void MorePassesCloseInOnWhereShotAndTargetMeet()
        {
            Vector3 origin = Vector3.zero, p = new Vector3(0f, 0f, 1000f), v = new Vector3(200f, 0f, 0f);
            const float speed = 500f;
            float Miss(Vector3 aim)
            {
                // the shot reaches `aim` after |aim| / speed; where is the target then?
                float t = Vector3.Distance(origin, aim) / speed;
                return Vector3.Distance(aim, p + v * t);
            }
            float one = Miss(Ballistics.Lead(origin, p, v, speed)), three = Miss(Ballistics.Lead(origin, p, v, speed, refine: 2));
            Assert.That(three, Is.LessThan(one));
            Assert.That(three, Is.LessThan(5f));
        }

        [Test]
        public void TheFlightTimeCanBeCapped()
        {
            // 10 km at 100 m/s would be 100 s; capped at 3 s it leads by 3 s of the target's motion
            var aim = Ballistics.Lead(Vector3.zero, new Vector3(0f, 0f, 10000f), new Vector3(10f, 0f, 0f), 100f, maxTime: 3f);
            Assert.That(aim.x, Is.EqualTo(30f).Within(1e-3f));
        }

        // -------------------------------------------------------------- PlayerShips.Retarget

        GameObject a, b;

        [TearDown]
        public void Clear()
        {
            foreach (var go in new[] { a, b })
                if (go != null) { PlayerShips.Remove(go.transform); Object.DestroyImmediate(go); }
            a = b = null;
        }

        Transform Ship(ref GameObject go, string name, Vector3 at)
        {
            go = new GameObject(name);
            go.transform.position = at;
            PlayerShips.Add(go.transform);
            return go.transform;
        }

        [Test]
        public void WithOneShipTheTargetStays()
        {
            var only = Ship(ref a, "A", new Vector3(0f, 0f, 5000f));
            float next = 0f;
            Assert.That(PlayerShips.Retarget(only, Vector3.zero, ref next, 0f), Is.SameAs(only));
        }

        [Test]
        public void WithTwoTheNearestIsTaken()
        {
            var far = Ship(ref a, "Far", new Vector3(0f, 0f, 5000f));
            var near = Ship(ref b, "Near", new Vector3(0f, 0f, 800f));
            float next = 0f;
            Assert.That(PlayerShips.Retarget(far, Vector3.zero, ref next, 0f), Is.SameAs(near));
        }

        [Test]
        public void ItIsntLookedForAgainTooSoon()
        {
            var far = Ship(ref a, "Far", new Vector3(0f, 0f, 5000f));
            Ship(ref b, "Near", new Vector3(0f, 0f, 800f));
            float next = Time.time + 60f;        // looked a moment ago
            Assert.That(PlayerShips.Retarget(far, Vector3.zero, ref next), Is.SameAs(far));
        }
    }
}
