using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CarrierGame.Tests
{
    /// <summary>A jammer (EcmEmitter, Jamming): its field falling off to its edge, ECCM cutting it,
    /// a jammed ship's sensors seeing less far and naming less, a missile losing its lock and
    /// taking it again, ghost contacts seen and seen through, an escort shown at a false place,
    /// a wingman's radio garbled.</summary>
    public class JammingTests : PlayModeFixture
    {
        Transform player;

        [SetUp]
        public void Player()
        {
            player = Make("Player ship").transform;
            PlayerShips.Add(player);
            Sensors.Forget();
        }

        [TearDown]
        public void NoPlayer() { PlayerShips.Remove(player); Sensors.Forget(); }

        EcmEmitter Jammer(Vector3 at, float strength = 0.6f, float radius = 6000f)
        {
            var go = Make("Jammer");
            go.transform.position = at;
            var e = go.AddComponent<EcmEmitter>();
            e.strength = strength;
            e.radius = radius;
            e.ghosts = 0;
            return e;
        }

        [Test]
        public void ItsFieldFallsOffToItsEdgeAndECCMCutsIt()
        {
            var e = Jammer(Vector3.zero);
            Assert.That(e.At(Vector3.zero), Is.EqualTo(0.6f).Within(1e-4f));
            Assert.That(e.At(new Vector3(3000f, 0f, 0f)), Is.EqualTo(0.45f).Within(1e-4f), "halfway out: three quarters of it");
            Assert.That(e.At(new Vector3(6100f, 0f, 0f)), Is.EqualTo(0f), "outside its field: none");
            player.position = new Vector3(3000f, 0f, 0f);
            Assert.That(Jamming.On(player), Is.EqualTo(0.45f).Within(1e-4f), "no ECCM: all of it");
            player.gameObject.AddComponent<FrigateClass>().eccm = 1f;
            Assert.That(Jamming.On(player), Is.EqualTo(0.45f * 0.3f).Within(1e-4f), "ECCM 1: 30 % of it");
            e.active = false;
            Assert.That(Jamming.On(player), Is.EqualTo(0f), "switched off: none");
            Assert.That(Jamming.Any, Is.False);
        }

        [Test]
        public void AJammedShipSeesLessFarAndNamesLess()
        {
            var contact = Make("Pirate").transform;
            contact.position = player.position + Vector3.forward * 20000f;
            Assert.That(Sensors.DetectedFrom(contact, player.position, player), Is.True, "20 km: inside 30");
            Assert.That(Sensors.Identified(contact), Is.True);
            Jammer(player.position, 0.5f);
            Sensors.Forget();
            Assert.That(Jamming.On(player), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(Sensors.DetectedFrom(contact, player.position, player), Is.False, "jammed by half: 15 km");
            Assert.That(Sensors.Identified(contact), Is.False, "and named only inside 7.5 km");
            Assert.That(Sensors.Unseen(contact), Is.False, "a ship without a signature is never 'unseen': the CIWS and wingman still know it");
            contact.position = player.position + Vector3.forward * 5000f;
            Sensors.Forget();
            Assert.That(Sensors.Identified(contact), Is.True, "5 km: named");
        }

        [Test]
        public void AMissileLosesItsLockInJammingAndTakesItAgainOutOfIt()
        {
            var target = Make("Target").transform;
            var m = Make("Missile").AddComponent<Missile>();
            m.target = target;
            m.JamStep(0.8f, 0.99f, 1f);
            Assert.That(m.target, Is.EqualTo(target), "a bad roll: kept");
            m.JamStep(0.8f, 0.1f, 1f);
            Assert.That(m.target, Is.Null, "lost (a chance of 40 % a second at 0.8)");
            Assert.That(m.LockLost, Is.True);
            m.JamStep(0.5f, 0.0f, 1f);
            Assert.That(m.target, Is.Null, "still jammed: still lost");
            m.JamStep(0f, 0.5f, 1f);
            Assert.That(m.target, Is.EqualTo(target), "out of the jamming: taken again");
            Assert.That(m.LockLost, Is.False);
        }

        [UnityTest]
        public IEnumerator GhostsShowOnJammedSensorsAndGoWhenLookedAtClosely()
        {
            var e = Jammer(player.position + Vector3.forward * 3000f);
            e.ghosts = 3;
            e.ghostRange = new Vector2(2500f, 5000f);
            var list = new List<Sensors.Ghost>();
            int through0 = Sensors.GhostsSeenThrough;
            Sensors.Ghosts(list);
            Assert.That(list.Count, Is.GreaterThan(0), "a jammed ship sees ghosts");
            // the ghosts brought in round the player: looked at closely, seen through
            e.transform.position = player.position;
            e.ghostRange = new Vector2(200f, 600f);
            yield return null;
            Sensors.Ghosts(list);
            Assert.That(list, Is.Empty, "looked at closely: gone");
            Assert.That(Sensors.GhostsSeenThrough - through0, Is.EqualTo(3), "each of the three seen through, once");
            e.active = false;
            Sensors.Ghosts(list);
            Assert.That(list, Is.Empty, "no jamming: no ghosts");
        }

        [Test]
        public void AnEscortIsShownAtAFalsePlaceTillNamed()
        {
            var e = Jammer(player.position + Vector3.forward * 12000f, 0.9f, 8000f);
            var escort = Make("Escort").transform;
            // (the player 3 km from the jammer: jammed 77 %, its sensors naming only inside 3.4 km;
            // the escort 5 km off it)
            escort.position = e.transform.position + Vector3.right * 4000f;
            player.position = e.transform.position + Vector3.back * 3000f;
            Sensors.Forget();
            Assert.That(Sensors.Identified(escort), Is.False);
            Vector3 shown = Sensors.ShownAt(escort);
            Assert.That(Vector3.Distance(shown, escort.position), Is.EqualTo(e.maskOffset).Within(1f), "shown a mask's offset away");
            Assert.That(Sensors.ShownAt(escort), Is.EqualTo(shown), "the same false place each time");
            player.position = escort.position + Vector3.back * 300f;
            Sensors.Forget();
            Assert.That(Sensors.ShownAt(escort), Is.EqualTo(escort.position), "named: where it is");
        }

        [Test]
        public void AWingmansRadioComesThroughBrokenUp()
        {
            string said = "Wingman: two contacts bearing zero four five, closing";
            Assert.That(Jamming.Garble(said, 0f, 7), Is.EqualTo(said), "no jamming: clean");
            string g = Jamming.Garble(said, 0.8f, 7);
            Assert.That(g, Is.Not.EqualTo(said));
            Assert.That(g.Length, Is.EqualTo(said.Length));
            Assert.That(Jamming.Garble(said, 0.8f, 7), Is.EqualTo(g), "the same message garbles the same way");
        }
    }
}
