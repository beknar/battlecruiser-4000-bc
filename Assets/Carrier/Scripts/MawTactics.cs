using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// How a Vrask Maw fights, on top of its CombatPilot (which still flies it, and keeps it among
    /// the active enemy pilots for the warp lock and the music), with its spinal artillery
    /// (ArtilleryGun) and its nuclear silos (NukeLauncher):
    ///
    /// - Backline station-keeping: it holds 8 to 12 km from its target, on the far side of the
    ///   rest of the Vrask fighting near it (they screen it), bow on, and once there it hardly
    ///   moves.
    /// - Spotting: it fires only at a target another Vrask ship has within its sensors (6 km, or
    ///   further for a Skink: VraskShip.sensorRange): a Skink's, a Snare's, a Fang's. On its own it
    ///   has no firing solution.
    /// - Salvos: its gun fires on a beat every Maw keeps (SalvoTick), so two Maws fire together;
    ///   one fires alone on the same beat.
    /// - Siege breaker: its shells go for a ship's shield generator while the shield is up, then
    ///   its weapons (ShipSystems.SetAim); at an installation, for its shield generators and gun
    ///   batteries first (SiegePart). The round fits the target (ArtilleryGun.ChooseRound).
    /// - Protect the battery: a ship within 3 km and it falls back toward its escorts, slowly.
    /// - Its nuclear missiles, rarely: at a fortified target (a big ship whose shield holds under
    ///   its shells, or ships bunched within the blast), one at a time, and only so many.
    ///
    /// On station with no pilot (the siege group over a city: VraskBombardment), it turns in
    /// place to bring its gun to bear on whatever its guns are firing at, and fires by the same
    /// rules.
    /// </summary>
    public class MawTactics : MonoBehaviour, CombatPilot.ITactics
    {
        [Tooltip("The band (m) it keeps from its target.")]
        public float nearBand = 8000f;
        public float farBand = 12000f;
        [Tooltip("How far (m) the other Vrask ships' sensors reach: it fires only at a target one of them has this near.")]
        public float spotRange = 6000f;
        [Tooltip("A ship this close (m) and it falls back toward its escorts.")]
        public float threatRange = 3000f;
        [Tooltip("Its escorts: Vrask ships this near it (m) it falls back toward.")]
        public float escortRange = 6000f;
        [Tooltip("On station (no pilot): how fast it turns (degrees a second) to bring its gun to bear.")]
        public float stationTurn = 4f;
        [Tooltip("Its shells hitting a target whose shield still holds above this share: the target is fortified (a nuke).")]
        [Range(0f, 1f)] public float holdsAbove = 0.5f;
        [Tooltip("Shells on one target before it counts its shield as holding.")]
        public int shellsBeforeNuke = 3;
        [Tooltip("This many of the players' ships within a nuke's blast of its target, and it is worth one.")]
        public int bunched = 3;

        /// <summary>Every Maw's tactics.</summary>
        public static readonly List<MawTactics> All = new List<MawTactics>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetAll() { All.Clear(); }

        /// <summary>The ship it is working (null out of the fight), whether another Vrask ship has
        /// it in sight and which, and what its shells go for on it.</summary>
        public Transform Target { get; private set; }
        public bool Spotted { get; private set; }
        public VraskShip Spotter { get; private set; }
        public ShipSystem AimedAt { get; private set; }
        public Transform SiegeAim { get; private set; }
        /// <summary>Falling back from a ship too close, and from which.</summary>
        public bool FallingBack { get; private set; }
        public Transform Threat { get; private set; }
        public int FallBacks { get; private set; }
        /// <summary>Salvos fired, how many of them another Maw fired on the same beat, and the last's beat.</summary>
        public int Salvos { get; private set; }
        public int SharedSalvos { get; private set; }
        public int LastTick { get; private set; } = -1;
        /// <summary>Nuclear missiles it has launched, and why the last.</summary>
        public int Nukes { get; private set; }
        public string LastNukeWhy { get; private set; } = "";
        /// <summary>Holding its fire for want of a spotter (since the fight began).</summary>
        public int HeldUnspotted { get; private set; }

        CombatPilot pilot;
        EnemyWeapons weapons;
        ArtilleryGun gun;
        NukeLauncher nukes;
        Health health;
        VraskShip self;
        Quaternion rest;
        float nextThink;
        Transform shellsOn;
        int shellsOnCount;

        void Awake()
        {
            pilot = GetComponent<CombatPilot>();
            weapons = GetComponent<EnemyWeapons>();
            gun = GetComponentInChildren<ArtilleryGun>(true);
            nukes = GetComponentInChildren<NukeLauncher>(true);
            health = GetComponent<Health>();
            self = GetComponent<VraskShip>();
            rest = transform.localRotation;
            if (pilot != null) pilot.tactics = this;
        }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }

        void OnDisable()
        {
            All.Remove(this);
            StandDown();
        }

        void OnDestroy()
        {
            if (pilot != null && ReferenceEquals(pilot.tactics, this)) pilot.tactics = null;
        }

        void StandDown()
        {
            if (Target != null) ShipSystems.SetAim(transform, ShipSystem.Hull);
            if (pilot != null) pilot.keepTarget = false;
            Target = null;
            Spotted = false;
            Spotter = null;
            SiegeAim = null;
            FallingBack = false;
            Threat = null;
        }

        // ------------------------------------------------------------------ the beat

        /// <summary>The salvo beat every Maw keeps: the number of the beat now.</summary>
        public static int SalvoTick(float interval) => Mathf.FloorToInt(Time.time / Mathf.Max(1f, interval));

        /// <summary>Other Maws that fired on a beat.</summary>
        static int FiredOn(int tick, MawTactics except)
        {
            int n = 0;
            foreach (var m in All) if (m != null && m != except && m.LastTick == tick) n++;
            return n;
        }

        // ------------------------------------------------------------------ spotting

        /// <summary>Does another Vrask ship (alive) have `target` within `range`? Which one.</summary>
        public static bool IsSpotted(Transform target, VraskShip self, float range, out VraskShip by)
        {
            by = null;
            if (target == null) return false;
            float best = float.MaxValue;
            foreach (var v in VraskShip.All)
            {
                if (v == null || v == self || !v.gameObject.activeInHierarchy) continue;
                var h = v.GetComponent<Health>();
                if (h != null && h.Dead) continue;
                float d = (v.transform.position - target.position).sqrMagnitude;
                // (a Skink's sensors reach further: the group's picket)
                float reach = Mathf.Max(range, v.sensorRange);
                if (d <= reach * reach && d < best) { best = d; by = v; }
            }
            return by != null;
        }

        // ------------------------------------------------------------------ the siege breaker

        /// <summary>At an installation (no ShipSystems): the part its shells go for, its shield
        /// generators and its gun batteries first, the nearest still standing; null for a ship.</summary>
        public static Transform SiegePart(Transform target, Vector3 from)
        {
            if (target == null || target.GetComponentInParent<ShipSystems>() != null) return null;
            // (the installation it belongs to: its defence grid's, or its own)
            Transform root = null;
            var grid = target.GetComponentInParent<DefenseGrid>();
            if (grid != null) root = grid.transform;
            else { var inst = target.GetComponentInParent<Installation>(); if (inst != null) root = inst.transform; }
            if (root == null) return null;
            Transform best = null;
            float bestD = float.MaxValue;
            void Consider(Component c, float weight)
            {
                if (c == null) return;
                var h = c.GetComponentInParent<Health>();
                if (h != null && h.Dead) return;
                float d = Vector3.Distance(from, c.transform.position) * weight;
                if (d < bestD) { bestD = d; best = c.transform; }
            }
            foreach (var s in root.GetComponentsInChildren<Shield>()) Consider(s, 0.5f);
            foreach (var g in root.GetComponentsInChildren<DefenseGrid>())
                if (g.weapons != null) foreach (var w in g.weapons) Consider(w, 0.75f);
            foreach (var w in root.GetComponentsInChildren<EnemyWeapons>()) Consider(w, 0.75f);
            return best;
        }

        // ------------------------------------------------------------------ thinking

        bool Alive => health == null || !health.Dead;

        void Update()
        {
            if (!Alive) { if (Target != null) StandDown(); return; }
            if (Time.time >= nextThink)
            {
                nextThink = Time.time + 0.25f;
                Think();
            }
            if (pilot == null) Station(Time.deltaTime);
            if (Target == null || gun == null) return;
            // the salvo: on the beat, at a spotted target in reach
            int tick = SalvoTick(gun.reload);
            if (tick == LastTick || !gun.Ready) return;
            if (!Spotted) return;
            if (gun.Fire(Target, SiegeAim))
            {
                Salvos++;
                LastTick = tick;
                // (the next not before the next beat starts: two Maws fall into step and fire together)
                gun.HoldUntil((tick + 1) * Mathf.Max(1f, gun.reload));
                if (FiredOn(tick, this) > 0) SharedSalvos++;
                if (shellsOn != Target) { shellsOn = Target; shellsOnCount = 0; }
                shellsOnCount++;
            }
        }

        void Think()
        {
            Transform t = null;
            if (pilot != null)
            {
                if (pilot.Alive && pilot.Current == CombatPilot.State.Engage)
                {
                    t = TractorBeam.ShipOf(pilot.target);
                    if (t == null) t = pilot.target;
                }
            }
            else if (weapons != null && weapons.weaponsFree) t = weapons.target;
            if (t != null && (!t.gameObject.activeInHierarchy || !PlayerShips.Present(t) || (t.GetComponentInParent<Health>()?.Dead ?? false))) t = null;
            if (t == null) { if (Target != null) StandDown(); return; }
            if (Target != t) { Target = t; shellsOnCount = 0; }
            // (its pilot still picks the nearest player's ship; the Maw works what it picks)
            Spotted = IsSpotted(t, self, spotRange, out var by);
            Spotter = by;
            if (!Spotted && gun != null && gun.Ready) HeldUnspotted++;

            // its shells' aim: the shield generator while the shield is up, then the weapons; at
            // an installation, its generators and batteries
            var round = ArtilleryGun.ChooseRound(t);
            AimedAt = round == ArtilleryGun.Round.Nullifier ? ShipSystem.Shields : ShipSystem.Weapons;
            ShipSystems.SetAim(transform, AimedAt);
            SiegeAim = SiegePart(t, transform.position);

            // protect the battery: a ship too close, and it falls back toward its escorts
            var threat = NearestPlayerShip(transform.position, out float threatAt);
            if (threat != null && threatAt < threatRange)
            {
                if (!FallingBack) FallBacks++;
                FallingBack = true;
                Threat = threat;
            }
            else if (FallingBack && (threat == null || threatAt > threatRange * 1.5f))
            {
                FallingBack = false;
                Threat = null;
            }

            // a nuclear missile, rarely: at a fortified target, spotted, in reach
            if (nukes != null && Spotted && nukes.Ready && Fortified(t, out string why) && nukes.Launch(t) != null)
            {
                Nukes++;
                LastNukeWhy = why;
            }
        }

        /// <summary>Is a target worth a nuclear missile: a big ship whose shield still holds after
        /// a few of its shells, or the players' ships bunched within the blast round it?</summary>
        bool Fortified(Transform t, out string why)
        {
            why = "";
            var info = t.GetComponentInParent<TargetInfo>();
            if (info != null && info.IsSmallCraft) return false;
            float blast = nukes.nukePrefab != null ? nukes.nukePrefab.blastRadius : 1200f;
            int near = 0;
            foreach (var s in PlayerShips.All)
                if (s != null && PlayerShips.Present(s) && (s.position - t.position).sqrMagnitude < blast * blast) near++;
            if (near >= bunched) { why = $"{near} ships within {blast / 1000f:0.#} km of each other"; return true; }
            if (shellsOn == t && shellsOnCount >= shellsBeforeNuke)
            {
                float held = ShieldShare(t);
                if (held > holdsAbove) { why = $"its shield holding at {held:P0} after {shellsOnCount} shells"; return true; }
            }
            return false;
        }

        static float ShieldShare(Transform t)
        {
            var rp = t.GetComponent<Demo.RemotePlayer>();
            if (rp != null) return rp.Shield;
            var s = t.GetComponentInChildren<Shield>(true);
            return s != null && s.Up ? s.Fraction : 0f;
        }

        static Transform NearestPlayerShip(Vector3 p, out float distance)
        {
            Transform best = null;
            float bestD = float.MaxValue;
            foreach (var s in PlayerShips.All)
            {
                if (s == null || !PlayerShips.Present(s) || !s.gameObject.activeInHierarchy) continue;
                var h = s.GetComponentInParent<Health>();
                if (h != null && h.Dead) continue;
                float d = (s.position - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = s; }
            }
            distance = best != null ? Mathf.Sqrt(bestD) : float.MaxValue;
            return best;
        }

        /// <summary>The middle of the other Vrask ships fighting within `range` of a point (false: none).</summary>
        bool Escorts(Vector3 p, float range, out Vector3 centre)
        {
            centre = Vector3.zero;
            int n = 0;
            foreach (var v in VraskShip.All)
            {
                if (v == null || v == self || !v.gameObject.activeInHierarchy) continue;
                var h = v.GetComponent<Health>();
                if (h != null && h.Dead) continue;
                if ((v.transform.position - p).sqrMagnitude > range * range) continue;
                centre += v.transform.position;
                n++;
            }
            if (n == 0) return false;
            centre /= n;
            return true;
        }

        // ------------------------------------------------------------------ flying

        /// <summary>CombatPilot's hook: where it flies. Falling back: toward its escorts (or away
        /// from the ship too close), at its cruising speed. Otherwise: to its place 10 km from the
        /// target on the far side of the Vrask fighting there; in the band, still, bow on.</summary>
        public void Steer(Vector3 pos, ref Vector3 desired, ref float wantSpeed)
        {
            if (pilot == null || Target == null) return;
            if (FallingBack && Threat != null)
            {
                Vector3 away = (pos - Threat.position).normalized;
                desired = Escorts(pos, escortRange, out var c) && (c - pos).sqrMagnitude > 400f * 400f
                    ? ((c - pos).normalized + away * 0.5f) : away;
                wantSpeed = pilot.cruiseSpeed;
                return;
            }
            Vector3 tp = Target.position;
            float d = Vector3.Distance(pos, tp);
            // the screen: the others between it and the target
            Vector3 back = pos - tp;
            if (Escorts(tp, escortRange * 1.5f, out var screen) && (screen - tp).sqrMagnitude > 100f * 100f) back = Vector3.Lerp(back.normalized, (screen - tp).normalized, 0.6f);
            if (back.sqrMagnitude < 1e-4f) back = -transform.forward;
            Vector3 goal = tp + back.normalized * (nearBand + farBand) * 0.5f;
            Vector3 to = goal - pos;
            if (d >= nearBand && d <= farBand && to.magnitude < (farBand - nearBand) * 0.75f)
            {
                // on station: bow on, barely moving
                desired = tp - pos;
                wantSpeed = 0f;
                return;
            }
            desired = to;
            wantSpeed = Mathf.Lerp(pilot.cruiseSpeed * 0.4f, pilot.cruiseSpeed, Mathf.Clamp01(to.magnitude / 2000f));
        }

        /// <summary>On station with no pilot: it turns in place toward its target (at rest when it
        /// has none).</summary>
        void Station(float dt)
        {
            Quaternion want;
            if (Target != null)
            {
                Vector3 to = Target.position - transform.position;
                if (to.sqrMagnitude < 1f) return;
                want = Quaternion.LookRotation(to, transform.up);
            }
            else want = transform.parent != null ? transform.parent.rotation * rest : rest;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, stationTurn * dt);
        }
    }
}
