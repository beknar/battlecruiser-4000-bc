using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CarrierGame.Tests
{
    /// <summary>Shared set-up: everything a test makes is cleared away after it, and the static
    /// co-op hooks are put back.</summary>
    public abstract class PlayModeFixture
    {
        readonly List<GameObject> made = new List<GameObject>();

        protected GameObject Make(string name, Vector3 position = default)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            made.Add(go);
            return go;
        }

        [TearDown]
        public void ClearAway()
        {
            Health.Intercept = null;
            Health.Source = null;
            foreach (var go in made) if (go != null) Object.Destroy(go);
            made.Clear();
        }
    }

    /// <summary>Hit points: damage, death, respawn, and co-op's scaling and remote control.</summary>
    public class HealthTests : PlayModeFixture
    {
        Health MakeHealth(float max = 1000f, float respawn = 0f, bool cannotDie = false)
        {
            var go = Make("Ship");
            go.SetActive(false);                       // so Awake sees the settings
            var h = go.AddComponent<Health>();
            h.maxHealth = max;
            h.respawnDelay = respawn;
            h.cannotDie = cannotDie;
            go.SetActive(true);
            return h;
        }

        [Test]
        public void ItStartsWhole()
        {
            var h = MakeHealth(750f);
            Assert.That(h.Current, Is.EqualTo(750f));
            Assert.That(h.Dead, Is.False);
            Assert.That(h.DamageTaken, Is.EqualTo(0f));
        }

        [Test]
        public void DamageIsTakenAndCounted()
        {
            var h = MakeHealth();
            h.Damage(120f);
            h.Damage(30f);
            Assert.That(h.Current, Is.EqualTo(850f));
            Assert.That(h.Hits, Is.EqualTo(2));
            Assert.That(h.TotalDamage, Is.EqualTo(150f));
            Assert.That(h.LastHitAmount, Is.EqualTo(30f));
            Assert.That(h.DamageTaken, Is.EqualTo(150f));
        }

        [Test]
        public void NothingOrLessDoesNothing()
        {
            var h = MakeHealth();
            h.Damage(0f);
            h.Damage(-50f);
            Assert.That(h.Current, Is.EqualTo(1000f));
            Assert.That(h.Hits, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator RunningOutDestroysIt()
        {
            var h = MakeHealth(100f);
            int destroyed = 0;
            h.onDestroyed.AddListener(() => destroyed++);
            h.Damage(250f);
            Assert.That(h.Dead, Is.True);
            Assert.That(h.Current, Is.EqualTo(0f));
            Assert.That(h.TotalDamage, Is.EqualTo(100f), "only what it had is counted");
            Assert.That(h.TimesDestroyed, Is.EqualTo(1));
            Assert.That(destroyed, Is.EqualTo(1));
            h.Damage(10f);
            Assert.That(h.Hits, Is.EqualTo(1), "a dead ship takes no more hits");
            yield return null;
            Assert.That(h == null, "gone");
        }

        [Test]
        public void TheDemoShipCantDie()
        {
            var h = MakeHealth(100f, cannotDie: true);
            h.Damage(500f);
            Assert.That(h.Dead, Is.False);
            Assert.That(h.Current, Is.EqualTo(1f));
        }

        [UnityTest]
        public IEnumerator ARespawningShipComesBackWhole()
        {
            var h = MakeHealth(100f, respawn: 0.2f);
            int back = 0;
            h.onRespawned.AddListener(() => back++);
            h.Damage(100f);
            Assert.That(h.Dead, Is.True);
            Assert.That(h.RespawnAt, Is.GreaterThan(Time.time));
            yield return new WaitForSeconds(0.35f);
            Assert.That(h != null && !h.Dead, "back");
            Assert.That(h.Current, Is.EqualTo(100f));
            Assert.That(back, Is.EqualTo(1));
            Assert.That(h.TimesDestroyed, Is.EqualTo(1));
        }

        [Test]
        public void RepairPutsTheHullBack()
        {
            var h = MakeHealth();
            h.Damage(400f);
            h.Repair();
            Assert.That(h.Current, Is.EqualTo(1000f));
        }

        [Test]
        public void ScalingKeepsHowDamagedItIs()
        {
            var h = MakeHealth(1000f);
            h.Damage(250f);
            h.ScaleTo(1.6f);
            Assert.That(h.maxHealth, Is.EqualTo(1600f).Within(1e-3f));
            Assert.That(h.Current, Is.EqualTo(1200f).Within(1e-3f), "still 75%");
            Assert.That(h.BuiltMaxHealth, Is.EqualTo(1000f));
            h.ScaleTo(4.6f);
            Assert.That(h.maxHealth, Is.EqualTo(4600f).Within(1e-2f), "from the built hull, not on top of the last scaling");
            h.ScaleTo(1f);
            Assert.That(h.maxHealth, Is.EqualTo(1000f).Within(1e-3f));
            Assert.That(h.Current, Is.EqualTo(750f).Within(1e-2f));
        }

        [Test]
        public void ScalingLeavesAWreckAWreckAndAShipAlive()
        {
            var dead = MakeHealth(100f, respawn: 5f);
            dead.Damage(100f);
            dead.ScaleTo(2f);
            Assert.That(dead.Dead, Is.True);
            Assert.That(dead.Current, Is.EqualTo(0f));

            var scraped = MakeHealth(1000f);
            scraped.Damage(999.9f);
            scraped.ScaleTo(0.5f);
            Assert.That(scraped.Current, Is.GreaterThanOrEqualTo(1f), "a live ship keeps at least 1");
        }

        [Test]
        public void ACoopHookCanTakeTheHit()
        {
            var h = MakeHealth();
            var seen = new List<(float amount, HitKind kind)>();
            Health.Intercept = (target, amount, point, kind) => { seen.Add((amount, kind)); return target == h; };
            h.Damage(100f, Vector3.zero, HitKind.Missile);
            h.Damage(50f);
            Assert.That(h.Current, Is.EqualTo(1000f), "taken by the hook: the hull is untouched");
            Assert.That(seen, Is.EqualTo(new[] { (100f, HitKind.Missile), (50f, HitKind.Cannon) }));
        }

        [UnityTest]
        public IEnumerator TheHostSaysHowItIs()
        {
            var h = MakeHealth(1000f, respawn: 0.1f);
            h.NetSetHull(400f);
            Assert.That(h.Current, Is.EqualTo(400f));
            h.NetSetHull(5000f);
            Assert.That(h.Current, Is.EqualTo(1000f));
            h.NetSetHull(-20f);
            Assert.That(h.Current, Is.EqualTo(1f), "alive until the host says it's destroyed");
            h.NetKill();
            Assert.That(h.Dead, Is.True);
            yield return new WaitForSeconds(0.3f);
            Assert.That(h.Dead, Is.True, "it stays down until the host has it back");
            h.NetRespawn();
            Assert.That(h.Dead, Is.False);
            Assert.That(h.Current, Is.EqualTo(1000f));
        }

        [UnityTest]
        public IEnumerator LostWithTheShipGoesTheCargo()
        {
            var h = MakeHealth(100f, respawn: 5f);
            var hold = h.gameObject.AddComponent<CargoHold>();
            hold.Add(Mineral.Gold, 20f);
            yield return null;                          // Start links the hold to the hull
            h.Damage(100f);
            Assert.That(hold.Used, Is.EqualTo(0f));
            Assert.That(hold.TotalLost, Is.EqualTo(20f));
        }
    }

    /// <summary>The shield: soaking up hits, collapsing, coming back and recharging.</summary>
    public class ShieldTests : PlayModeFixture
    {
        Shield MakeShield(Transform parent = null, float capacity = 100f, float recharge = 0f, float delay = 3f, float down = 6f, float radius = 15f)
        {
            var go = Make("Shield");
            if (parent != null) go.transform.SetParent(parent, false);
            go.SetActive(false);
            go.AddComponent<SphereCollider>().radius = radius;
            var s = go.AddComponent<Shield>();
            s.capacity = capacity;
            s.rechargeRate = recharge;
            s.rechargeDelay = delay;
            s.downTime = down;
            go.SetActive(true);
            return s;
        }

        [Test]
        public void ItSoaksUpHitsUntilItRunsOut()
        {
            var s = MakeShield();
            Assert.That(s.Absorb(30f, Vector3.zero, HitKind.Projectile), Is.EqualTo(0f));
            Assert.That(s.Strength, Is.EqualTo(70f));
            Assert.That(s.Fraction, Is.EqualTo(0.7f).Within(1e-5f));
            Assert.That(s.Absorb(100f, Vector3.zero, HitKind.Cannon), Is.EqualTo(30f), "what it can't stop gets through");
            Assert.That(s.Up, Is.False);
            Assert.That(s.Collapses, Is.EqualTo(1));
            Assert.That(s.GetComponent<Collider>().enabled, Is.False, "fire passes through a collapsed shield");
            Assert.That(s.Absorb(10f, Vector3.zero, HitKind.Beam), Is.EqualTo(10f));
            Assert.That(s.HitsByKind[(int)HitKind.Projectile], Is.EqualTo(1));
            Assert.That(s.HitsByKind[(int)HitKind.Cannon], Is.EqualTo(1));
            Assert.That(s.HitsByKind[(int)HitKind.Beam], Is.EqualTo(0), "a hit on a collapsed shield isn't one");
        }

        [UnityTest]
        public IEnumerator ACollapsedShieldComesBackAtThirtyPercent()
        {
            var s = MakeShield(down: 0.2f);
            s.Absorb(100f, Vector3.zero, HitKind.Missile);
            Assert.That(s.Up, Is.False);
            yield return new WaitForSeconds(0.3f);
            Assert.That(s.Up, Is.True);
            Assert.That(s.Strength, Is.EqualTo(30f).Within(0.5f));
            Assert.That(s.GetComponent<Collider>().enabled, Is.True);
        }

        [UnityTest]
        public IEnumerator ItRechargesOnlyAfterAQuietSpell()
        {
            // a long quiet spell, so a slow frame can't reach it before the first look
            var s = MakeShield(recharge: 400f, delay: 1.5f);
            s.Absorb(60f, Vector3.zero, HitKind.Projectile);
            yield return new WaitForSeconds(0.2f);
            Assert.That(s.Strength, Is.EqualTo(40f), "not yet: it was hit just now");
            yield return new WaitForSeconds(2f);
            Assert.That(s.Strength, Is.EqualTo(100f), "full again, and no more");
        }

        [Test]
        public void ScalingKeepsHowChargedItIs()
        {
            var s = MakeShield(capacity: 100f, recharge: 10f);
            s.Absorb(25f, Vector3.zero, HitKind.Projectile);
            s.ScaleTo(2f);
            Assert.That(s.capacity, Is.EqualTo(200f));
            Assert.That(s.rechargeRate, Is.EqualTo(20f));
            Assert.That(s.Strength, Is.EqualTo(150f).Within(1e-3f));
            s.ScaleTo(1f);
            Assert.That(s.capacity, Is.EqualTo(100f), "from the built shield, not on top of the last scaling");
            Assert.That(s.rechargeRate, Is.EqualTo(10f));
            Assert.That(s.Strength, Is.EqualTo(75f).Within(1e-3f));
        }

        [Test]
        public void ScalingLeavesACollapsedShieldDown()
        {
            var s = MakeShield();
            s.Absorb(500f, Vector3.zero, HitKind.Missile);
            s.ScaleTo(3f);
            Assert.That(s.Strength, Is.EqualTo(0f));
            Assert.That(s.Up, Is.False);
        }

        [UnityTest]
        public IEnumerator AnOfflineShieldStaysDown()
        {
            var s = MakeShield(down: 0.05f);
            s.SetOffline(true);
            yield return new WaitForSeconds(0.2f);
            Assert.That(s.Up, Is.False);
            Assert.That(s.Strength, Is.EqualTo(0f));
            s.SetOffline(false);
            Assert.That(s.Up, Is.True);
            Assert.That(s.Strength, Is.EqualTo(100f));
        }

        [Test]
        public void TheShieldTakesAHitBeforeTheHull()
        {
            var ship = Make("Ship");
            var h = ship.AddComponent<Health>();
            var s = MakeShield(ship.transform, capacity: 100f);
            h.Damage(60f, Vector3.zero, HitKind.Cannon);
            Assert.That(h.Current, Is.EqualTo(1000f));
            Assert.That(s.Strength, Is.EqualTo(40f));
            h.Damage(70f, Vector3.zero, HitKind.Cannon);
            Assert.That(h.Current, Is.EqualTo(970f), "the overflow reaches the hull");
            h.Damage(50f, Vector3.zero, HitKind.Projectile);
            Assert.That(h.Current, Is.EqualTo(920f), "all of it, with the shield down");
            Assert.That(h.Hits, Is.EqualTo(2));
        }

        [Test]
        public void AHitWithNoPlaceGoesStraightToTheHull()
        {
            var ship = Make("Ship");
            var h = ship.AddComponent<Health>();
            var s = MakeShield(ship.transform);
            h.Damage(60f);
            Assert.That(h.Current, Is.EqualTo(940f));
            Assert.That(s.Strength, Is.EqualTo(100f));
        }
    }

    /// <summary>Integration: shots flying through the physics world and landing on ships.</summary>
    public class ProjectileTests : PlayModeFixture
    {
        GameObject Ship(string name, Vector3 at, float size = 10f, string faction = null)
        {
            var go = Make(name, at);
            go.AddComponent<BoxCollider>().size = Vector3.one * size;
            go.AddComponent<Health>();
            if (faction != null) go.AddComponent<TargetInfo>().faction = faction;
            return go;
        }

        Projectile Fire(Vector3 from, Vector3 direction, Transform owner = null, float damage = 40f, HitKind kind = HitKind.Projectile, float lifetime = 4f)
        {
            var go = Make("Shot", from);
            go.transform.rotation = Quaternion.LookRotation(direction);
            var p = go.AddComponent<Projectile>();
            p.speed = 1000f;
            p.damage = damage;
            p.kind = kind;
            p.owner = owner;
            p.lifetime = lifetime;
            Physics.SyncTransforms();
            return p;
        }

        /// <summary>Long enough for a shot at 1000 m/s to cross the test's few hundred metres,
        /// however short the frames are.</summary>
        static IEnumerator Flight() { yield return new WaitForSeconds(0.5f); }

        [UnityTest]
        public IEnumerator AShotHitsWhatItIsAimedAt()
        {
            var target = Ship("Target", new Vector3(0f, 0f, 100f));
            var shot = Fire(Vector3.zero, Vector3.forward, damage: 40f);
            yield return Flight();
            Assert.That(target.GetComponent<Health>().Current, Is.EqualTo(960f));
            Assert.That(shot == null, "the shot is spent");
        }

        [UnityTest]
        public IEnumerator AShotThatMissesFliesOnAndExpires()
        {
            var target = Ship("Target", new Vector3(0f, 0f, 100f));
            var shot = Fire(Vector3.zero, Vector3.right, lifetime: 0.15f);
            yield return new WaitForSeconds(0.4f);
            Assert.That(target.GetComponent<Health>().Hits, Is.EqualTo(0));
            Assert.That(shot == null, "gone after its lifetime");
        }

        [UnityTest]
        public IEnumerator AShieldStopsAShot()
        {
            var target = Ship("Shielded", new Vector3(0f, 0f, 200f));
            var bubble = new GameObject("Bubble");
            bubble.transform.SetParent(target.transform, false);
            bubble.SetActive(false);
            bubble.AddComponent<SphereCollider>().radius = 30f;
            var shield = bubble.AddComponent<Shield>();
            shield.capacity = 500f;
            bubble.SetActive(true);
            Fire(Vector3.zero, Vector3.forward, damage: 40f, kind: HitKind.Cannon);
            yield return Flight();
            Assert.That(shield.Strength, Is.EqualTo(460f));
            Assert.That(shield.HitsByKind[(int)HitKind.Cannon], Is.EqualTo(1));
            Assert.That(target.GetComponent<Health>().Current, Is.EqualTo(1000f));
        }

        [UnityTest]
        public IEnumerator AShipDoesntShootItself()
        {
            var shooter = Ship("Shooter", Vector3.zero, size: 4f);
            // a part of the ship in the line of fire (a ray starting inside a collider never hits it)
            var turret = new GameObject("Turret");
            turret.transform.SetParent(shooter.transform, false);
            turret.transform.localPosition = new Vector3(0f, 0f, 20f);
            turret.AddComponent<BoxCollider>().size = Vector3.one * 6f;
            var target = Ship("Target", new Vector3(0f, 0f, 150f));
            Fire(new Vector3(0f, 0f, 3f), Vector3.forward, owner: shooter.transform);
            yield return Flight();
            Assert.That(shooter.GetComponent<Health>().Hits, Is.EqualTo(0));
            Assert.That(target.GetComponent<Health>().Hits, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator AnEnemysShotPassesItsOwnSideAndHitsThePlayer()
        {
            var enemy = Ship("Raider", Vector3.zero, size: 4f, faction: "Hostile");
            var flagship = Ship("Flagship", new Vector3(0f, 0f, 60f), faction: "Hostile");
            var player = Ship("Frigate", new Vector3(0f, 0f, 160f), faction: "Allied");
            Fire(new Vector3(0f, 0f, 5f), Vector3.forward, owner: enemy.transform);
            yield return Flight();
            Assert.That(flagship.GetComponent<Health>().Hits, Is.EqualTo(0), "no friendly fire among the hostiles");
            Assert.That(player.GetComponent<Health>().Hits, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator APlayersShotHitsAnyShipInTheWay()
        {
            var frigate = Ship("Frigate", Vector3.zero, size: 4f, faction: "Allied");
            var raider = Ship("Raider", new Vector3(0f, 0f, 60f), faction: "Hostile");
            Fire(new Vector3(0f, 0f, 5f), Vector3.forward, owner: frigate.transform);
            yield return Flight();
            Assert.That(raider.GetComponent<Health>().Hits, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator AHitKnowsWhoFiredIt()
        {
            var shooter = Ship("Shooter", Vector3.zero, size: 4f);
            var target = Ship("Target", new Vector3(0f, 0f, 100f));
            Transform source = null;
            HitKind kind = HitKind.Beam;
            Health.Intercept = (h, amount, point, k) => { source = Health.Source; kind = k; return false; };
            Fire(new Vector3(0f, 0f, 5f), Vector3.forward, owner: shooter.transform, kind: HitKind.Cannon);
            yield return Flight();
            Assert.That(source, Is.SameAs(shooter.transform));
            Assert.That(kind, Is.EqualTo(HitKind.Cannon));
            Assert.That(Health.Source, Is.Null, "cleared after the hit");
            Assert.That(target.GetComponent<Health>().Hits, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator AShotIsAnnouncedForCoop()
        {
            int announced = 0;
            System.Action<Projectile> count = p => announced++;
            Projectile.Spawned += count;
            try
            {
                Fire(Vector3.zero, Vector3.up);
                yield return null;
                Assert.That(announced, Is.EqualTo(1));
            }
            finally { Projectile.Spawned -= count; }
        }
    }
}
