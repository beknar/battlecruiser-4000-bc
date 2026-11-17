using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Credits passed between co-op games: the AI wingman hands what it has earned to a player
    /// ("Wingman, give me 500 credits"). The giver's hold pays first and the gift goes to the one
    /// player's game, which takes it only from an AI player (CoopBot), adds it to the hold of the
    /// ship flown there, says so and answers with a receipt. A gift with no receipt in
    /// ReceiptWait seconds (the player left, their game had no ship) goes back into the giver's hold;
    /// a game that only sees a gift later than TakeWithin seconds after it was sent (its tab in the
    /// background, a level loading) refuses it, so it is never both refunded and received.
    /// </summary>
    public static class CoopGifts
    {
        public const float ReceiptWait = 10f, TakeWithin = 6f;

        /// <summary>Credits received this session (the self-tests read it).</summary>
        public static float Received { get; private set; }

        class Pending { public int id, to; public float credits, until; public CargoHold from; }
        static readonly List<Pending> pending = new List<Pending>();
        static int lastId;

        /// <summary>Gifts sent and not yet received.</summary>
        public static int Outstanding => pending.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Received = 0f; pending.Clear(); lastId = 0; CoopClient.Message -= OnMessage; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Listen() { CoopClient.Message -= OnMessage; CoopClient.Message += OnMessage; }

        /// <summary>Pay `credits` out of `from` to player `to`: false (and nothing paid) when
        /// there aren't enough, or there's no game to send them to.</summary>
        public static bool Give(CargoHold from, int to, float credits)
        {
            credits = Mathf.Floor(credits);
            if (from == null || credits < 1f || !CoopClient.Active || to <= 0 || to == CoopClient.Instance.MyId) return false;
            if (!from.Spend(credits)) return false;
            var g = new Pending { id = ++lastId, to = to, credits = credits, until = Time.realtimeSinceStartup + ReceiptWait, from = from };
            pending.Add(g);
            CoopClient.Instance.Send(new GiftMsg { t = CoopWire.Gift, to = to, a = credits, gid = g.id, ts = CoopClient.Instance.ServerNow });
            return true;
        }

        /// <summary>Gifts that were never received go back to the giver (called every frame by the bot).</summary>
        public static void Tick()
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var g = pending[i];
                if (Time.realtimeSinceStartup < g.until) continue;
                pending.RemoveAt(i);
                if (g.from != null) g.from.Earn(g.credits);
                Debug.LogWarning($"[CoopGifts] no receipt for {g.credits:F0} cr to player {g.to}: refunded");
            }
        }

        static void OnMessage(CoopMsg m)
        {
            var c = CoopClient.Instance;
            if (c == null || m.from == c.MyId) return;
            if (m.t == CoopWire.GiftReceipt)
            {
                pending.RemoveAll(g => g.id == m.gid && g.to == m.from);
                return;
            }
            if (m.t != CoopWire.Gift) return;
            if (!(m.a >= 1f) || m.a > 1e9f) return;
            // (only from an AI player: one the server let in with an AI key; on an open server,
            // where nobody has a key, one that says it is a bot)
            var giver = c.Players.Find(p => p != null && p.id == m.from);
            bool keyed = c.Players.Exists(p => p != null && p.ai);
            if (giver == null || !(giver.ai || (!keyed && CoopBot.IsBot(giver))))
            {
                Debug.LogWarning($"[CoopGifts] a gift from player {m.from}, who isn't an AI player: refused");
                return;
            }
            // (too late: the giver has had it back, or soon will)
            if (m.ts > 0 && c.ServerNow - m.ts > TakeWithin)
            {
                Debug.LogWarning($"[CoopGifts] {m.a:F0} cr from {giver.name} came {c.ServerNow - m.ts:F1} s late: refused (it goes back to them)");
                return;
            }
            var demo = Object.FindAnyObjectByType<CarrierDemoInput>();
            var hold = demo != null && demo.carrier != null ? demo.carrier.GetComponent<CargoHold>() : null;
            if (hold == null) { Debug.LogWarning($"[CoopGifts] {m.a:F0} cr from {giver.name}, but no hold to put them in"); return; }
            hold.Earn(m.a);
            Received += m.a;
            c.Send(new GiftMsg { t = CoopWire.GiftReceipt, to = m.from, a = m.a, gid = m.gid });
            Notices.Post($"{giver.name} transferred {m.a:N0} cr to you");
            Debug.Log($"[CoopGifts] {m.a:F0} cr from {giver.name}");
        }
    }
}
