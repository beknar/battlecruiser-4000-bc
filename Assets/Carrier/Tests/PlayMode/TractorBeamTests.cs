using System.Collections;
using CarrierGame.Demo;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CarrierGame.Tests
{
    /// <summary>Tractor beams (TractorBeam, CarrierController.Tractor): the pull toward the emitter,
    /// the speed held down, one beam out-flown on working engines but not on knocked-out ones, two
    /// beams not at all, the lock breaking out of range or with the beam's ship, no warp while
    /// held, and co-op's message.</summary>
    public class TractorBeamTests : PlayModeFixture
    {
        /// <summary>A ship (with its components) at a point, its nose along `facing`.</summary>
        CarrierController Ship(Vector3 at, Vector3 facing, float maxSpeed = 100f, bool warp = false)
        {
            var go = Make("Frigate", at);
            go.SetActive(false);
            go.transform.rotation = Quaternion.LookRotation(facing);
            go.AddComponent<Health>().maxHealth = 1000f;
            var cc = go.AddComponent<CarrierController>();
            cc.maxSpeed = maxSpeed;
            cc.acceleration = 1000f;
            cc.turnRate = 0f;
            if (warp) go.AddComponent<WarpDrive>();
            go.SetActive(true);
            return cc;
        }

        TractorBeam Beam(Vector3 at, bool withHull = false)
        {
            var go = Make("Snare", at);
            if (withHull)
            {
                var h = go.AddComponent<Health>();
                h.maxHealth = 100f;
                h.respawnDelay = 60f;                   // (destroyed, it waits to come back, as a Snare does)
            }
            return go.AddComponent<TractorBeam>();
        }

        static float Away(CarrierController cc, TractorBeam b) => Vector3.Distance(cc.transform.position, b.From);

        [Test]
        public void OneBeamCanBeOutflownTwoOrAKnockedOutEngineCannot()
        {
            TractorBeam.Strength(1, false, 150f, 2400f, out float pull, out float cap);
            Assert.That(pull, Is.EqualTo(TractorBeam.PullSpeed));
            Assert.That(cap, Is.GreaterThan(pull), "one beam: working engines can out-fly it");
            TractorBeam.Strength(1, true, 150f, 2400f, out pull, out cap);
            Assert.That(cap, Is.LessThan(pull), "held fast (engines out): they can't");
            TractorBeam.Strength(2, false, 300f, 2400f, out pull, out cap);
            Assert.That(pull, Is.EqualTo(2f * TractorBeam.PullSpeed));
            Assert.That(cap, Is.LessThan(pull), "two beams: no ship can");
            TractorBeam.Strength(1, false, 150f, TractorBeam.HoldDistance, out pull, out _);
            Assert.That(pull, Is.EqualTo(0f), "it stops pulling at its hold distance");
        }

        [UnityTest]
        public IEnumerator ItPullsAShipTowardTheEmitter()
        {
            var cc = Ship(new Vector3(0f, 0f, 2000f), Vector3.right);
            var beam = Beam(Vector3.zero);
            yield return null;
            Assert.That(beam.Lock(cc.transform, out string why), Is.True, why);
            Assert.That(TractorBeam.HeldTargets, Has.Member(cc.transform));
            Assert.That(TractorBeam.BeamsOn(cc.transform), Is.EqualTo(1));
            float h0 = cc.Heading;
            yield return new WaitForSeconds(1f);
            Assert.That(cc.Tractored, Is.True);
            Assert.That(cc.TractorBeams, Is.EqualTo(1));
            Assert.That(Away(cc, beam), Is.LessThan(2000f - 15f), "pulled in at about 30 m/s");
            Assert.That(cc.TractorPulled, Is.GreaterThan(15f));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(cc.Heading, 0f)), Is.LessThan(Mathf.Abs(Mathf.DeltaAngle(h0, 0f))),
                        "turned toward stern on to the beam (its nose away)");
        }

        [UnityTest]
        public IEnumerator ItHoldsTheShipsSpeedDown()
        {
            var cc = Ship(new Vector3(0f, 0f, 2000f), Vector3.right, maxSpeed: 400f);
            var beam = Beam(Vector3.zero);
            yield return null;
            cc.throttle = 1f;
            yield return new WaitForSeconds(0.6f);
            Assert.That(cc.Speed, Is.GreaterThan(350f), "free, it goes as fast as it can");
            Assert.That(beam.Lock(cc.transform, out string why), Is.True, why);
            // (time to brake from 400 m/s down to the cap)
            yield return new WaitForSeconds(1f);
            Assert.That(cc.tractorCap, Is.EqualTo(400f * TractorBeam.EscapeShare).Within(0.01f));
            Assert.That(cc.Speed, Is.LessThanOrEqualTo(cc.tractorCap + 0.01f), "held to its share of top speed");
            beam.Release("test");
            yield return new WaitForSeconds(0.5f);
            Assert.That(cc.Tractored, Is.False, "the hold ends a moment after the beam lets go");
            Assert.That(float.IsPositiveInfinity(cc.tractorCap), Is.True);
        }

        [UnityTest]
        public IEnumerator OneBeamIsOutflownOnWorkingEnginesButNotWithThemKnockedOut()
        {
            // flying straight away at full thrust, 50 m inside where it lets go
            var cc = Ship(new Vector3(0f, 0f, 2450f), Vector3.forward, maxSpeed: 150f);
            var beam = Beam(Vector3.zero);
            beam.breakFactor = 1f;
            yield return null;
            cc.throttle = 1f;
            Assert.That(beam.Lock(cc.transform, out string why), Is.True, why);
            float until = Time.time + 4f;
            while (beam.Locked && Time.time < until) yield return null;
            Assert.That(beam.Locked, Is.False, "full thrust away breaks one beam");
            Assert.That(beam.LastRelease, Is.EqualTo("out of range"));

            // the same with its engines knocked out: pulled in instead
            cc.throttle = 0f;
            cc.transform.position = new Vector3(0f, 0f, 2450f);
            cc.SetMotion(0f);
            var sys = cc.GetComponent<ShipSystems>();
            sys.SetCondition(ShipSystem.Engines, 0f);
            cc.throttle = 1f;
            Assert.That(beam.Lock(cc.transform, out why), Is.True, why);
            yield return new WaitForSeconds(2f);
            Assert.That(beam.Locked, Is.True, "with its engines out it can't get away");
            Assert.That(cc.TractorFirm, Is.True);
            Assert.That(Away(cc, beam), Is.LessThan(2450f), "and is pulled in");
        }

        [UnityTest]
        public IEnumerator TwoBeamsCantBeOutflown()
        {
            var cc = Ship(new Vector3(0f, 0f, 2450f), Vector3.forward, maxSpeed: 400f);
            var a = Beam(Vector3.zero);
            var b = Beam(new Vector3(60f, 0f, 0f));
            a.breakFactor = b.breakFactor = 1f;
            yield return null;
            cc.throttle = 1f;
            Assert.That(a.Lock(cc.transform, out string why), Is.True, why);
            Assert.That(b.Lock(cc.transform, out why), Is.True, why);
            yield return new WaitForSeconds(2f);
            Assert.That(a.Locked && b.Locked, Is.True, "both still hold it");
            Assert.That(cc.TractorBeams, Is.EqualTo(2));
            Assert.That(cc.TractorFirm, Is.True);
            Assert.That(TractorBeam.Firm(cc.transform), Is.True);
            Assert.That(cc.Speed, Is.LessThanOrEqualTo(2f * TractorBeam.PullSpeed * TractorBeam.FirmShare + 0.01f));
            Assert.That(Away(cc, a), Is.LessThan(2450f), "pulled in, full thrust or not");
        }

        [UnityTest]
        public IEnumerator TheLockBreaksOutOfRangeOrWithTheBeamsShip()
        {
            var cc = Ship(new Vector3(0f, 0f, 3000f), Vector3.forward);
            var beam = Beam(Vector3.zero, withHull: true);
            yield return null;
            Assert.That(beam.Lock(cc.transform, out string why), Is.False, "too far to lock on");
            Assert.That(why, Is.EqualTo("out of range"));

            cc.transform.position = new Vector3(0f, 0f, 2000f);
            Assert.That(beam.Lock(cc.transform, out why), Is.True, why);
            cc.transform.position = new Vector3(0f, 0f, 2500f * 1.3f + 50f);
            yield return null;
            yield return null;
            Assert.That(beam.Locked, Is.False);
            Assert.That(beam.LastRelease, Is.EqualTo("out of range"));
            Assert.That(TractorBeam.HeldTargets, Has.No.Member(cc.transform));

            cc.transform.position = new Vector3(0f, 0f, 2000f);
            Assert.That(beam.Lock(cc.transform, out why), Is.True, why);
            beam.GetComponent<Health>().Damage(1000f);
            yield return null;
            yield return null;
            Assert.That(beam.Locked, Is.False, "its ship destroyed, the beam lets go");
            Assert.That(beam.LastRelease, Is.EqualTo("destroyed"));
            yield return new WaitForSeconds(0.4f);
            Assert.That(cc.Tractored, Is.False);
        }

        [UnityTest]
        public IEnumerator ItLetsGoWithItsWeaponsKnockedOut()
        {
            var cc = Ship(new Vector3(0f, 0f, 2000f), Vector3.forward);
            var beam = Beam(Vector3.zero, withHull: true);
            var sys = beam.gameObject.AddComponent<ShipSystems>();
            yield return null;
            Assert.That(beam.Lock(cc.transform, out string why), Is.True, why);
            sys.SetCondition(ShipSystem.Weapons, 0f);
            yield return null;
            yield return null;
            Assert.That(beam.Locked, Is.False);
            Assert.That(beam.LastRelease, Is.EqualTo("its weapons are knocked out"));
            Assert.That(beam.Lock(cc.transform, out why), Is.False);
        }

        [UnityTest]
        public IEnumerator NoWarpWhileHeld()
        {
            var cc = Ship(new Vector3(0f, 0f, 2000f), Vector3.forward, warp: true);
            var drive = cc.GetComponent<WarpDrive>();
            var beam = Beam(Vector3.zero);
            yield return null;
            Assert.That(beam.Lock(cc.transform, out string why), Is.True, why);
            yield return null;
            Assert.That(drive.CanWarp(out why), Is.False);
            Assert.That(why, Is.EqualTo("held by a tractor beam"));
            Assert.That(drive.InCombat(out _), Is.True, "and so no docking or landing either");
            beam.Release("test");
            yield return new WaitForSeconds(0.4f);
            drive.CanWarp(out why);
            Assert.That(why, Does.Not.Contain("tractor"), "free again");
        }

        [Test]
        public void TheHoldGoesOverTheCoopWire()
        {
            string json = JsonUtility.ToJson(new TractorMsg { to = 4, on = true, n = 2, held = true, rf = "Thalassa", x = 1.5, y = 2, z = -3 });
            StringAssert.Contains("\"t\":\"tr\"", json);
            var m = JsonUtility.FromJson<CoopMsg>(json);
            Assert.That(m.t, Is.EqualTo(CoopWire.Tractor));
            Assert.That(m.on && m.held, Is.True);
            Assert.That(m.n, Is.EqualTo(2));
            Assert.That(m.rf, Is.EqualTo("Thalassa"));
            Assert.That(m.x, Is.EqualTo(1.5));
            CollectionAssert.DoesNotContain(CoopWire.ServerOnly, CoopWire.Tractor, "games send it");
            // an enemy from an older game has no beam to show
            var e = JsonUtility.FromJson<CoopEntity>("{\"id\":\"a\"}");
            Assert.That(e.tr, Is.EqualTo(0));
        }
    }
}
