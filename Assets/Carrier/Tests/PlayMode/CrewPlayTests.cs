using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CarrierGame.Tests
{
    /// <summary>Crew in play: the frigate's crew, its fittings and its hull together.</summary>
    public class CrewPlayTests : PlayModeFixture
    {
        (Health health, Crew crew, ShipFittings fit) MakeShip()
        {
            var go = Make("Ship");
            go.SetActive(false);                       // so Awake sees the settings
            var h = go.AddComponent<Health>();
            h.maxHealth = 1000f;
            h.respawnDelay = 30f;                      // (stays put when destroyed)
            go.AddComponent<CargoHold>();
            var crew = go.AddComponent<Crew>();
            crew.complement = 100;
            var fit = go.AddComponent<ShipFittings>();
            go.SetActive(true);
            return (h, crew, fit);
        }

        [UnityTest]
        public IEnumerator AKillingHitStillKillsAShortHandedShip()
        {
            var (health, crew, fit) = MakeShip();
            yield return null;
            fit.SetFitted(null);
            // short-handed: every hit now changes how well the crew work the ship
            health.Damage(400f);
            Assert.That(crew.Efficiency, Is.LessThan(1f));
            health.Damage(5000f);
            Assert.That(health.Dead, Is.True, "a killing hit kills, whatever the crew's loss sets off");
            Assert.That(health.Current, Is.LessThanOrEqualTo(0f));
        }

        [UnityTest]
        public IEnumerator LossesAreReckonedAgainstTheHullAsItIsNow()
        {
            var (health, crew, _) = MakeShip();
            yield return null;
            health.ScaleTo(2f);                        // (co-op: twice as strong)
            health.Damage(500f);                       // a quarter of the hull now
            Assert.That(crew.Current, Is.EqualTo(100f - 100f * CrewTable.Get().casualties * 0.25f).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator TheHostsDamageCostsCrewInAnotherPlayersGameToo()
        {
            var (health, crew, _) = MakeShip();
            yield return null;
            health.NetSetHull(700f);                   // joining: catching up with damage done before
            Assert.That(crew.Current, Is.EqualTo(100f), "the first word from the host costs nobody");
            health.NetSetHull(500f);                   // then the host says 200 more is gone
            Assert.That(crew.Current, Is.EqualTo(100f - 100f * CrewTable.Get().casualties * 0.2f).Within(0.01f));
            health.NetKill();                          // and destroyed: the last 500
            Assert.That(health.Dead, Is.True);
            Assert.That(crew.Current, Is.EqualTo(100f - 100f * CrewTable.Get().casualties * 0.7f).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator HullsDestroyedOutrightStillCountForTheirShare()
        {
            // an installation of two guns: one destroyed outright, then the other hit for half its hull
            var root = Make("Installation");
            root.SetActive(false);
            var crew = root.AddComponent<Crew>();
            crew.complement = 100;
            var a = new GameObject("Gun A"); a.transform.SetParent(root.transform);
            var b = new GameObject("Gun B"); b.transform.SetParent(root.transform);
            var ha = a.AddComponent<Health>(); ha.maxHealth = 1000f;
            var hb = b.AddComponent<Health>(); hb.maxHealth = 1000f;
            root.SetActive(true);
            yield return null;
            ha.Damage(1000f);                          // (destroyed, and gone the frame after)
            yield return null;
            yield return null;
            float after = crew.Current;
            hb.Damage(500f);                           // a quarter of the two guns' hull
            Assert.That(after - crew.Current, Is.EqualTo(100f * CrewTable.Get().casualties * 0.25f).Within(0.01f));
        }
    }
}
