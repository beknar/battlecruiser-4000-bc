using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CarrierGame.Tests
{
    /// <summary>EMP (HitKind.Emp, ShipSystems.Knock, EmpBurst): a component knocked out for a
    /// time and back after it, less with ECCM, not ended early by the repair tick, through a shield
    /// at a share; its co-op wire form; a burst hitting only the ships in its reach.</summary>
    public class EmpTests : PlayModeFixture
    {
        [TearDown]
        public void ClearAims() => ShipSystems.ClearAims();

        /// <summary>A ship with a 1,000-point hull (a component's toughness: 120 points), a shield if asked.</summary>
        ShipSystems Ship(string name = "Ship", float shield = 0f, float eccm = 0f)
        {
            var go = Make(name);
            go.SetActive(false);
            go.AddComponent<Health>().maxHealth = 1000f;
            var sys = go.AddComponent<ShipSystems>();
            if (eccm > 0f) go.AddComponent<FrigateClass>().eccm = eccm;
            if (shield > 0f)
            {
                var bubble = new GameObject("Shield");
                bubble.transform.SetParent(go.transform, false);
                bubble.AddComponent<SphereCollider>().radius = 20f;
                var s = bubble.AddComponent<Shield>();
                s.capacity = shield;
                s.rechargeRate = 0f;
            }
            go.SetActive(true);
            return sys;
        }

        void Emp(ShipSystems sys, float amount, Transform from)
        {
            Health.Source = from;
            sys.GetComponent<Health>().Damage(amount, sys.transform.position, HitKind.Emp);
            Health.Source = null;
        }

        [UnityTest]
        public IEnumerator AnEmpKnocksAComponentOutForATimeAndItComesBack()
        {
            var sys = Ship();
            var gun = Make("Gun").transform;
            ShipSystems.SetAim(gun, ShipSystem.Weapons);
            var hull = sys.GetComponent<Health>();
            // a pulse of 12 points: a tenth of a component's toughness, 3 s
            Emp(sys, 12f, gun);
            Assert.That(sys.WeaponsDown, Is.True, "the weapons, aimed at, are out");
            Assert.That(sys.Condition(ShipSystem.Weapons), Is.EqualTo(1f), "their condition untouched: knocked out, not worn");
            Assert.That(sys.KnockedFor(ShipSystem.Weapons), Is.EqualTo(3f).Within(0.05f));
            Assert.That(hull.Current, Is.EqualTo(1000f - 1.2f).Within(0.01f), "a tenth of it to the hull");
            Assert.That(sys.EnginesDown || sys.IsDown(ShipSystem.Shields), Is.False);
            yield return new WaitForSeconds(3.2f);
            Assert.That(sys.WeaponsDown, Is.False, "back after its time");
            Assert.That(sys.KnockedFor(ShipSystem.Weapons), Is.EqualTo(0f));
        }

        [Test]
        public void ECCMShortensIt()
        {
            var gun = Make("Gun").transform;
            ShipSystems.SetAim(gun, ShipSystem.Engines);
            var plain = Ship("Plain");
            var hard = Ship("Hardened", eccm: 1f);
            Emp(plain, 60f, gun);    // half its toughness: 15 s, the most
            Emp(hard, 60f, gun);
            Assert.That(plain.KnockedFor(ShipSystem.Engines), Is.EqualTo(15f).Within(0.05f));
            Assert.That(hard.KnockedFor(ShipSystem.Engines), Is.EqualTo(15f * 0.4f).Within(0.05f), "ECCM 1: 40 % of it");
            Assert.That(hard.EnginesDown && plain.EnginesDown, Is.True);
        }

        [UnityTest]
        public IEnumerator TheRepairTickDoesntEndItEarly()
        {
            var sys = Ship();
            sys.quietTime = 0f;
            sys.repairRate = 1f;     // (a whole component a second)
            sys.SetCondition(ShipSystem.Shields, 0.6f);
            sys.Knock(ShipSystem.Shields, 1f);
            Assert.That(sys.IsDown(ShipSystem.Shields), Is.True);
            yield return new WaitForSeconds(0.5f);
            Assert.That(sys.Condition(ShipSystem.Shields), Is.EqualTo(1f), "repaired whole");
            Assert.That(sys.IsDown(ShipSystem.Shields), Is.True, "still out: the EMP's time isn't up");
            yield return new WaitForSeconds(0.7f);
            Assert.That(sys.IsDown(ShipSystem.Shields), Is.False);
            Assert.That(sys.Pack() & (32 << 12), Is.EqualTo(0), "and co-op says so");
        }

        [Test]
        public void AnEmpGoesThroughAShieldAtAShare()
        {
            var gun = Make("Gun").transform;
            ShipSystems.SetAim(gun, ShipSystem.Weapons);
            var sys = Ship(shield: 5000f);
            // 40 points into the shield: 30 % of it (12) reaches the components: 3 s
            Emp(sys, 40f, gun);
            Assert.That(sys.KnockedFor(ShipSystem.Weapons), Is.EqualTo(3f).Within(0.05f));
            Assert.That(sys.GetComponent<Health>().Current, Is.EqualTo(1000f), "the hull untouched behind the shield");
        }

        [Test]
        public void ItsCoopWireFormIsABeamToAnOlderGame()
        {
            var (kd, kx) = DamageTable.ToWire(HitKind.Emp);
            Assert.That((HitKind)kd, Is.EqualTo(HitKind.Beam), "an older game reads a beam's hit, not a cannon's");
            Assert.That(DamageTable.FromWire(kd, kx), Is.EqualTo(HitKind.Emp));
            Assert.That(DamageTable.Label(HitKind.Emp), Is.EqualTo("EMP"));
            Assert.That(DamageTable.Of(HitKind.Emp).component, Is.EqualTo(0f));
            Assert.That(DamageTable.Count, Is.EqualTo(10));
        }

        [UnityTest]
        public IEnumerator ABurstHitsOnlyTheShipsInItsReach()
        {
            var near = Ship("Near");
            var far = Ship("Far");
            near.transform.position = new Vector3(0f, 0f, 2000f);
            far.transform.position = new Vector3(0f, 0f, 4000f);
            PlayerShips.Add(near.transform);
            PlayerShips.Add(far.transform);
            try
            {
                var b = Make("Shroud").AddComponent<EmpBurst>();
                b.radius = 2500f;
                b.chargeTime = 0.3f;
                b.strength = 120f;
                yield return null;
                Assert.That(b.Current, Is.EqualTo(EmpBurst.State.Charging), "a ship in reach: it charges");
                Assert.That(EmpBurst.HudText(near.transform), Does.StartWith("EMP charging"));
                Assert.That(EmpBurst.HudText(far.transform), Is.Empty, "out of reach: no warning");
                yield return new WaitForSeconds(0.45f);
                Assert.That(b.Bursts, Is.EqualTo(1));
                Assert.That(b.ShipsHit, Is.EqualTo(1));
                Assert.That(near.Knocks, Is.EqualTo(1), "the near ship knocked");
                Assert.That(far.Knocks, Is.EqualTo(0), "the far one missed");
                Assert.That(b.Current, Is.EqualTo(EmpBurst.State.Cooling));
            }
            finally
            {
                PlayerShips.Remove(near.transform);
                PlayerShips.Remove(far.transform);
            }
        }
    }
}
