using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A long-range artillery piece: the Vrask Maw's spinal gun. It reaches far further than any
    /// turret (10 to 14 km) but fires far less often, and its shells (ArtilleryShell) are slow:
    /// each is aimed at where the target will be when it arrives (Predict, from the target's
    /// velocity), so it takes many seconds to land and can be dodged, and it bursts over a wide
    /// area.
    ///
    /// It picks its round for each target (ChooseRound): a shield nullifier at a ship whose shield
    /// is up, an armour penetrator at one that is armoured or has its shield down, high explosive
    /// at anything else. It lays its shells within `cone` degrees of the ship's bow (the gun runs
    /// along the keel: the ship turns to bring it to bear). It doesn't choose what to fire at, or
    /// when: the Maw's tactics do (MawTactics: only at a target another Vrask ship has spotted,
    /// on the salvo's beat). Not while its ship is destroyed, lying in wait, or its weapons are
    /// knocked out (ShipSystems).
    /// </summary>
    public class ArtilleryGun : MonoBehaviour
    {
        public enum Round { HighExplosive, Nullifier, Penetrator }

        public ArtilleryShell shellPrefab;
        [Tooltip("Where the shells leave the barrel, facing along it (the Maw's spinal_muzzle).")]
        public Transform muzzle;
        [Tooltip("Seconds between shots.")]
        public float reload = 16f;
        [Tooltip("Furthest (m) it fires.")]
        public float range = 13000f;
        [Tooltip("Nearest (m) it fires: closer, its own blast would be too near.")]
        public float minRange = 1500f;
        [Tooltip("How far (degrees) off the ship's bow it can lay a shell.")]
        public float cone = 20f;
        public string label = "Spinal artillery";
        [Tooltip("A shield up above this share of its strength gets the nullifier.")]
        [Range(0f, 1f)] public float shieldUpAbove = 0.1f;

        public int Fired { get; private set; }
        /// <summary>How far its last shot's aim point was, its round, its aim, and its flight (s).</summary>
        public float LastRange { get; private set; }
        public Round LastRound { get; private set; }
        public Vector3 LastAim { get; private set; }
        public float LastFlight { get; private set; }
        public ArtilleryShell LastShell { get; private set; }
        /// <summary>What it has fired at, by name (the self-test).</summary>
        public readonly HashSet<string> FiredAt = new HashSet<string>();
        public float ReadyIn => Mathf.Max(0f, nextShot - Time.time);

        float nextShot;
        Health health;
        ShipSystems systems;
        EnemyWeapons weapons;
        CombatPilot pilot;

        void Awake()
        {
            health = GetComponentInParent<Health>();
            weapons = GetComponentInParent<EnemyWeapons>();
            pilot = GetComponentInParent<CombatPilot>();
            nextShot = Time.time + reload * 0.4f;
        }

        Transform Muzzle => muzzle != null ? muzzle : transform;
        /// <summary>Its ship (not the scene's root: the siege group's ships sit in a planet's frame).</summary>
        Transform Ship => health != null ? health.transform : transform.root;

        /// <summary>It can fire now (loaded, its ship alive and in the fight, its weapons working).</summary>
        /// <summary>No shot before `time` (a beat shared with other guns).</summary>
        public void HoldUntil(float time) => nextShot = Mathf.Max(nextShot, time);

        public bool Ready => shellPrefab != null && Time.time >= nextShot && (health == null || !health.Dead) &&
                             (weapons == null || weapons.weaponsFree) && (pilot == null || pilot.Current != CombatPilot.State.Lurk) &&
                             !ShipSystems.WeaponsDownOn(this, ref systems);

        /// <summary>Where a target at `at`, moving at `velocity`, will be when a shell fired from
        /// `from` at `speed` gets there, and how long that takes (s).</summary>
        public static Vector3 Predict(Vector3 from, Vector3 at, Vector3 velocity, float speed, out float flight)
        {
            Vector3 p = Ballistics.Lead(from, at, velocity, speed, refine: 3);
            flight = Vector3.Distance(from, p) / Mathf.Max(1f, speed);
            return p;
        }

        /// <summary>The kind of hit a round does.</summary>
        public static HitKind KindOf(Round r) => r == Round.Nullifier ? HitKind.Nullifier : r == Round.Penetrator ? HitKind.Penetrator : HitKind.Artillery;

        /// <summary>The round for a target: the nullifier while its shield is up; the penetrator
        /// at armour, or a shield that is down; high explosive at anything else (an unshielded,
        /// unarmoured target). Another player's ship: as its game last said of its shield.</summary>
        public static Round ChooseRound(Transform target, float shieldUpAbove = 0.1f)
        {
            if (target == null) return Round.HighExplosive;
            var ship = TractorBeam.ShipOf(target);
            if (ship == null) ship = target;
            var rp = ship.GetComponent<Demo.RemotePlayer>();
            if (rp != null) return rp.Shield > shieldUpAbove ? Round.Nullifier : Round.Penetrator;
            var shield = ship.GetComponentInChildren<Shield>(true);
            if (shield != null && shield.Up && shield.Fraction > shieldUpAbove) return Round.Nullifier;
            var armor = ship.GetComponentInParent<Armor>();
            if (armor != null && armor.Integrity > 0f) return Round.Penetrator;
            return shield != null ? Round.Penetrator : Round.HighExplosive;
        }

        /// <summary>Where it would aim at a target now, how far that is, and whether it can lay a
        /// shell there (in its range and its cone).</summary>
        public bool CanHit(Transform target, out Vector3 aim, out float flight)
        {
            aim = Vector3.zero;
            flight = 0f;
            if (target == null || shellPrefab == null) return false;
            var mover = Motion.Of(target);
            var from = Muzzle.position;
            aim = Predict(from, target.position, mover != null ? mover.Velocity : Vector3.zero, shellPrefab.speed, out flight);
            float d = Vector3.Distance(from, aim);
            if (d > range || d < minRange || flight > shellPrefab.maxFlight) return false;
            return Vector3.Angle(Ship.forward, aim - from) <= cone;
        }

        /// <summary>Fire a shell at a target if it can (see Ready, CanHit): the round chosen for
        /// it, at where it will be. `at`: aim there instead (a part of an installation).</summary>
        public bool Fire(Transform target, Transform at = null)
        {
            if (!Ready || !CanHit(at != null ? at : target, out var aim, out float flight)) return false;
            return FireAt(aim, ChooseRound(target, shieldUpAbove), target != null ? target.name : "");
        }

        /// <summary>A shell at a point, of a round (the self-test; Fire): no checks but loaded.</summary>
        public bool FireAt(Vector3 aim, Round round, string at = "")
        {
            if (shellPrefab == null) return false;
            nextShot = Time.time + reload;
            var from = Muzzle.position;
            LastShell = ArtilleryShell.Launch(shellPrefab, from, aim, KindOf(round), Ship);
            if (LastShell == null) return false;
            Fired++;
            LastRange = Vector3.Distance(from, aim);
            LastRound = round;
            LastAim = aim;
            LastFlight = LastShell.Flight;
            if (!string.IsNullOrEmpty(at)) FiredAt.Add(at);
            SoundFX.Play(Sfx.Cannon, from, 1f, 0.35f);
            return true;
        }
    }
}
