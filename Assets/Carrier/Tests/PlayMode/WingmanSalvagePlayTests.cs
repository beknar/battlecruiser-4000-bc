using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CarrierGame.Demo;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CarrierGame.Tests
{
    /// <summary>The AI wingman's Salvage order in play, against wrecks made in the test: the beam
    /// on the nearest wreck, then the next, and with none left back to the order before (holding,
    /// here), saying how much it took; the wreck named chosen over a nearer one; and a full hold
    /// sending it to sell, the beam off. Its frigate, hold, beam (Salvage) and pilot (Wingman)
    /// are made here; the ship's controls (CarrierDemoInput) are never woken, as their Awake
    /// builds the whole HUD: the pilot and the beam only read the ship from them.</summary>
    public class WingmanSalvagePlayTests : PlayModeFixture
    {
        CarrierController ship;
        CargoHold hold;
        Salvage beam;
        Wingman pilot;
        readonly List<string> remarks = new List<string>();

        [SetUp]
        public void Ship()
        {
            Modes.Reset();
            Targets.Reset(new Transform[0]);
            remarks.Clear();
            var go = Make("Wingman's frigate");
            go.AddComponent<Health>().maxHealth = 1000f;
            ship = go.AddComponent<CarrierController>();
            hold = go.AddComponent<CargoHold>();
            hold.capacity = 600f;
            var controls = Make("Controls");
            controls.SetActive(false);
            var demo = controls.AddComponent<CarrierDemoInput>();
            demo.carrier = ship;
            beam = Make("Salvage beam").AddComponent<Salvage>();
            beam.demo = demo;
            beam.seconds = 1f;            // (a wreck stripped in one take, a second after the beam goes on)
            pilot = Make("Pilot").AddComponent<Wingman>();
            pilot.demo = demo;
            pilot.Remark += s => remarks.Add(s);
        }

        [TearDown]
        public void NoWrecks()
        {
            foreach (var w in Wreck.All.ToList()) if (w != null) Object.Destroy(w.gameObject);
            // (out of the list now: Destroy waits for the frame's end, and the next test may start first)
            Wreck.All.Clear();
            Modes.Reset();
        }

        Wreck WreckAt(Vector3 at, string of, float steel)
        {
            var dead = Make(of, at).transform;
            return Wreck.Make(dead, of, new[] { ("SteelPlate", steel), ("CopperWire", 2f) }, 30f, null, false);
        }

        [UnityTest]
        public IEnumerator ItStripsTheWrecksOneAfterAnotherThenGoesBackToItsOrder()
        {
            var near = WreckAt(new Vector3(600f, 0f, 0f), "Pirate Cruiser", 10f);
            var next = WreckAt(new Vector3(-1100f, 0f, 0f), "Vrask Fang Hunter", 6f);
            yield return null;
            Assert.That(Wingman.NearestWreck(ship.transform.position), Is.EqualTo(near));
            Assert.That(Wingman.WreckNamed("Fang Hunter", ship.transform.position), Is.EqualTo(next), "by name, over the nearer one");
            Assert.That(Wingman.WreckNamed("the wreck of the Fang Hunter", ship.transform.position), Is.EqualTo(next));
            Assert.That(Wingman.WreckNamed("Skink", ship.transform.position), Is.Null);
            Assert.That(Wingman.NearestWreck(ship.transform.position + Vector3.forward * 40000f), Is.Null, "none within 30 km");

            pilot.SalvageWrecks(near);
            Assert.That(pilot.Current, Is.EqualTo(Wingman.Order.Salvage));
            bool beamOnNear = false;
            float until = Time.time + 10f;
            while (pilot.Current == Wingman.Order.Salvage && Time.time < until)
            {
                if (beam.Working == near) beamOnNear = true;
                yield return null;
            }
            Assert.That(beamOnNear, Is.True, "the beam on the nearest wreck (in its reach: no flight)");
            Assert.That(Modes.Is(ModeOfPlay.Combat), Is.True, "in combat mode, where the beam works");
            Assert.That(pilot.Current, Is.EqualTo(Wingman.Order.Hold), "none left: back to the order before");
            Assert.That(pilot.SalvageFinished, Is.True);
            Assert.That(near == null || near.Empty, Is.True, "the first stripped");
            Assert.That(next == null || next.Empty, Is.True, "and then the next");
            Assert.That(hold.Amount("SteelPlate"), Is.EqualTo(16f).Within(0.01f));
            Assert.That(hold.Amount("CopperWire"), Is.EqualTo(4f).Within(0.01f));
            Assert.That(beam.Working, Is.Null, "the beam off");
            Assert.That(remarks, Has.Member("nothing left to salvage; 20 t taken"));
        }

        [UnityTest]
        public IEnumerator WithNoWreckNearItSaysSoAndGoesBack()
        {
            WreckAt(new Vector3(50000f, 0f, 0f), "Pirate Cruiser", 10f);
            yield return null;
            pilot.SalvageWrecks(null);
            yield return null;
            yield return null;
            Assert.That(pilot.Current, Is.EqualTo(Wingman.Order.Hold), "the only wreck is 50 km off");
            Assert.That(remarks, Has.Member("nothing to salvage within 30 km"));
        }

        [UnityTest]
        public IEnumerator AFullHoldSendsItToSellWithTheBeamOff()
        {
            hold.capacity = 8f;
            var wreck = WreckAt(new Vector3(500f, 0f, 0f), "Pirate Cruiser", 30f);
            yield return null;
            pilot.SalvageWrecks(wreck);
            bool selling = false;
            float until = Time.time + 6f;
            while (!selling && Time.time < until)
            {
                selling = pilot.Current == Wingman.Order.Salvage && pilot.SalvagingStep == Wingman.SalvageStep.ToMarket;
                // (or already given up: no market in the test)
                if (pilot.Current == Wingman.Order.Hold && pilot.Doing.Length > 0) break;
                yield return null;
            }
            yield return null;
            Assert.That(hold.Free, Is.LessThan(1f), "the hold filled");
            Assert.That(wreck.Empty, Is.False, "with the wreck not yet stripped");
            Assert.That(beam.Working, Is.Null, "the beam off to go and sell");
            Assert.That(pilot.Current, Is.EqualTo(Wingman.Order.Hold), "and with no market here, it holds");
        }
    }
}
