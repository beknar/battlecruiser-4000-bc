using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CarrierGame.Tests
{
    /// <summary>Boarding (Boarders, BoardingPod, Health.Capture) and point defence against missiles
    /// and pods (CiwsDirector, Projectile, Missile): the fight's numbers, a big crew repelling a
    /// pod's breachers, a crew at its survivors captured, the hold seized and the credits kept, the
    /// ship back whole, a pod getting aboard only with the shield down, the CIWS taking a hostile
    /// missile (and not a friendly one), a bolt shooting one down, and the prisoners saved.</summary>
    public class BoardingTests : PlayModeFixture
    {
        /// <summary>A crewed ship with a hull, a hold and (optionally) a shield.</summary>
        (Health health, Crew crew, CargoHold hold) Ship(int complement = 140, float respawn = 30f, bool shield = false)
        {
            var go = Make("Boarded", new Vector3(0f, 0f, 0f));
            go.SetActive(false);                       // (so Awake sees the settings)
            var h = go.AddComponent<Health>();
            h.maxHealth = 1000f;
            h.respawnDelay = respawn;
            var crew = go.AddComponent<Crew>();
            crew.complement = complement;
            var hold = go.AddComponent<CargoHold>();
            if (shield)
            {
                var sg = new GameObject("Shield");
                sg.transform.SetParent(go.transform, false);
                sg.AddComponent<SphereCollider>().radius = 120f;   // (a shield needs its collider)
                sg.AddComponent<Shield>().capacity = 1000f;
            }
            go.SetActive(true);
            return (h, crew, hold);
        }

        /// <summary>Run a fight to its end (at most `seconds` of it), in quarter-second steps.</summary>
        static void FightOut(Boarders b, float seconds = 120f)
        {
            for (float t = 0f; t < seconds && b.Result == Boarders.Outcome.Fighting; t += 0.25f) b.Step(0.25f);
        }

        [Test]
        public void TheFightGoesByNumbers()
        {
            Boarders.Fight(10f, 140f, 1f, out float crewLost, out float boardersLost);
            Assert.That(crewLost, Is.EqualTo(10f * Boarders.KillRate).Within(1e-4f), "each boarder overcomes its share of crew");
            Assert.That(boardersLost, Is.EqualTo(40f * Boarders.DefendRate).Within(1e-4f), "only so many crew can get at ten boarders");
            Boarders.Fight(20f, 140f, 1f, out float crewLost2, out float boardersLost2);
            Assert.That(crewLost2, Is.EqualTo(2f * crewLost).Within(1e-4f), "twice the boarders, twice the crew lost");
            Assert.That(boardersLost2, Is.EqualTo(2f * boardersLost).Within(1e-4f), "and twice as many of the crew fighting them");
            Boarders.Fight(10f, 20f, 1f, out _, out float fewKill);
            Assert.That(fewKill, Is.EqualTo(20f * Boarders.DefendRate).Within(1e-4f), "a small crew kills fewer");
            Boarders.Fight(10f, 1000f, 1f, out _, out float manyKill);
            Assert.That(manyKill, Is.EqualTo(boardersLost).Within(1e-4f), "a huge crew no more than the frontage allows");
            Boarders.Fight(10f, 140f, 0f, out float none, out _);
            Assert.That(none, Is.EqualTo(0f));
        }

        [Test]
        public void ABigCrewRepelsAPodsBreachers()
        {
            var (health, crew, _) = Ship();
            var b = Boarders.Board(health.transform, 10);
            Assert.That(b, Is.Not.Null);
            Assert.That(b.Result, Is.EqualTo(Boarders.Outcome.Fighting));
            Assert.That(b.Aboard, Is.EqualTo(10));
            Assert.That(Boarders.CountOn(health.transform), Is.EqualTo(10));
            StringAssert.StartsWith("BOARDERS ABOARD: 10", Boarders.HudText(health.transform));
            FightOut(b);
            Assert.That(b.Result, Is.EqualTo(Boarders.Outcome.Repelled));
            Assert.That(b.Aboard, Is.EqualTo(0));
            Assert.That(Boarders.HudText(health.transform), Is.Empty);
            Assert.That(health.Dead || health.Captured, Is.False);
            Assert.That(b.CrewKilled, Is.GreaterThan(1f), "they cost the crew");
            Assert.That(crew.Current, Is.LessThan(140f));
            Assert.That(crew.AtFloor, Is.False, "a full crew is nowhere near its survivors");
            Assert.That(b.Freed, Is.GreaterThan(0), "those they had taken are freed");
            Assert.That(b.BoardersKilled, Is.EqualTo(10f).Within(0.01f));
        }

        [Test]
        public void MoreBoardersCostMoreCrew()
        {
            var (h1, c1, _) = Ship();
            var (h2, c2, _) = Ship();
            var one = Boarders.Board(h1.transform, 10);
            var three = Boarders.Board(h2.transform, 30);
            FightOut(one);
            FightOut(three);
            Assert.That(c2.Current, Is.LessThan(c1.Current), "three pods' worth hurts more than one");
        }

        [Test]
        public void ACrewDownToItsSurvivorsIsCaptured()
        {
            var (health, crew, _) = Ship();
            int destroyed = 0, taken = 0, anyCaptured = 0;
            void OnAny(Health h) { if (h == health) destroyed++; }
            void OnAnyCaptured(Health h) { if (h == health) anyCaptured++; }
            health.onDestroyed.AddListener(() => destroyed++);
            health.Taken += () => taken++;
            Health.AnyDestroyed += OnAny;
            Health.AnyCaptured += OnAnyCaptured;
            try
            {
                int prisoners0 = WorldState.Prisoners;
                crew.SetAboard(crew.Floor + 6f);
                var b = Boarders.Board(health.transform, 12);
                FightOut(b);
                Assert.That(b.Result, Is.EqualTo(Boarders.Outcome.Captured));
                Assert.That(crew.Current, Is.EqualTo(crew.Floor).Within(0.01f), "the survivors are left");
                Assert.That(health.Dead && health.Captured, Is.True);
                Assert.That(health.TimesDestroyed, Is.EqualTo(0), "captured, not destroyed");
                Assert.That(health.TimesCaptured, Is.EqualTo(1));
                Assert.That(destroyed, Is.EqualTo(0), "no breakup, no wreck: neither onDestroyed nor AnyDestroyed");
                Assert.That(taken, Is.EqualTo(1));
                Assert.That(anyCaptured, Is.EqualTo(1));
                Assert.That(b.Prisoners, Is.EqualTo(Mathf.RoundToInt(6f * Boarders.TakenShare)));
                Assert.That(WorldState.Prisoners - prisoners0, Is.EqualTo(b.Prisoners), "the Vrask hold them");
                Assert.That(health.Current, Is.GreaterThan(0f), "the hull isn't touched");
            }
            finally
            {
                Health.AnyDestroyed -= OnAny;
                Health.AnyCaptured -= OnAnyCaptured;
            }
        }

        [Test]
        public void ACrewAlreadyAtItsSurvivorsCantFightThemOff()
        {
            var (health, crew, _) = Ship();
            crew.SetAboard(crew.Floor);
            var b = Boarders.Board(health.transform, 8);
            Assert.That(b.Result, Is.EqualTo(Boarders.Outcome.Fighting), "not taken the moment the pod breaches");
            FightOut(b, Boarders.MinFight + 5f);
            Assert.That(b.Result, Is.EqualTo(Boarders.Outcome.Captured));
            Assert.That(health.Captured, Is.True);
        }

        [UnityTest]
        public IEnumerator CaptureSeizesTheHoldButNotTheCredits()
        {
            var (health, crew, hold) = Ship();
            yield return null;                         // (the hold listens from Start)
            hold.Add(Mineral.Iron, 50f);
            hold.Earn(1200f);
            Assert.That(hold.Used, Is.GreaterThan(0f));
            crew.SetAboard(crew.Floor + 2f);
            FightOut(Boarders.Board(health.transform, 10));
            Assert.That(health.Captured, Is.True);
            Assert.That(hold.Used, Is.EqualTo(0f), "the cargo is seized");
            Assert.That(hold.TotalSeized, Is.EqualTo(50f).Within(0.01f));
            Assert.That(hold.Credits, Is.EqualTo(1200f), "the credits are kept");
        }

        [UnityTest]
        public IEnumerator ACapturedShipComesBackWhole()
        {
            var (health, crew, _) = Ship(respawn: 0.4f);
            yield return null;
            health.Damage(300f);
            crew.SetAboard(crew.Floor + 1f);
            var b = Boarders.Board(health.transform, 10);
            FightOut(b);
            Assert.That(health.Captured, Is.True);
            Assert.That(health.RespawnAt, Is.GreaterThan(Time.time));
            yield return new WaitForSeconds(0.7f);
            Assert.That(health.Dead, Is.False);
            Assert.That(health.Captured, Is.False);
            Assert.That(health.Current, Is.EqualTo(health.maxHealth));
            Assert.That(b.Result, Is.EqualTo(Boarders.Outcome.None), "the fight cleared");
            Assert.That(crew.Current, Is.EqualTo(crew.Floor).Within(0.01f), "the survivors bring her back");
        }

        [Test]
        public void AShipWithNoCrewIsntBoarded()
        {
            var (health, crew, _) = Ship(complement: 0);
            Assert.That(Boarders.CanBoard(health.transform), Is.False);
            Assert.That(Boarders.Board(health.transform, 10), Is.Null);
        }

        [UnityTest]
        public IEnumerator APodBreachesOnlyWithTheShieldDown()
        {
            var (health, _, _) = Ship(shield: true);
            var shield = health.GetComponentInChildren<Shield>();
            yield return null;
            Assert.That(BoardingPod.ShieldLets(health.transform), Is.False, "a full shield keeps pods out");

            BoardingPod Pod()
            {
                var go = Make("Pod", health.transform.position + Vector3.forward * 20f);
                go.SetActive(false);
                var p = go.AddComponent<BoardingPod>();
                p.target = health.transform;
                p.boostTime = 0f;
                p.boostSpeed = 1f;
                p.proximity = 25f;
                p.damage = 30f;
                go.SetActive(true);
                return p;
            }

            int breached0 = BoardingPod.Breached, burst0 = BoardingPod.Burst;
            Pod();
            for (int i = 0; i < 5; i++) yield return null;
            Assert.That(BoardingPod.Burst, Is.EqualTo(burst0 + 1), "it bursts on the shield");
            Assert.That(BoardingPod.Breached, Is.EqualTo(breached0));
            Assert.That(Boarders.CountOn(health.transform), Is.EqualTo(0));

            shield.SetOffline(true);
            Assert.That(BoardingPod.ShieldLets(health.transform), Is.True);
            Pod();
            for (int i = 0; i < 5; i++) yield return null;
            Assert.That(BoardingPod.Breached, Is.EqualTo(breached0 + 1), "with the shield down it breaches");
            Assert.That(Boarders.CountOn(health.transform), Is.InRange(Boarders.PodMin - 1, Boarders.PodMax),
                        "a swarm of breachers aboard (the crew already fighting them)");

            shield.SetOffline(false);
            shield.Absorb(shield.capacity * 0.8f, health.transform.position, HitKind.Cannon);
            Assert.That(shield.Up && shield.Fraction < BoardingPod.ShieldBelow, Is.True);
            Assert.That(BoardingPod.ShieldLets(health.transform), Is.True, "a shield under a quarter lets them through too");
        }

        /// <summary>A missile from a ship of a side, sitting nearly still `at`.</summary>
        Missile SlowMissile(Vector3 at, string faction)
        {
            var owner = Make(faction + " ship", at + Vector3.up * 3000f);
            owner.AddComponent<TargetInfo>().faction = faction;
            var go = Make(faction + " missile", at);
            go.SetActive(false);
            var m = go.AddComponent<Missile>();
            m.owner = owner.transform;
            m.boostTime = 100f;
            m.boostSpeed = 1f;
            m.lifetime = 100f;
            go.SetActive(true);
            return m;
        }

        [UnityTest]
        public IEnumerator TheCiwsTakeAHostileMissileNotAFriendlyOne()
        {
            var ship = Make("Ship");
            var mountGo = new GameObject("CIWS");
            mountGo.transform.SetParent(ship.transform, false);
            var mount = mountGo.AddComponent<CarrierTurret>();
            mount.enabled = false;                     // (it only has to be picked for)
            mount.autoFire = false;
            mount.yawLimit = 180f;
            mount.range = 2200f;
            var ciws = ship.AddComponent<CiwsDirector>();
            ciws.mounts = new[] { mount };
            var friendly = SlowMissile(new Vector3(0f, 0f, 300f), "Friendly");
            var hostile = SlowMissile(new Vector3(0f, 0f, 900f), "Hostile");
            Assert.That(hostile.Hostile, Is.True);
            Assert.That(friendly.Hostile, Is.False);
            yield return null;
            yield return null;
            Assert.That(mount.target, Is.EqualTo(hostile.transform), "the hostile missile, not the nearer friendly one");
            Assert.That(ciws.EngagedTarget, Is.EqualTo(hostile.transform));
            Object.Destroy(hostile.gameObject);
            yield return new WaitForSeconds(0.3f);
            Assert.That(mount.target, Is.Null, "nothing left to engage");
        }

        [UnityTest]
        public IEnumerator ABoltShootsAHostileMissileDown()
        {
            // (well away from the middle of the scene, where other tests' ships stand)
            Vector3 here = new Vector3(-7300f, 4100f, 13900f);
            var m = SlowMissile(here + new Vector3(0f, 0f, 40f), "Hostile");
            m.toughness = 5f;
            Transform by = null;
            void OnDown(Missile x, Transform t) { if (x == m) by = t; }
            Missile.Downed += OnDown;
            try
            {
                int down0 = Missile.ShotDownCount;
                var shooter = Make("Shooter", here + Vector3.back * 50f);
                var go = Make("Bolt", here);
                go.SetActive(false);
                var p = go.AddComponent<Projectile>();
                p.speed = 2300f;
                p.lifetime = 2f;
                p.damage = 8f;
                go.transform.rotation = Quaternion.LookRotation(Vector3.forward);
                go.SetActive(true);
                p.owner = shooter.transform;
                // (by time, not frames: with nothing drawn, frames can come thousands a second)
                float until = Time.time + 1f;
                while (Time.time < until && Missile.ShotDownCount == down0) yield return null;
                Assert.That(Missile.ShotDownCount, Is.EqualTo(down0 + 1), "shot down");
                Assert.That(by, Is.EqualTo(shooter.transform), "by the ship whose bolt it was");
                yield return null;
                Assert.That(m == null, Is.True, "gone");
            }
            finally { Missile.Downed -= OnDown; }
        }

        [Test]
        public void ThePrisonersAreSavedAndAnOldSaveHasNone()
        {
            var old = JsonUtility.FromJson<WorldState.Saved>("{\"clock\":12.5}");
            Assert.That(old.prisoners, Is.EqualTo(0));
            Assert.That(old.shipsTaken, Is.EqualTo(0));
            var kept = WorldState.Snapshot();
            try
            {
                WorldState.Restore(old);
                Assert.That(WorldState.Prisoners, Is.EqualTo(0));
                WorldState.TakePrisoners(7);
                WorldState.ShipTaken();
                var s = JsonUtility.FromJson<WorldState.Saved>(JsonUtility.ToJson(WorldState.Snapshot()));
                Assert.That(s.prisoners, Is.EqualTo(7));
                Assert.That(s.shipsTaken, Is.EqualTo(1));
                WorldState.Clear();
                Assert.That(WorldState.Prisoners, Is.EqualTo(0));
                WorldState.Restore(s);
                Assert.That(WorldState.Prisoners, Is.EqualTo(7));
                Assert.That(WorldState.ShipsTaken, Is.EqualTo(1));
            }
            finally { WorldState.Restore(kept); }
        }
    }
}
