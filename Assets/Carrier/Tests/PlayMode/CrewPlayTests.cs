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
            health.NetSetHull(800f);                   // the host says 200 of it is gone
            Assert.That(crew.Current, Is.EqualTo(100f - 100f * CrewTable.Get().casualties * 0.2f).Within(0.01f));
        }
    }
}
