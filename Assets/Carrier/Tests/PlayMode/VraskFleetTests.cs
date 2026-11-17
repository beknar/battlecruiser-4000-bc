using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Phase = CarrierGame.VraskFleet.Phase;

namespace CarrierGame.Tests
{
    /// <summary>The Vrask fleet's phases (VraskFleet.Next) on made-up facts: picket, bombard,
    /// screen and disable, board, pursue, withdraw, each moving on when its fact is so; its choice
    /// of target (Score); and the Skink's point defence (PointDefence): its picker taking the
    /// players' missile on its way to a Vrask ship (not a hostile one, nor one at someone else), a
    /// mount it aims kept from the ship's own guns, and its shots bringing a players' missile down
    /// where a hostile gun that isn't point defence doesn't.</summary>
    public class VraskFleetTests : PlayModeFixture
    {
        // ------------------------------------------------------------------ the phases

        static VraskFleet.Facts InFight(bool contact = true) => new VraskFleet.Facts
        {
            engaged = true, hasTarget = true, contact = contact, artillery = true, captors = true, shield = 1f,
        };

        [Test]
        public void ThePhasesGoInOrderOnTheFacts()
        {
            var f = InFight(contact: false);
            Assert.That(VraskFleet.Next(Phase.Idle, new VraskFleet.Facts(), 0f), Is.EqualTo(Phase.Idle), "out of the fight");
            Assert.That(VraskFleet.Next(Phase.Idle, f, 0f), Is.EqualTo(Phase.Picket), "in the fight, nobody sees the target yet");
            Assert.That(VraskFleet.Next(Phase.Picket, f, 30f), Is.EqualTo(Phase.Picket));
            f.contact = true;
            Assert.That(VraskFleet.Next(Phase.Picket, f, 30f), Is.EqualTo(Phase.Bombard), "contact: the artillery opens");
            Assert.That(VraskFleet.Next(Phase.Bombard, f, 10f), Is.EqualTo(Phase.Bombard), "the rest hold the line");
            Assert.That(VraskFleet.Next(Phase.Bombard, f, 45f), Is.EqualTo(Phase.Disable), "after a while the Snares go in anyway");
            f.shield = 0.4f;
            Assert.That(VraskFleet.Next(Phase.Bombard, f, 10f), Is.EqualTo(Phase.Disable), "the shield under half");
            f.shield = 1f;
            f.closing = true;
            Assert.That(VraskFleet.Next(Phase.Bombard, f, 10f), Is.EqualTo(Phase.Disable), "the target reacts, closing on the line");
            f.closing = false;
            Assert.That(VraskFleet.Next(Phase.Disable, f, 100f), Is.EqualTo(Phase.Disable), "not disabled yet: the Fang waits");
            f.enginesDown = true;
            Assert.That(VraskFleet.Next(Phase.Disable, f, 1f), Is.EqualTo(Phase.Board), "engines out: the Fang goes in");
            f.enginesDown = false;
            f.freeFor = 5f;
            Assert.That(VraskFleet.Next(Phase.Board, f, 30f), Is.EqualTo(Phase.Board), "free only a moment");
            f.freeFor = 20f;
            Assert.That(VraskFleet.Next(Phase.Board, f, 30f), Is.EqualTo(Phase.Disable), "free again for a while: back to the Snares");
            f.freeFor = 0f;
            f.targetLost = true;
            Assert.That(VraskFleet.Next(Phase.Board, f, 30f), Is.EqualTo(Phase.Pursue), "taken or destroyed: the main fight is over");
            f.targetLost = false;
            Assert.That(VraskFleet.Next(Phase.Pursue, f, 5f), Is.EqualTo(Phase.Pursue), "the cripples first");
            Assert.That(VraskFleet.Next(Phase.Pursue, f, 25f), Is.EqualTo(Phase.Picket), "then the next target, from the top");
            Assert.That(VraskFleet.Next(Phase.Idle, f, 0f), Is.EqualTo(Phase.Picket), "every start by way of the picket, contact or not");
            f.hasTarget = false;
            Assert.That(VraskFleet.Next(Phase.Pursue, f, 100f), Is.EqualTo(Phase.Pursue), "nothing left: still after cripples");
        }

        [Test]
        public void DisabledMeansEnginesOutHeldFastHeldWithoutAShieldOrBoarded()
        {
            var f = InFight();
            Assert.That(f.Disabled, Is.False);
            f.held = true;
            Assert.That(f.Disabled, Is.False, "one beam on a ship with its shield up can be flown out of");
            f.shieldDown = true;
            Assert.That(f.Disabled, Is.True, "held, its shield down");
            f = InFight();
            f.heldFast = true;
            Assert.That(f.Disabled, Is.True, "held fast");
            f = InFight();
            f.boardersAboard = true;
            Assert.That(f.Disabled, Is.True, "boarders aboard already");
            f = InFight();
            f.boardersAboard = true;
            Assert.That(VraskFleet.Next(Phase.Picket, f, 0f), Is.EqualTo(Phase.Board), "a target already disabled at contact: straight to the boarding");
        }

