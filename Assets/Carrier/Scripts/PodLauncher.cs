using System.Collections;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A Vrask ship's boarding-pod hatches: launches a wave of boarding pods (BoardingPod), one from
    /// each hatch in turn, then waits `waveInterval` before the next. Not while the ship is
    /// destroyed, lying in wait (its weapons cold), or its weapons are knocked out (ShipSystems).
    ///
    /// The Fang's tactics fire it as it closes (FangTactics). A Snare's (`atHeld`) fires by itself
    /// at the ship its tractor beam holds, once that ship's shield is down: the Snare boards what
    /// it has caught (there is no command ship to hand it to yet).
    /// </summary>
    public class PodLauncher : MonoBehaviour
    {
        public BoardingPod podPrefab;
        [Tooltip("Where the pods come out, facing the way they leave (the Fang's boarding_pod points).")]
        public Transform[] hatches;
        public int perWave = 3;
        public float waveInterval = 18f;
        [Tooltip("Furthest (m) it launches at a ship.")]
        public float range = 3500f;
        [Tooltip("Launches by itself at the ship its tractor beam holds (the Snare).")]
        public bool atHeld;

        /// <summary>Pods launched, and waves, since the level started.</summary>
        public int Launched { get; private set; }
        public int Waves { get; private set; }

        float nextWave;
        int next;
        Health health;
        ShipSystems systems;
        EnemyWeapons weapons;
        CombatPilot pilot;
        TractorBeam beam;

        void Awake()
        {
            health = GetComponentInParent<Health>();
            weapons = GetComponentInParent<EnemyWeapons>();
            pilot = GetComponentInParent<CombatPilot>();
            beam = GetComponentInParent<TractorBeam>();
            nextWave = Time.time + waveInterval * 0.5f;
        }

        /// <summary>A wave can go now.</summary>
        public bool Ready => podPrefab != null && Time.time >= nextWave && (health == null || !health.Dead) &&
                             (weapons == null || weapons.weaponsFree) && !ShipSystems.WeaponsDownOn(this, ref systems);

        /// <summary>Launch a wave at a ship in range, if one can go: how many pods (0: none).</summary>
        public int LaunchWave(Transform target)
        {
            if (!Ready || target == null || (target.position - transform.position).sqrMagnitude > range * range) return 0;
            nextWave = Time.time + waveInterval;
            Waves++;
            StartCoroutine(Wave(target, perWave));
            return perWave;
        }

        IEnumerator Wave(Transform target, int n)
        {
            for (int i = 0; i < n; i++)
            {
                if (target == null || (health != null && health.Dead)) yield break;
                var at = Hatch();
                Vector3 v = pilot != null ? pilot.Velocity : Vector3.zero;
                Fire(podPrefab, at.position, at.rotation, transform.root, target, v);
                Launched++;
                yield return new WaitForSeconds(0.3f);
            }
        }

        Transform Hatch()
        {
            if (hatches == null || hatches.Length == 0) return transform;
            for (int tries = 0; tries < hatches.Length; tries++)
            {
                var h = hatches[next];
                next = (next + 1) % hatches.Length;
                if (h != null) return h;
            }
            return transform;
        }

        /// <summary>One pod, launched from a point at a ship (also the self-test's).</summary>
        public static BoardingPod Fire(BoardingPod prefab, Vector3 at, Quaternion rotation, Transform owner, Transform target, Vector3 inherited = default)
        {
            var pod = Instantiate(prefab, at, rotation);
            pod.owner = owner;
            pod.target = target;
            pod.inheritedVelocity = inherited;
            return pod;
        }

        void Update()
        {
            // the Snare: boards the ship it holds, once its shield is down
            if (!atHeld || beam == null || !Ready) return;
            var held = beam.Holding;
            if (held == null || !BoardingPod.ShieldLets(held) || !Boarders.CanBoard(held)) return;
            if (pilot != null && pilot.Current != CombatPilot.State.Engage) return;
            LaunchWave(held);
        }
    }
}
