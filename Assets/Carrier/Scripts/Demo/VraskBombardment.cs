using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The Vrask siege of a city: their bombardment group (a Maw and its escorts, on station over
    /// the city and turning with its world, as the corsairs' strike satellites do) raids it from
    /// orbit every `intervalMinutes` of game time, whether or not anyone is near (on the game's
    /// clock, WorldState, and saved). Destroy the Maw and the raids stop till it is back (its
    /// Health's respawn); its clock starts again from then.
    ///
    /// A raid is a siege-breaker's: its shells (`shells` of them, each `shellRadius` wide) go for
    /// the fixed defences round the city first (gun batteries, missile sites, bases: the ground
    /// forces OrbitalStrikes knows of within `siegeReach`), the nearest first, then into the city
    /// itself, building by building. Rarely (`nukeChance` of a raid), and only at a fortified
    /// target (defences round it, or `fortified`), and only when its silos have been filled again
    /// since the last (`nukeRestockMinutes`), the Maw sends a nuclear missile down instead: on a
    /// world with air its blast covers the whole city (its layout's reach and some), every
    /// building is ruined for `nukeRebuildMinutes` (far longer than a kinetic strike's hour) and a
    /// share of its people are killed. All of it goes through OrbitalStrikes, the same as any
    /// strike from orbit, and is saved with the world.
    ///
    /// Co-op: the raids are the host's (this is turned off on a guest's copy of the group, as the
    /// enemies' are), and each strike is sent to the other games (Struck: CoopWorld's "strike"),
    /// which apply it to their own world (Apply), so every game has the city as it is.
    /// </summary>
    public class VraskBombardment : MonoBehaviour
    {
        [Tooltip("The settlement it besieges (its warp point's name): on a world with air.")]
        public string target = "Port Ochre";
        public float intervalMinutes = 120f;
        [Tooltip("Shells a raid.")]
        public int shells = 4;
        public float shellRadius = 500f;
        public float shellCrater = 30f;
        [Tooltip("How far round the city (m) its fixed defences are looked for.")]
        public float siegeReach = 3000f;
        [Tooltip("Counts as fortified whatever defences it has (its own aren't modelled as ground forces).")]
        public bool fortified;
        [Tooltip("The share of raids on a fortified target that end with a nuclear strike.")]
        [Range(0f, 1f)] public float nukeChance = 0.1f;
        [Tooltip("Game minutes before the silos have another after one is used.")]
        public float nukeRestockMinutes = 360f;
        public float nukeRebuildMinutes = 480f;
        public float nukeCrater = 90f;
        [Tooltip("The nuclear blast's reach past the city's farthest building (m).")]
        public float nukeMargin = 1000f;
        [Tooltip("People killed for each building a nuclear strike ruins, and the share of the rest of them.")]
        public int nukeCasualtiesPerBuilding = 40;
        [Range(0f, 1f)] public float nukePopulationShare = 0.5f;
        [Tooltip("Within this of the Maw (m), its strikes are seen.")]
        public float seenWithin = 2500000f;
        [Tooltip("The Maw: no raids while it is destroyed.")]
        public Health battery;

        public static readonly List<VraskBombardment> All = new List<VraskBombardment>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { All.Clear(); Struck = null; }

        /// <summary>One strike from orbit, as every game applies it (co-op sends it).</summary>
        public struct Order
        {
            public string body, place, weapon;
            public double u, v;
            public float radius, crater, rebuildMinutes, population;
            public int perBuilding;
        }

        /// <summary>Told of each strike a raid makes (co-op passes it to the other games).</summary>
        public static event System.Action<Order> Struck;

        public int Raids { get; private set; }
        public int NukesUsed { get; private set; }
        public OrbitalStrikes.Result LastShell { get; private set; }
        public OrbitalStrikes.Result LastNuke { get; private set; }
        /// <summary>What the last raid did (the self-test, the log).</summary>
        public string LastRaid { get; private set; } = "";
        /// <summary>The fixed defences its last raid found round the city, and destroyed.</summary>
        public int DefencesFound { get; private set; }
        public int DefencesDestroyed { get; private set; }

        public string Timer => "vrask-siege:" + target;
        string NukeTimer => "vrask-siege-nuke:" + target;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        void Start()
        {
            if (!WorldState.TryTimer(Timer, out _)) WorldState.SetTimer(Timer, WorldState.Clock + intervalMinutes * 60.0 * 0.75);
            if (battery != null)
            {
                battery.onRespawned ??= new UnityEngine.Events.UnityEvent();
                battery.onRespawned.AddListener(() => WorldState.SetTimer(Timer, WorldState.Clock + intervalMinutes * 60.0));
            }
        }

        /// <summary>When it raids next (game seconds), or never while the Maw is destroyed.</summary>
        public double NextAt => battery != null && battery.Dead ? double.PositiveInfinity
            : WorldState.TryTimer(Timer, out double at) ? at : WorldState.Clock + intervalMinutes * 60.0 * 0.75;

        /// <summary>A nuclear missile is aboard (the silos filled since the last).</summary>
        public bool NukeReady => !WorldState.TryTimer(NukeTimer, out double at) || WorldState.Clock >= at;

        void Update()
        {
            if (battery != null && battery.Dead) return;
            if (!WorldState.TryTimer(Timer, out _)) { WorldState.SetTimer(Timer, NextAt); return; }
            if (WorldState.Clock >= NextAt) RaidNow();
        }

        /// <summary>The settlement's place and its body (false: no such settlement on a world with air).</summary>
        public bool Site(out WarpPoint place, out WarpPoint body, out TileFeatures site)
        {
            body = null;
            site = null;
            place = WarpPoint.All.Find(w => w != null && w.displayName == target);
            return place != null && PlanetLanding.Instance != null && OrbitalStrikes.SiteOf(place, out body, out site) && HasAir(body);
        }

        /// <summary>A body with air (a planet: the moons and the fields have none; star_system.json's
        /// atmosphereScale).</summary>
        public static bool HasAir(WarpPoint body) => body != null && PlanetFlight.ScaleHeight(body) > 0f;

        /// <summary>A raid now (its time come, or the self-test). `nuke`: true, a nuclear strike
        /// whatever the odds (if it has one aboard; the self-test: `force`, even if not); false,
        /// none; null, as the odds fall.</summary>
        public void RaidNow(bool? nuke = null, bool force = false)
        {
            WorldState.SetTimer(Timer, WorldState.Clock + intervalMinutes * 60.0);
            if (!Site(out var place, out var body, out var site)) { LastRaid = $"no {target} on a world with air"; return; }
            Raids++;
            double su = site.su, sv = site.sv;
            var ship = PlayerShips.Primary;
            bool seen = ship != null && !PlanetLanding.Busy && Vector3.Distance(ship.position, transform.position) < seenWithin;

            // its fixed defences first (the guns, the missile sites, the bases), nearest the city first
            var found = new List<OrbitalStrikes.Defence>();
            OrbitalStrikes.Defences(body, su, sv, siegeReach, found);
            found.Sort((a, b) => Rank(a).CompareTo(Rank(b)) != 0 ? Rank(a).CompareTo(Rank(b)) : Dist(a, su, sv).CompareTo(Dist(b, su, sv)));
            DefencesFound = found.Count;
            bool fort = fortified || found.Count > 0;
            bool goNuke = nuke ?? (fort && Random.value < nukeChance);
            if (goNuke && !force && (!fort || !NukeReady)) goNuke = false;

            var shots = new List<Order>();
            if (goNuke)
            {
                float reach = OrbitalStrikes.SettlementReach(target) + nukeMargin;
                shots.Add(new Order
                {
                    body = body.displayName, place = target, weapon = "Vrask nuclear missile", u = su, v = sv,
                    radius = Mathf.Max(reach, shellRadius * 2f), crater = nukeCrater, rebuildMinutes = nukeRebuildMinutes,
                    perBuilding = nukeCasualtiesPerBuilding, population = nukePopulationShare,
                });
                NukesUsed++;
                WorldState.SetTimer(NukeTimer, WorldState.Clock + nukeRestockMinutes * 60.0);
            }
            else
            {
                int left = shells;
                for (int i = 0; i < found.Count && left > 0; i++, left--)
                    shots.Add(Shell(body, found[i].u, found[i].v));
                // then the city: at its buildings still standing, the nearest its middle first
                var targets = new List<(double u, double v)>();
                OrbitalStrikes.StandingBuildings(place, targets);
                targets.Sort((a, b) => Dist(a.u, a.v, su, sv).CompareTo(Dist(b.u, b.v, su, sv)));
                for (int i = 0; left > 0; left--)
                {
                    // (one shell's blast takes in its neighbours: the next aim is one it left standing)
                    while (i < targets.Count && shots.Exists(s => Dist(targets[i].u, targets[i].v, s.u, s.v) < (shellRadius * 0.8) * (shellRadius * 0.8))) i++;
                    if (i >= targets.Count) { shots.Add(Shell(body, su, sv)); continue; }
                    shots.Add(Shell(body, targets[i].u, targets[i].v));
                    i++;
                }
            }

            int units = 0, buildings = 0, killed = 0;
            System.Action land = () =>
            {
                foreach (var o in shots)
                {
                    var r = Apply(o, seen);
                    units += r.units;
                    buildings += r.buildings;
                    killed += r.casualties;
                    if (o.weapon.Contains("nuclear")) LastNuke = r; else LastShell = r;
                    Struck?.Invoke(o);
                }
                DefencesDestroyed = units;
                LastRaid = goNuke ? $"a nuclear strike on {target}: {buildings} buildings ruined, {killed} killed"
                                  : $"{shots.Count} shells on {target}: {units} defences destroyed, {buildings} buildings ruined, {killed} killed";
                if (buildings > 0 || units > 0 || seen)
                    Notices.Post(goNuke ? $"{target} destroyed by a Vrask nuclear strike from orbit: {buildings} buildings ruined, {killed} killed"
                                        : $"{target} bombarded from orbit by the Vrask: {units} defences and {buildings} buildings destroyed, {killed} killed");
            };
            if (!seen) { land(); return; }
            // seen: the shells (or the missile) streak down from the Maw, the damage as they land
            Vector3 from = body.transform.InverseTransformPoint(transform.position);
            var fireball = battery != null ? battery.deathEffect : null;
            for (int k = 0; k < shots.Count; k++)
            {
                var o = shots[k];
                bool last = k == shots.Count - 1, big = o.weapon.Contains("nuclear");
                Vector3 Ground() => OrbitalStrikes.GroundPoint(body, o.u, o.v);
                float fall = Vector3.Distance(transform.position, Ground());
                StrikeFx.Rod(() => body.transform.TransformPoint(from), Ground, Mathf.Clamp(fall / (big ? 4000f : 8000f), 3f, 30f) + k * 0.7f,
                             big ? new Color(1f, 0.95f, 0.8f) : new Color(0.6f, 1f, 0.45f), () =>
                {
                    StrikeFx.Flash(Ground(), big ? Mathf.Max(o.radius * 14f, 16000f) : o.radius * 6f, big ? new Color(1f, 0.9f, 0.7f) : new Color(0.65f, 1f, 0.5f), fireball, big ? 6f : 4f);
                    // (a nuclear burst in air raises its cloud, kilometres high: seen from orbit)
                    if (big) MushroomCloud.Rise(body, Ground(), o.radius);
                    if (last) land();
                });
            }
            SoundFX.Play(Sfx.Cannon, transform.position, 1f, 0.35f);
        }

        Order Shell(WarpPoint body, double u, double v) => new Order
        {
            body = body.displayName, place = target, weapon = "Vrask artillery", u = u, v = v, radius = shellRadius, crater = shellCrater,
            rebuildMinutes = OrbitalStrikes.BuildingRebuildMinutes, perBuilding = OrbitalStrikes.CasualtiesPerBuilding, population = 0f,
        };

        /// <summary>A strike applied to this game's world (a raid's own, or one another game sent):
        /// OrbitalStrikes.Hit, and for a nuclear one the share of the people left that it kills.</summary>
        public static OrbitalStrikes.Result Apply(Order o, bool seen)
        {
            var body = WarpPoint.All.Find(w => w != null && w.displayName == o.body);
            if (body == null || PlanetLanding.Instance == null) return default;
            var r = OrbitalStrikes.Hit(body, o.u, o.v, o.radius, o.crater, o.weapon, seen, o.rebuildMinutes, o.perBuilding);
            if (o.population > 0f && !string.IsNullOrEmpty(o.place))
            {
                float more = Mathf.Floor(IndustryLedger.Pool(o.place) * Mathf.Clamp01(o.population));
                if (more > 0f) { IndustryLedger.Lose(o.place, more); r.casualties += (int)more; }
            }
            return r;
        }

        // the siege breaker's order: guns and missile sites (and the launchers that move about),
        // then bases, sea bases and warships, then the rest (tanks, LAVs, submarines, patrols)
        static int Rank(OrbitalStrikes.Defence d)
        {
            switch (d.kind)
            {
                case UnitKind.Battery:
                case UnitKind.MissileSite:
                case UnitKind.MobileLauncher: return 0;
                case UnitKind.SeaBase:
                case UnitKind.Warship:
                case UnitKind.PadGunship:
                case UnitKind.PadFighter: return 1;
                case UnitKind.Tank:
                case UnitKind.Lav: return 2;
                default: return 2;
            }
        }

        static double Dist(OrbitalStrikes.Defence d, double u, double v) => Dist(d.u, d.v, u, v);
        static double Dist(double u0, double v0, double u1, double v1) { double du = u0 - u1, dv = v0 - v1; return du * du + dv * dv; }
    }
}
