using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CarrierGame.Tests
{
    /// <summary>For the swap test: something on the ship that keeps hold of it (as PlanetFlight does).</summary>
    public class HoldsShip : MonoBehaviour
    {
        public Transform ship;
        public CarrierController controller;
    }

    /// <summary>A ship swapped for another while the level runs (taking command at a shipyard, the
    /// frigates reel): what was on the old ship and not the new is carried across, pointing at the
    /// new ship, not the old one that is gone.</summary>
    public class SwapTests : PlayModeFixture
    {
        [UnityTest]
        public IEnumerator WhatIsCarriedAcrossPointsAtTheNewShip()
        {
            var old = Make("Ship");
            var oldShip = old.AddComponent<CarrierController>();
            var held = old.AddComponent<HoldsShip>();
            held.ship = old.transform;
            held.controller = oldShip;
            var prefab = Make("Another ship", new Vector3(0f, -50000f, 0f));
            prefab.AddComponent<CarrierController>();
            yield return null;

            var ship = PlayerFrigate.Swap(oldShip, prefab);
            yield return null;
            Assert.That(ship, Is.Not.Null, "the new ship");
            var copy = ship.GetComponent<HoldsShip>();
            Assert.That(copy, Is.Not.Null, "carried across");
            Assert.That(copy.ship, Is.EqualTo(ship.transform), "its ship is the new one");
            Assert.That(copy.controller, Is.EqualTo(ship), "and so is its controller");
            Object.Destroy(ship.gameObject);
        }
    }
}
