using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace CarrierGame.Demo
{
    /// <summary>
    /// A combat information centre display for a fleet battle (the CIC reel, -cic), drawn with
    /// IMGUI in the style of a warship's multi-window tactical console: a dark blue-black screen,
    /// thin cyan and green lines, monospaced text and framed windows with title bars.
    ///
    /// Windows: (a) the tactical plot, top down on the fight's centroid and scaled to fit, with
    /// range rings and bearing ticks; friendly frigates as blue circles with their class letter,
    /// hostile Vrask as red diamonds with their role letter, missiles and pods as chevrons, each
    /// with a velocity leader; fading tracks for every ship, gunfire tracers, dotted missile tracks,
    /// artillery shells' tracks to their aim points, the bow beams and tractor beams, point defence;
    /// (b) the track table: id, class, hull, shield, range, tactic and weapon in use; (c) the
    /// weapons and engagement log; (d) the status strip: battle clock, the Vrask fleet's phase,
    /// ships left and damage dealt by each side.
    ///
    /// It follows the scene live from Begin to End, and records every ship's position each second
    /// for the post-action replay (Mode.Replay) with callouts at the key moments; Mode.Results is
    /// the battle's summary card. Full screen, or an inset (picture in picture) over the picture.
    /// F9 cycles the modes (not in a reel). Lines are drawn with GL inside OnGUI; -cicnogl draws
    /// them as rotated IMGUI rectangles instead.
    /// </summary>
    public class TacticalDisplay : MonoBehaviour
    {
        public enum Mode { Off, Full, Inset, Results, Replay, EndCard }
        public Mode mode = Mode.Off;

        public class Track
        {
            public int index;
            public string id, name, cls;
            public char glyph;
            /// <summary>0 blue (the frigates), 1 red, 2 the third side (a three-way battle).</summary>
            public int side;
            public bool friendly => side == 0;
            public Transform t;
            public Health health;
            public Shield shield;
            public SimPilot pilot;
            public CarrierController ship;
            public FactionShip faction;
            public VraskShip vrask;
            public SnareTactics snare;
            public FangTactics fang;
            public MawTactics maw;
            public SkinkTactics skink;
            public PointDefence pd;
            public TractorBeam tractor;
            public CombatPilot combat;
            public CiwsDirector ciws;
            public MainBeam beam;
            public Vector3 pos, vel;
            public bool dead, captured, seen;
            public float diedAt = -1f;
            public float hull = 1f, shieldF, hull0, hullTaken;
            public float dealt, taken;
            public int kills;
            public string weapon = "";
            public float weaponAt = -100f;
            public Track lastBy;
            public float lastByAt = -100f;
            public readonly List<Vector3> trail = new List<Vector3>();
            public bool wasLocked, boarded;
            public FangTactics.Phase fangWas;
            public Transform lockedOn;
            public string Label => $"{id} {name}";
        }

        public class KeyEvent
        {
            public float t;
            public string kind, title, text;
            public Vector3 pos;
        }

        class Sample
        {
            public float t;
            public Vector3[] pos;
            public byte[] state;          // 0 alive, 1 destroyed, 2 captured
            public float[] hull;
            public int[] fire;            // attacker | target << 8 | kind << 16
            public float blue, red, third; // each side's strength: its hull left, as a share of what it began with
        }

        struct Line { public float t; public string text; public Color c; }
        struct Seg { public Vector2 a, b; public Color c; public float w; }

        // ---------------------------------------------------------------- the battle

        public readonly List<Track> Tracks = new List<Track>();
        public readonly List<KeyEvent> Keys = new List<KeyEvent>();
        /// <summary>Moments worth a caption (phases, kills, locks), as they happen: the reel reads them.</summary>
        public readonly List<KeyEvent> Moments = new List<KeyEvent>();
        public readonly List<string> Analysis = new List<string>();
        /// <summary>Each line logged, for the reel's own log.</summary>
        public event Action<string> Logged;

        readonly Dictionary<Transform, Track> byT = new Dictionary<Transform, Track>();
        readonly List<Line> lines = new List<Line>();
        readonly List<Sample> samples = new List<Sample>();
        readonly List<(float t, Vector3 p, byte k)> dots = new List<(float, Vector3, byte)>();
        readonly HashSet<int> fireNow = new HashSet<int>();
        readonly Dictionary<long, float> pairLogged = new Dictionary<long, float>();
        readonly Dictionary<Track, float> shooterLogged = new Dictionary<Track, float>();
        readonly HashSet<Projectile> shotSet = new HashSet<Projectile>();
        readonly List<Projectile> shots = new List<Projectile>();
        readonly Dictionary<Missile, List<Vector3>> missileTrails = new Dictionary<Missile, List<Vector3>>();
        readonly Dictionary<ArtilleryShell, List<Vector3>> shellTrails = new Dictionary<ArtilleryShell, List<Vector3>>();
        readonly List<string> phases = new List<string>();
        List<Craft> drones = new List<Craft>();

        Transform frame;
        VraskFleet fleet;
        EncounterGroup group;
        string place = "";
        bool began, running;
        float t0, tEnd, nextSample, nextTrail, nextMissileTrail, nextDrones, lastHitAt, lastPoll;
        Func<Health, float, Vector3, HitKind, bool> prevIntercept;
        bool hooked;

        public string Outcome { get; private set; } = "";
        public string Winner { get; private set; } = "";
        public bool Running => running;
        public float Clock => began ? (running ? Time.time : tEnd) - t0 : 0f;
        public float Duration => began ? (running ? Time.time : tEnd) - t0 : 0f;
        public float SinceLastHit => Time.time - lastHitAt;
        public string ClockText => Clk(Clock);
        public int BlueLeft => SideLeft(0);
        public int RedLeft => SideLeft(1);
        public int BlueCount => SideCount(0);
        public int RedCount => SideCount(1);
        public float BlueDealt => SideDealt(0);
        public float RedDealt => SideDealt(1);
        public int SideLeft(int side) => Tracks.Count(k => k.side == side && !k.dead);
        public int SideCount(int side) => Tracks.Count(k => k.side == side);
        public float SideDealt(int side) => Tracks.Where(k => k.side == side).Sum(k => k.dealt);
        /// <summary>A third side (the three-way CIC reel: two factions and the frigates, all at war).</summary>
        public bool ThreeWay { get; private set; }
        /// <summary>Each side's name: BLUE, RED (or the factions' own, in a three-way battle).</summary>
        public readonly string[] SideNames = { "BLUE", "RED", "" };
        public int Sides => ThreeWay ? 3 : 2;
        /// <summary>The damage each side dealt each other side: [by, to].</summary>
        public readonly float[,] SideDamage = new float[3, 3];
        /// <summary>The fleets' phases in words (a three-way battle: the reel says them), for the status strip.</summary>
        public Func<string> PhaseText;

        /// <summary>Replay: game seconds into the battle shown.</summary>
        [NonSerialized] public float replayT;
        [NonSerialized] public float replaySpeed = 15f;

        static string Clk(float s)
        {
            s = Mathf.Max(0f, s);
            return $"{Mathf.FloorToInt(s / 60f):00}:{Mathf.FloorToInt(s % 60f):00}";
        }

        Vector3 east = Vector3.right, up = Vector3.up, north = Vector3.forward;

        /// <summary>A point in the display's frame: from the frame's place, x east, y up, z
        /// north, in the plane of the fight (by a planet, level over it: up is out from it).</summary>
        Vector3 Rel(Vector3 world)
        {
            Vector3 d = frame != null ? world - frame.position : world;
            return new Vector3(Vector3.Dot(d, east), Vector3.Dot(d, up), Vector3.Dot(d, north));
        }

        /// <summary>A direction in the world as one on the plot (screen y down).</summary>
        Vector2 Dir2(Vector3 v) => new Vector2(Vector3.Dot(v, east), -Vector3.Dot(v, north));

        /// <summary>Start following a battle: the frigates (blue) against the Vrask (red), the
        /// positions taken from `frameOf` (the place both sides are carried along with), drawn on
        /// the plane square to `planeUp` with `planeNorth` at the top (000).</summary>
        public void Begin(Transform frameOf, Vector3 planeUp, Vector3 planeNorth, IList<SimPilot> blue, IList<FactionShip> red, VraskFleet vraskFleet, EncounterGroup band, string where,
                          IList<FactionShip> third = null, string redName = null, string thirdName = null)
        {
            ThreeWay = third != null && third.Count > 0;
            SideNames[1] = ThreeWay && !string.IsNullOrEmpty(redName) ? redName.ToUpperInvariant() : "RED";
            SideNames[2] = ThreeWay ? (string.IsNullOrEmpty(thirdName) ? "GREEN" : thirdName.ToUpperInvariant()) : "";
            Array.Clear(SideDamage, 0, SideDamage.Length);
            frame = frameOf;
            up = planeUp.sqrMagnitude > 1e-6f ? planeUp.normalized : Vector3.up;
            north = Vector3.ProjectOnPlane(planeNorth, up);
            if (north.sqrMagnitude < 1e-4f) north = Vector3.ProjectOnPlane(Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.forward, up);
            north.Normalize();
            east = Vector3.Cross(up, north).normalized;
            fleet = vraskFleet;
            group = band;
            place = where ?? "";
            Tracks.Clear();
            byT.Clear();
            for (int i = 0; i < blue.Count; i++)
            {
                var p = blue[i];
                if (p == null) continue;
                string n = p.ship != null && !string.IsNullOrEmpty(p.ship.shipName) ? p.ship.shipName : p.name;
                var k = NewTrack(p.transform, 0, $"T{Tracks.Count(x => x.friendly) + 1:00}", n.ToUpperInvariant(), string.IsNullOrEmpty(n) ? 'F' : char.ToUpperInvariant(n[0]), "FRIGATE");
                k.pilot = p;
                k.ship = p.ship;
                k.ciws = p.GetComponent<CiwsDirector>();
                k.beam = p.GetComponentInChildren<MainBeam>(true);
            }
            var counts = new Dictionary<string, int>();
            var hostiles = red.Where(v => v != null).Select(v => (v, 1)).ToList();
            if (ThreeWay) hostiles.AddRange(third.Where(v => v != null).Select(v => (v, 2)));
            foreach (var (v, sd) in hostiles)
            {
                string key = sd + v.RoleName;
                counts.TryGetValue(key, out int c);
                counts[key] = c + 1;
                string role = v.RoleName.ToUpperInvariant();
                char prefix = ThreeWay ? SideNames[sd][0] : 'H';
                var k = NewTrack(v.transform, sd, $"{prefix}{Tracks.Count(x => x.side == sd) + 1:00}", $"{role} {(char)('A' + c)}", v.RoleLetter, role);
                k.faction = v;
                k.vrask = v as VraskShip;
                k.snare = v.GetComponent<SnareTactics>();
                k.fang = v.GetComponent<FangTactics>();
                k.maw = v.GetComponent<MawTactics>();
                k.skink = v.GetComponent<SkinkTactics>();
                k.pd = v.GetComponent<PointDefence>();
                k.tractor = v.GetComponent<TractorBeam>();
                k.combat = v.GetComponent<CombatPilot>();
                k.beam = v.GetComponentInChildren<MainBeam>(true);
            }
            began = running = true;
            t0 = Time.time;
            lastHitAt = Time.time;
            nextTrail = Time.time;
            nextSample = Time.time + 1f;
            Hook(true);
            string them = string.Join(" AND ", red.Where(v => v != null).Select(v => v.FactionName.ToUpperInvariant()).Distinct());
            if (ThreeWay) Log($"{place} ACTION, THREE SIDES: BLUE {BlueCount} FRIGATES, {SideNames[1]} {RedCount}, {SideNames[2]} {SideCount(2)}: ALL AT WAR", Cyan);
            else Log($"{place} ACTION: BLUE {BlueCount} FRIGATES, RED {RedCount} {(them.Length > 0 ? them : "VRASK")}", Cyan);
            Poll();
            Sample1();
        }

        Track NewTrack(Transform t, int side, string id, string name, char glyph, string cls)
        {
            var k = new Track { index = Tracks.Count, t = t, side = side, id = id, name = name, glyph = glyph, cls = cls };
            k.health = t.GetComponent<Health>();
            k.shield = t.GetComponentInChildren<Shield>(true);
            k.hull0 = k.health != null ? k.health.TotalDamage : 0f;
            k.pos = Rel(t.position);
            Tracks.Add(k);
            byT[t] = k;
            return k;
        }

        /// <summary>The battle is over: the clock stops, the hooks come off and the analysis is made.</summary>
        public void End(string outcome, string winner)
        {
            if (!running) return;
            Poll();
            Sample1();
            running = false;
            tEnd = Time.time;
            Outcome = outcome ?? "";
            Winner = winner ?? "";
            Hook(false);
            Log($"ENGAGEMENT ENDED: {Outcome}", Amber);
            try { Analyse(); } catch (Exception e) { Debug.LogException(e); }
        }

        void OnDestroy() => Hook(false);

        void Hook(bool on)
        {
            if (on == hooked) return;
            hooked = on;
            if (on)
            {
                prevIntercept = Health.Intercept;
                Health.Intercept = OnHit;
                Health.AnyDestroyed += OnDestroyed;
                Health.AnyCaptured += OnCaptured;
                MainBeam.Began += OnBeam;
                Missile.Spawned += OnMissile;
                Missile.Downed += OnDowned;
                ArtilleryShell.Spawned += OnShell;
                Projectile.Spawned += OnShot;
                VraskFleet.Changed += OnPhase;
                Boarders.Over += OnBoardersOver;
            }
            else
            {
                if (Health.Intercept == (Func<Health, float, Vector3, HitKind, bool>)OnHit) Health.Intercept = prevIntercept;
                Health.AnyDestroyed -= OnDestroyed;
                Health.AnyCaptured -= OnCaptured;
                MainBeam.Began -= OnBeam;
                Missile.Spawned -= OnMissile;
                Missile.Downed -= OnDowned;
                ArtilleryShell.Spawned -= OnShell;
                Projectile.Spawned -= OnShot;
                VraskFleet.Changed -= OnPhase;
                Boarders.Over -= OnBoardersOver;
            }
        }

        /// <summary>The track a transform belongs to (itself or a ship it is part of), or null.</summary>
        public Track Of(Transform t)
        {
            for (int i = 0; t != null && i < 16; i++, t = t.parent)
                if (byT.TryGetValue(t, out var k)) return k;
            return null;
        }

        static string Weapon(HitKind k)
        {
            switch (k)
            {
                case HitKind.Projectile: return "GUNS";
                case HitKind.Cannon: return "CANNON";
                case HitKind.Missile: return "MISSILE";
                case HitKind.Beam: return "BEAM";
                case HitKind.Ion: return "ION";
                case HitKind.Penetrator: return "PENETRATOR";
                case HitKind.Nullifier: return "NULLIFIER";
                case HitKind.Artillery: return "ARTILLERY";
                case HitKind.Nuclear: return "NUCLEAR";
                default: return k.ToString().ToUpperInvariant();
            }
        }

        void Log(string text, Color c)
        {
            lines.Add(new Line { t = Clock, text = text, c = c });
            if (lines.Count > 400) lines.RemoveRange(0, lines.Count - 400);
            try { Logged?.Invoke($"{Clk(Clock)} {text}"); } catch { }
        }

        void Key(string kind, string title, string text, Vector3 pos)
        {
            if (Keys.Any(k => k.kind == kind)) return;
            var e = new KeyEvent { t = Clock, kind = kind, title = title, text = text, pos = pos };
            Keys.Add(e);
            Moment(title, text);
        }

        void Moment(string title, string text) => Moments.Add(new KeyEvent { t = Time.time, title = title, text = text });

        Color SideColor(Track k) => k == null ? Grey : ColorOf(k.side);
        static Color ColorOf(int side) => side == 0 ? Blue : side == 1 ? Red : Magenta;
        /// <summary>A side's gunfire: pale blue, orange, or pale violet.</summary>
        static Color ShotColor(Track o) => o == null ? Grey : o.side == 0 ? new Color(0.75f, 0.95f, 1f) : o.side == 1 ? new Color(1f, 0.55f, 0.3f) : new Color(0.95f, 0.7f, 1f);
        /// <summary>Ships left on each side, in words ("blue 2 of 2, red 8 of 10").</summary>
        string LeftText()
        {
            var parts = new List<string>();
            for (int i = 0; i < Sides; i++) parts.Add($"{SideNames[i].ToLowerInvariant()} {SideLeft(i)} of {SideCount(i)}");
            return string.Join(", ", parts) + " left";
        }

        // ---------------------------------------------------------------- the hooks

        bool OnHit(Health h, float amount, Vector3 point, HitKind kind)
        {
            bool taken = false;
            if (prevIntercept != null) taken = prevIntercept(h, amount, point, kind);
            if (!running || taken || h == null) return taken;
            // (the hull's share of a hit, passed on after the shield: counted already)
            if (kind == HitKind.Cannon && point == h.transform.position) return taken;
            try { Hit(Of(h.transform), Health.Source != null ? Of(Health.Source) : null, amount, kind, point); }
            catch (Exception e) { Debug.LogException(e); }
            return taken;
        }

        void Hit(Track victim, Track by, float amount, HitKind kind, Vector3 point)
        {
            if (victim == null || victim.dead) return;
            if (by != null && by.side == victim.side) return;
            victim.taken += amount;
            if (by != null) SideDamage[by.side, victim.side] += amount;
            lastHitAt = Time.time;
            if (by == null) return;
            by.dealt += amount;
            by.weapon = Weapon(kind);
            by.weaponAt = Time.time;
            victim.lastBy = by;
            victim.lastByAt = Time.time;
            fireNow.Add(by.index | (victim.index << 8) | ((int)kind << 16));
            if (!Keys.Any(k => k.kind == "contact"))
                Key("contact", "FIRST CONTACT", $"{by.Label} opens fire on {victim.Label}: {Weapon(kind)}", victim.pos);
            long pair = (long)by.index | ((long)victim.index << 8) | ((long)kind << 16);
            if (!pairLogged.TryGetValue(pair, out float at) || Time.time - at > 25f)
            {
                pairLogged[pair] = Time.time;
                Log($"{by.Label} > {victim.id} {victim.name}  {Weapon(kind)}", SideColor(by));
            }
        }

        void OnDestroyed(Health h)
        {
            if (!running || h == null) return;
            var v = Of(h.transform);
            if (v == null || v.dead) return;
            Down(v, false);
        }

        void OnCaptured(Health h)
        {
            if (!running || h == null) return;
            var v = Of(h.transform);
            if (v == null || v.dead) return;
            Down(v, true);
        }

        void Down(Track v, bool captured)
        {
            v.dead = true;
            v.captured = captured;
            v.diedAt = Clock;
            if (v.t != null) v.pos = Rel(v.t.position);
            var by = v.lastBy != null && Time.time - v.lastByAt < 45f ? v.lastBy : null;
            if (by != null) by.kills++;
            string how = captured ? "CAPTURED BY BOARDERS" : "DESTROYED";
            Log($"{v.Label} {how}{(by != null ? $"  ({by.id} {by.name})" : "")}", captured ? Amber : SideColor(v));
            string title = $"{v.id} {v.name} {how}";
            string left = LeftText();
            string text = by != null ? $"{(captured ? "Taken" : "Killed")} by {by.Label}; {left}" : char.ToUpperInvariant(left[0]) + left.Substring(1);
            if (!Keys.Any(k => k.kind == "kill")) Key("kill", "FIRST KILL: " + title, text, v.pos);
            else Moment(title, text);
        }

        void OnBeam(MainBeam b)
        {
            if (!running || b == null) return;
            var o = Of(b.transform);
            if (o == null) return;
            var tgt = o.pilot != null && o.pilot.Target != null ? Of(o.pilot.Target) : (b.target != null ? Of(b.target) : null);
            o.weapon = "MAIN BEAM";
            o.weaponAt = Time.time;
            Log($"{o.Label} > {(tgt != null ? tgt.id + " " + tgt.name : "")}  MAIN BEAM", SideColor(o));
        }

        static string MissileKind(Missile m) => m is BoardingPod ? "BOARDING POD" : m is NukeMissile ? "NUCLEAR MISSILE" : "MISSILE";

        void OnMissile(Missile m)
        {
            if (!running || m == null) return;
            if (!missileTrails.ContainsKey(m)) missileTrails[m] = new List<Vector3> { Rel(m.transform.position) };
            var o = Of(m.owner);
            if (o == null) return;
            o.weapon = m is BoardingPod ? "PODS" : m is NukeMissile ? "NUKE" : "MISSILES";
            o.weaponAt = Time.time;
            var tgt = Of(m.target);
            if (shooterLogged.TryGetValue(o, out float at) && Time.time - at < 6f && !(m is NukeMissile)) return;
            shooterLogged[o] = Time.time;
            Log($"{o.Label} > {(tgt != null ? tgt.id + " " + tgt.name : "")}  {MissileKind(m)}", m is NukeMissile ? Amber : SideColor(o));
            if (m is BoardingPod && !Keys.Any(k => k.kind == "boarding"))
                Key("boarding", "BOARDING", $"{o.Label} launches boarding pods at {(tgt != null ? tgt.Label : "the blue line")}", o.pos);
            if (m is NukeMissile) Moment("NUCLEAR MISSILE", $"{o.Label} launches a nuclear missile at {(tgt != null ? tgt.Label : "the blue line")}");
        }

        void OnDowned(Missile m, Transform by)
        {
            if (!running || m == null) return;
            var k = Of(by);
            if (k == null) return;
            k.weapon = k.friendly ? "CIWS" : "PD";
            k.weaponAt = Time.time;
            long key = -1 - k.index;
            if (pairLogged.TryGetValue(key, out float at) && Time.time - at < 2.5f) return;
            pairLogged[key] = Time.time;
            string what = m is BoardingPod ? "POD" : m is NukeMissile ? "NUKE" : "MISSILE";
            Log($"{k.Label}  {(k.friendly ? "CIWS" : "PD")} KILL {what}", k.friendly ? Cyan : SideColor(k));
        }

        void OnShell(ArtilleryShell s)
        {
            if (!running || s == null) return;
            if (!shellTrails.ContainsKey(s)) shellTrails[s] = new List<Vector3> { Rel(s.transform.position) };
            var o = Of(s.owner);
            if (o == null) return;
            o.weapon = "ARTILLERY";
            o.weaponAt = Time.time;
            Vector3 aim = Rel(s.Aim);
            var tgt = Tracks.Where(k => k.side != o.side && !k.dead).OrderBy(k => (k.pos - aim).sqrMagnitude).FirstOrDefault();
            Log($"{o.Label} > {(tgt != null ? tgt.id : "")}  ARTILLERY SHELL ({Weapon(s.kind)})", Amber);
        }

        void OnShot(Projectile p)
        {
            if (!running || p == null) return;
            if (shotSet.Add(p)) shots.Add(p);
            var o = Of(p.owner);
            if (o == null) return;
            if (o.friendly && o.ciws != null && o.ciws.mounts != null && o.ciws.mounts.Any(m => m != null && m.target != null && m.target.GetComponent<Missile>() != null))
                o.weapon = "GUNS + CIWS";
            else if (!o.friendly && o.pd != null && o.pd.EngagedTarget != null) o.weapon = "PD";
            else o.weapon = Weapon(p.kind);
            o.weaponAt = Time.time;
        }

        void OnPhase(VraskFleet f)
        {
            if (!running || f == null || f != fleet) return;
            string n = VraskFleet.Name(f.Current).ToUpperInvariant();
            phases.Add(n);
            Log($"VRASK FLEET: {n}  ({f.Why})", Red);
            if (f.Current == VraskFleet.Phase.Withdraw)
                Key("withdraw", "THE VRASK WITHDRAW", f.Why, RedCentroid());
            else Moment($"VRASK FLEET: {n}", f.Why);
        }

        void OnBoardersOver(Boarders b)
        {
            if (!running || b == null) return;
            var k = Of(b.transform);
            if (k == null) return;
            if (b.Result == Boarders.Outcome.Repelled) Log($"{k.Label} REPELS BOARDERS ({Mathf.RoundToInt(b.CrewKilled)} CREW LOST)", Blue);
        }

        Vector3 RedCentroid()
        {
            var a = Tracks.Where(k => !k.friendly && !k.dead).ToList();
            return a.Count == 0 ? Vector3.zero : a.Aggregate(Vector3.zero, (s, k) => s + k.pos) / a.Count;
        }

        // ---------------------------------------------------------------- following the scene

        void Update()
        {
            if (!Hud.ReelMode && Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame)
                mode = mode == Mode.Off ? Mode.Inset : mode == Mode.Inset ? Mode.Full : Mode.Off;
            if (!running) return;
            try
            {
                Poll();
                if (Time.time >= nextSample) { nextSample += 1f; Sample1(); }
                if (Time.time >= nextTrail)
                {
                    nextTrail = Time.time + 0.5f;
                    foreach (var k in Tracks)
                    {
                        if (k.dead && k.trail.Count > 0 && (k.trail[k.trail.Count - 1] - k.pos).sqrMagnitude < 1f) continue;
                        k.trail.Add(k.pos);
                        if (k.trail.Count > 90) k.trail.RemoveAt(0);
                    }
                }
                if (Time.time >= nextMissileTrail)
                {
                    nextMissileTrail = Time.time + 0.2f;
                    foreach (var m in Missile.Active)
                    {
                        if (m == null) continue;
                        if (!missileTrails.TryGetValue(m, out var l)) missileTrails[m] = l = new List<Vector3>();
                        l.Add(Rel(m.transform.position));
                        if (l.Count > 80) l.RemoveAt(0);
                    }
                    foreach (var s in ArtilleryShell.Active)
                    {
                        if (s == null) continue;
                        if (!shellTrails.TryGetValue(s, out var l)) shellTrails[s] = l = new List<Vector3>();
                        l.Add(Rel(s.transform.position));
                        if (l.Count > 250) l.RemoveAt(0);
                    }
                    foreach (var dead in missileTrails.Keys.Where(m => m == null || !m.isActiveAndEnabled).ToList()) missileTrails.Remove(dead);
                    foreach (var dead in shellTrails.Keys.Where(s => s == null || !s.isActiveAndEnabled).ToList()) shellTrails.Remove(dead);
                }
                if (Time.time >= nextDrones)
                {
                    nextDrones = Time.time + 2f;
                    drones = FindObjectsByType<Craft>(FindObjectsSortMode.None).Where(c => c != null && c.isActiveAndEnabled).ToList();
                }
                for (int i = shots.Count - 1; i >= 0; i--)
                {
                    var p = shots[i];
                    if (p == null || !p.gameObject.activeInHierarchy) { shots.RemoveAt(i); shotSet.Remove(p); }
                }
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        void Poll()
        {
            float dt = Mathf.Max(1e-3f, Time.time - lastPoll);
            lastPoll = Time.time;
            foreach (var k in Tracks)
            {
                if (k.t == null || (k.faction == null && k.pilot == null))
                {
                    if (!k.dead) { k.dead = true; k.diedAt = Clock; Log($"{k.Label} LOST", Grey); }
                    k.vel = Vector3.zero;
                    continue;
                }
                Vector3 p = Rel(k.t.position);
                if (k.seen) k.vel = Vector3.Lerp(k.vel, (p - k.pos) / dt, Mathf.Clamp01(dt * 2f));
                k.seen = true;
                k.pos = p;
                if (k.health != null)
                {
                    k.hull = k.health.maxHealth > 0f ? Mathf.Clamp01(k.health.Current / k.health.maxHealth) : 0f;
                    k.hullTaken = k.health.TotalDamage - k.hull0;
                    if (k.health.Dead && !k.dead) Down(k, k.health.Captured);
                }
                k.shieldF = k.shield != null && k.shield.Up ? k.shield.Fraction : 0f;
                if (k.dead) { k.vel = Vector3.zero; continue; }
                // the tractor beams locking and letting go
                if (k.tractor != null)
                {
                    bool locked = k.tractor.Locked;
                    if (locked && !k.wasLocked)
                    {
                        var held = Of(k.tractor.Holding);
                        k.lockedOn = k.tractor.Holding;
                        Log($"{k.Label} TRACTOR LOCK > {(held != null ? held.Label : "?")}", Red);
                        string text = $"{k.Label}'s beam holds {(held != null ? held.Label : "a frigate")}: pulled in, no escape while it holds";
                        if (!Keys.Any(e => e.kind == "tractor")) Key("tractor", "TRACTOR LOCK", text, k.pos);
                        else Moment("TRACTOR LOCK", text);
                    }
                    else if (!locked && k.wasLocked)
                        Log($"{k.Label} TRACTOR RELEASED{(string.IsNullOrEmpty(k.tractor.LastRelease) ? "" : $" ({k.tractor.LastRelease.ToUpperInvariant()})")}", Grey);
                    k.wasLocked = locked;
                }
                // the Fangs' runs and clamps
                if (k.fang != null && k.fang.Current != k.fangWas)
                {
                    var tg = Of(k.fang.Target);
                    string on = tg != null ? tg.Label : "?";
                    switch (k.fang.Current)
                    {
                        case FangTactics.Phase.Run: Log($"{k.Label} BOARDING RUN > {on}", Red); break;
                        case FangTactics.Phase.Clamped:
                            Log($"{k.Label} CLAMPED ON {on}", Red);
                            if (!Keys.Any(e => e.kind == "boarding")) Key("boarding", "BOARDING", $"{k.Label} clamps onto {on}'s hull", k.pos);
                            else Moment("CLAMPED", $"{k.Label} clamps onto {on}'s hull");
                            break;
                        case FangTactics.Phase.Extract: Log($"{k.Label} CASTS OFF", Grey); break;
                    }
                    k.fangWas = k.fang.Current;
                }
                // boarders aboard a frigate
                if (k.friendly)
                {
                    var b = Boarders.On(k.t);
                    bool aboard = b != null && b.Aboard > 0;
                    if (aboard && !k.boarded)
                    {
                        Log($"BOARDERS ABOARD {k.Label} ({b.Aboard})", Amber);
                        string text = $"{b.Aboard} Vrask boarders fighting the crew of {k.Label}";
                        if (!Keys.Any(e => e.kind == "boarding")) Key("boarding", "BOARDERS ABOARD", text, k.pos);
                        else Moment("BOARDERS ABOARD", text);
                    }
                    k.boarded = aboard;
                    if (k.beam != null && k.beam.Current == MainBeam.State.Firing) { k.weapon = "MAIN BEAM"; k.weaponAt = Time.time; }
                }
                else
                {
                    if (k.tractor != null && k.tractor.Locked) { k.weapon = "TRACTOR"; k.weaponAt = Time.time; }
                    else if (k.pd != null && k.pd.EngagedTarget != null && Time.time - k.weaponAt > 1f) { k.weapon = "PD"; k.weaponAt = Time.time; }
                }
            }
            if (fleet != null && fleet.ContactBy != null && !Keys.Any(e => e.kind == "contact"))
            {
                var by = Of(fleet.ContactBy.transform);
                if (by != null) Key("contact", "FIRST CONTACT", $"{by.Label} has the blue line on its sensors at {fleet.ContactRange / 1000f:F1} km", by.pos);
            }
        }

        void Sample1()
        {
            var s = new Sample
            {
                t = Clock,
                pos = new Vector3[Tracks.Count],
                state = new byte[Tracks.Count],
                hull = new float[Tracks.Count],
                fire = fireNow.ToArray(),
            };
            fireNow.Clear();
            float b = 0f, r = 0f, g = 0f;
            int nb = 0, nr = 0, ng = 0;
            for (int i = 0; i < Tracks.Count; i++)
            {
                var k = Tracks[i];
                s.pos[i] = k.pos;
                s.state[i] = (byte)(k.dead ? (k.captured ? 2 : 1) : 0);
                s.hull[i] = k.dead ? 0f : k.hull;
                if (k.side == 0) { b += s.hull[i]; nb++; } else if (k.side == 1) { r += s.hull[i]; nr++; } else { g += s.hull[i]; ng++; }
            }
            s.blue = nb > 0 ? b / nb : 0f;
            s.red = nr > 0 ? r / nr : 0f;
            s.third = ng > 0 ? g / ng : 0f;
            samples.Add(s);
            foreach (var m in Missile.Active)
                if (m != null) dots.Add((s.t, Rel(m.transform.position), (byte)(m is BoardingPod ? 2 : m is NukeMissile ? 3 : (Of(m.owner)?.side ?? 1) == 0 ? 0 : Of(m.owner)?.side == 2 ? 5 : 1)));
            foreach (var sh in ArtilleryShell.Active)
                if (sh != null) dots.Add((s.t, Rel(sh.transform.position), (byte)4));
        }

        // ---------------------------------------------------------------- for the reel

        /// <summary>What the fleet's guns are on: the target most of the frigates have.</summary>
        public Track CommonTarget()
        {
            Track best = null;
            int n = 0;
            foreach (var g in Tracks.Where(k => k.friendly && !k.dead && k.pilot != null && k.pilot.Target != null).GroupBy(k => Of(k.pilot.Target)))
                if (g.Key != null && !g.Key.dead && g.Count() > n) { n = g.Count(); best = g.Key; }
            return best;
        }

        /// <summary>The hottest exchange now, for the camera: a tractor lock, a Fang's clamp or run,
        /// a bow beam, else the fleet's target and one of the frigates on it.</summary>
        public Track Hottest(int variant, out Track other, out string why)
        {
            other = null;
            why = "";
            var alive = Tracks.Where(k => !k.dead && k.t != null).ToList();
            if (alive.Count == 0) return null;
            var snare = alive.FirstOrDefault(k => k.tractor != null && k.tractor.Locked && Of(k.tractor.Holding) != null);
            if (snare != null && variant % 2 == 0) { other = Of(snare.tractor.Holding); why = "tractor"; return snare; }
            var fang = alive.FirstOrDefault(k => k.fang != null && (k.fang.Current == FangTactics.Phase.Clamped || k.fang.Current == FangTactics.Phase.Run) && Of(k.fang.Target) != null);
            if (fang != null && variant % 3 != 2) { other = Of(fang.fang.Target); why = "boarding"; return fang; }
            var beamer = alive.FirstOrDefault(k => k.friendly && k.beam != null && k.beam.Current != MainBeam.State.Idle && k.pilot != null && k.pilot.Target != null);
            if (beamer != null && variant % 2 == 1) { other = Of(beamer.pilot.Target); why = "beam"; return beamer; }
            // (three-way: now and then the two factions at each other)
            if (ThreeWay && variant % 3 == 2)
            {
                var hit = alive.Where(k => k.side != 0 && k.lastBy != null && !k.lastBy.dead && k.lastBy.t != null && k.lastBy.side != 0 && k.lastBy.side != k.side && Time.time - k.lastByAt < 6f)
                               .OrderByDescending(k => k.lastByAt).FirstOrDefault();
                if (hit != null) { other = hit; why = "rivals"; return hit.lastBy; }
            }
            var tgt = CommonTarget();
            var blue = alive.Where(k => k.friendly).ToList();
            if (tgt != null && blue.Count > 0)
            {
                var on = blue.Where(k => k.pilot != null && Of(k.pilot.Target) == tgt).ToList();
                var shooter = (on.Count > 0 ? on : blue)[variant % (on.Count > 0 ? on.Count : blue.Count)];
                // (now the frigate, looking at its target; now the target under fire)
                if (variant % 2 == 0) { other = tgt; why = "guns"; return shooter; }
                other = shooter; why = "target"; return tgt;
            }
            var a = alive[variant % alive.Count];
            other = alive.Where(k => k.side != a.side).OrderBy(k => (k.pos - a.pos).sqrMagnitude).FirstOrDefault();
            why = "any";
            return a;
        }

        // ---------------------------------------------------------------- the analysis

        public KeyEvent TurningPoint { get; private set; }

        void Analyse()
        {
            Analysis.Clear();
            if (ThreeWay) { AnalyseThree(); return; }
            bool blueWon = Winner == "BLUE";
            bool redWon = Winner == "RED";
            // the turning point: the last time the winner's lead in strength came about (else the
            // loser falling under half strength, else the first kill)
            if (samples.Count > 2 && (blueWon || redWon))
            {
                Sample tp = null;
                for (int i = 1; i < samples.Count; i++)
                {
                    float lead0 = blueWon ? samples[i - 1].blue - samples[i - 1].red : samples[i - 1].red - samples[i - 1].blue;
                    float lead1 = blueWon ? samples[i].blue - samples[i].red : samples[i].red - samples[i].blue;
                    if (lead0 <= 0f && lead1 > 0f) tp = samples[i];
                }
                if (tp == null) tp = samples.FirstOrDefault(x => (blueWon ? x.red : x.blue) < 0.5f);
                if (tp != null && tp.t > 1f)
                {
                    var alive = Enumerable.Range(0, Tracks.Count).Where(i => tp.state[i] == 0).ToList();
                    Vector3 c = alive.Count > 0 ? alive.Aggregate(Vector3.zero, (s, i) => s + tp.pos[i]) / alive.Count : Vector3.zero;
                    TurningPoint = new KeyEvent
                    {
                        t = tp.t, kind = "turn", title = "TURNING POINT", pos = c,
                        text = $"{(blueWon ? "Blue" : "Red")} takes the lead for good: strength blue {tp.blue:P0}, red {tp.red:P0}",
                    };
                    Keys.Add(TurningPoint);
                }
            }
            Keys.Add(new KeyEvent { t = Duration, kind = "end", title = "ENGAGEMENT ENDS", text = Outcome, pos = Tracks.Any(k => !k.dead) ? Tracks.Where(k => !k.dead).Aggregate(Vector3.zero, (s, k) => s + k.pos) / Tracks.Count(k => !k.dead) : Vector3.zero });
            Keys.Sort((a, b) => a.t.CompareTo(b.t));

            Analysis.Add($"RESULT       {Outcome}");
            Analysis.Add($"DURATION     {Clk(Duration)}");
            int blueLost = Tracks.Count(k => k.friendly && k.dead), redLost = Tracks.Count(k => !k.friendly && k.dead);
            Analysis.Add($"EXCHANGE     BLUE LOST {blueLost} OF {BlueCount}  :  RED LOST {redLost} OF {RedCount}");
            Analysis.Add($"FIREPOWER    BLUE DEALT {K(BlueDealt)}  :  RED DEALT {K(RedDealt)}");
            if (phases.Count > 0) Analysis.Add($"VRASK PLAN   {string.Join(" > ", phases.Distinct())}");
            var bestBlue = Tracks.Where(k => k.friendly).OrderByDescending(k => k.kills).ThenByDescending(k => k.dealt).FirstOrDefault();
            var bestRed = Tracks.Where(k => !k.friendly).OrderByDescending(k => k.kills).ThenByDescending(k => k.dealt).FirstOrDefault();
            if (bestBlue != null) Analysis.Add($"TOP BLUE     {bestBlue.Label}: {bestBlue.kills} KILLS, {K(bestBlue.dealt)} DEALT");
            if (bestRed != null) Analysis.Add($"TOP RED      {bestRed.Label}: {bestRed.kills} KILLS, {K(bestRed.dealt)} DEALT");
            float redTotal = Mathf.Max(1f, Tracks.Where(k => k.friendly).Sum(k => k.taken));
            var focus = Tracks.Where(k => k.friendly).OrderByDescending(k => k.taken).FirstOrDefault();
            if (focus != null && focus.taken > 0f)
                Analysis.Add($"RED FOCUS    {focus.Label} TOOK {focus.taken / redTotal:P0} OF RED FIRE");
            // what the blue fleet's guns went for first
            var firstKills = Tracks.Where(k => !k.friendly && k.dead).OrderBy(k => k.diedAt).Take(3).Select(k => k.cls).ToList();
            if (firstKills.Count > 0) Analysis.Add($"BLUE KILLED  {string.Join(", ", firstKills)} FIRST");
            var lostSnares = Tracks.Count(k => !k.friendly && k.dead && (k.cls == "SNARE" || k.cls == "MAW"));
            int snaresMaws = Tracks.Count(k => !k.friendly && (k.cls == "SNARE" || k.cls == "MAW"));
            Analysis.Add($"RED CORE     {lostSnares} OF {snaresMaws} MAWS AND SNARES LOST{(Keys.Any(k => k.kind == "withdraw") ? ": THE VRASK BROKE OFF" : "")}");
            if (TurningPoint != null) Analysis.Add($"TURNING PT   {Clk(TurningPoint.t)}  {TurningPoint.text.ToUpperInvariant()}");
        }

        /// <summary>The three-way battle's analysis: each side's losses and fire, who fired on
        /// whom, the best ship of each, when each side broke (under half its strength).</summary>
        void AnalyseThree()
        {
            float StrengthAt(Sample x, int side) => side == 0 ? x.blue : side == 1 ? x.red : x.third;
            // the turning point: a side broken (under half), the first
            for (int side = 0; side < 3; side++)
            {
                var broke = samples.FirstOrDefault(x => StrengthAt(x, side) < 0.5f);
                if (broke == null || broke.t < 1f) continue;
                var alive = Enumerable.Range(0, Tracks.Count).Where(i => broke.state[i] == 0 && Tracks[i].side == side).ToList();
                Vector3 c = alive.Count > 0 ? alive.Aggregate(Vector3.zero, (a, i) => a + broke.pos[i]) / alive.Count : Vector3.zero;
                Keys.Add(new KeyEvent { t = broke.t, kind = "broken" + side, title = $"{SideNames[side]} BROKEN", pos = c,
                                        text = $"{SideNames[side].ToLowerInvariant()} down to {StrengthAt(broke, side):P0} of its strength: {Enumerable.Range(0, 3).Select(x => $"{SideNames[x].ToLowerInvariant()} {StrengthAt(broke, x):P0}").Aggregate((a, b2) => a + ", " + b2)}" });
            }
            TurningPoint = Keys.Where(k => k.kind.StartsWith("broken")).OrderBy(k => k.t).FirstOrDefault();
            Keys.Add(new KeyEvent { t = Duration, kind = "end", title = "ENGAGEMENT ENDS", text = Outcome, pos = Tracks.Any(k => !k.dead) ? Tracks.Where(k => !k.dead).Aggregate(Vector3.zero, (a, k) => a + k.pos) / Tracks.Count(k => !k.dead) : Vector3.zero });
            Keys.Sort((a, b) => a.t.CompareTo(b.t));

            string N(int side) => SideNames[side];
            Analysis.Add($"RESULT       {Outcome}");
            Analysis.Add($"DURATION     {Clk(Duration)}");
            Analysis.Add($"LOSSES       {string.Join("  :  ", Enumerable.Range(0, 3).Select(x => $"{N(x)} {Tracks.Count(k => k.side == x && k.dead)} OF {SideCount(x)}"))}");
            Analysis.Add($"FIREPOWER    {string.Join("  :  ", Enumerable.Range(0, 3).Select(x => $"{N(x)} {K(SideDealt(x))}"))}");
            Analysis.Add($"RIVALS       {N(1)} ON {N(2)} {K(SideDamage[1, 2])}  :  {N(2)} ON {N(1)} {K(SideDamage[2, 1])}");
            Analysis.Add($"ON BLUE      {N(1)} {K(SideDamage[1, 0])}  :  {N(2)} {K(SideDamage[2, 0])}");
            Analysis.Add($"BLUE FIRE    ON {N(1)} {K(SideDamage[0, 1])}  :  ON {N(2)} {K(SideDamage[0, 2])}");
            for (int side = 0; side < 3; side++)
            {
                var best = Tracks.Where(k => k.side == side).OrderByDescending(k => k.kills).ThenByDescending(k => k.dealt).FirstOrDefault();
                if (best != null) Analysis.Add($"TOP {N(side),-8} {best.Label}: {best.kills} KILLS, {K(best.dealt)} DEALT");
            }
            var firstKills = Tracks.Where(k => k.dead).OrderBy(k => k.diedAt).Take(3).Select(k => $"{k.id} {k.cls}").ToList();
            if (firstKills.Count > 0) Analysis.Add($"FIRST DOWN   {string.Join(", ", firstKills)}");
            if (TurningPoint != null) Analysis.Add($"TURNING PT   {Clk(TurningPoint.t)}  {TurningPoint.title}");
        }

        static string K(float v) => v >= 10000f ? $"{v / 1000f:F0}K" : v >= 1000f ? $"{v / 1000f:F1}K" : $"{v:F0}";

        // ================================================================ drawing

        static readonly Color Bg = new Color(0.012f, 0.03f, 0.055f, 1f);
        static readonly Color PanelBg = new Color(0.02f, 0.05f, 0.085f, 0.96f);
        static readonly Color TitleBg = new Color(0.03f, 0.16f, 0.2f, 1f);
        static readonly Color Frame = new Color(0.3f, 0.85f, 0.85f, 0.55f);
        static readonly Color Cyan = new Color(0.4f, 0.95f, 0.95f, 1f);
        static readonly Color Green = new Color(0.45f, 1f, 0.55f, 1f);
        static readonly Color Dim = new Color(0.3f, 0.6f, 0.62f, 1f);
        static readonly Color Blue = new Color(0.42f, 0.75f, 1f, 1f);
        static readonly Color Red = new Color(1f, 0.33f, 0.28f, 1f);
        static readonly Color Amber = new Color(1f, 0.76f, 0.28f, 1f);
        static readonly Color Violet = new Color(0.78f, 0.52f, 1f, 1f);
        static readonly Color Magenta = new Color(0.95f, 0.45f, 1f, 1f);
        static readonly Color Grey = new Color(0.48f, 0.53f, 0.58f, 1f);
        static readonly Color Yellow = new Color(1f, 0.95f, 0.45f, 1f);

        static Color A(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);
        static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        float s = 1f;                // layout scale: 1 at 1080 px tall
        /// <summary>The inset over the action, as a share of its own size (its lines and words with it).</summary>
        public float insetScale = 1f;
        Rect clip;
        readonly List<Seg> segs = new List<Seg>(8192);
        static Material lineMat;
        static bool triedMat;
        static Font mono;
        static bool triedFont;
        readonly Dictionary<int, GUIStyle> styles = new Dictionary<int, GUIStyle>();

        void OnGUI()
        {
            GUI.depth = -900;
            if (mode == Mode.Off || !began) return;
            if (Event.current.type != EventType.Repaint) return;
            s = Screen.height / 1080f;
            float W = Screen.width, H = Screen.height, m = 10f * s;
            try
            {
                if (mode == Mode.Inset)
                {
                    s *= insetScale;
                    float iw = Mathf.Round(W * 0.34f * insetScale), ih = Mathf.Round(iw * 0.68f);
                    var r = new Rect(W - iw - 24f * s, 24f * s, iw, ih);
                    Hud.Fill(new Rect(r.x - 3f * s, r.y - 3f * s, r.width + 6f * s, r.height + 6f * s), new Color(0f, 0f, 0f, 0.6f));
                    Plot(r, false, true);
                    return;
                }
                Hud.Fill(new Rect(0f, 0f, W, H), Bg);
                Banner(new Rect(0f, 0f, W, 22f * s));
                var strip = new Rect(m, 24f * s, W - 2f * m, 58f * s);
                float top = strip.yMax + m;
                float plotW = Mathf.Min(W * 0.56f, H - top - m);
                var plot = new Rect(m, top, plotW, H - top - m);
                float rx = plot.xMax + m, rw = W - rx - m;
                var upper = new Rect(rx, top, rw, (H - top - m) * 0.52f);
                var lower = new Rect(rx, upper.yMax + m, rw, H - upper.yMax - 2f * m);
                switch (mode)
                {
                    case Mode.Full:
                        Status(strip, false);
                        Plot(plot, false, false);
                        Table(upper);
                        LogPanel(lower);
                        break;
                    case Mode.Results:
                        Status(strip, false);
                        Results(new Rect(m, top, W - 2f * m, H - top - m));
                        break;
                    case Mode.Replay:
                        Status(strip, true);
                        Plot(plot, true, false);
                        AnalysisPanel(upper);
                        KeyPanel(lower);
                        break;
                    case Mode.EndCard:
                        EndCard(new Rect(0f, 0f, W, H));
                        break;
                }
            }
            catch (Exception e)
            {
                segs.Clear();
                Debug.LogException(e);
            }
        }

        // ---------------------------------------------------------------- primitives

        GUIStyle St(int px, bool bold = false, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            int size = Mathf.Max(8, Mathf.RoundToInt(px * s));
            int key = size * 64 + (bold ? 32 : 0) + (int)anchor;
            if (styles.TryGetValue(key, out var st)) return st;
            if (!triedFont)
            {
                triedFont = true;
                try { mono = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Lucida Console", "Courier New", "DejaVu Sans Mono" }, 16); }
                catch { mono = null; }
            }
            st = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                richText = true,
                wordWrap = false,
                alignment = anchor,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
            };
            if (mono != null) st.font = mono;
            styles[key] = st;
            return st;
        }

        void Tx(Rect r, string text, int px, Color c, bool bold = false, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            var st = St(px, bold, anchor);
            st.normal.textColor = c;
            GUI.Label(r, text, st);
        }

        void Tx(float x, float y, string text, int px, Color c, bool bold = false, float w = 800f) =>
            Tx(new Rect(x, y, w * s, (px + 8) * s), text, px, c, bold);

        float TextW(string text, int px) => St(px).CalcSize(new GUIContent(text)).x;

        void Seg1(Vector2 a, Vector2 b, Color c, float w)
        {
            if (!ClipSeg(ref a, ref b)) return;
            segs.Add(new Seg { a = a, b = b, c = c, w = Mathf.Max(1f, w) });
        }

        void Dashed(Vector2 a, Vector2 b, Color c, float w, float dash, float gap)
        {
            float len = Vector2.Distance(a, b);
            if (len < 0.5f) return;
            Vector2 d = (b - a) / len;
            for (float x = 0f; x < len; x += dash + gap) Seg1(a + d * x, a + d * Mathf.Min(len, x + dash), c, w);
        }

        void Circle(Vector2 c, float r, Color col, float w, int n = 48, float from = 0f, float to = 360f)
        {
            Vector2 prev = c + new Vector2(Mathf.Sin(from * Mathf.Deg2Rad), -Mathf.Cos(from * Mathf.Deg2Rad)) * r;
            for (int i = 1; i <= n; i++)
            {
                float a = Mathf.Lerp(from, to, i / (float)n) * Mathf.Deg2Rad;
                Vector2 p = c + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * r;
                Seg1(prev, p, col, w);
                prev = p;
            }
        }

        /// <summary>Liang-Barsky: the part of a segment inside the clip rectangle (false: none).</summary>
        bool ClipSeg(ref Vector2 a, ref Vector2 b)
        {
            float t0c = 0f, t1c = 1f;
            Vector2 d = b - a;
            bool Edge(float p, float q)
            {
                if (Mathf.Abs(p) < 1e-6f) return q >= 0f;
                float r = q / p;
                if (p < 0f) { if (r > t1c) return false; if (r > t0c) t0c = r; }
                else { if (r < t0c) return false; if (r < t1c) t1c = r; }
                return true;
            }
            if (!Edge(-d.x, a.x - clip.xMin) || !Edge(d.x, clip.xMax - a.x) || !Edge(-d.y, a.y - clip.yMin) || !Edge(d.y, clip.yMax - a.y)) return false;
            Vector2 a0 = a;
            a = a0 + d * t0c;
            b = a0 + d * t1c;
            return true;
        }

        static bool UseGL()
        {
            // (lines drawn through IMGUI unless -cicgl: with GL in OnGUI the plot came out upside down
            // against its text on Direct3D, its y the other way to the GUI's)
            if (!LaunchArgs.Has("-cicgl")) return false;
            if (!triedMat)
            {
                triedMat = true;
                var sh = Shader.Find("Hidden/Internal-Colored");
                if (sh == null) sh = Shader.Find("Sprites/Default");
                if (sh == null) sh = Shader.Find("UI/Default");
                if (sh != null)
                {
                    lineMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                    lineMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    lineMat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    lineMat.SetFloat("_Cull", (float)CullMode.Off);
                    lineMat.SetFloat("_ZWrite", 0f);
                    lineMat.SetFloat("_ZTest", (float)CompareFunction.Always);
                }
            }
            return lineMat != null;
        }

        /// <summary>Draw the lines gathered so far (under whatever is drawn after).</summary>
        void Flush()
        {
            if (segs.Count == 0) return;
            if (UseGL())
            {
                float H = Screen.height;
                GL.PushMatrix();
                lineMat.SetPass(0);
                GL.LoadPixelMatrix();
                GL.Begin(GL.QUADS);
                foreach (var g in segs)
                {
                    Vector2 d = g.b - g.a;
                    float len = d.magnitude;
                    if (len < 0.05f) continue;
                    Vector2 n = new Vector2(-d.y, d.x) / len * (g.w * 0.5f);
                    GL.Color(g.c);
                    GL.Vertex3(g.a.x + n.x, H - (g.a.y + n.y), 0f);
                    GL.Vertex3(g.b.x + n.x, H - (g.b.y + n.y), 0f);
                    GL.Vertex3(g.b.x - n.x, H - (g.b.y - n.y), 0f);
                    GL.Vertex3(g.a.x - n.x, H - (g.a.y - n.y), 0f);
                }
                GL.End();
                GL.PopMatrix();
            }
            else
            {
                var m0 = GUI.matrix;
                foreach (var g in segs)
                {
                    Vector2 d = g.b - g.a;
                    float len = d.magnitude;
                    if (len < 0.05f) continue;
                    GUI.matrix = m0;
                    GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, g.a);
                    Hud.Fill(new Rect(g.a.x, g.a.y - g.w * 0.5f, len, g.w), g.c);
                }
                GUI.matrix = m0;
            }
            segs.Clear();
        }

        /// <summary>A window: its frame, title bar (title left, `right` on the right) and the
        /// rectangle inside it.</summary>
        Rect Panel(Rect r, string title, string right = null)
        {
            Hud.Fill(r, PanelBg);
            float th = 24f * s;
            Hud.Fill(new Rect(r.x, r.y, r.width, th), TitleBg);
            Hud.Outline(r, Mathf.Max(1f, s), Frame);
            Hud.Fill(new Rect(r.x, r.y + th, r.width, Mathf.Max(1f, s)), Frame);
            Hud.Corners(new Rect(r.x - 2f * s, r.y - 2f * s, r.width + 4f * s, r.height + 4f * s), 14f * s, Mathf.Max(1f, 2f * s), Cyan);
            Tx(new Rect(r.x + 10f * s, r.y + 3f * s, r.width - 20f * s, th), title, 15, Cyan, true);
            if (!string.IsNullOrEmpty(right)) Tx(new Rect(r.x + 10f * s, r.y + 3f * s, r.width - 20f * s, th), right, 14, Dim, false, TextAnchor.UpperRight);
            return new Rect(r.x + 1f, r.y + th + 1f, r.width - 2f, r.height - th - 2f);
        }

        void Banner(Rect r)
        {
            Hud.Fill(r, new Color(0.02f, 0.12f, 0.06f, 1f));
            Tx(r, "UNCLASSIFIED // TRAINING      CIC   COMBAT SYSTEMS DISPLAY 1      BATTLECRUISER 4000 BC", 13, Green, true, TextAnchor.MiddleCenter);
        }

        // ---------------------------------------------------------------- (d) the status strip

        void Status(Rect r, bool replay)
        {
            Hud.Fill(r, PanelBg);
            Hud.Outline(r, Mathf.Max(1f, s), Frame);
            float x = r.x, w = r.width;
            float[] share = { 0.15f, 0.43f, 0.18f, 0.24f };
            string[] head = { replay ? $"REPLAY  x{replaySpeed:F0}" : "BATTLE CLOCK", PhaseText != null ? "FLEET PHASES" : "VRASK FLEET PHASE", "SHIPS REMAINING", "DAMAGE DEALT" };
            if (ThreeWay) share = new[] { 0.13f, 0.33f, 0.24f, 0.30f };
            for (int i = 0; i < 4; i++)
            {
                float cw = w * share[i];
                var cell = new Rect(x + 10f * s, r.y + 5f * s, cw - 20f * s, r.height - 8f * s);
                Tx(cell, head[i], 12, Dim, true);
                var val = new Rect(cell.x, cell.y + 16f * s, cell.width, cell.height - 16f * s);
                switch (i)
                {
                    case 0:
                        Tx(val, replay ? $"T+{Clk(replayT)} / {Clk(Duration)}" : $"T+{ClockText}{(running ? "" : "  END")}", 22, Green, true);
                        break;
                    case 1 when PhaseText != null:
                        Tx(val, (PhaseText() ?? "").ToUpperInvariant(), 17, Amber);
                        break;
                    case 1:
                        string ph = fleet != null ? VraskFleet.Name(fleet.Current).ToUpperInvariant() : "NO FLEET";
                        string why = fleet != null ? fleet.Why : "";
                        if (replay) { ph = phases.Count > 0 ? string.Join(" > ", phases.Distinct()) : ph; why = ""; }
                        Tx(val, $"<b>{ph}</b>  <color=#{Hex(Dim)}>{(why ?? "").ToUpperInvariant()}</color>", 17, Red);
                        break;
                    case 2:
                        Tx(val, string.Join("  ", Enumerable.Range(0, Sides).Select(x => $"<color=#{Hex(ColorOf(x))}>{SideNames[x]} {SideLeft(x)}/{SideCount(x)}</color>")), ThreeWay ? 17 : 20, Cyan, true);
                        break;
                    case 3:
                        Tx(val, string.Join("  ", Enumerable.Range(0, Sides).Select(x => $"<color=#{Hex(ColorOf(x))}>{SideNames[x]} {K(SideDealt(x))}</color>")), ThreeWay ? 17 : 20, Cyan, true);
                        break;
                }
                x += cw;
                if (i < 3) Hud.Fill(new Rect(x, r.y + 6f * s, Mathf.Max(1f, s), r.height - 12f * s), Frame);
            }
        }

        // ---------------------------------------------------------------- (a) the tactical plot

        Vector3 plotC;
        float plotR = 6000f;
        bool plotInit;

        /// <summary>The last sample at or before `t` (battle seconds).</summary>
        int SampleAt(float t)
        {
            int lo = 0, hi = samples.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (samples[mid].t <= t) lo = mid; else hi = mid - 1;
            }
            return Mathf.Max(0, lo);
        }

        static float Nice(float x)
        {
            float[] steps = { 250f, 500f, 1000f, 2000f, 2500f, 5000f, 10000f, 20000f, 25000f, 50000f };
            foreach (var st in steps) if (st >= x) return st;
            return 100000f;
        }

        void Plot(Rect r, bool replay, bool inset)
        {
            Sample cur = null, next = null;
            float f = 0f;
            if (replay && samples.Count > 0)
            {
                int i = SampleAt(replayT);
                cur = samples[i];
                next = samples[Mathf.Min(i + 1, samples.Count - 1)];
                f = next.t > cur.t ? Mathf.Clamp01((replayT - cur.t) / (next.t - cur.t)) : 0f;
            }
            // where and how big: live, on the ships left (eased); in the replay, the whole battle
            Vector3 c;
            float rad;
            if (replay)
            {
                var all = samples.SelectMany(x => x.pos).ToList();
                if (all.Count == 0) all.Add(Vector3.zero);
                float x0 = all.Min(p => p.x), x1 = all.Max(p => p.x), z0 = all.Min(p => p.z), z1 = all.Max(p => p.z);
                c = new Vector3((x0 + x1) * 0.5f, 0f, (z0 + z1) * 0.5f);
                rad = Mathf.Max(2000f, Mathf.Max(x1 - x0, z1 - z0) * 0.55f);
            }
            else
            {
                var alive = Tracks.Where(k => !k.dead).ToList();
                if (alive.Count == 0) alive = Tracks;
                Vector3 cc = alive.Count > 0 ? alive.Aggregate(Vector3.zero, (a, k) => a + k.pos) / alive.Count : Vector3.zero;
                cc.y = 0f;
                float rr = 2500f;
                foreach (var k in alive) rr = Mathf.Max(rr, new Vector2(k.pos.x - cc.x, k.pos.z - cc.z).magnitude * 1.2f);
                if (!plotInit) { plotC = cc; plotR = rr; plotInit = true; }
                float e = Mathf.Clamp01(Time.deltaTime * 1.5f);
                plotC = Vector3.Lerp(plotC, cc, e);
                plotR = Mathf.Lerp(plotR, rr, e);
                c = plotC;
                rad = plotR;
            }
            float ring = Nice(rad / 3f);
            string title = replay ? $"POST-ACTION REPLAY  //  {place}" : $"TACTICAL PLOT  //  {place}";
            var inner = Panel(r, title, inset ? $"T+{ClockText}  {string.Join(" ", Enumerable.Range(0, Sides).Select(x => $"{SideNames[x][0]}{SideLeft(x)}"))}" : $"RINGS {ring / 1000f:0.##} KM   000 = THREAT AXIS   CTR FLEET CENTROID");
            clip = inner;
            Vector2 cp = inner.center;
            float scopeR = Mathf.Min(inner.width, inner.height) * 0.5f - 22f * s;
            float k2 = scopeR / rad;
            Vector2 P(Vector3 rel) => cp + new Vector2((rel.x - c.x) * k2, -(rel.z - c.z) * k2);
            int fs = inset ? 11 : 13;
            var labels = new List<(Rect r, string t, Color c, int px)>();

            // the scope: grid, range rings, bearing ticks
            Seg1(new Vector2(cp.x - scopeR, cp.y), new Vector2(cp.x + scopeR, cp.y), A(Frame, 0.25f), 1f);
            Seg1(new Vector2(cp.x, cp.y - scopeR), new Vector2(cp.x, cp.y + scopeR), A(Frame, 0.25f), 1f);
            for (int n = 1; n * ring <= rad * 1.02f && n < 12; n++)
            {
                float pr = n * ring * k2;
                Circle(cp, pr, A(Green, 0.28f), 1f, 96);
                labels.Add((new Rect(cp.x + pr * 0.7071f + 3f * s, cp.y - pr * 0.7071f - 14f * s, 80f * s, 16f * s), $"{n * ring / 1000f:0.#} KM", A(Green, 0.6f), fs - 1));
            }
            Circle(cp, scopeR, A(Cyan, 0.6f), 1.5f * s, 120);
            for (int deg = 0; deg < 360; deg += 5)
            {
                float a = deg * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Sin(a), -Mathf.Cos(a));
                float len = deg % 30 == 0 ? 12f * s : deg % 10 == 0 ? 7f * s : 3.5f * s;
                Seg1(cp + dir * scopeR, cp + dir * (scopeR - len), A(Cyan, 0.75f), 1f);
                if (deg % 30 == 0 && !inset)
                {
                    Vector2 lp = cp + dir * (scopeR + 11f * s);
                    labels.Add((new Rect(lp.x - 20f * s, lp.y - 8f * s, 40f * s, 16f * s), deg == 0 ? "000 T" : $"{deg:000}", Cyan, fs - 1));
                }
            }

            if (replay) ReplayGeometry(cur, next, f, P, labels, fs);
            else LiveGeometry(P, labels, fs, inset);
            Flush();
            foreach (var l in labels)
            {
                var st = St(l.px, false, TextAnchor.UpperLeft);
                st.normal.textColor = l.c;
                GUI.Label(l.r, l.t, st);
            }
            if (replay) Callouts(P, inner);
            else if (!inset) Legend(inner);
        }

        void Icon(Vector2 p, Track k, bool dead, bool captured, float hull, List<(Rect r, string t, Color c, int px)> labels, int fs, bool target, Vector3 vel, Func<Vector3, Vector2> P)
        {
            Color col = dead ? (captured ? A(Amber, 0.7f) : A(Grey, 0.6f)) : SideColor(k);
            float r = 10f * s;
            float w = 1.6f * s;
            if (k.friendly)
            {
                // friendly: a circle (the upper half heavier, as the space frame)
                Circle(p, r, col, w, 28);
                Circle(p, r + 2f * s, A(col, 0.5f), w, 14, -90f, 90f);
            }
            else
            {
                // hostile: a diamond
                Vector2 u = new Vector2(0f, -r * 1.25f), rt = new Vector2(r * 1.25f, 0f);
                Seg1(p + u, p + rt, col, w); Seg1(p + rt, p - u, col, w); Seg1(p - u, p - rt, col, w); Seg1(p - rt, p + u, col, w);
            }
            if (dead)
            {
                Seg1(p + new Vector2(-r, -r), p + new Vector2(r, r), col, w);
                Seg1(p + new Vector2(-r, r), p + new Vector2(r, -r), col, w);
            }
            else
            {
                // the velocity leader: where it will be in 30 s (capped)
                Vector2 lead = P(k.pos + vel * 30f) - P(k.pos);
                if (lead.magnitude > 70f * s) lead = lead.normalized * 70f * s;
                if (lead.magnitude > r) Seg1(p + lead.normalized * r, p + lead, col, w);
            }
            if (target && !dead)
            {
                float b = r + 6f * s, l = 5f * s;
                Color tc = A(Yellow, 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(Time.time * 3f)));
                foreach (var sx in new[] { -1f, 1f })
                    foreach (var sy in new[] { -1f, 1f })
                    {
                        Vector2 q = p + new Vector2(sx * b, sy * b);
                        Seg1(q, q - new Vector2(sx * l, 0f), tc, w);
                        Seg1(q, q - new Vector2(0f, sy * l), tc, w);
                    }
            }
            labels.Add((new Rect(p.x - 4f * s, p.y - 8f * s, 14f * s, 16f * s), $"<b>{k.glyph}</b>", col, fs));
            string tag = dead ? (captured ? " CAPT" : " DEAD") : "";
            labels.Add((new Rect(p.x + r + 6f * s, p.y - r - 4f * s, 220f * s, 16f * s), $"{k.id} {k.name}{tag}", col, fs));
            if (!dead)
            {
                // the hull bar under the label
                float bw = 54f * s, bx = p.x + r + 6f * s, by = p.y - r + 11f * s;
                Seg1(new Vector2(bx, by), new Vector2(bx + bw, by), A(Grey, 0.45f), 3f * s);
                Color hc = hull > 0.6f ? Green : hull > 0.3f ? Amber : Red;
                if (hull > 0.01f) Seg1(new Vector2(bx, by), new Vector2(bx + bw * hull, by), hc, 3f * s);
            }
        }

        void Chevron(Vector2 p, Vector2 dir, Color c, float size)
        {
            if (dir.sqrMagnitude < 1e-6f) dir = Vector2.up * -1f;
            dir.Normalize();
            Vector2 side = new Vector2(-dir.y, dir.x);
            Seg1(p, p - dir * size + side * size * 0.7f, c, 1.4f * s);
            Seg1(p, p - dir * size - side * size * 0.7f, c, 1.4f * s);
        }


        void LiveGeometry(Func<Vector3, Vector2> P, List<(Rect r, string t, Color c, int px)> labels, int fs, bool inset)
        {
            // the ships' tracks, fading with age
            foreach (var k in Tracks)
            {
                Color col = k.dead ? Grey : SideColor(k);
                int n = k.trail.Count;
                for (int i = 1; i < n; i++)
                    Seg1(P(k.trail[i - 1]), P(k.trail[i]), A(col, 0.05f + 0.45f * i / n), 1.2f * s);
                if (n > 0 && !k.dead) Seg1(P(k.trail[n - 1]), P(k.pos), A(col, 0.5f), 1.2f * s);
            }
            // range lines: the fleet's target and each frigate on it
            var tgt = CommonTarget();
            if (tgt != null)
                foreach (var k in Tracks.Where(x => x.friendly && !x.dead && x.pilot != null && Of(x.pilot.Target) == tgt))
                {
                    Vector2 a = P(k.pos), b = P(tgt.pos);
                    Dashed(a, b, A(Yellow, 0.4f), 1f, 6f * s, 5f * s);
                    if (!inset)
                    {
                        Vector2 mid = (a + b) * 0.5f;
                        labels.Add((new Rect(mid.x + 3f * s, mid.y - 7f * s, 70f * s, 15f * s), $"{Vector3.Distance(k.pos, tgt.pos) / 1000f:F1}", A(Yellow, 0.8f), fs - 2));
                    }
                }
            // gunfire: a short tracer back along each shot's path
            foreach (var p in shots)
            {
                if (p == null || !p.gameObject.activeInHierarchy) continue;
                var o = Of(p.owner);
                Color col = p.kind == HitKind.Ion ? Violet : ShotColor(o);
                Vector3 head = Rel(p.transform.position);
                Vector3 tail = Rel(p.transform.position - p.transform.forward * Mathf.Min(p.speed * 0.25f, 600f));
                Seg1(P(tail), P(head), A(col, 0.85f), 1.3f * s);
            }
            // missiles and pods: dotted tracks and a chevron at the head
            foreach (var kv in missileTrails)
            {
                var m = kv.Key;
                if (m == null || !m.isActiveAndEnabled) continue;
                var o = Of(m.owner);
                Color col = m is BoardingPod ? new Color(1f, 0.4f, 0.85f) : m is NukeMissile ? Yellow : o != null && o.friendly ? Cyan : o != null && o.side == 2 ? Magenta : Red;
                var l = kv.Value;
                for (int i = 1; i < l.Count; i += 2) Seg1(P(l[i - 1]), P(l[i]), A(col, 0.6f), 1.2f * s);
                Vector3 head = Rel(m.transform.position);
                Chevron(P(head), Dir2(m.Velocity), col, 7f * s);
                if (m is NukeMissile && !inset) labels.Add((new Rect(P(head).x + 6f * s, P(head).y, 60f * s, 15f * s), "NUKE", Yellow, fs - 2));
            }
            // artillery shells: the track flown, and dashed to where it is aimed
            foreach (var kv in shellTrails)
            {
                var sh = kv.Key;
                if (sh == null || !sh.isActiveAndEnabled) continue;
                var l = kv.Value;
                for (int i = 1; i < l.Count; i++) if (i % 3 != 0) Seg1(P(l[i - 1]), P(l[i]), A(Amber, 0.75f), 1.3f * s);
                Vector2 head = P(Rel(sh.transform.position)), aim = P(Rel(sh.Aim));
                Dashed(head, aim, A(Amber, 0.35f), 1f, 3f * s, 4f * s);
                Seg1(aim + new Vector2(-5f, -5f) * s, aim + new Vector2(5f, 5f) * s, Amber, 1.2f * s);
                Seg1(aim + new Vector2(-5f, 5f) * s, aim + new Vector2(5f, -5f) * s, Amber, 1.2f * s);
                Circle(head, 3f * s, Amber, 1.5f * s, 8);
            }
            // drones and small craft, if any are about
            foreach (var d in drones)
            {
                if (d == null || !d.isActiveAndEnabled) continue;
                Vector3 rp = Rel(d.transform.position);
                Chevron(P(rp), Dir2(d.transform.forward), Yellow, 6f * s);
                if (!inset) labels.Add((new Rect(P(rp).x + 5f * s, P(rp).y - 2f * s, 50f * s, 14f * s), d.kind == Craft.Kind.Drone ? "DRN" : "FTR", A(Yellow, 0.8f), fs - 3));
            }
            // beams: the bow beams firing, the tractor beams holding, point defence, a Fang's clamp
            foreach (var k in Tracks)
            {
                if (k.dead || k.t == null) continue;
                if (k.beam != null && k.beam.Current == MainBeam.State.Firing)
                {
                    Vector3 from = Rel(k.beam.muzzle != null ? k.beam.muzzle.position : k.t.position);
                    Seg1(P(from), P(Rel(k.beam.BeamEnd)), A(k.friendly ? Cyan : Red, 0.35f), 6f * s);
                    Seg1(P(from), P(Rel(k.beam.BeamEnd)), k.friendly ? new Color(0.85f, 1f, 1f) : Red, 2.2f * s);
                }
                else if (k.beam != null && k.beam.Current == MainBeam.State.Charging && k.friendly)
                    Circle(P(k.pos), (14f + 6f * k.beam.ChargeProgress) * s, A(Cyan, 0.3f + 0.5f * k.beam.ChargeProgress), 1f, 20);
                if (k.tractor != null && k.tractor.Locked && k.tractor.Holding != null)
                {
                    Vector2 a = P(Rel(k.tractor.From)), b = P(Rel(k.tractor.Holding.position));
                    Seg1(a, b, A(Green, 0.25f), 7f * s);
                    Dashed(a, b, Green, 2f * s, 8f * s, 4f * s);
                    if (!inset) labels.Add((new Rect((a.x + b.x) * 0.5f + 4f * s, (a.y + b.y) * 0.5f + 2f * s, 80f * s, 15f * s), "TRACTOR", Green, fs - 2));
                }
                if (k.pd != null && k.pd.EngagedTarget != null)
                    Dashed(P(k.pos), P(Rel(k.pd.EngagedTarget.position)), A(SideColor(k), 0.7f), 1f, 2f * s, 3f * s);
                if (k.fang != null && k.fang.IsClamped && k.fang.Target != null)
                    Seg1(P(k.pos), P(Rel(k.fang.Target.position)), A(new Color(1f, 0.4f, 0.85f), 0.9f), 4f * s);
            }
            // the ships, over all of it
            foreach (var k in Tracks.OrderBy(x => x.dead ? 0 : 1))
                Icon(P(k.pos), k, k.dead, k.captured, k.hull, labels, fs, k == tgt, k.vel, P);
        }

        void ReplayGeometry(Sample cur, Sample next, float f, Func<Vector3, Vector2> P, List<(Rect r, string t, Color c, int px)> labels, int fs)
        {
            if (cur == null) return;
            int upto = SampleAt(replayT);
            // every ship's whole track so far, brighter the more recent
            for (int i = 0; i < Tracks.Count; i++)
            {
                var k = Tracks[i];
                Color col = SideColor(k);
                for (int j = 1; j <= upto; j++)
                {
                    if (samples[j - 1].state[i] != 0) break;
                    float age = (upto - j) / 60f;
                    Seg1(P(samples[j - 1].pos[i]), P(samples[j].pos[i]), A(col, Mathf.Lerp(0.85f, 0.2f, Mathf.Clamp01(age))), 1.4f * s);
                }
            }
            // missiles and shells, as dots where they were
            foreach (var d in dots)
            {
                if (d.t > replayT) break;
                float age = replayT - d.t;
                if (age > 40f) continue;
                Color col = d.k == 5 ? Magenta : d.k == 0 ? Cyan : d.k == 1 ? Red : d.k == 2 ? new Color(1f, 0.4f, 0.85f) : d.k == 3 ? Yellow : Amber;
                Vector2 q = P(d.p);
                Seg1(q - new Vector2(1.5f, 0f) * s, q + new Vector2(1.5f, 0f) * s, A(col, Mathf.Lerp(0.9f, 0.15f, age / 40f)), 3f * s);
            }
            // who was firing on whom, over the last few seconds
            for (int j = Mathf.Max(0, upto - 2); j <= upto; j++)
                foreach (int code in samples[j].fire)
                {
                    int a = code & 0xff, b = (code >> 8) & 0xff, kind = code >> 16;
                    if (a >= Tracks.Count || b >= Tracks.Count) continue;
                    Color col = (HitKind)kind == HitKind.Ion ? Violet : (HitKind)kind == HitKind.Artillery || (HitKind)kind == HitKind.Nullifier || (HitKind)kind == HitKind.Penetrator ? Amber
                              : ShotColor(Tracks[a]);
                    Vector3 pa = Vector3.Lerp(cur.pos[a], next.pos[a], f), pb = Vector3.Lerp(cur.pos[b], next.pos[b], f);
                    Dashed(P(pa), P(pb), A(col, 0.45f), 1f, 4f * s, 4f * s);
                }
            for (int i = 0; i < Tracks.Count; i++)
            {
                var k = Tracks[i];
                Vector3 p = cur.state[i] != 0 ? cur.pos[i] : Vector3.Lerp(cur.pos[i], next.pos[i], f);
                Vector3 v = cur.state[i] == 0 && next.t > cur.t ? (next.pos[i] - cur.pos[i]) / (next.t - cur.t) : Vector3.zero;
                Icon(P(p), k, cur.state[i] != 0, cur.state[i] == 2, Mathf.Lerp(cur.hull[i], next.hull[i], f), labels, fs, false, v, P);
            }
        }

        void Callouts(Func<Vector3, Vector2> P, Rect inner)
        {
            int n = 0;
            foreach (var e in Keys)
            {
                n++;
                if (e.t > replayT) break;
                Vector2 p = P(e.pos);
                float age = (replayT - e.t) / Mathf.Max(1f, replaySpeed);     // real seconds since it came up
                float pulse = 1f + 0.25f * Mathf.Sin(Time.time * 6f);
                clip = inner;
                Circle(p, 18f * s * (age < 3.5f ? pulse : 0.7f), A(Yellow, age < 3.5f ? 0.95f : 0.4f), 1.5f * s, 32);
                Flush();
                Tx(new Rect(p.x - 8f * s, p.y - 32f * s, 30f * s, 16f * s), $"<b>{n}</b>", 13, Yellow);
                if (age > 4.5f) continue;
                float a = Mathf.Clamp01(age / 0.3f) * Mathf.Clamp01((4.5f - age) / 0.6f);
                float bw = 430f * s, bh = 62f * s;
                var box = new Rect(Mathf.Clamp(p.x + 40f * s, inner.x + 8f * s, inner.xMax - bw - 8f * s), Mathf.Clamp(p.y - 90f * s, inner.y + 8f * s, inner.yMax - bh - 8f * s), bw, bh);
                Hud.Fill(box, new Color(0.02f, 0.06f, 0.08f, 0.92f * a));
                Hud.Outline(box, Mathf.Max(1f, s), A(Yellow, a));
                Seg1(p, new Vector2(box.x, box.y + bh * 0.5f), A(Yellow, a * 0.8f), 1.2f * s);
                Flush();
                Tx(new Rect(box.x + 10f * s, box.y + 6f * s, bw - 20f * s, 20f * s), $"{n}. T+{Clk(e.t)}  {e.title}", 15, A(Yellow, a), true);
                var st = St(13);
                st.wordWrap = true;
                st.normal.textColor = A(Cyan, a);
                GUI.Label(new Rect(box.x + 10f * s, box.y + 27f * s, bw - 20f * s, bh - 30f * s), e.text, st);
                st.wordWrap = false;
            }
        }

        void Legend(Rect inner)
        {
            float x = inner.x + 10f * s, y = inner.yMax - 92f * s;
            Hud.Fill(new Rect(x - 4f * s, y - 4f * s, 300f * s, 88f * s), new Color(0f, 0.02f, 0.04f, 0.75f));
            Tx(x, y, ThreeWay ? $"<color=#{Hex(Blue)}>CIRCLE</color> FRIGATE  <color=#{Hex(Red)}>DIAMOND</color> {SideNames[1]}  <color=#{Hex(Magenta)}>DIAMOND</color> {SideNames[2]}"
                              : $"<color=#{Hex(Blue)}>CIRCLE</color> FRIGATE   <color=#{Hex(Red)}>DIAMOND</color> VRASK", 12, Dim);
            Tx(x, y + 16f * s, $"<color=#{Hex(new Color(0.75f, 0.95f, 1f))}>TRACER</color> GUNS  <color=#{Hex(Violet)}>ION</color>  <color=#{Hex(Amber)}>ARTILLERY</color>", 12, Dim);
            Tx(x, y + 32f * s, $"<color=#{Hex(Red)}>CHEVRON</color> MISSILE  <color=#ff66d9>POD</color>  <color=#{Hex(Yellow)}>NUKE</color>", 12, Dim);
            Tx(x, y + 48f * s, $"<color=#{Hex(Cyan)}>MAIN BEAM</color>  <color=#{Hex(Green)}>TRACTOR</color>  <color=#{Hex(Red)}>PD</color>", 12, Dim);
            Tx(x, y + 64f * s, $"<color=#{Hex(Yellow)}>[ ]</color> BLUE TARGET   - - RANGE KM", 12, Dim);
        }

        // ---------------------------------------------------------------- (b) the track table

        string Tactic(Track k)
        {
            if (k.dead) return k.captured ? "CAPTURED" : "DESTROYED";
            if (k.friendly)
            {
                string d = k.pilot != null ? k.pilot.Doing : "";
                // (a section's: why she has her target too)
                if (k.pilot != null && k.pilot.section && k.pilot.Why.StartsWith("COVER")) d += " / " + k.pilot.Why;
                if (k.ship != null && k.ship.Clamped) return "CLAMPED BY FANG";
                if (k.ship != null && k.ship.Tractored) return "HELD: TRACTOR / " + d;
                if (k.boarded) return "BOARDED / " + d;
                return d;
            }
            string role = k.cls + ": ";
            if (k.combat != null && k.combat.Current == CombatPilot.State.Lurk) return role + "LURK";
            if (k.combat != null && k.combat.Current == CombatPilot.State.Hide) return role + "WITHDRAW";
            if (k.snare != null)
            {
                if (k.tractor != null && k.tractor.Locked) return role + "TRACTOR HOLD";
                if (k.snare.BreakingOff) return role + "BREAK OFF";
                switch (k.snare.Current)
                {
                    case SnareTactics.Duty.Disable: return role + "DISABLE (ION)";
                    case SnareTactics.Duty.Hold: return role + "TRACTOR HOLD";
                    case SnareTactics.Duty.Solo: return role + "SOLO ATTACK";
                    default: return role + "STAND OFF";
                }
            }
            if (k.fang != null)
                switch (k.fang.Current)
                {
                    case FangTactics.Phase.Approach: return role + "APPROACH";
                    case FangTactics.Phase.Run: return role + "BOARDING RUN";
                    case FangTactics.Phase.Clamped: return role + "CLAMPED";
                    case FangTactics.Phase.Extract: return role + "EXTRACT";
                    default: return role + "HOLD BACK";
                }
            if (k.maw != null)
            {
                if (k.maw.FallingBack) return role + "FALL BACK";
                if (k.maw.Target != null && k.maw.Spotted) return role + "BOMBARD";
                if (k.maw.Target != null) return role + "HOLD (NO SPOTTER)";
                return role + "STATION";
            }
            if (k.skink != null)
            {
                string pd = k.pd != null && k.pd.EngagedTarget != null ? " / PD" : "";
                switch (k.skink.Current)
                {
                    case SkinkTactics.Duty.Picket: return role + "PICKET" + pd;
                    case SkinkTactics.Duty.Intercept: return role + "INTERCEPT" + pd;
                    case SkinkTactics.Duty.Pursue: return role + "PURSUE" + pd;
                    case SkinkTactics.Duty.Patrol: return role + "PATROL" + pd;
                    default: return role + "SCREEN" + pd;
                }
            }
            return role + "ENGAGE";
        }

        void Table(Rect r)
        {
            var inner = Panel(r, "TRACK TABLE", string.Join("   ", Enumerable.Range(0, Sides).Select(x => $"{SideNames[x]} {SideLeft(x)}/{SideCount(x)}")));
            float x = inner.x + 8f * s, y = inner.y + 6f * s, rh = Mathf.Min(26f * s, (inner.height - 34f * s) / Mathf.Max(1, Tracks.Count + 1));
            float[] cw = { 50f, 150f, 82f, 62f, 62f, 66f, 250f, 140f };
            string[] head = { "TRK", "NAME", "CLASS", "HULL", "SHLD", "RNG KM", "TACTIC / DOING", "WEAPON" };
            float total = cw.Sum();
            float k1 = (inner.width - 16f * s) / (total * s);
            float cx = x;
            for (int i = 0; i < head.Length; i++) { Tx(new Rect(cx, y, cw[i] * s * k1, rh), head[i], 13, Dim, true); cx += cw[i] * s * k1; }
            y += rh;
            Hud.Fill(new Rect(inner.x + 4f * s, y - 3f * s, inner.width - 8f * s, Mathf.Max(1f, s)), A(Frame, 0.6f));
            var alive = Tracks.Where(t => !t.dead).ToList();
            Vector3 cen = alive.Count > 0 ? alive.Aggregate(Vector3.zero, (a, t) => a + t.pos) / alive.Count : Vector3.zero;
            var tgt = CommonTarget();
            foreach (var k in Tracks)
            {
                if (k == tgt) Hud.Fill(new Rect(inner.x + 4f * s, y - 1f * s, inner.width - 8f * s, rh), new Color(1f, 0.95f, 0.45f, 0.08f));
                Color col = k.dead ? Grey : SideColor(k);
                Color hc = k.dead ? Grey : k.hull > 0.6f ? Green : k.hull > 0.3f ? Amber : Red;
                string weapon = k.dead ? "" : Time.time - k.weaponAt < 4f ? k.weapon : "-";
                string[] cells =
                {
                    k.id, k.name, k.cls, k.dead ? "--" : $"{k.hull * 100f:F0}%", k.dead || k.shield == null ? "--" : $"{k.shieldF * 100f:F0}%",
                    $"{Vector3.Distance(k.pos, cen) / 1000f:F1}", Tactic(k), weapon,
                };
                Color[] cc = { col, col, A(col, 0.8f), hc, k.shieldF > 0.05f ? Cyan : Grey, Dim, k.dead ? Grey : Green, Amber };
                cx = x;
                for (int i = 0; i < cells.Length; i++) { Tx(new Rect(cx, y, cw[i] * s * k1 - 4f * s, rh), cells[i], 14, cc[i], i == 0); cx += cw[i] * s * k1; }
                y += rh;
            }
        }

        // ---------------------------------------------------------------- (c) the engagement log

        void LogPanel(Rect r)
        {
            var inner = Panel(r, "WEAPONS / ENGAGEMENT LOG", $"{lines.Count} ENTRIES");
            float lh = 19f * s;
            int n = Mathf.Max(1, Mathf.FloorToInt((inner.height - 10f * s) / lh));
            int from = Mathf.Max(0, lines.Count - n);
            float y = inner.y + 5f * s;
            for (int i = from; i < lines.Count; i++)
            {
                var l = lines[i];
                bool fresh = i >= lines.Count - 2 && running;
                Tx(new Rect(inner.x + 10f * s, y, 70f * s, lh), Clk(l.t), 14, Dim);
                Tx(new Rect(inner.x + 72f * s, y, inner.width - 82f * s, lh), l.text, 14, fresh ? Color.white : l.c, fresh);
                y += lh;
            }
        }

        // ---------------------------------------------------------------- results, analysis, end

        void Results(Rect r)
        {
            var inner = Panel(r, $"ENGAGEMENT SUMMARY  //  {place}", $"DURATION {Clk(Duration)}");
            float x = inner.x + 30f * s, y = inner.y + 20f * s;
            int ws = Array.IndexOf(SideNames, Winner);
            Color wc = ws >= 0 && ws < Sides ? ColorOf(ws) : Amber;
            string head = Winner == "BLUE" ? "BLUE FORCE VICTORY" : ThreeWay && ws > 0 ? $"{Winner} VICTORY" : Winner == "RED" ? "VRASK VICTORY" : "NO DECISION";
            Tx(new Rect(x, y, inner.width - 60f * s, 50f * s), head, 40, wc, true);
            Tx(new Rect(x, y + 50f * s, inner.width - 60f * s, 26f * s), Outcome, 20, Cyan);
            y += 96f * s;
            float colW = (inner.width - 60f * s) / Sides;
            void Side(float sx, int side)
            {
                var all = Tracks.Where(k => k.side == side).ToList();
                Color c = ColorOf(side);
                float yy = y;
                Tx(new Rect(sx, yy, colW, 26f * s), side == 0 ? $"BLUE FORCE  //  {all.Count} FRIGATES" : ThreeWay ? $"{SideNames[side]}  //  {all.Count} SHIPS" : $"RED FORCE  //  {all.Count} VRASK", 20, c, true);
                yy += 32f * s;
                string[] rows =
                {
                    $"COMMITTED        {all.Count}",
                    $"SURVIVING        {all.Count(k => !k.dead)}",
                    $"DESTROYED        {all.Count(k => k.dead && !k.captured)}",
                    $"CAPTURED         {all.Count(k => k.captured)}",
                    $"DAMAGE DEALT     {K(all.Sum(k => k.dealt))}",
                    $"DAMAGE TAKEN     {K(all.Sum(k => k.taken))}  (HULL {K(all.Sum(k => k.hullTaken))})",
                    $"KILLS            {all.Sum(k => k.kills)}",
                };
                foreach (var row in rows) { Tx(new Rect(sx, yy, colW, 24f * s), row, 17, Cyan); yy += 24f * s; }
            }
            for (int side = 0; side < Sides; side++) Side(x + side * colW, side);
            y += 32f * s + 7 * 24f * s + 20f * s;
            Hud.Fill(new Rect(inner.x + 20f * s, y - 8f * s, inner.width - 40f * s, Mathf.Max(1f, s)), Frame);
            Tx(new Rect(x, y, 600f * s, 24f * s), "KILLS BY SHIP", 17, Cyan, true);
            y += 28f * s;
            float[] cw = { 60f, 170f, 100f, 150f, 80f, 130f, 130f, 80f };
            string[] hd = { "TRK", "NAME", "CLASS", "STATUS", "KILLS", "DEALT", "TAKEN", "HULL" };
            int most = Enumerable.Range(0, Sides).Max(x => SideCount(x));
            float rowH = Mathf.Min(24f * s, (inner.yMax - y - 10f * s) / Mathf.Max(1, most + 1));
            float half = (inner.width - 60f * s) / Sides;
            float k1 = half / (cw.Sum() * s);
            for (int side = 0; side < Sides; side++)
            {
                float sx = x + side * half, yy = y, cx = sx;
                for (int i = 0; i < hd.Length; i++) { Tx(new Rect(cx, yy, cw[i] * s * k1, rowH), hd[i], 13, Dim, true); cx += cw[i] * s * k1; }
                yy += rowH;
                foreach (var k in Tracks.Where(t => t.side == side))
                {
                    Color col = k.dead ? Grey : SideColor(k);
                    string[] cells = { k.id, k.name, k.cls, k.dead ? (k.captured ? "CAPTURED" : $"LOST {Clk(k.diedAt)}") : "SURVIVED", $"{k.kills}", K(k.dealt), K(k.taken), k.dead ? "--" : $"{k.hull * 100f:F0}%" };
                    cx = sx;
                    for (int i = 0; i < cells.Length; i++) { Tx(new Rect(cx, yy, cw[i] * s * k1 - 4f * s, rowH), cells[i], 14, i == 4 && k.kills > 0 ? Yellow : col, i == 0); cx += cw[i] * s * k1; }
                    yy += rowH;
                }
            }
        }

        void AnalysisPanel(Rect r)
        {
            var inner = Panel(r, "POST-ACTION ANALYSIS", "WHAT HAPPENED");
            float y = inner.y + 10f * s, lh = Mathf.Min(30f * s, (inner.height - 20f * s) / Mathf.Max(1, Analysis.Count));
            var st = St(15);
            foreach (var a in Analysis)
            {
                string head = a.Length > 13 ? a.Substring(0, 13) : a;
                string body = a.Length > 13 ? a.Substring(13) : "";
                Tx(new Rect(inner.x + 12f * s, y, 140f * s, lh), head, 15, Dim, true);
                st.wordWrap = true;
                st.normal.textColor = Cyan;
                GUI.Label(new Rect(inner.x + 150f * s, y, inner.width - 160f * s, lh), body, st);
                st.wordWrap = false;
                y += lh;
            }
        }

        void KeyPanel(Rect r)
        {
            var inner = Panel(r, "KEY EVENTS", $"T+{Clk(replayT)}");
            float y = inner.y + 8f * s, lh = Mathf.Min(48f * s, (inner.height - 16f * s) / Mathf.Max(1, Keys.Count));
            int n = 0;
            foreach (var e in Keys)
            {
                n++;
                bool past = e.t <= replayT;
                bool now = past && replayT - e.t < 4.5f * Mathf.Max(1f, replaySpeed);
                Color c = now ? Yellow : past ? Cyan : A(Dim, 0.6f);
                if (now) Hud.Fill(new Rect(inner.x + 4f * s, y - 2f * s, inner.width - 8f * s, lh - 2f * s), new Color(1f, 0.95f, 0.45f, 0.08f));
                Tx(new Rect(inner.x + 12f * s, y, inner.width - 24f * s, 20f * s), $"{n}. T+{Clk(e.t)}  {e.title}", 15, c, true);
                Tx(new Rect(inner.x + 34f * s, y + 19f * s, inner.width - 46f * s, 20f * s), e.text, 13, past ? A(Cyan, 0.75f) : A(Dim, 0.5f));
                y += lh;
            }
        }

        void EndCard(Rect r)
        {
            Hud.Fill(r, Bg);
            Banner(new Rect(0f, 0f, r.width, 22f * s));
            clip = r;
            Vector2 c = r.center;
            // a faint scope behind the title
            for (int i = 1; i <= 4; i++) Circle(c, i * 110f * s, A(Green, 0.12f), 1f, 96);
            for (int deg = 0; deg < 360; deg += 10)
            {
                float a = deg * Mathf.Deg2Rad;
                Vector2 d = new Vector2(Mathf.Sin(a), -Mathf.Cos(a));
                Seg1(c + d * 440f * s, c + d * (deg % 30 == 0 ? 425f : 433f) * s, A(Cyan, 0.4f), 1f);
            }
            float sweep = Time.time * 60f * Mathf.Deg2Rad;
            Seg1(c, c + new Vector2(Mathf.Sin(sweep), -Mathf.Cos(sweep)) * 440f * s, A(Green, 0.35f), 2f * s);
            Flush();
            Tx(new Rect(0f, c.y - 90f * s, r.width, 60f * s), "BATTLECRUISER 4000 BC", 52, Cyan, true, TextAnchor.MiddleCenter);
            Tx(new Rect(0f, c.y - 24f * s, r.width, 30f * s), $"CIC RECORD  //  ACTION AT {place}  //  {Clk(Duration)}", 20, Dim, false, TextAnchor.MiddleCenter);
            int we = Array.IndexOf(SideNames, Winner);
            Tx(new Rect(0f, c.y + 14f * s, r.width, 30f * s), Outcome, 20, we >= 0 && we < Sides ? ColorOf(we) : Amber, true, TextAnchor.MiddleCenter);
            if (Mathf.Repeat(Time.time, 1.2f) < 0.8f)
                Tx(new Rect(0f, c.y + 70f * s, r.width, 30f * s), "END OF RECORD", 18, Green, true, TextAnchor.MiddleCenter);
        }
    }
}
