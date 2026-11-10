using System;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Keeps a client's clock in step with the server's from ping round trips. The shortest round
    /// trips give the truest clock, so a pong only moves the clock when its round trip is close to
    /// the best seen. The best is let go slowly (a few per cent a ping), so a lasting rise in the
    /// latency (a LAN game moved to Wi-Fi, a busy link) is taken up within a minute or so instead
    /// of freezing the clock for good.
    /// </summary>
    public sealed class ClockSync
    {
        /// <summary>How much the best round trip is let go per pong.</summary>
        public const double Relax = 1.05;
        /// <summary>A pong whose round trip is within this factor of the best moves the clock.</summary>
        public const double Tolerance = 1.2;
        /// <summary>A clock further out than this (seconds) is set outright, not blended.</summary>
        public const double Jump = 1.0;

        /// <summary>Server time minus local time, seconds.</summary>
        public double Offset { get; private set; }
        /// <summary>The smoothed round trip, seconds (0 until the first pong).</summary>
        public double Rtt { get; private set; }
        /// <summary>The best round trip lately, seconds.</summary>
        public double BestRtt { get; private set; } = double.MaxValue;
        /// <summary>Has a pong come back yet?</summary>
        public bool Synced => BestRtt != double.MaxValue;

        public void Reset()
        {
            BestRtt = double.MaxValue;
            Rtt = 0;
        }

        /// <summary>The server's welcome: its clock, taken as it is (no round trip known yet).</summary>
        public void Welcome(double serverNow, double localNow) => Offset = serverNow - localNow;

        /// <summary>A pong: the local time the ping was sent, the server's time when it answered,
        /// and the local time now.</summary>
        public void Pong(double sentAt, double serverAt, double localNow)
        {
            double rtt = Math.Max(0.0, localNow - sentAt);
            Rtt = Rtt <= 0 ? rtt : Rtt * 0.8 + rtt * 0.2;
            if (Synced) BestRtt *= Relax;
            if (rtt > BestRtt * Tolerance) return;
            BestRtt = Math.Min(BestRtt, rtt);
            double offset = serverAt + rtt * 0.5 - localNow;
            Offset = Math.Abs(offset - Offset) > Jump ? offset : Offset * 0.7 + offset * 0.3;
        }
    }
}
