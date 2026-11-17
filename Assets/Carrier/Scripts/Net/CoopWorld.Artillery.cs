using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Artillery and the Vrask's strikes from orbit in co-op:
    ///
    /// - A shell (ArtilleryShell) fired by one of the host's Maws is shown in the other games: a
    ///   fire message of a new kind (CoopWire.Shell: {src, pf, x, y, z, rf} from where it was
    ///   fired, {ax, ay, az, arf} where it is going, its kind kx) launches a copy with no damage
    ///   there, on the same flight, that bursts where the real one does. The real blast's hits on
    ///   other players' ships go to their games as any of the host's hits do (HitPlayer). A game
    ///   from before shells skips the kind.
    /// - A nuclear missile is a missile: it goes with the missiles (PrefabOf finds it).
    /// - A strike from orbit by the Vrask siege (VraskBombardment.Struck) is sent to every other
    ///   game in the room (CoopWire.Strike: {body, u, v, radius, weapon} and the rest of the
    ///   order), which applies it to its own world: the city is ruined in every game, and each
    ///   saves it. Only the host's siege raids (a guest's copy of the group is a puppet).
    /// </summary>
    public partial class CoopWorld
    {
        /// <summary>For the self-test: shells sent and shown; strikes sent and applied.</summary>
        public int ShellsSent { get; private set; }
        public int ShellsShown { get; private set; }
        public int StrikesSent { get; private set; }
        public int StrikesTaken { get; private set; }

        void HookArtillery(bool on)
        {
            if (on)
            {
                ArtilleryShell.Spawned += OnShell;
                VraskBombardment.Struck += OnStrike;
                return;
            }
            ArtilleryShell.Spawned -= OnShell;
            VraskBombardment.Struck -= OnStrike;
        }

        void OnShell(ArtilleryShell s)
        {
            if (s == null) return;
            s.damage *= EnemyFire(s.owner);
            if (!On || s.damage <= 0f) return;            // (a shown one isn't news)
            string src = SourceOf(s.owner);
            if (src == null) return;
            var np = ToNet(s.transform.position);
            var na = ToNet(s.Aim);
            Client.Send(new ShellMsg
            {
                src = src, k = CoopWire.Shell, pf = BaseName(s.name), rf = np.rf, x = np.p.x, y = np.p.y, z = np.p.z,
                arf = na.rf, ax = na.p.x, ay = na.p.y, az = na.p.z, kx = (int)s.kind + 1,
            });
            ShellsSent++;
        }

        /// <summary>Another game's shell, for show here (no damage).</summary>
        void ShowShell(Transform ship, CoopMsg m)
        {
            var prefab = PrefabOf<ArtilleryShell>(ship, m.pf);
            if (prefab == null) return;
            Vector3 at = FromNet(m.rf, new Double3(m.x, m.y, m.z)), aim = FromNet(m.arf, new Double3(m.ax, m.ay, m.az));
            var kind = m.kx > 0 && m.kx <= DamageTable.Count ? (HitKind)(m.kx - 1) : HitKind.Artillery;
            ArtilleryShell.Launch(prefab, at, aim, kind, ship, 0f);
            ShellsShown++;
            ShotsShown++;
        }

        void OnStrike(VraskBombardment.Order o)
        {
            if (!On || !IsHost) return;
            Client.Send(new StrikeMsg
            {
                body = o.body, place = o.place ?? "", weapon = o.weapon ?? "", u = o.u, v = o.v, radius = o.radius, cd = o.crater,
                rb = o.rebuildMinutes, cpb = o.perBuilding, pop = o.population,
            });
            StrikesSent++;
        }

        /// <summary>The host's siege struck a city: the same strike on this game's world.</summary>
        void TakeStrike(CoopMsg m)
        {
            if (string.IsNullOrEmpty(m.body) || m.radius <= 0f) return;
            var o = new VraskBombardment.Order
            {
                body = m.body, place = m.place, weapon = string.IsNullOrEmpty(m.weapon) ? "Vrask artillery" : m.weapon, u = m.u, v = m.v,
                radius = m.radius, crater = m.cd, rebuildMinutes = m.rb > 0f ? m.rb : OrbitalStrikes.BuildingRebuildMinutes,
                perBuilding = m.cpb > 0 ? m.cpb : OrbitalStrikes.CasualtiesPerBuilding, population = Mathf.Clamp01(m.pop),
            };
            var r = VraskBombardment.Apply(o, false);
            StrikesTaken++;
            // (near it, it is seen: the flash where it lands)
            var body = WarpPoint.All.Find(w => w != null && w.displayName == o.body);
            if (body != null && me != null && PlanetLanding.Instance != null && !PlanetLanding.Busy)
            {
                Vector3 ground = OrbitalStrikes.GroundPoint(body, o.u, o.v);
                if (Vector3.Distance(me.transform.position, ground) < 2500000f)
                    StrikeFx.Flash(ground, o.radius * (o.weapon.Contains("nuclear") ? 14f : 6f), new Color(0.8f, 1f, 0.6f), null);
            }
            if (r.buildings > 0 || r.units > 0)
                Notices.Post($"{(string.IsNullOrEmpty(o.place) ? o.body : o.place)} struck from orbit by the Vrask: {r.units} defences and {r.buildings} buildings destroyed, {r.casualties} killed");
        }
    }
}
