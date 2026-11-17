using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CarrierGame.Tests
{
    /// <summary>A ship's components (ShipSystems): aimed hits, ion through a shield, knocked out
    /// and working again, repairs, what a component knocked out does, and co-op's copy of them.</summary>
    public class ShipSystemsTests : PlayModeFixture
    {
        [TearDown]
        public void ClearAims() => ShipSystems.ClearAims();

        /// <summary>A ship with a 1,000-point hull (so 120 points a component), and a shield if asked.</summary>
        ShipSystems Ship(float shield = 0f)
        {
            var go = Make("Ship");
            go.SetActive(false);
            var h = go.AddComponent<Health>();
            h.maxHealth = 1000f;
            var sys = go.AddComponent<ShipSystems>();
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

        [Test]
        public void AnAimedHitDamagesThatComponent()
        {
            var sys = Ship();
            var h = sys.GetComponent<Health>();
            var shooter = Make("Shooter").transform;
            ShipSystems.SetAim(shooter, ShipSystem.Engines);
            Health.Source = shooter;
            h.Damage(100f, Vector3.zero, HitKind.Projectile);
            Health.Source = null;
            // (70 of the 100 to the engines' 120)
            Assert.That(sys.Condition(ShipSystem.Engines), Is.EqualTo(1f - 70f / 120f).Within(1e-4f));
            Assert.That(sys.Condition(ShipSystem.Weapons), Is.EqualTo(1f));
            Assert.That(sys.Condition(ShipSystem.Shields), Is.EqualTo(1f));
            Assert.That(h.Current, Is.EqualTo(900f).Within(1e-3f), "the hull takes the hit as it would anyway");
        }

        [Test]
        public void AnUnaimedHitWearsOneAtRandomALittle()
        {
            var sys = Ship();
            sys.GetComponent<Health>().Damage(100f, Vector3.zero, HitKind.Cannon);
            float worn = 0f;
            foreach (var s in ShipSystems.Components) worn += 1f - sys.Condition(s);
            Assert.That(worn, Is.EqualTo(25f / 120f).Within(1e-4f), "a quarter of it, to one of them");
        }

        [Test]
        public void IonDisablesEnginesThroughAShield()
        {
            var sys = Ship(shield: 1e5f);
            var h = sys.GetComponent<Health>();
            var shield = sys.GetComponentInChildren<Shield>();
            var shooter = Make("Ion ship").transform;
            ShipSystems.SetAim(shooter, ShipSystem.Engines);
            Health.Source = shooter;
            h.Damage(100f, Vector3.zero, HitKind.Cannon);
            Assert.That(sys.Condition(ShipSystem.Engines), Is.EqualTo(1f), "a shield that holds stops a cannon's");
            h.Damage(300f, Vector3.zero, HitKind.Ion);
            Health.Source = null;
            Assert.That(shield.Up, Is.True, "the shield held");
            Assert.That(h.Current, Is.EqualTo(1000f), "nothing reached the hull");
            Assert.That(sys.IsDown(ShipSystem.Engines), Is.True, "but ion reached the engines");
            Assert.That(sys.Condition(ShipSystem.Weapons), Is.EqualTo(1f));
        }

        [Test]
        public void KnockedOutBelowAQuarterWorkingAgainOnlyAboveAHalf()
        {
            var sys = Ship();
            sys.SetCondition(ShipSystem.Weapons, 0.3f);
            Assert.That(sys.WeaponsDown, Is.False);
            sys.SetCondition(ShipSystem.Weapons, 0.2f);
            Assert.That(sys.WeaponsDown, Is.True);
            sys.SetCondition(ShipSystem.Weapons, 0.45f);
            Assert.That(sys.WeaponsDown, Is.True, "still out, under a half");
            sys.SetCondition(ShipSystem.Weapons, 0.55f);
            Assert.That(sys.WeaponsDown, Is.False);
            sys.SetCondition(ShipSystem.Weapons, 0.3f);
            Assert.That(sys.WeaponsDown, Is.False, "and working down to a quarter");
            Assert.That(sys.KnockedOut, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ItRepairsOutOfCombatAndAllAtOnceInARepair()
        {
            var sys = Ship();
            sys.quietTime = 0.1f;
            sys.repairRate = 2f;
            sys.SetCondition(ShipSystem.Engines, 0.1f);
            Assert.That(sys.EnginesDown, Is.True);
            yield return new WaitForSeconds(0.6f);
            Assert.That(sys.Condition(ShipSystem.Engines), Is.EqualTo(1f));
            Assert.That(sys.EnginesDown, Is.False, "working again once over a half");

            sys.quietTime = 1e9f;
            sys.SetCondition(ShipSystem.Shields, 0f);
            sys.SetCondition(ShipSystem.Weapons, 0.4f);
            yield return new WaitForSeconds(0.2f);
            Assert.That(sys.Condition(ShipSystem.Weapons), Is.EqualTo(0.4f), "not while it isn't quiet");
            sys.GetComponent<Health>().Repair();
            foreach (var s in ShipSystems.Components)
            {
                Assert.That(sys.Condition(s), Is.EqualTo(1f), $"{s} whole after a repair");
                Assert.That(sys.IsDown(s), Is.False);
            }
        }

        [UnityTest]
        public IEnumerator EnginesKnockedOutSlowACarrierController()
        {
            var go = Make("Frigate");
            go.SetActive(false);
            go.AddComponent<Health>();
            var cc = go.AddComponent<CarrierController>();
            cc.maxSpeed = 100f;
            cc.acceleration = 1000f;
            go.SetActive(true);
            yield return null;                          // Start fits its components
            var sys = go.GetComponent<ShipSystems>();
            Assert.That(sys, Is.Not.Null, "a ship fits its own");
            sys.SetCondition(ShipSystem.Engines, 0f);
            Assert.That(cc.engineScale, Is.EqualTo(ShipSystems.EngineSpeed));
            Assert.That(cc.engineAccelScale, Is.EqualTo(ShipSystems.EngineAcceleration));
            cc.throttle = 1f;
            yield return new WaitForSeconds(0.3f);
            Assert.That(cc.Speed, Is.LessThanOrEqualTo(15.01f), "15% of top speed");
            Assert.That(cc.Speed, Is.GreaterThan(1f));
            sys.RepairAll();
            Assert.That(cc.engineScale, Is.EqualTo(1f));
            Assert.That(cc.engineAccelScale, Is.EqualTo(1f));
        }

        [Test]
        public void WeaponsKnockedOutStopTheGuns()
        {
            var sys = Ship();
            var gun = new GameObject("Gun");
            gun.transform.SetParent(sys.transform, false);
            var turret = gun.AddComponent<CarrierTurret>();
            turret.autoFire = false;
            turret.muzzles = new[] { gun.transform };
            var shotGo = Make("TestShot");
            shotGo.SetActive(false);                    // (its copies are made asleep, and stay so)
            turret.projectilePrefab = shotGo.AddComponent<Projectile>();
            try
            {
                turret.Fire();
                Assert.That(turret.ShotsFired, Is.EqualTo(1));
                sys.SetCondition(ShipSystem.Weapons, 0f);
                turret.Fire();
                Assert.That(turret.ShotsFired, Is.EqualTo(1), "no shot with the weapons knocked out");
                sys.SetCondition(ShipSystem.Weapons, 1f);
                turret.Fire();
                Assert.That(turret.ShotsFired, Is.EqualTo(2));
            }
            finally
            {
                foreach (var p in Object.FindObjectsByType<Projectile>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (p != null && p.gameObject != shotGo && p.name.StartsWith("TestShot")) Object.Destroy(p.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator AShieldBackFromBeingDisabledIsAtThirtyPercent()
        {
            var sys = Ship(shield: 100f);
            var shield = sys.GetComponentInChildren<Shield>();
            shield.downTime = 0.05f;
            sys.SetCondition(ShipSystem.Shields, 0.1f);
            Assert.That(shield.Disabled, Is.True);
            Assert.That(shield.Up, Is.False);
            Assert.That(shield.Strength, Is.EqualTo(0f));
            Assert.That(shield.GetComponent<Collider>().enabled, Is.False);
            yield return new WaitForSeconds(0.2f);
            Assert.That(shield.Up, Is.False, "it stays down, past its down time");
            shield.SetOffline(false);
            Assert.That(shield.Up, Is.False, "switching it back on doesn't bring it back");
            Assert.That(shield.Strength, Is.EqualTo(0f));
            sys.SetCondition(ShipSystem.Shields, 0.6f);
            yield return null;
            yield return null;
            Assert.That(shield.Up, Is.True);
            Assert.That(shield.Strength, Is.EqualTo(30f).Within(0.01f), "back at 30%, not full");
        }

        [Test]
        public void TheComponentsGoOverTheWireAndBack()
        {
            var a = Ship();
            a.SetCondition(ShipSystem.Weapons, 0.6f);
            a.SetCondition(ShipSystem.Engines, 0.1f);
            a.SetCondition(ShipSystem.Shields, 0.1f);
            a.SetCondition(ShipSystem.Shields, 0.4f);   // (still out: under a half)
            int wire = a.ToWire();
            Assert.That(wire, Is.GreaterThan(0), "0 is kept for 'not sent'");
            var b = Ship();
            b.NetSet(wire);
            foreach (var s in ShipSystems.Components)
            {
                Assert.That(b.Condition(s), Is.EqualTo(a.Condition(s)).Within(0.5f / 31f), $"{s}'s condition");
                Assert.That(b.IsDown(s), Is.EqualTo(a.IsDown(s)), $"{s} out or not");
            }
            Assert.That(b.IsDown(ShipSystem.Shields), Is.True);
            Assert.That(ShipSystems.ToWire(null), Is.EqualTo(0));
            Assert.That(ShipSystems.FromWire((int)ShipSystem.Engines), Is.EqualTo(ShipSystem.Engines));
            Assert.That(ShipSystems.FromWire(0), Is.EqualTo(ShipSystem.Hull));
            Assert.That(ShipSystems.FromWire(99), Is.EqualTo(ShipSystem.Hull), "one this game doesn't know");
        }

        [Test]
        public void NothingSentLeavesItWhole()
        {
            var sys = Ship();
            sys.SetCondition(ShipSystem.Engines, 0f);
            sys.SetCondition(ShipSystem.Weapons, 0.3f);
            sys.NetSet(0);
            foreach (var s in ShipSystems.Components)
            {
                Assert.That(sys.Condition(s), Is.EqualTo(1f), $"{s} whole");
                Assert.That(sys.IsDown(s), Is.False);
            }
        }

        [Test]
        public void ANewTargetIsAimedAtTheHullAgain()
        {
            var cc = Make("Frigate").AddComponent<CarrierController>();
            var ship = cc.transform.root;
            var a = Make("Raider").transform;
            var b = Make("Cruiser").transform;
            cc.SetTarget(a);
            ShipSystems.SetAim(ship, ShipSystem.Weapons);
            Assert.That(ShipSystems.AimOf(ship), Is.EqualTo(ShipSystem.Weapons));
            cc.SetTarget(a);
            Assert.That(ShipSystems.AimOf(ship), Is.EqualTo(ShipSystem.Weapons), "the same target: as it was");
            cc.SetTarget(b);
            Assert.That(ShipSystems.AimOf(ship), Is.EqualTo(ShipSystem.Hull), "a new one: the hull");
            Assert.That(ShipSystems.Next(ShipSystem.Hull), Is.EqualTo(ShipSystem.Weapons));
            Assert.That(ShipSystems.Next(ShipSystem.Shields), Is.EqualTo(ShipSystem.Hull), "round again after the last");
            Assert.That(ShipSystems.AimOf(null), Is.EqualTo(ShipSystem.Hull));
        }
    }
}
