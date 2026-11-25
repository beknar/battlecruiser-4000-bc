using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Cargo passed between co-op games, from one ship's hold to another's, as credits are
    /// (CoopGifts): ore by mineral, and anything else the hold keeps by its item id (refined goods,
    /// parts). Only between a person and an AI wingman (CoopBot), in the same level, within Reach
    /// (50 km) of each other.
    /// - "Wingman, give me your ore" (the platinum, 100 t of iron): the bot sends it.
    /// - "Wingman, take my ore" (the iron): the player's own game reads its own order as it goes
    ///   out on the comms and sends the cargo with it; G in the mining menu's hold rows hands a
    ///   mineral over. Nothing a bot sends can take cargo out of a player's hold.
    /// The sender takes the items out of its hold (Unload) and holds them ReceiptWait (10 s) for
    /// a receipt. The receiver refuses a message older than TakeWithin (6 s), loads what fits
    /// (Load) and answers with a receipt of what it loaded; the rest goes back into the sender's
    /// hold, and all of it when no receipt comes. A person's game saves the hold at once when
    /// cargo goes out and when any comes back or in (the hold is otherwise saved a few seconds
    /// after a change), so closing the game can't keep cargo that has gone. Both games run the
    /// refund (this object's Update).
    /// </summary>
    public class CoopCargo : MonoBehaviour
    {
        public const float ReceiptWait = CoopGifts.ReceiptWait, TakeWithin = CoopGifts.TakeWithin;
        /// <summary>How near the two ships must be (m).</summary>
        public const float Reach = 50000f;
        /// <summary>The receiver's reach: a little more, as the ships move while the message travels.</summary>
        public const float ReachReceived = Reach * 1.1f;

        /// <summary>A hold's cargo has come (from, what was loaded, what there was no room for and
        /// went back, and why none was taken: "" when it was taken).</summary>
        public static event Action<CoopPlayer, List<CoopItem>, List<CoopItem>, string> Arrived;
        /// <summary>Cargo sent has been answered (to whom, what they loaded, what came back into
        /// the hold, and why: "" a receipt with no room for the rest, "refused: ..." refused,
        /// "no answer" none in ReceiptWait).</summary>
        public static event Action<int, List<CoopItem>, List<CoopItem>, string> Returned;

        /// <summary>Tonnes (and parts) received this session (the self-tests read it).</summary>
        public static float Received { get; private set; }

        internal class Pending
        {
            public int id, to;
            public float until;
            public CargoHold from;
            public List<CoopItem> items;
        }
        static readonly List<Pending> pending = new List<Pending>();
        static int lastId;
        static CoopCargo instance;

        /// <summary>Cargo sent and not yet answered.</summary>
        public static int Outstanding => pending.Count;

        // (internal: the tests start from nothing waiting)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics()
        {
            Received = 0f;
            pending.Clear();
            lastId = 0;
            Arrived = null;
            Returned = null;
            instance = null;
            CoopClient.Message -= OnMessage;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Make()
        {
            CoopClient.Message -= OnMessage;
            CoopClient.Message += OnMessage;
            CoopChat.Received -= Said;
            CoopChat.Received += Said;
            if (instance != null) return;
            var go = new GameObject("Coop cargo");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<CoopCargo>();
        }

        void Update() => Tick(Time.realtimeSinceStartup);

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        // ------------------------------------------------------------ who may, and how much

        /// <summary>An AI player: one the server let in with an AI key, or on an open server
        /// (where nobody has a key) one that says it is a bot.</summary>
        public static bool IsAi(CoopPlayer p, List<CoopPlayer> players)
        {
            if (p == null) return false;
            bool keyed = players != null && players.Exists(x => x != null && x.ai);
            return p.ai || (!keyed && CoopBot.IsBot(p));
        }

        /// <summary>This game is an AI wingman's (CoopBot).</summary>
        static bool IAmAi => CoopBot.Instance != null;

        /// <summary>Why cargo may not pass ("" if it may): only between a person and an AI
        /// player, in the same level, with the other ship seen (`metres` below 0: not seen) and
        /// within `reach`, and (`age`, seconds since it was sent; below 0, not asked) no older
        /// than TakeWithin.</summary>
        public static string Refusal(bool fromAi, bool toAi, bool sameLevel, float metres, float reach = Reach, double age = -1)
        {
            if (fromAi == toAi) return fromAi ? "AI wingmen don't trade among themselves" : "cargo goes only to and from an AI wingman";
            if (!sameLevel) return "not in this part of space";
            if (age > TakeWithin) return $"it came {age.ToString("F1", CultureInfo.InvariantCulture)} s late";
            if (metres < 0f) return "can't see the other ship";
            if (metres > reach) return $"{CoopBot.HowFar(metres)} apart: within {reach / 1000f:F0} km to hand cargo over";
            return "";
        }

        /// <summary>What an order asks for, out of a hold: `amount` a number of tonnes (parts),
        /// "all", "half" (or "?", not said: null, with why "how much?"); `item` "ore" (every
        /// mineral aboard, the most valuable first), a mineral (its name or symbol as said, or
        /// its id), or another item's id. Null, and why, when there's none of it aboard.</summary>
        public static List<CoopItem> Wanted(CargoHold hold, string amount, string item, out string why)
        {
            why = "";
            if (hold == null) { why = "no cargo hold"; return null; }
            string aw = (amount ?? "").Trim().ToLowerInvariant();
            string what = (item ?? "").Trim();
            if (aw == "?") { why = "how much?"; return null; }
            float n = -1f;
            bool all = aw.Length == 0 || aw == "all" || aw == "everything", half = aw == "half";
            if (!all && !half && (!float.TryParse(aw.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out n) || !(n > 0f) || float.IsInfinity(n)))
            { why = "how much?"; return null; }
            var list = new List<CoopItem>();
            if (what.Length == 0 || what.Equals("ore", StringComparison.OrdinalIgnoreCase))
            {
                var ore = hold.Manifest();
                if (ore.Count == 0) { why = "no ore aboard"; return null; }
                float left = n;
                foreach (var (m, t) in ore)
                {
                    float a = all ? t : half ? t / 2f : Mathf.Min(t, left);
                    if (!all && !half) left -= a;
                    if (a > 0.005f) list.Add(new CoopItem { id = m.ToString(), a = a });
                    if (!all && !half && left <= 0.005f) break;
                }
                return list;
            }
            var table = IndustryTable.Get();
            string id = Minerals.TryParse(what, out var mineral) ? mineral.ToString() : what;
            if (table.Kind(id) == ItemKind.None) { why = $"don't know what {what} is"; return null; }
            float have = hold.Amount(id);
            if (have < 0.005f) { why = $"no {table.Name(id).ToLowerInvariant()} aboard"; return null; }
            float take = all ? have : half ? have / 2f : Mathf.Min(n, have);
            if (table.Part(id) != null) take = Mathf.Floor(take + 1e-4f);
            if (take <= 0f) { why = $"not a whole {table.Name(id).ToLowerInvariant()} to hand over"; return null; }
            list.Add(new CoopItem { id = id, a = take });
            return list;
        }

        /// <summary>Take the items out of a hold: what there was of each (none of what wasn't aboard).</summary>
        public static List<CoopItem> TakeOut(CargoHold hold, IEnumerable<CoopItem> items)
        {
            var taken = new List<CoopItem>();
            if (hold == null || items == null) return taken;
            foreach (var it in items)
            {
                if (it == null || string.IsNullOrEmpty(it.id) || !(it.a > 0f)) continue;
                float t = hold.Unload(it.id, it.a);
                if (t > 0f) Add(taken, it.id, t);
            }
            return taken;
        }

        /// <summary>Put the items into a hold as far as there is room: what was loaded of each
        /// (an id the hold doesn't know, or an amount that isn't one, loads nothing).</summary>
        public static List<CoopItem> PutIn(CargoHold hold, IEnumerable<CoopItem> items)
        {
            var loaded = new List<CoopItem>();
            if (hold == null || items == null) return loaded;
            foreach (var it in items)
            {
                if (it == null || string.IsNullOrEmpty(it.id) || !(it.a > 0f) || it.a > 1e6f) continue;
                float t = hold.Load(it.id, it.a);
                if (t > 0f) Add(loaded, it.id, t);
            }
            return loaded;
        }

        /// <summary>What was sent and not loaded, item by item (a receipt that says more was
        /// loaded than was sent counts as all of it).</summary>
        public static List<CoopItem> Left(IEnumerable<CoopItem> sent, IEnumerable<CoopItem> loaded)
        {
            var left = new List<CoopItem>();
            if (sent == null) return left;
            var got = new Dictionary<string, float>();
            if (loaded != null)
                foreach (var it in loaded)
                    if (it != null && !string.IsNullOrEmpty(it.id) && it.a > 0f && !float.IsInfinity(it.a))
                        got[it.id] = (got.TryGetValue(it.id, out float g) ? g : 0f) + it.a;
            foreach (var it in sent)
            {
                if (it == null || string.IsNullOrEmpty(it.id) || !(it.a > 0f)) continue;
                float used = got.TryGetValue(it.id, out float g) ? Mathf.Min(g, it.a) : 0f;
                if (used > 0f) got[it.id] = g - used;
                if (it.a - used > 0.0005f) Add(left, it.id, it.a - used);
            }
            return left;
        }

        static void Add(List<CoopItem> list, string id, float a)
        {
            var had = list.Find(x => x.id == id);
            if (had != null) had.a += a;
            else list.Add(new CoopItem { id = id, a = a });
        }

        /// <summary>The tonnes (and parts) of a list, all told.</summary>
        public static float Total(IEnumerable<CoopItem> items) => items == null ? 0f : items.Where(x => x != null && x.a > 0f).Sum(x => x.a);

        /// <summary>A list as said: "50 t of iron", "120 t of ore (80 t of iron, 40 t of nickel)",
        /// "3 parts of shield emitter", or "nothing".</summary>
        public static string Describe(IEnumerable<CoopItem> items)
        {
            var list = items == null ? new List<CoopItem>() : items.Where(x => x != null && x.a > 0.0005f).ToList();
            if (list.Count == 0) return "nothing";
            var table = IndustryTable.Get();
            string One(CoopItem x) => $"{table.Amount(x.id, x.a)} of {table.Name(x.id).ToLowerInvariant()}";
            if (list.Count == 1) return One(list[0]);
            bool allOre = list.All(x => IndustryTable.AsMineral(x.id, out _));
            string first = string.Join(", ", list.Take(3).Select(One)) + (list.Count > 3 ? $", {list.Count - 3} more" : "");
            return allOre ? $"{Tonnes(Total(list))} t of ore ({first})" : first;
        }

        static string Tonnes(float t) => t >= 9.95f ? t.ToString("F0", CultureInfo.InvariantCulture) : t.ToString("0.#", CultureInfo.InvariantCulture);

        // ------------------------------------------------------------ the wingman near

        /// <summary>The AI wingman nearest a point, in this level, seen and within Reach (and how
        /// far); null if none is.</summary>
        public static CoopPlayer WingmanNear(Vector3 at, out float metres)
        {
            metres = -1f;
            var c = CoopClient.Instance;
            var w = CoopWorld.Instance;
            if (!CoopClient.Active || w == null || IAmAi) return null;
            var mine = c.Players.Find(p => p != null && p.id == c.MyId);
            CoopPlayer best = null;
            foreach (var p in c.Players)
            {
                if (p == null || p.id == c.MyId || !IsAi(p, c.Players) || (mine != null && p.level != mine.level)) continue;
                var r = w.RemoteOf(p.id);
                if (r == null || r.Hidden) continue;
                float d = Vector3.Distance(r.transform.position, at);
                if (d <= Reach && (best == null || d < metres)) { best = p; metres = d; }
            }
            return best;
        }

        /// <summary>Hand items from the player's hold to the AI wingman nearest (G in the mining
        /// menu): said in a notice, as is why not.</summary>
        public static bool GiveToWingman(CargoHold hold, List<CoopItem> items)
        {
            if (hold == null) return false;
            var to = WingmanNear(hold.transform.position, out _);
            if (to == null)
            {
                SoundFX.Ui(Sfx.UiError);
                Notices.Post(CoopClient.Active ? $"No AI wingman within {Reach / 1000f:F0} km to hand cargo to" : "Cargo goes to an AI wingman, in a co-op game");
                return false;
            }
            string why = Send(hold, to.id, items, out var sent);
            if (why.Length > 0)
            {
                SoundFX.Ui(Sfx.UiError);
                Notices.Post($"Nothing handed to {to.name}: {why}");
                return false;
            }
            SoundFX.Ui(Sfx.UiLock);
            Notices.Post($"Handing {Describe(sent)} to {to.name}");
            return true;
        }

        // ------------------------------------------------------------ sending

        /// <summary>Send items out of `from` to player `to`: why not ("" when sent, and `sent`
        /// is what went: what there was of what was asked for). The other player must be an AI
        /// wingman if this game isn't one, a person if it is, in this level, seen and within Reach.</summary>
        public static string Send(CargoHold from, int to, List<CoopItem> items, out List<CoopItem> sent)
        {
            sent = new List<CoopItem>();
            var c = CoopClient.Instance;
            if (from == null) return "no cargo hold";
            if (!CoopClient.Active) return "not connected to a co-op game";
            if (to <= 0 || to == c.MyId) return "nobody to hand it to";
            var other = c.Players.Find(p => p != null && p.id == to);
            if (other == null) return "they've left";
            var mine = c.Players.Find(p => p != null && p.id == c.MyId);
            var w = CoopWorld.Instance;
            var r = w != null ? w.RemoteOf(to) : null;
            float d = r != null && !r.Hidden ? Vector3.Distance(r.transform.position, from.transform.position) : -1f;
            string why = Refusal(IAmAi, IsAi(other, c.Players), mine == null || mine.level == other.level, d);
            if (why.Length > 0) return why;
            sent = TakeOut(from, items);
            if (sent.Count == 0) return "none of it aboard";
            var g = Hold(from, to, sent, Time.realtimeSinceStartup);
            c.Send(new CargoMsg { t = CoopWire.Cargo, to = to, gid = g.id, ts = c.ServerNow, items = sent.ToArray() });
            SaveNow();
            Debug.Log($"[CoopCargo] {Describe(sent)} to player {to}");
            return "";
        }

        /// <summary>Hold what was sent for its receipt (until `now` + ReceiptWait).</summary>
        internal static Pending Hold(CargoHold from, int to, List<CoopItem> items, float now)
        {
            var g = new Pending { id = ++lastId, to = to, from = from, items = items, until = now + ReceiptWait };
            pending.Add(g);
            return g;
        }

        /// <summary>Cargo never answered goes back to the hold it came from.</summary>
        internal static void Tick(float now)
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var g = pending[i];
                if (now < g.until) continue;
                pending.RemoveAt(i);
                Debug.LogWarning($"[CoopCargo] no receipt for {Describe(g.items)} to player {g.to}: back in the hold");
                Back(g, new List<CoopItem>(), "no answer");
            }
        }

        /// <summary>A receipt for cargo sent (from player `from`): what wasn't loaded goes back
        /// into the hold. False if no cargo sent to them is waiting on it (answered already, or
        /// back in the hold after ReceiptWait).</summary>
        internal static bool Receipt(int from, int gid, CoopItem[] loaded, string why)
        {
            var g = pending.Find(x => x.id == gid && x.to == from);
            if (g == null) return false;
            pending.Remove(g);
            var took = new List<CoopItem>();
            if (loaded != null)
                foreach (var it in loaded)
                    if (it != null && !string.IsNullOrEmpty(it.id) && it.a > 0f) Add(took, it.id, it.a);
            // (no more than was sent counts as taken)
            var left = Left(g.items, took);
            var given = Left(g.items, left);
            Back(g, given, string.IsNullOrEmpty(why) ? "" : "refused: " + why);
            return true;
        }

        static void Back(Pending g, List<CoopItem> taken, string why)
        {
            var left = Left(g.items, taken);
            var back = PutIn(g.from, left);
            // (what there's no room for now, the hold having filled since: lost, and said)
            var lost = Left(left, back);
            if (lost.Count > 0) Debug.LogWarning($"[CoopCargo] no room for {Describe(lost)} coming back: lost");
            if (back.Count > 0) SaveNow();
            Returned?.Invoke(g.to, taken, back, why);
            // (said in the player's game; the bot says it on the comms: CoopBot)
            if (IAmAi || (back.Count == 0 && why.Length == 0)) return;
            string name = CoopClient.Instance != null ? CoopClient.Instance.NameOf(g.to) : $"Player {g.to}";
            if (why == "no answer") Notices.Post($"No answer from {name}'s game: {Describe(back)} back in the hold");
            else if (why.Length > 0) Notices.Post($"{name}'s game {why}: {Describe(back)} back in the hold");
            else Notices.Post($"{name} took {Describe(taken)}; no room for the rest: {Describe(back)} back in the hold");
        }

        // ------------------------------------------------------------ receiving

        static void OnMessage(CoopMsg m)
        {
            var c = CoopClient.Instance;
            if (c == null || m.from == c.MyId) return;
            if (m.t == CoopWire.CargoReceipt) { Receipt(m.from, m.gid, m.items, m.why); return; }
            if (m.t != CoopWire.Cargo) return;
            var giver = c.Players.Find(p => p != null && p.id == m.from);
            string name = giver != null ? giver.name : $"Player {m.from}";
            var offered = (m.items ?? new CoopItem[0]).Where(x => x != null && !string.IsNullOrEmpty(x.id) && x.a > 0f && x.a <= 1e6f).ToList();
            if (offered.Count == 0) return;
            var demo = FindAnyObjectByType<CarrierDemoInput>();
            var hold = demo != null && demo.carrier != null ? demo.carrier.GetComponentInChildren<CargoHold>() : null;
            var mine = c.Players.Find(p => p != null && p.id == c.MyId);
            var r = CoopWorld.Instance != null ? CoopWorld.Instance.RemoteOf(m.from) : null;
            float d = r != null && !r.Hidden && hold != null ? Vector3.Distance(r.transform.position, hold.transform.position) : -1f;
            // (never sent: a message with no time is as good as late)
            double age = m.ts > 0 ? c.ServerNow - m.ts : TakeWithin + 1;
            string why = giver == null ? "they've left"
                       : hold == null ? "no cargo hold here"
                       : Refusal(IsAi(giver, c.Players), IAmAi, mine == null || mine.level == giver.level, d, ReachReceived, age);
            var loaded = why.Length == 0 ? PutIn(hold, offered) : new List<CoopItem>();
            // (refused, or loaded: the receipt says which, and the rest goes back to them)
            c.Send(new CargoMsg { t = CoopWire.CargoReceipt, to = m.from, gid = m.gid, items = loaded.ToArray(), why = why });
            var left = Left(offered, loaded);
            if (why.Length > 0) Debug.LogWarning($"[CoopCargo] {Describe(offered)} from {name} refused: {why}");
            else
            {
                Received += Total(loaded);
                if (loaded.Count > 0) SaveNow();
                Debug.Log($"[CoopCargo] {Describe(loaded)} from {name}{(left.Count > 0 ? $"; no room for {Describe(left)}" : "")}");
            }
            if (giver != null) Arrived?.Invoke(giver, loaded, left, why);
            if (IAmAi) return;
            if (why.Length > 0) Notices.Post($"Cargo from {name} refused: {why}");
            else if (loaded.Count == 0) Notices.Post($"No room in the hold for {Describe(offered)} from {name}: it goes back");
            else Notices.Post($"{name} handed over {Describe(loaded)}{(left.Count > 0 ? $"; no room for {Describe(left)}, it goes back" : "")}");
        }

        // ------------------------------------------------------------ the player's own orders

        /// <summary>A message this game sent: when it tells an AI wingman to take cargo ("Wingman,
        /// take my ore", "take the iron"), the cargo goes with it. Only in a person's game, and
        /// only for its own messages, so nothing a bot says can take cargo out of a player's hold.</summary>
        static void Said(CoopChat.Line line)
        {
            var c = CoopClient.Instance;
            if (c == null || !CoopClient.Active || line.from != c.MyId || IAmAi) return;
            var to = TakeOrderTo(line.text, c.Players.Where(p => p != null && p.id != c.MyId && IsAi(p, c.Players)).ToList(), out string arg);
            if (to == null) return;
            var demo = FindAnyObjectByType<CarrierDemoInput>();
            var hold = demo != null && demo.carrier != null ? demo.carrier.GetComponentInChildren<CargoHold>() : null;
            // ("all ships, take my ore": the nearest of them)
            if (to.Count > 1 && hold != null)
            {
                var near = WingmanNear(hold.transform.position, out _);
                to = near != null && to.Contains(near) ? new List<CoopPlayer> { near } : to.Take(1).ToList();
            }
            var who = to[0];
            SplitCargo(arg, out string amount, out string item, out _);
            var want = Wanted(hold, amount, item, out string why);
            if (want == null)
            {
                Notices.Post(why == "how much?" ? $"Nothing handed to {who.name}: say how much (\"take 50 t of iron\", \"take all my iron\")"
                                                : $"Nothing handed to {who.name}: {why}");
                return;
            }
            why = Send(hold, who.id, want, out var sent);
            Notices.Post(why.Length > 0 ? $"Nothing handed to {who.name}: {why}" : $"Handing {Describe(sent)} to {who.name}");
        }

        /// <summary>The AI wingmen a message tells to take cargo (by name, or "all ships": all of
        /// them), and what (`arg`: "&lt;amount&gt; &lt;item&gt;", CoopBot.ParseOrder's "take");
        /// null if it isn't such an order. A "take" among several orders counts ("take my ore,
        /// then mine").</summary>
        public static List<CoopPlayer> TakeOrderTo(string text, List<CoopPlayer> wingmen, out string arg)
        {
            arg = null;
            if (string.IsNullOrWhiteSpace(text) || wingmen == null) return null;
            var to = new List<CoopPlayer>();
            // (each by what follows its name: "Wingman 2, take my ore" isn't to "Wingman", whose
            // order would be "2, take my ore")
            foreach (var p in wingmen)
            {
                if (p == null || string.IsNullOrEmpty(p.name)) continue;
                string rest = CoopBot.AddressedPart(text, p.name);
                if (rest == null) continue;
                var chain = CoopBot.Clauses(CoopBot.Plain(rest));
                var take = chain != null ? chain.FirstOrDefault(o => o.kind == "take") : (CoopBot.ParseOrder(rest) ?? default);
                if (take.kind != "take") continue;
                to.Add(p);
                arg = take.arg;
            }
            return to.Count > 0 ? to : null;
        }

        /// <summary>A cargo order's target taken apart: "&lt;amount&gt; &lt;item&gt;[ to
        /// &lt;name&gt;]" ("100 Iron", "all ore to Ann", "half helium 3"): the amount (a number,
        /// "all", "half" or "?"), the item ("ore", a mineral, an id) and the player's name ("").</summary>
        public static void SplitCargo(string target, out string amount, out string item, out string to)
        {
            string t = (target ?? "").Trim();
            to = "";
            int at = t.IndexOf(" to ", StringComparison.OrdinalIgnoreCase);
            if (at >= 0) { to = t.Substring(at + 4).Trim(); t = t.Substring(0, at).Trim(); }
            else if (t.StartsWith("to ", StringComparison.OrdinalIgnoreCase)) { to = t.Substring(3).Trim(); t = ""; }
            int sp = t.IndexOf(' ');
            string first = sp < 0 ? t : t.Substring(0, sp);
            string fl = first.ToLowerInvariant();
            bool isAmount = fl == "all" || fl == "half" || fl == "?" || (fl.Length > 0 && (char.IsDigit(fl[0]) || fl[0] == '.'));
            amount = isAmount ? first : "all";
            item = (isAmount ? (sp < 0 ? "" : t.Substring(sp + 1)) : t).Trim();
            if (item.Length == 0) item = "ore";
        }

        /// <summary>Save the player's game now (the hold changed by cargo passed over): not in a
        /// bot's game, which never touches the save.</summary>
        static void SaveNow()
        {
            if (IAmAi) return;
            var d = IndustryDirector.Instance;
            if (d != null) d.Save();
        }
    }
}
