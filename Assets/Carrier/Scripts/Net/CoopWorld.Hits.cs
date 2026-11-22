using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarrierGame.Demo
{
    /// <summary>Where a hit goes in co-op: applied here, or sent to the game that owns what was hit.</summary>
    public partial class CoopWorld
    {
        // ------------------------------------------------------------------ hits

        /// <summary>Every hit comes here first (Health.Intercept): it is taken (true) when it
        /// belongs to another player's game.</summary>
        bool Intercept(Health h, float amount, Vector3 point, HitKind kind)
        {
            if (!On || me == null) return false;
            var src = Health.Source;
            var srcRoot = src != null ? src.root : null;
            bool fromMe = srcRoot != null && srcRoot == me.transform.root;
            bool fromPlayers = fromMe || (srcRoot != null && remoteByRoot.ContainsKey(srcRoot));
            // another player's ship: the host's enemies' hits go to that player (no friendly fire)
            if (remoteByRoot.TryGetValue(h.transform.root, out var rp))
            {
                if (!fromPlayers && IsHost && !UnknownThere(rp, srcRoot, kind))
                {
                    var np = ToNet(point);
                    Double3 p = np.p;
                    var (kd, kx) = DamageTable.ToWire(kind);
                    Client.Send(new HitPlayerMsg { to = rp.Id, a = amount, kd = kd, kx = kx, cp = (int)ShipSystems.AimOf(srcRoot), rf = np.rf, x = p.x, y = p.y, z = p.z });
                    HitsSentToPlayers++;
                }
                return true;
            }
            // my ship: nothing from another player's (shown) shots
            if (h == myHealth) return fromPlayers && !fromMe;
            // an enemy whose real self is in the host's game: my hits go there
            if (puppeting && entByHealth.TryGetValue(h, out var e))
            {
                // (its shield, as the host has it, shows the hit here: the host takes it)
                if (e.shields != null && e.shields.Length > 0 && e.shields[0] != null && e.shields[0].Up)
                    e.shields[0].ShowHit(point, kind);
                if (fromMe)
                {
                    var np = ToNet(point);
                    Double3 p = np.p;
                    var (kd, kx) = DamageTable.ToWire(kind);
                    Client.Send(new HitEnemyMsg { tg = e.id, a = amount, kd = kd, kx = kx, cp = (int)ShipSystems.AimOf(srcRoot), rf = np.rf, x = p.x, y = p.y, z = p.z });
                    HitsSentToHost++;
                }
                return true;
            }
            return false;
        }

        /// <summary>Hits on another player's ship dropped because its game is older and hasn't
        /// got what fired them (a Greys or Empire ship, an enemy made during play).</summary>
        public int HitsDroppedForOlder { get; private set; }
        readonly HashSet<int> toldOlder = new HashSet<int>();

        /// <summary>A hit from `src` on `rp`'s ship that its older game couldn't show (it hasn't
        /// got the ship that fired it): dropped, and the host told once.</summary>
        bool UnknownThere(RemotePlayer rp, Transform src, HitKind kind = HitKind.Projectile)
        {
            // (an EMP to a game that would read it as a beam's hit: ten times the hull damage)
            bool emp = kind == HitKind.Emp && rp != null && rp.ContentVersion < CoopWire.ContentEmp;
            if (rp == null || (!emp && (rp.ContentVersion >= CoopWire.Content || !NewerThan(src, rp.ContentVersion)))) return false;
            HitsDroppedForOlder++;
            if (toldOlder.Add(rp.Id))
                Notices.Post($"{rp.Name}'s game is older: the Greys, the Empire and launched drones aren't in it, and can't hit them");
            return true;
        }

        /// <summary>Something an older game (of content `cv`) hasn't got: an enemy made during
        /// play, or a ship of a faction newer than the Vrask.</summary>
        bool NewerThan(Transform src, int cv)
        {
            // (the factions and enemies made during play came with content 1)
            if (cv >= 1 || src == null) return false;
            var e = EntOf(src);
            if (e != null && !string.IsNullOrEmpty(e.key)) return true;
            var f = FactionShip.Of(src);
            return f != null && f.FactionName != VraskShip.Faction;
        }
    }
}
