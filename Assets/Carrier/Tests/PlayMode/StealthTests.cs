using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CarrierGame.Tests
{
    /// <summary>A cloak (Cloak) keeping a ship's signature low until it fires, and coming back;
    /// ECCM seeing a cloaked ship from further; a targeting laser's paint (Designation) held while
    /// it is on and wearing off after; a warp inhibitor's field (Interdictor).</summary>
    public class StealthTests : PlayModeFixture
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

        Transform Ship(string name, float km)
        {
            var go = Make(name);
            go.transform.position = player.position + Vector3.forward * km * 1000f;
            return go.transform;
        }

        [UnityTest]
        public IEnumerator ACloakKeepsItLowUntilItFiresAndComesBackAfter()
        {
            var t = Ship("Scout", 4f);
            var sig = t.gameObject.AddComponent<Signature>();
            sig.lightUp = 0.3f;
            var cloak = t.gameObject.AddComponent<Cloak>();
            cloak.recloakTime = 0.3f;
            yield return null;
            Sensors.Forget();
            Assert.That(cloak.Up, Is.True);
            Assert.That(sig.hiddenScale, Is.EqualTo(0.1f).Within(1e-4f));
            Assert.That(Sensors.Detected(t), Is.False, "cloaked at 4 km: seen only inside 3");
            sig.Fired();
            yield return null;
            Sensors.Forget();
            Assert.That(cloak.Up, Is.False, "firing drops the cloak");
            Assert.That(Sensors.Detected(t), Is.True);
            yield return new WaitForSeconds(0.45f);   // (lit up 0.3 s; the cloak is still coming back)
            Assert.That(cloak.Up, Is.False, "not back the moment the light-up is over");
            yield return new WaitForSeconds(0.5f);
            Sensors.Forget();
            Assert.That(cloak.Up, Is.True, "back once it has taken its time");
            Assert.That(Sensors.Detected(t), Is.False);
            cloak.Engage(false);
            yield return null;
            Sensors.Forget();
            Assert.That(Sensors.Detected(t), Is.True, "turned off: seen as any ship");
        }

        [UnityTest]
        public IEnumerator ECCMSeesACloakedShipFromFurther()
        {
            var t = Ship("Scout", 4f);
            t.gameObject.AddComponent<Signature>();
            t.gameObject.AddComponent<Cloak>();
            yield return null;
            Sensors.Forget();
            Assert.That(Sensors.Detected(t), Is.False, "no ECCM: 3 km");
            player.gameObject.AddComponent<FrigateClass>().eccm = 0.7f;
            Sensors.Forget();
            Assert.That(Sensors.Detected(t), Is.True, "ECCM 0.7: 5.1 km");
            Assert.That(FrigateClass.EccmOf(player), Is.EqualTo(0.7f));
        }

        [UnityTest]
        public IEnumerator APaintIsHeldWhileTheLaserIsOnAndWearsOffAfter()
        {
            var painter = Ship("Recon", 10f);
            Assert.That(Designation.HudText(player), Is.Empty);
            Designation.Mark(painter, player, "Empire");
            Assert.That(Designation.PaintedFor(player, "Empire"), Is.True);
            Assert.That(Designation.PaintedFor(player, "Greys"), Is.False, "painted for its own faction only");
            Assert.That(Designation.PainterOf(player), Is.EqualTo(painter));
            StringAssert.StartsWith("Targeting laser: Recon", Designation.HudText(player));
            StringAssert.Contains("bearing 000, 10 km", Designation.HudText(player));
            yield return new WaitForSeconds(Designation.Hold + 0.2f);
            Assert.That(Designation.PaintedFor(player, null), Is.False, "the laser off it: worn off");
            Designation.Mark(painter, player, "Empire");
            Object.Destroy(painter.gameObject);
            yield return null;
            Assert.That(Designation.PainterOf(player), Is.Null, "its painter gone: no paint");
        }

        [UnityTest]
        public IEnumerator ADesignatorPaintsItsTargetInRange()
        {
            var painter = Ship("Recon", 10f);
            var d = painter.gameObject.AddComponent<Designator>();
            d.range = 12000f;
            d.target = player;
            yield return null;
            Assert.That(d.Painting, Is.EqualTo(player));
            Assert.That(Designation.PainterOf(player), Is.EqualTo(painter));
            painter.position = player.position + Vector3.forward * 14000f;
            yield return null;
            Assert.That(d.Painting, Is.Null, "out of its range: no paint");
        }

        [UnityTest]
        public IEnumerator AnInhibitorsFieldHoldsAShip()
        {
            var t = Ship("Patrol", 10f);
            var i = t.gameObject.AddComponent<Interdictor>();
            i.radius = 12000f;
            yield return null;
            Assert.That(Interdictor.At(player.position), Is.EqualTo(i));
            i.radius = 8000f;
            Assert.That(Interdictor.At(player.position), Is.Null);
            i.radius = 12000f;
            i.enabled = false;
            Assert.That(Interdictor.At(player.position), Is.Null, "off: no field");
        }
    }
}
