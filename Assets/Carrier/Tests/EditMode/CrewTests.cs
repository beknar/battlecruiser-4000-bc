using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CarrierGame.Tests
{
    /// <summary>Crew (Resources/Data/crew.json): what hull damage costs, how a short crew slows a
    /// frigate, recruiting, and the save.</summary>
    public class CrewTests
    {
        static CrewTable T => CrewTable.Get();

        [Test]
        public void TheRulesAreSound()
        {
            Assert.That(T.recruitCost, Is.GreaterThan(0f));
            Assert.That(T.threshold, Is.EqualTo(0.9f), "more than 10% lost and the ship starts to suffer");
            Assert.That(T.slope, Is.GreaterThan(0f));
            Assert.That(T.minimumEfficiency, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.That(T.casualties, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f));
            Assert.That(T.survivors, Is.GreaterThan(0f).And.LessThan(T.threshold));
            var names = T.places.Select(p => p.name).ToArray();
            Assert.That(names.Distinct().Count(), Is.EqualTo(names.Length), "every place once");
            foreach (var p in T.places)
            {
                Assert.That(p.crew, Is.GreaterThanOrEqualTo(0), p.name);
                Assert.That(p.kind, Is.Not.Empty, p.name);
                if (p.Recruits) Assert.That(p.refillPerMinute, Is.GreaterThan(0f), p.name + ": its pool refills");
                if (p.Automated) Assert.That(p.Recruits, Is.False, p.name + ": nobody to recruit at an automated place");
            }
            Assert.That(T.places.Count(p => p.Recruits), Is.GreaterThanOrEqualTo(4), "several places recruit");
            Assert.That(T.places.Any(p => p.Automated), Is.True, "automated places have no crew");
        }

        [Test]
        public void TheShipWorksFullyDownToTheThresholdThenLess()
        {
            Assert.That(T.Efficiency(1f), Is.EqualTo(1f));
            Assert.That(T.Efficiency(0.9f), Is.EqualTo(1f), "10% lost: still full strength");
            Assert.That(T.Efficiency(0.85f), Is.LessThan(1f), "more than 10% lost: it drops");
            Assert.That(T.Efficiency(0.6f), Is.LessThan(T.Efficiency(0.8f)), "and keeps dropping");
            Assert.That(T.Efficiency(0f), Is.EqualTo(T.minimumEfficiency));
        }

        [Test]
        public void EveryFrigateHasACrewOfItsOwnSize()
        {
            var roster = FrigateRoster.Get();
            var crews = roster.frigates.Select(f => f.prefab.GetComponent<Crew>()).ToArray();
            Assert.That(crews, Has.None.Null, "every frigate has a crew");
            Assert.That(crews.All(c => c.complement > 0), Is.True);
            Assert.That(crews.Select(c => c.complement).Distinct().Count(), Is.EqualTo(crews.Length), "each type its own number");
        }
    }

    /// <summary>A crew on a ship: losses to hull damage, the floor, recruits, and what it does to
    /// the ship's numbers.</summary>
    public class CrewOnAShipTests
    {
        GameObject go;
        Health health;
        Crew crew;
        CargoHold hold;
        ShipFittings fit;
        CarrierController cc;

        [SetUp]
        public void SetUp()
        {
            IndustryLedger.Clear();
            go = new GameObject("Ship");
            health = go.AddComponent<Health>();
            health.maxHealth = 1000f;
            hold = go.AddComponent<CargoHold>();
            hold.capacity = 500f;
            cc = go.AddComponent<CarrierController>();
            cc.maxSpeed = 100f;
            cc.turnRate = 20f;
            crew = go.AddComponent<Crew>();
            crew.complement = 100;
            fit = go.AddComponent<ShipFittings>();
            // (edit mode: no Awake or Start of their own)
            typeof(Crew).GetMethod("Start", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(crew, null);
            fit.SetFitted(null);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(go);
            IndustryLedger.Clear();
        }

        [Test]
        public void HullDamageCostsCrewButNeverTheLastSurvivors()
        {
            typeof(Health).GetProperty("Current").SetValue(health, 1000f);
            health.Damage(200f);   // a fifth of the hull
            Assert.That(crew.Current, Is.EqualTo(100f - 100f * CrewTable.Get().casualties * 0.2f).Within(0.01f));
            crew.Lose(1000f);
            Assert.That(crew.Current, Is.EqualTo(100f * CrewTable.Get().survivors).Within(0.01f), "the survivors stay");
            Assert.That(crew.TotalLost, Is.GreaterThan(0f));
        }

        [Test]
        public void AShortCrewSlowsTheShipAndRecruitsBringItBack()
        {
            Assert.That(cc.maxSpeed, Is.EqualTo(100f));
            crew.Lose(8f);
            Assert.That(cc.maxSpeed, Is.EqualTo(100f), "8% lost: no change");
            crew.Lose(32f);   // 60% aboard
            float eff = CrewTable.Get().Efficiency(0.6f);
            Assert.That(crew.Efficiency, Is.EqualTo(eff).Within(1e-4f));
            Assert.That(cc.maxSpeed, Is.EqualTo(100f * eff).Within(0.01f), "top speed");
            Assert.That(cc.turnRate, Is.EqualTo(20f * eff).Within(0.01f), "turning");
            Assert.That(health.maxHealth, Is.EqualTo(1000f), "the hull itself is no weaker");
            Assert.That(hold.capacity, Is.EqualTo(500f), "nor the hold");
            Assert.That(fit.Value("speed"), Is.EqualTo(cc.maxSpeed).Within(0.01f));
            Assert.That(crew.Add(100), Is.EqualTo(40), "only as many as are missing");
            Assert.That(cc.maxSpeed, Is.EqualTo(100f));
        }

        [Test]
        public void RecruitingTakesCreditsAndThePlacesPool()
        {
            var table = CrewTable.Get();
            var place = table.places.First(p => p.Recruits);
            crew.Lose(50f);
            hold.Restore(null, table.recruitCost * 30f);
            int n = IndustryLedger.Recruit(place.name, crew, hold, 100, out string why);
            int expect = Mathf.Min(Mathf.Min(30, crew.complement), place.recruits);
            Assert.That(n, Is.EqualTo(Mathf.Min(expect, 50)), why);
            Assert.That(hold.Credits, Is.EqualTo(table.recruitCost * (30 - n)).Within(0.01f));
            Assert.That(IndustryLedger.Pool(place.name), Is.EqualTo(place.recruits - n).Within(0.01f));
            Assert.That(IndustryLedger.Recruit(place.name, crew, hold, 10, out why), Is.EqualTo(0), "out of credits");
            Assert.That(why, Does.Contain("credits"));
            var nowhere = table.places.First(p => !p.Recruits);
            Assert.That(IndustryLedger.Recruit(nowhere.name, crew, hold, 1, out why), Is.EqualTo(0));
        }

        [Test]
        public void ThePoolsRefillAndEverythingIsSaved()
        {
            var place = CrewTable.Get().places.First(p => p.Recruits);
            crew.Lose(50f);
            hold.Restore(null, 1e6f);
            int n = IndustryLedger.Recruit(place.name, crew, hold, 1000, out _);
            float pool = IndustryLedger.Pool(place.name);
            IndustryLedger.Advance(60f);
            Assert.That(IndustryLedger.Pool(place.name), Is.EqualTo(Mathf.Min(place.recruits, pool + place.refillPerMinute)).Within(0.01f), "a minute's refill");
            IndustryLedger.SetCrew("Sentinel", 77f);
            var (pools, crews) = IndustryLedger.PeopleSnapshot();
            var json = IndustrySave.ToJson(new IndustrySave.Data { pools = pools, crews = crews });
            IndustryLedger.Clear();
            var back = IndustrySave.FromJson(json);
            IndustryLedger.RestorePeople(back.pools, back.crews);
            Assert.That(IndustryLedger.CrewOf("Sentinel"), Is.EqualTo(77f));
            Assert.That(IndustryLedger.Pool(place.name), Is.EqualTo(Mathf.Min(place.recruits, pool + place.refillPerMinute)).Within(0.01f));
            Assert.That(n, Is.GreaterThan(0));
        }
    }
}
