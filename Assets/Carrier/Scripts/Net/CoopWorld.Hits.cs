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
                if (!fromPlayers && IsHost)
                {
                    var np = ToNet(point);
                    Double3 p = np.p;
                    Client.Send(new HitPlayerMsg { to = rp.Id, a = amount, kd = (int)kind, rf = np.rf, x = p.x, y = p.y, z = p.z });
                    HitsSentToPlayers++;
                }
                return true;
            }
            // my ship: nothing from another player's (shown) shots
            if (h == myHealth) return fromPlayers && !fromMe;
            // an enemy whose real self is in the host's game: my hits go there
            if (puppeting && entByHealth.TryGetValue(h, out var e))
            {
                if (fromMe)
                {
                    var np = ToNet(point);
                    Double3 p = np.p;
                    Client.Send(new HitEnemyMsg { tg = e.id, a = amount, kd = (int)kind, rf = np.rf, x = p.x, y = p.y, z = p.z });
                    HitsSentToHost++;
                }
                return true;
            }
            return false;
        }
    }
}
