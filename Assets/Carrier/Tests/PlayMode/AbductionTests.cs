using NUnit.Framework;
using UnityEngine;

namespace CarrierGame.Tests
{
    /// <summary>Abduction (Crew.Take, CargoHold.Abduct, Abductor.Apply): crew taken alive as the
    /// Greys' prisoners, never below the survivors and not counted as killed; ore taken from the
    /// hold; a sample halving a component.</summary>
    public class AbductionTests : PlayModeFixture
    {
        [SetUp]
        public void Clean() => WorldState.Clear();

        [TearDown]
        public void After() => WorldState.Clear();

        [Test]
        public void CrewAreTakenAliveNeverBelowTheSurvivors()
        {
            var go = Make("Ship");
            var crew = go.AddComponent<Crew>();
            crew.complement = 100;
            int taken = crew.Take(30, GreysShip.Faction);
            Assert.That(taken, Is.EqualTo(30));
            Assert.That(crew.Aboard, Is.EqualTo(70));
            Assert.That(crew.TotalLost, Is.EqualTo(0f), "taken, not killed");
            Assert.That(crew.TotalTaken, Is.EqualTo(30));
            Assert.That(WorldState.PrisonersOf(GreysShip.Faction), Is.EqualTo(30), "the Greys' prisoners");
            int more = crew.Take(1000, GreysShip.Faction);
            Assert.That(crew.AtFloor, Is.True, "down to the survivors, no further");
            Assert.That(crew.Aboard, Is.GreaterThan(0));
            Assert.That(crew.Take(5, GreysShip.Faction), Is.EqualTo(0), "the survivors aren't taken: the Greys take people, not ships");
            Assert.That(WorldState.PrisonersOf(GreysShip.Faction), Is.EqualTo(30 + more));
        }

        [Test]
        public void OreIsTakenFromTheHoldTheBiggestLoadFirst()
        {
            var go = Make("Ship");
            var hold = go.AddComponent<CargoHold>();
            hold.capacity = 100f;
            hold.Add(Mineral.Iron, 20f);
            hold.Add(Mineral.Nickel, 5f);
            float t = hold.Abduct(12f);
            Assert.That(t, Is.EqualTo(12f).Within(1e-3f));
            Assert.That(hold[Mineral.Iron], Is.EqualTo(8f).Within(1e-3f), "from the biggest load first");
            Assert.That(hold[Mineral.Nickel], Is.EqualTo(5f).Within(1e-3f));
            Assert.That(hold.Used, Is.EqualTo(13f).Within(1e-3f));
            Assert.That(hold.Abduct(100f), Is.EqualTo(13f).Within(1e-3f), "no more than there is");
            Assert.That(hold.Used, Is.EqualTo(0f).Within(1e-3f));
        }

        [Test]
        public void ASampleHalvesAComponent()
        {
            var go = Make("Ship");
            go.AddComponent<Health>().maxHealth = 1000f;
            var sys = go.AddComponent<ShipSystems>();
            var crew = go.AddComponent<Crew>();
            crew.complement = 50;
            var (n, _) = Abductor.Apply(go.transform, 4, 0f, (int)ShipSystem.Engines + 1, GreysShip.Faction);
            Assert.That(n, Is.EqualTo(4));
            Assert.That(sys.Condition(ShipSystem.Engines), Is.EqualTo(0.5f).Within(1e-3f), "a sample of the engines: half their condition");
            Assert.That(sys.Condition(ShipSystem.Weapons), Is.EqualTo(1f));
        }
    }
}
