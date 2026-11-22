using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Boarding and point defence in co-op. The enemies are the host's, but a player's crew lives
    /// in that player's own game, so:
    ///
    /// - A boarding pod (or a Fang's ram) the host's game puts aboard another player's ship is
    ///   sent to that player's game (CoopWire.Board: {to, n}), which runs the fight (Boarders) and
    ///   decides the capture. Its ship's updates (CoopWire.Ship) then say how many boarders are
    ///   aboard (bd: the host's Fang holds on while they fight) and whether it was taken (cpt, with
    ///   dead: the other games show no explosion). The other players' copies of a ship can't die
    ///   (RemotePlayer: cannotDie), so only its own game ever captures it.
    /// - A missile or pod shot down by my point defence is told to the others (CoopWire.ShotDown:
    ///   {src, x, y, z, rf}), and each takes down its own copy of it: the real one on the host,
    ///   the copies shown elsewhere. So is a players' missile the host's enemies shoot down (a
    ///   Vrask Skink's point defence): the real one is in its player's game.
    /// </summary>
    public partial class CoopWorld
    {
        /// <summary>For the self-test: boardings sent to other players' games and taken from the
        /// host; shoot-downs told and taken.</summary>
        public int BoardsSent { get; private set; }
        public int BoardsTaken { get; private set; }
        public int ShotDownsSent { get; private set; }
        public int ShotDownsTaken { get; private set; }

        void HookBoarding(bool on)
        {
            if (on)
            {
                Boarders.SendAboard = SendBoard;
                Boarders.RemoteCount = RemoteBoarders;
                Missile.Downed += OnDowned;
                return;
            }
            if (Boarders.SendAboard == (System.Func<Transform, int, string, bool>)SendBoard) Boarders.SendAboard = null;
            if (Boarders.RemoteCount == (System.Func<Transform, int>)RemoteBoarders) Boarders.RemoteCount = null;
            Missile.Downed -= OnDowned;
        }

        /// <summary>Boarders getting aboard a ship: another player's is boarded in their game (the
        /// host tells it; true either way, so it isn't fought here).</summary>
        bool SendBoard(Transform ship, int n, string faction)
        {
            if (!On || ship == null || !remoteByRoot.TryGetValue(ship.root, out var rp) || rp == null) return false;
            if (IsHost)
            {
                Client.Send(new BoardMsg { to = rp.Id, n = n, fa = faction == VraskShip.Faction ? null : faction });
                BoardsSent++;
            }
            return true;
        }

        /// <summary>Boarders aboard another player's ship, as its game last said.</summary>
        int RemoteBoarders(Transform ship) =>
            ship != null && remoteByRoot.TryGetValue(ship.root, out var rp) && rp != null && !rp.Hidden ? rp.BoardersAboard : 0;

        /// <summary>The host's pods have got aboard my ship: the fight is mine.</summary>
        void TakeBoard(CoopMsg m)
        {
            if (me == null || myHealth == null || myHealth.Dead || m.n <= 0) return;
            BoardsTaken++;
            Boarders.Board(me.transform, m.n, Boarders.FactionOr(m.fa));
        }

        /// <summary>A missile or pod shot down by this game's own guns (mine, my fleet's): the others
        /// take theirs down. (One downed by another player's shown shots is theirs to say.)</summary>
        void OnDowned(Missile m, Transform by)
        {
            if (!On || m == null || by == null || me == null || remoteByRoot.ContainsKey(by.root)) return;
            string src = IdOf(m.owner);
            if (string.IsNullOrEmpty(src)) return;
            var np = ToNet(m.transform.position);
            Client.Send(new ShotDownMsg { src = src, rf = np.rf, x = np.p.x, y = np.p.y, z = np.p.z });
            ShotDownsSent++;
        }

        /// <summary>Another game shot one down: the nearest of that ship's missiles to where it was
        /// (within 500 m) goes down here too.</summary>
        void TakeShotDown(CoopMsg m)
        {
            var ship = Resolve(m.src);
            if (ship == null) return;
            Vector3 at = FromNet(m.rf, new Double3(m.x, m.y, m.z));
            Missile best = null;
            float bestD = 500f * 500f;
            foreach (var mi in Missile.Active)
            {
                if (mi == null || mi.owner == null || mi.owner.root != ship.root) continue;
                float d = (mi.transform.position - at).sqrMagnitude;
                if (d < bestD) { bestD = d; best = mi; }
            }
            if (best == null) return;
            ShotDownsTaken++;
            best.ShootDown(null);
        }
    }
}
