using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using CarrierGame.Demo;

namespace CarrierGame.Tests
{
    /// <summary>What the players' sensors see (Sensors, Signature): a ship without a signature
    /// as always; one hard to see only close in, and named closer still; lit up by firing; what an
    /// enemy sees of a player's ship.</summary>
    public class SensorsTests : PlayModeFixture
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

        Transform Contact(string name, float km, float signature = -1f)
        {
            var go = Make(name);
            go.transform.position = player.position + Vector3.forward * km * 1000f;
            if (signature >= 0f) go.AddComponent<Signature>().value = signature;
            return go.transform;
        }

        void MoveTo(Transform t, float km)
        {
            t.position = player.position + Vector3.forward * km * 1000f;
            Sensors.Forget();
        }

        [Test]
        public void AShipWithoutASignatureIsSeenAsItAlwaysWas()
        {
            var t = Contact("Pirate", 25f);
            Assert.That(Sensors.Detected(t), Is.True);
            Assert.That(Sensors.Identified(t), Is.True, "named at any range, as before");
            Assert.That(Sensors.Unseen(t), Is.False);
            Assert.That(Sensors.Hidden(t), Is.False);
            Assert.That(TargetDisplay.InRange(t, player.position), Is.True, "inside the HUD's 30 km");
            MoveTo(t, 35f);
            Assert.That(TargetDisplay.InRange(t, player.position), Is.False, "beyond it, as before");
            Assert.That(Sensors.Unseen(t), Is.False, "never unseen: the wingman, the CIWS and the warp lock see it as before");
        }

        [Test]
        public void AShipHardToSeeIsSeenCloseInAndNamedCloserStill()
        {
            // a fifth of a ship's signature: seen within 6 km of the 30, named within 3
            var t = Contact("Grey", 10f, 0.2f);
            Assert.That(Sensors.Detected(t), Is.False, "10 km out: not seen");
            Assert.That(Sensors.Unseen(t) && Sensors.Hidden(t), Is.True);
            Assert.That(TargetDisplay.InRange(t, player.position), Is.False);
            MoveTo(t, 5f);
            Assert.That(Sensors.Detected(t), Is.True, "5 km: seen");
            Assert.That(Sensors.Identified(t), Is.False, "but not named");
            Assert.That(Sensors.NameOf(t, "Grey Scout"), Is.EqualTo(Sensors.UnknownContact));
            Assert.That(TargetDisplay.InRange(t, player.position), Is.True);
            MoveTo(t, 2f);
            Assert.That(Sensors.Identified(t), Is.True, "2 km: named");
            Assert.That(Sensors.NameOf(t, "Grey Scout"), Is.EqualTo("Grey Scout"));
        }

        [UnityTest]
        public IEnumerator FiringLightsAShipUpForAWhile()
        {
            var t = Contact("Grey", 10f, 0.2f);
            var sig = t.GetComponent<Signature>();
            sig.lightUp = 0.3f;
            Assert.That(Sensors.Detected(t), Is.False);
            sig.Fired();
            Sensors.Forget();
            Assert.That(sig.Current, Is.EqualTo(1f));
            Assert.That(Sensors.Detected(t) && Sensors.Identified(t), Is.True, "firing gives it away: seen and named");
            yield return new WaitForSeconds(0.4f);
            Sensors.Forget();
            Assert.That(Sensors.Detected(t), Is.False, "quiet again: gone from the sensors");
            sig.Hit();
            Sensors.Forget();
            Assert.That(Sensors.Detected(t), Is.True, "a hit lights it up too");
        }

        [Test]
        public void AHiddenScaleKeepsItLowUntilItFires()
        {
            var t = Contact("Grey", 2.5f, 1f);
            var sig = t.GetComponent<Signature>();
            sig.hiddenScale = 0.1f;    // (a cloak: 3 km)
            Assert.That(Sensors.Detected(t), Is.True, "2.5 km: just inside 3");
            MoveTo(t, 4f);
            Assert.That(Sensors.Detected(t), Is.False);
            sig.Fired();
            Sensors.Forget();
            Assert.That(Sensors.Detected(t), Is.True, "firing lights a cloaked ship up to a full signature");
        }

        [Test]
        public void AnEnemySeesAPlayersShipWithinItsRangeTimesItsSignature()
        {
            var enemy = Make("Enemy").transform;
            enemy.position = player.position + Vector3.forward * 20000f;
            Assert.That(Sensors.CanSee(enemy, player), Is.True, "20 km, a ship's signature, 30 km sensors");
            player.gameObject.AddComponent<Signature>().value = 0.5f;
            Assert.That(Sensors.CanSee(enemy, player), Is.False, "half the signature: 15 km");
            enemy.position = player.position + Vector3.forward * 12000f;
            Assert.That(Sensors.CanSee(enemy, player), Is.True);
            Assert.That(Sensors.CanSee(null, player) || Sensors.CanSee(enemy, null), Is.False);
        }
    }
}
