using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A tractor beam (the Vrask Snare's): it locks onto a ship within `range` and pulls it toward
    /// the emitter, holding its speed down and turning it slowly, stern to the emitter.
    ///
    /// - One beam on a ship whose engines work can be out-flown: full thrust away is faster than
    ///   the pull, and the lock breaks once the ship is `breakFactor` times the range away.
    /// - Two or more beams on one ship, or one on a ship whose engines are knocked out
    ///   (ShipSystems), hold it fast: its speed is held under the pull.
    /// - The lock breaks out of range, when this ship is destroyed, or when its weapons are
    ///   knocked out.
    ///
    /// The held ship moves itself (CarrierController.Tractor), so in co-op the host's beams on
    /// another player's ship are sent to that player's game (CoopWorld.Tractor). HeldTargets is
    /// every ship held now, for the boarding ships to come.
    /// </summary>
    public class TractorBeam : MonoBehaviour
    {
        /// <summary>How fast one beam pulls (m/s).</summary>
        public const float PullSpeed = 30f;
        /// <summary>The pull fades out over the last 300 m to this distance from the beams (m): the
        /// ship is held there, not rammed into them.</summary>
        public const float HoldDistance = 1500f;
        /// <summary>One beam on working engines holds the ship's top speed to this share (but never
        /// under one and a half times the pull, so it can always fly free).</summary>
        public const float EscapeShare = 0.6f;
        /// <summary>Held fast, its speed is held to this share of the pull.</summary>
        public const float FirmShare = 0.4f;
        /// <summary>How fast one beam turns the ship (degrees a second).</summary>
        public const float TurnSpeed = 2f;
        /// <summary>How hard a held ship faster than it may go is slowed (m/s²).</summary>
        public const float Brake = 20f;
        /// <summary>Beams past this many on one ship add nothing.</summary>
        public const int MaxBeams = 3;

        [Tooltip("Where the beam comes from (the Snare's tractor projector).")]
        public Transform emitter;
        [Tooltip("How far it can lock on (m).")]
        public float range = 2500f;
        [Tooltip("It lets go at this many times its range.")]
        public float breakFactor = 1.3f;
        [Tooltip("The beam drawn while it holds a ship (made at run time if none is given).")]
        public LineRenderer line;
        public float width = 8f;

        /// <summary>The ship it holds (its root: the one with the Health), or null.</summary>
        public Transform Holding { get; private set; }
        public bool Locked => Holding != null;
        /// <summary>When it last locked on.</summary>
        public float LockedAt { get; private set; } = -1f;
        /// <summary>Locks made, and why the last one ended (for the tests).</summary>
        public int Locks { get; private set; }
        public string LastRelease { get; private set; } = "";

        /// <summary>Where the beam comes from now.</summary>
        public Vector3 From => emitter != null ? emitter.position : transform.position;

        Health health;
        ShipSystems systems;
        Transform shown;
        float shownUntil;

        // ------------------------------------------------------------------ every ship held

        static readonly Dictionary<Transform, List<TractorBeam>> held = new Dictionary<Transform, List<TractorBeam>>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHeld() => held.Clear();

        /// <summary>Every ship a tractor beam holds now.</summary>
        public static IReadOnlyCollection<Transform> HeldTargets => held.Keys;

        /// <summary>The beams holding a ship (none: empty).</summary>
        public static IReadOnlyList<TractorBeam> BeamsHolding(Transform ship) =>
            ship != null && held.TryGetValue(ShipOf(ship), out var list) ? list : (IReadOnlyList<TractorBeam>)System.Array.Empty<TractorBeam>();

        /// <summary>How many beams hold a ship.</summary>
        public static int BeamsOn(Transform ship) => BeamsHolding(ship).Count;

        /// <summary>Held fast: two beams or more, or its engines knocked out.</summary>
        public static bool Firm(Transform ship)
        {
            int n = BeamsOn(ship);
            if (n == 0) return false;
            if (n >= 2) return true;
            var sys = ShipOf(ship).GetComponent<ShipSystems>();
            return sys != null && sys.EnginesDown;
        }

        /// <summary>Where the beams holding a ship pull it toward (between their emitters).</summary>
        public static Vector3 PullPoint(Transform ship)
        {
            var beams = BeamsHolding(ship);
            if (beams.Count == 0) return ship != null ? ship.position : Vector3.zero;
            Vector3 sum = Vector3.zero;
            foreach (var b in beams) sum += b.From;
            return sum / beams.Count;
        }

        /// <summary>The ship a transform is part of (the one with the Health).</summary>
        public static Transform ShipOf(Transform t)
        {
            if (t == null) return null;
            var h = t.GetComponentInParent<Health>();
            return h != null ? h.transform : t;
        }

        /// <summary>How hard `beams` pull a ship `distance` from them (m/s toward them), and the
        /// most its own engines may then take it (m/s). Held fast (`firm`, or two beams) that is
        /// less than the pull, so it can't get away; one beam on working engines, more.</summary>
        public static void Strength(int beams, bool firm, float topSpeed, float distance, out float pull, out float cap)
        {
            int n = Mathf.Clamp(beams, 1, MaxBeams);
            float full = PullSpeed * n;
            pull = full * Mathf.Clamp01((distance - HoldDistance) / 300f);
            cap = firm || beams >= 2 ? full * FirmShare : Mathf.Max(topSpeed * EscapeShare, full * 1.5f);
        }

        // ------------------------------------------------------------------ locking on

        void Awake()
        {
            health = GetComponentInParent<Health>();
            if (emitter == null) emitter = transform;
        }

        void OnDisable()
        {
            Release("switched off");
            if (line != null) line.enabled = false;
        }

        /// <summary>Is it able to hold a ship (not destroyed, its weapons working)? Says why not.</summary>
        public bool Working(out string why)
        {
            why = "";
            if (!isActiveAndEnabled) why = "switched off";
            else if (health != null && health.Dead) why = "destroyed";
            else if (ShipSystems.WeaponsDownOn(this, ref systems)) why = "its weapons are knocked out";
            return why == "";
        }

        /// <summary>Lock onto a ship in range (letting go of any other). Says why not.</summary>
        public bool Lock(Transform target, out string why)
        {
            var ship = ShipOf(target);
            why = "";
            if (ship == null) { why = "nothing to lock onto"; return false; }
            if (ship == Holding) return true;
            if (!Working(out why)) return false;
            if (Dead(ship)) { why = "it is destroyed"; return false; }
            if ((ship.position - From).sqrMagnitude > range * range) { why = "out of range"; return false; }
            var cc = ship.GetComponent<CarrierController>();
            if ((cc != null && cc.externalMotion) || !PlayerShips.Present(ship) || !ship.gameObject.activeInHierarchy)
            {
                why = "it isn't flying free (in warp, docked or gone)";
                return false;
            }
            Release("locked onto another");
            Holding = ship;
            if (!held.TryGetValue(ship, out var list)) held[ship] = list = new List<TractorBeam>();
            if (!list.Contains(this)) list.Add(this);
            LockedAt = Time.time;
            Locks++;
            SoundFX.Play(Sfx.BeamCharge, From, 0.8f, 0.55f);
            return true;
        }

        /// <summary>Let go of the ship it holds (if any), saying why.</summary>
        public void Release(string why)
        {
            var ship = Holding;
            if (ReferenceEquals(ship, null)) return;
            Holding = null;
            LastRelease = why;
            if (held.TryGetValue(ship, out var list))
            {
                list.Remove(this);
                if (list.Count == 0) held.Remove(ship);
            }
        }

        /// <summary>Co-op: show the beam on a ship the host's copy of this ship holds (a guest's
        /// copy only shows it: the host's game and the held ship's do the pulling). Null: none.</summary>
        public void NetShow(Transform ship)
        {
            shown = ship;
            shownUntil = Time.time + 0.6f;
        }

        // ------------------------------------------------------------------ every frame

        void Update()
        {
            if (!ReferenceEquals(Holding, null))
            {
                string why = null;
                if (!Working(out string w)) why = w;
                else if (Holding == null || !Holding.gameObject.activeInHierarchy || Dead(Holding) || !PlayerShips.Present(Holding)) why = "the ship is gone";
                else if ((Holding.position - From).sqrMagnitude > range * breakFactor * range * breakFactor) why = "out of range";
                if (why != null) Release(why);
                else if (held.TryGetValue(Holding, out var beams) && beams[0] == this) Pull(Holding, beams);
            }
            Draw();
        }

        static bool Dead(Transform ship)
        {
            var h = ship.GetComponent<Health>();
            return h != null && h.Dead;
        }

        /// <summary>The first of the beams on a ship hands their hold to it each frame (a ship
        /// without a controller here, another player's in co-op, is pulled in its own game).</summary>
        static void Pull(Transform ship, List<TractorBeam> beams)
        {
            var cc = ship.GetComponent<CarrierController>();
            if (cc == null) return;
            Vector3 sum = Vector3.zero;
            foreach (var b in beams) sum += b.From;
            cc.Tractor(sum / beams.Count, beams.Count, false, 0.25f);
        }

        void Draw()
        {
            Transform to = Holding != null ? Holding : Time.time < shownUntil ? shown : null;
            if (to == null)
            {
                if (line != null && line.enabled) line.enabled = false;
                return;
            }
            if (line == null) line = MakeLine();
            line.enabled = true;
            line.SetPosition(0, From);
            line.SetPosition(1, to.position);
            // (a slow throb)
            line.widthMultiplier = width * (0.8f + 0.2f * Mathf.Sin(Time.time * 7f));
        }

        LineRenderer MakeLine()
        {
            var go = new GameObject("Tractor beam");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.numCapVertices = 4;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            var shader = Shader.Find("Sprites/Default");
            if (shader != null) lr.sharedMaterial = new Material(shader);
            lr.startColor = new Color(0.45f, 1f, 0.6f, 0.8f);
            lr.endColor = new Color(0.45f, 1f, 0.6f, 0.25f);
            return lr;
        }
    }
}
