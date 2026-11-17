using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>Tractor beams in co-op. The host's beams hold ships in the host's game, but another
    /// player's ship is flown in that player's game, so the host tells that game (CoopWire.Tractor)
    /// when a hold starts, changes or ends, and every half second while it lasts; that game pulls
    /// its own ship, and lets go if it hasn't heard for 1.5 s. The other games show the beams on
    /// their copies of the host's ships, from the enemies' updates (CoopEntity.tr).</summary>
    public partial class CoopWorld
    {
        /// <summary>How long a hold lasts in the held ship's game without word from the host (s).</summary>
        public const float TractorTimeout = 1.5f;
        const float TractorRefresh = 0.5f;

        struct TractorSent { public int n; public bool held; public float at; }
        readonly Dictionary<int, TractorSent> tractorSent = new Dictionary<int, TractorSent>();

        /// <summary>For the self-test: holds sent to other players' games, and taken from the host.</summary>
        public int TractorsSent { get; private set; }
        public int TractorsTaken { get; private set; }

        /// <summary>The host: every other player's ship its beams hold, told to that player's game.</summary>
        void SendTractors()
        {
            foreach (var kv in remotes)
            {
                var r = kv.Value;
                int n = r != null ? TractorBeam.BeamsOn(r.transform) : 0;
                bool had = tractorSent.TryGetValue(kv.Key, out var last);
                if (n == 0)
                {
                    if (!had) continue;
                    tractorSent.Remove(kv.Key);
                    Client.Send(new TractorMsg { to = kv.Key, on = false });
                    TractorsSent++;
                    continue;
                }
                bool held = TractorBeam.Firm(r.transform);
                if (had && last.n == n && last.held == held && Time.unscaledTime - last.at < TractorRefresh) continue;
                var np = ToNet(TractorBeam.PullPoint(r.transform));
                Client.Send(new TractorMsg { to = kv.Key, on = true, n = n, held = held, rf = np.rf, x = np.p.x, y = np.p.y, z = np.p.z });
                tractorSent[kv.Key] = new TractorSent { n = n, held = held, at = Time.unscaledTime };
                TractorsSent++;
            }
            // (players who have gone)
            if (tractorSent.Count > 0)
                foreach (var id in new List<int>(tractorSent.Keys))
                    if (!remotes.ContainsKey(id)) tractorSent.Remove(id);
        }

        /// <summary>The host's beams on my ship, or letting go of it.</summary>
        void TakeTractor(CoopMsg m)
        {
            if (me == null) return;
            TractorsTaken++;
            if (!m.on) { me.Untractor(); return; }
            me.Tractor(FromNet(m.rf, new Double3(m.x, m.y, m.z)), Mathf.Max(1, m.n), m.held, TractorTimeout);
        }

        /// <summary>For the enemies' updates: the player whose ship an enemy's beam holds (1 + their
        /// id; 0: none, or not a player's ship).</summary>
        int TractorOf(Ent e)
        {
            var held = e.beam != null ? e.beam.Holding : null;
            if (held == null) return 0;
            if (me != null && held == me.transform) return 1 + Client.MyId;
            return remoteByRoot.TryGetValue(held, out var rp) && rp != null ? 1 + rp.Id : 0;
        }

        /// <summary>A guest: an enemy's beam shown on the ship the host's says it holds.</summary>
        void ShowTractor(Ent e, int tr)
        {
            if (e.beam == null) return;
            Transform on = null;
            if (tr > 0)
            {
                int id = tr - 1;
                if (me != null && id == Client.MyId) on = me.transform;
                else if (remotes.TryGetValue(id, out var rp) && rp != null && !rp.Hidden) on = rp.transform;
            }
            e.beam.NetShow(on);
        }
    }
}
