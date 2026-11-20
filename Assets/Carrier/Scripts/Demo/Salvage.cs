using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Salvaging, in combat mode. Whatever hostile is destroyed (a ship, a fighter or drone, a gun
    /// emplacement, a battery platform) leaves a wreck: a few of its scorched pieces drifting
    /// where it died, with steel, aluminium and copper in it, and now and then a ship part
    /// (Wreck). Choose one in the combat menu (Tab), or just fly to one: within 1.5 km of it, Z
    /// (Back + X on the pad) starts the salvage beam, which strips it into the hold over a few
    /// seconds, as much as there is room for. Z again stops it. A wreck lasts 15 minutes.
    /// </summary>
    public class Salvage : MonoBehaviour
    {
        public CarrierDemoInput demo;
        [Tooltip("How near the beam reaches, beyond the wreck's own size (m).")]
        public float range = 1500f;
        [Tooltip("Seconds to strip a wreck (it takes a share each second).")]
        public float seconds = 6f;
        [Tooltip("Wrecks kept at once (the oldest go first).")]
        public int maxWrecks = 24;

        public static Salvage Instance { get; private set; }

        /// <summary>The wreck chosen in the combat menu (null: the nearest is salvaged).</summary>
        public Wreck Chosen { get; private set; }
        /// <summary>The beam is working on this wreck now.</summary>
        public Wreck Working { get; private set; }
        /// <summary>Wrecks left since the level started (for the self-test).</summary>
        public int WrecksMade { get; private set; }
        /// <summary>Tonnes salvaged since the level started.</summary>
        public float Salvaged { get; private set; }

        CarrierController Ship => demo != null ? demo.carrier : null;
        LineRenderer beam;
        float nextTake;
        // the wrecks near, nearest first, made once a frame for the markers
        readonly List<Wreck> near = new List<Wreck>();
        int nearFrame = -1;

        void Awake() => Instance = this;
        void OnEnable() => Health.AnyDestroyed += Died;
        void OnDisable() => Health.AnyDestroyed -= Died;
        void OnDestroy() { if (Instance == this) Instance = null; }

        // ------------------------------------------------------------ wrecks

        void Died(Health h)
        {
            if (h == null || demo == null) return;
            var t = h.transform;
            var ship = Ship;
            if (ship != null && (t == ship.transform || t.IsChildOf(ship.transform))) return;
            if (PlayerShips.All.Any(p => p != null && (t == p || t.IsChildOf(p)))) return;
            bool listed = Targets.All.Contains(t);
            var info = t.GetComponent<TargetInfo>();
            if (!listed && (info == null || info.faction == "Friendly")) return;
            // (one that respawns leaves a wreck each time it dies, but not another while the
            // last is still there to strip: no endless salvage from one practice target)
            if (h.respawnDelay > 0f && Wreck.All.Any(w => w != null && w.From == h && !w.Empty)) return;
            var contents = ContentsFor(t, info, out float size, out string of);
            if (contents.Count == 0) return;
            // (the oldest go, past the limit: out of the list now, as Destroy waits for the
            // frame's end)
            while (Wreck.All.Count >= maxWrecks && Wreck.All.Count > 0)
            {
                var old = Wreck.All[0];
                Wreck.All.RemoveAt(0);
                if (old != null) Destroy(old.gameObject);
            }
            Wreck.Make(t, of, contents, size, h);
            WrecksMade++;
        }

        /// <summary>What a wreck of `t` holds: by its size, more for a capital ship (and a part,
        /// half the time), less for a small craft.</summary>
        public static List<(string id, float amount)> ContentsFor(Transform t, TargetInfo info, out float size, out string of)
        {
            of = info != null ? info.Name : t.name;
            // its size, from the meshes shown: a ship that has just come apart has its pieces
            // in its breakup's chunks (moved off it, but not yet flung apart)
            var breakup = t.GetComponent<ShipBreakup>();
            IEnumerable<Transform> sources = breakup != null && breakup.chunks != null && breakup.chunks.Any(c => c != null)
                ? breakup.chunks.Where(c => c != null) : new[] { t };
            bool any = false;
            var b = new Bounds(t.position, Vector3.zero);
            foreach (var src in sources)
                foreach (var r in src.GetComponentsInChildren<Renderer>(false))
                {
                    if (!r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                    if (!any) { b = r.bounds; any = true; }
                    else b.Encapsulate(r.bounds);
                }
            size = Mathf.Clamp(b.extents.magnitude, 8f, 400f);
            bool small = info != null && info.IsSmallCraft;
            float steel = Mathf.Round(Mathf.Clamp(size * (small ? 0.12f : 0.25f), 1f, 90f));
            var list = new List<(string, float)> { ("SteelPlate", steel), ("AluminiumSheet", Mathf.Round(steel * 0.4f)), ("CopperWire", Mathf.Round(steel * 0.15f)) };
            var rng = new System.Random(of.GetHashCode() ^ Mathf.RoundToInt(t.position.x) ^ Time.frameCount);
            if (!small && rng.NextDouble() < 0.5)
            {
                string[] parts = { "ArmourPlating", "TurretMount", "FireControl", "CapacitorBank", "RailBarrels", "AutoLoader" };
                list.Add((parts[rng.Next(parts.Length)], 1f));
            }
            else if (small && rng.NextDouble() < 0.25) list.Add(("VectoringJets", 1f));
            var table = IndustryTable.Get();
            return list.Where(c => c.Item2 > 0f && table != null && table.Kind(c.Item1) != ItemKind.None).ToList();
        }

        /// <summary>How far off a wreck is listed and salvaged without choosing it (the combat
        /// menu lists them as far).</summary>
        public const float Around = 30000f;

        /// <summary>The wrecks within 30 km, nearest first, worked out once a frame.</summary>
        List<Wreck> NearNow()
        {
            if (nearFrame == Time.frameCount) return near;
            nearFrame = Time.frameCount;
            near.Clear();
            var ship = Ship;
            if (ship == null) return near;
            Vector3 me = ship.transform.position;
            foreach (var w in Wreck.All)
                if (w != null && !w.Empty && (w.transform.position - me).sqrMagnitude < Around * Around) near.Add(w);
            near.Sort((a, c) => (a.transform.position - me).sqrMagnitude.CompareTo((c.transform.position - me).sqrMagnitude));
            return near;
        }

        /// <summary>The wrecks within `reach` of the ship, nearest first.</summary>
        public IEnumerable<Wreck> Near(float reach)
        {
            var ship = Ship;
            if (ship == null) return Enumerable.Empty<Wreck>();
            Vector3 me = ship.transform.position;
            return Wreck.All.Where(w => w != null && !w.Empty && (w.transform.position - me).sqrMagnitude < reach * reach)
                            .OrderBy(w => (w.transform.position - me).sqrMagnitude);
        }

        public float DistanceTo(Wreck w) => Ship == null || w == null ? float.MaxValue : Vector3.Distance(Ship.transform.position, w.transform.position);
        public bool InReach(Wreck w) => w != null && DistanceTo(w) <= range + w.Size;

        // ------------------------------------------------------------ the beam

        /// <summary>The wreck of the ship targeted, when it has been destroyed (it stays the
        /// target; its wreck is a thing of its own), and there is still something in it.</summary>
        public Wreck TargetWreck()
        {
            var t = Ship != null ? Ship.target : null;
            if (t == null || !t.TryGetComponent(out Health h) || !h.Dead) return null;
            return Wreck.All.LastOrDefault(w => w != null && w.From == h && !w.Empty);
        }

        /// <summary>The wreck K would land beside: one being salvaged or flown to, the target's,
        /// or one chosen or in reach (within 30 km): Z salvages it, K doesn't land there.</summary>
        public Wreck WreckHere()
        {
            if (Working != null) return Working;
            if (FlyingToWreck) return flyingTo;
            var w = TargetWreck() ?? (Chosen != null && !Chosen.Empty ? Chosen : null);
            if (w != null && DistanceTo(w) < Around) return w;
            var nearest = NearNow().FirstOrDefault();
            return nearest != null && InReach(nearest) ? nearest : null;
        }

        public void Choose(Wreck w)
        {
            Chosen = w;
            if (w == null) return;
            // (a wreck chosen over the destroyed ship still targeted: the target let go, so Z
            // salvages the one chosen)
            var ship = Ship;
            if (ship != null && ship.target != null && ship.target.TryGetComponent(out Health th) && th.Dead && TargetWreck() != w) demo.ClearTarget();
            SoundFX.Ui(Sfx.UiLock);
            Notices.Post(InReach(w) ? $"{w.Name}: in reach. Z salvages it" : $"{w.Name} chosen, {CombatMode.Distance(DistanceTo(w))} off: Z flies there and salvages it");
        }

        /// <summary>Z: salvage the chosen wreck (or the nearest in reach); one out of reach is
        /// flown to first, and salvaged on arrival. Z again stops the beam, or the flight.</summary>
        public void Toggle()
        {
            if (Working != null) { Stop("Salvage beam off"); return; }
            if (FlyingToWreck) { AutoFly.Instance.Stop("Auto-flight off"); flyingTo = null; return; }
            // the destroyed target's wreck, the one chosen, or the nearest about
            var w = TargetWreck() ?? (Chosen != null && !Chosen.Empty ? Chosen : NearNow().FirstOrDefault());
            if (w == null) { Refuse("No wreck near: destroy a hostile, or choose a wreck in the combat menu (Tab)"); return; }
            SalvageWreck(w);
        }

        /// <summary>Salvage this wreck: the beam on it if it is in reach, or a flight there (instead
        /// of any flight or beam under way) and the beam on arrival. Says why, if it can't.</summary>
        public void SalvageWreck(Wreck w)
        {
            if (w == null || w.Empty) { Refuse("Nothing left in that wreck"); return; }
            Chosen = w;
            if (Working == w) return;
            if (!CanSalvage()) return;
            if (Working != null) Stop();
            if (!InReach(w))
            {
                if (FlyingToWreck && flyingTo == w) return;
                if (!FlyTo(w) && AutoFly.Instance == null) Refuse($"Can't fly to {w.Name} here");
                return;
            }
            if (FlyingToWreck) { AutoFly.Instance.Stop(null); flyingTo = null; }
            StartOn(w);
        }

        /// <summary>A flight to a wreck under way (this one's: not another auto-flight).</summary>
        bool FlyingToWreck
        {
            get
            {
                var fly = AutoFly.Instance;
                if (flyingTo != null && (fly == null || !fly.Active || fly.Label != flyingTo.Name)) flyingTo = null;
                return flyingTo != null;
            }
        }

        /// <summary>A hold to salvage into, with room: says why not.</summary>
        bool CanSalvage()
        {
            var ship = Ship;
            if (ship == null) return false;
            if (ship.Destroyed) { Refuse("The ship is lost: no salvage till she is back"); return false; }
            var hold = demo.Cargo;
            if (hold == null) { Refuse($"The {ship.shipName.ToLower()} has no hold to salvage into"); return false; }
            if (hold.Full) { Refuse("The hold is full"); return false; }
            return true;
        }

        /// <summary>Fly to a wreck (until it is in the beam's reach), and salvage it there.</summary>
        public bool FlyTo(Wreck w)
        {
            var fly = AutoFly.Instance;
            if (w == null || fly == null || !CanSalvage()) return false;
            Chosen = w;
            flyingTo = w;
            return fly.Begin(w.Name, () => w != null ? w.transform.position : Ship.transform.position, range * 0.6f + w.Size,
                             () => { flyingTo = null; if (w != null && !w.Empty && InReach(w) && Working == null) StartOn(w); },
                             () => w != null && !w.Empty);
        }

        /// <summary>Shift+Z: choose the next wreck about, nearest first.</summary>
        public void ChooseNext()
        {
            var list = NearNow();
            if (list.Count == 0) { Refuse("No wreck near"); return; }
            int i = Chosen != null ? list.IndexOf(Chosen) : -1;
            Choose(list[(i + 1) % list.Count]);
        }

        Wreck flyingTo;

        void StartOn(Wreck w)
        {
            Working = w;
            Chosen = w;
            nextTake = Time.time + 1f;
            SoundFX.Ui(Sfx.UiLock);
            Notices.Post($"Salvaging {w.Name}: {w.Summary()}");
        }

        void Refuse(string why)
        {
            SoundFX.Ui(Sfx.UiError);
            Notices.Post(why);
        }

        public void Stop(string why = null)
        {
            Working = null;
            if (beam != null) beam.enabled = false;
            if (why != null) Notices.Post(why);
        }

        void Update()
        {
            if (Chosen != null && Chosen.Empty) Chosen = null;
            var w = Working;
            if (w == null) { if (beam != null) beam.enabled = false; return; }
            // (a combat mode tool: off when the player goes to another mode, where Z is theirs)
            if (!Modes.Is(ModeOfPlay.Combat)) { Stop("Salvage beam off"); return; }
            var ship = Ship;
            var hold = demo.Cargo;
            if (ship == null || ship.Destroyed || hold == null || w == null || w.Empty) { Stop(w != null && w.Empty ? $"{w.Name} stripped" : null); return; }
            if (!InReach(w)) { Stop($"{w.Name} out of reach: salvage beam off"); return; }
            DrawBeam(ship, w);
            if (Time.time < nextTake) return;
            nextTake = Time.time + 1f;
            float before = hold.Used;
            string got = w.TakeInto(hold, 1f / Mathf.Max(1f, seconds));
            Salvaged += hold.Used - before;
            if (got == null) { Stop(hold.Full ? "The hold is full: salvage beam off" : "Nothing more to take"); return; }
            Notices.Post($"Salvaged {got}");
            if (w.Empty) Stop($"{w.Name} stripped");
        }

        void DrawBeam(CarrierController ship, Wreck w)
        {
            if (beam == null)
            {
                var go = new GameObject("Salvage beam");
                go.transform.SetParent(transform, false);
                beam = go.AddComponent<LineRenderer>();
                beam.positionCount = 2;
                beam.useWorldSpace = true;
                beam.widthMultiplier = 6f;
                var laser = demo.Laser;
                if (laser != null && laser.beamCore != null) beam.sharedMaterial = laser.beamCore.sharedMaterial;
                beam.startColor = new Color(1f, 0.75f, 0.35f, 0.9f);
                beam.endColor = new Color(1f, 0.55f, 0.2f, 0.5f);
            }
            beam.enabled = true;
            var laserNow = demo.Laser;
            Vector3 from = laserNow != null && laserNow.muzzle != null ? laserNow.muzzle.position : ship.transform.position;
            beam.SetPosition(0, from);
            beam.SetPosition(1, w.transform.position);
            beam.widthMultiplier = 5f + Mathf.Sin(Time.time * 18f) * 1.5f;
        }

        // ------------------------------------------------------------ on screen

        void OnGUI()
        {
            if (Hud.ReelMode && !Hud.ReelShowsMarks) return;
            var cam = Camera.main;
            var ship = Ship;
            if (cam == null || ship == null || !Modes.Is(ModeOfPlay.Combat)) return;
            float s = Hud.S;
            // the chosen wreck, and those near
            var list = NearNow();
            for (int i = 0; i < list.Count && i < 6; i++)
            {
                var w = list[i];
                if (w == null) continue;
                float dist = DistanceTo(w);
                bool inReach = dist <= range + w.Size;
                Vector3 sp = cam.WorldToScreenPoint(w.transform.position);
                if (sp.z <= 0f) continue;
                var p = new Vector2(sp.x, Screen.height - sp.y);
                bool chosen = w == Chosen || w == Working;
                var c = chosen ? new Color(1f, 0.75f, 0.35f) : new Color(0.75f, 0.65f, 0.5f, 0.7f);
                float box = (chosen ? 16f : 10f) * s;
                Hud.Corners(new Rect(p.x - box, p.y - box, box * 2f, box * 2f), 5f * s, Mathf.Max(1f, 1.5f * s), c);
                if (chosen || inReach)
                {
                    string text = $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{w.Name}  {CombatMode.Distance(dist)}{(inReach ? "  in reach" : "")}</color>";
                    GUI.Label(new Rect(p.x + box + 4f * s, p.y - 9f * s, 400f * s, 18f * s), text, Hud.Text(12));
                }
            }
        }
    }
}