        [Test]
        public void WithoutTheMawOrTheCaptorsThePhasesSkip()
        {
            var f = InFight();
            f.artillery = false;
            Assert.That(VraskFleet.Next(Phase.Picket, f, 0f), Is.EqualTo(Phase.Disable), "no artillery: the Snares go in at contact");
            Assert.That(VraskFleet.Next(Phase.Bombard, f, 0f), Is.EqualTo(Phase.Disable), "the Maw lost mid-bombardment");
            f = InFight();
            f.captors = false;
            Assert.That(VraskFleet.Next(Phase.Bombard, f, 500f), Is.EqualTo(Phase.Bombard), "no Snares or Fang (the siege): it shells on");
        }

        [Test]
        public void LosingTheMawAndTheSnaresIsAWithdrawal()
        {
            var f = InFight();
            f.withdraw = true;
            foreach (var p in new[] { Phase.Picket, Phase.Bombard, Phase.Disable, Phase.Board, Phase.Pursue })
                Assert.That(VraskFleet.Next(p, f, 0f), Is.EqualTo(Phase.Withdraw), p.ToString());
            f.engaged = false;
            Assert.That(VraskFleet.Next(Phase.Withdraw, f, 100f), Is.EqualTo(Phase.Withdraw), "hidden again, it stays withdrawn till they are back");
            Assert.That(VraskFleet.Next(Phase.Idle, f, 0f), Is.EqualTo(Phase.Idle), "out of the fight with them already gone: nothing to withdraw from");
            f.withdraw = false;
            Assert.That(VraskFleet.Next(Phase.Withdraw, f, 100f), Is.EqualTo(Phase.Idle), "back, lying in wait");
            f.engaged = true;
            Assert.That(VraskFleet.Next(Phase.Withdraw, f, 100f), Is.EqualTo(Phase.Picket), "back in a fight: from the top");
        }

        [Test]
        public void TheTargetIsTheBiggestPrizeEasiestToDisable()
        {
            float whole = VraskFleet.Score(10000f, 1f, 1f, 3000f);
            Assert.That(VraskFleet.Score(10000f, 0f, 1f, 3000f), Is.GreaterThan(whole), "its shield down");
            Assert.That(VraskFleet.Score(10000f, 1f, 0.2f, 3000f), Is.GreaterThan(whole), "its engines worn");
            Assert.That(VraskFleet.Score(20000f, 1f, 1f, 3000f), Is.GreaterThan(whole), "a bigger prize");
            Assert.That(VraskFleet.Score(10000f, 1f, 1f, 9000f), Is.LessThan(whole), "further off");
            Assert.That(VraskFleet.Score(6000f, 0f, 0f, 3000f), Is.GreaterThan(VraskFleet.Score(10000f, 1f, 1f, 3000f)),
                        "a smaller ship already broken beats a bigger whole one");
        }

        // ------------------------------------------------------------------ point defence

        /// <summary>A ship of a side ("Hostile", or anything else for the players'), `vrask`: one
        /// of the Vrask.</summary>
        GameObject Ship(string name, Vector3 at, string faction, bool vrask = false)
        {
            var go = Make(name, at);
            go.AddComponent<TargetInfo>().faction = faction;
            if (vrask) go.AddComponent<VraskShip>();
            return go;
        }

        /// <summary>A missile from `owner` at `target`, sitting nearly still `at`.</summary>
        Missile SlowMissile(Vector3 at, Transform owner, Transform target)
        {
            var go = Make("Missile from " + owner.name, at);
            go.SetActive(false);
            var m = go.AddComponent<Missile>();
            m.owner = owner;
            m.target = target;
            m.boostTime = 100f;
            m.boostSpeed = 1f;
            m.lifetime = 100f;
            go.SetActive(true);
            return m;
        }

        [Test]
        public void ThePickerTakesThePlayersMissileAtTheVrask()
        {
            var vrask = Ship("Skink", new Vector3(0f, 0f, 5000f), "Hostile", vrask: true);
            var pirate = Ship("Pirate", new Vector3(0f, 3000f, 0f), "Hostile");
            var player = Ship("Frigate", new Vector3(0f, -3000f, 0f), "Player");
            var bystander = Make("Freighter", new Vector3(3000f, 0f, 0f));
            var atVrask = SlowMissile(new Vector3(0f, 0f, 900f), player.transform, vrask.transform);
            var hostile = SlowMissile(new Vector3(0f, 0f, 300f), pirate.transform, player.transform);
            var atOther = SlowMissile(new Vector3(0f, 0f, 200f), player.transform, bystander.transform);
            Assert.That(atVrask.Hostile || atOther.Hostile, Is.False);
            Assert.That(hostile.Hostile, Is.True);
            Assert.That(PointDefence.AtVrask(atVrask) && !PointDefence.AtVrask(atOther), Is.True);
            Assert.That(PointDefence.Pick(Vector3.zero, p => true), Is.EqualTo(atVrask.transform),
                        "the players' missile on its way to a Vrask ship: not the hostile one, nor the nearer one at someone else");
            Assert.That(PointDefence.Pick(Vector3.zero, p => p.z < 500f), Is.Null, "nothing it can bring to bear");
            var unowned = SlowMissile(new Vector3(0f, 0f, 100f), player.transform, vrask.transform);
            unowned.owner = null;
            Assert.That(PointDefence.Pick(Vector3.zero, p => true), Is.EqualTo(atVrask.transform), "(not one with no owner: nobody's)");
        }

        [UnityTest]
        public IEnumerator AMountItAimsIsKeptFromTheShipsOwnGuns()
        {
            var skink = Ship("Skink", Vector3.zero, "Hostile", vrask: true);
            var player = Ship("Frigate", new Vector3(0f, -3000f, 0f), "Player");
            var mountGo = new GameObject("PD gun");
            mountGo.transform.SetParent(skink.transform, false);
            var mount = mountGo.AddComponent<CarrierTurret>();
            mount.enabled = false;                     // (it only has to be aimed)
            mount.autoFire = false;
            mount.yawLimit = 180f;
            mount.range = 2400f;
            var guns = skink.AddComponent<EnemyWeapons>();
            guns.turrets = new[] { mount };
            guns.holdFire = 0f;
            var pd = skink.AddComponent<PointDefence>();
            pd.mounts = new[] { mount };
            var m = SlowMissile(new Vector3(0f, 0f, 800f), player.transform, skink.transform);
            yield return null;
            yield return null;
            yield return null;
            Assert.That(mount.target, Is.EqualTo(m.transform), "the players' missile coming at it");
            Assert.That(pd.EngagedTarget, Is.EqualTo(m.transform));
            Assert.That(pd.Engagements, Is.GreaterThanOrEqualTo(1));
            guns.weaponsFree = false;
            yield return new WaitForSeconds(0.3f);
            Assert.That(mount.target, Is.Null, "weapons cold: no point defence either");
            guns.weaponsFree = true;
            yield return new WaitForSeconds(0.3f);
            Assert.That(mount.target, Is.EqualTo(m.transform));
            Object.Destroy(m.gameObject);
            yield return new WaitForSeconds(0.3f);
            Assert.That(mount.target, Is.Null, "nothing left: back to its ship's target (none here)");
        }

        [UnityTest]
        public IEnumerator APointDefenceShotBringsAPlayersMissileDown()
        {
            var skink = Ship("Skink", new Vector3(0f, 0f, 3000f), "Hostile", vrask: true);
            var player = Ship("Frigate", new Vector3(0f, -3000f, 0f), "Player");
            var m = SlowMissile(new Vector3(0f, 0f, 40f), player.transform, skink.transform);
            m.toughness = 5f;
            var pirate = Ship("Pirate gun", new Vector3(0f, 5f, -20f), "Hostile");
            var pdShip = Ship("Skink gun", new Vector3(0f, -5f, -20f), "Hostile", vrask: true);
            var pd = pdShip.AddComponent<PointDefence>();
            pd.missileScale = 2f;
            Assert.That(PointDefence.MissileScale(pirate.transform), Is.EqualTo(0f), "a plain hostile gun isn't point defence");
            Assert.That(PointDefence.MissileScale(pdShip.transform), Is.EqualTo(2f));
            Transform by = null;
            void OnDown(Missile x, Transform t) { if (x == m) by = t; }
            Missile.Downed += OnDown;
            try
            {
                int down0 = Missile.ShotDownCount;
                Projectile Bolt(Transform owner)
                {
                    var go = Make("Needle", Vector3.zero);
                    go.SetActive(false);
                    var p = go.AddComponent<Projectile>();
                    p.speed = 2100f;
                    p.lifetime = 1f;
                    p.damage = 3f;
                    go.transform.rotation = Quaternion.LookRotation(Vector3.forward);
                    go.SetActive(true);
                    p.owner = owner;
                    return p;
                }
                Bolt(pirate.transform);
                yield return new WaitForSeconds(0.5f);   // (by time: frames can come thousands a second)
                Assert.That(Missile.ShotDownCount, Is.EqualTo(down0), "a pirate's bolt passes it by");
                Assert.That(m != null, Is.True);
                Bolt(pdShip.transform);
                float until = Time.time + 1f;
                while (Time.time < until && Missile.ShotDownCount == down0) yield return null;
                Assert.That(Missile.ShotDownCount, Is.EqualTo(down0 + 1), "the Skink's needle (3 x 2 against its 5) brings it down");
                Assert.That(by, Is.EqualTo(pdShip.transform), "by the ship whose needle it was");
                Assert.That(pd.ShotDown, Is.EqualTo(1));
            }
            finally { Missile.Downed -= OnDown; }
        }
    }
}
