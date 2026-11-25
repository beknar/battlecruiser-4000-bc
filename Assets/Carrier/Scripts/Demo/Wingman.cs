using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// An AI pilot for the player's ship: a co-op player's wingman (CoopBot), flying by itself on
    /// orders. It drives the ship through the same controls a player has (CarrierDemoInput's
    /// autopilot inputs, the throttle, the target, the guns, the main gun, missiles, the warp
    /// drive and docking), so the ship behaves exactly as anyone's would.
    ///
    ///   Follow  - keep station on a player's ship (off its starboard quarter), warping after it
    ///             when it goes far (to the place nearest it)
    ///   Hold    - stop where it is
    ///   Attack  - fight a target (named, or the nearest enemy): broadside at gun range, the bow
    ///             beam when lined up, missiles now and then; a mini-warp to it first if it's far
    ///   Goto    - warp to a place, then hold there
    ///   Dock    - dock at the station it is at
    ///   Mine    - mine asteroids, sell the ore at the nearest market when the hold is full, and
    ///             go back for more (Wingman.Mining.cs)
    ///   Defend  - go to a player from wherever it is and fight what's on them (what they've
    ///             locked, or the nearest enemy to them), then keep station on them as Follow
    ///             does. In a fight of its own far from them, it breaks off as soon as the warp
    ///             drive will go (the drive won't under fire).
    /// When an enemy comes close while it is following or holding, it fights, and goes back to
    /// its order when the fight is over.
    /// </summary>
    public partial class Wingman : MonoBehaviour
    {
        public enum Order { Hold, Follow, Attack, Goto, Dock, Mine, Defend }

        public CarrierDemoInput demo;
        [Tooltip("Where it keeps station off its leader: right, up and behind, in the leader's frame, in ship sizes.")]
        public Vector3 slot = new Vector3(1.7f, 0.2f, -2.2f);
        public float gunRange = 1600f;
        [Tooltip("An enemy this close (m) is fought even while following or holding.")]
        public float defendRange = 6000f;
        [Tooltip("How far the leader may get before it mini-warps to them (m).")]
        public float warpAfter = 15000f;

        public Order Current { get; private set; } = Order.Hold;
        public int LeaderId { get; private set; }
        /// <summary>Told to hold, go somewhere or dock: it doesn't form up on a player by itself.</summary>
        public bool Released { get; private set; }
        /// <summary>In a fight: attacking, or fighting back.</summary>
        /// <summary>In a fight: attacking its target, or defending (which goes on, between
        /// targets, till the calm is out).</summary>
        public bool Fighting => (Current == Order.Attack && Target != null) || defending;
        public string LeaderName => LeaderId != 0 && CoopClient.Instance != null ? CoopClient.Instance.NameOf(LeaderId) : "";
        public Transform Target { get; private set; }
        public WarpPoint Destination { get; private set; }
        /// <summary>What it is doing now, in a few words (for the status and the comms).</summary>
        public string Doing { get; private set; } = "holding position";

        CarrierController Ship => demo != null ? demo.carrier : null;
        WarpDrive drive;
        bool defending;
        float nextMissiles, nextWarp, nextLook;
        // a warp the drive refused: why, and until when it isn't tried again
        string warpWhy;
        float warpBlockedUntil;
        // the ship's size (its bounds' half-diagonal), worked out once a ship
        CarrierController sized;
        float size = 150f;

        void OnDestroy() => Release();

        // ------------------------------------------------------------ orders

        // (hold, go to and dock end the following: when a fight ends it goes back to what it was told)
        public void Hold() { LeaveLeader(); Set(Order.Hold); }
        public void Follow(int playerId) { LeaderId = playerId; Released = false; Set(Order.Follow); }
        /// <summary>Go to a player and fight what's on them (they become its leader).</summary>
        public void Defend(int playerId) { LeaderId = playerId; Released = false; Set(Order.Defend); }
        public void Attack(Transform t) { Target = t; Set(Order.Attack); }
        public void Goto(WarpPoint p) { LeaveLeader(); Destination = p; Set(Order.Goto); }
        public void Dock() { LeaveLeader(); Set(Order.Dock); }

        void LeaveLeader()
        {
            LeaderId = 0;
            Released = true;
        }

        void Set(Order o)
        {
            WaitingOver = null;
            // (leaving the Mine order: the laser off, the approach stopped)
            if (Current == Order.Mine && o != Order.Mine) StopMiningGear();
            Current = o;
            defending = false;
            nextWarp = 0f;
            warpBlockedUntil = 0f;
            if (o != Order.Attack) Target = null;
        }

        /// <summary>The leader's ship in this game (null if it isn't here: in another level, or
        /// not seen: landed, docked or in warp).</summary>
        public RemotePlayer Leader()
        {
            var w = CoopWorld.Instance;
            var r = w != null && LeaderId != 0 ? w.RemoteOf(LeaderId) : null;
            return r != null && !r.Hidden ? r : null;
        }

        /// <summary>The world the leader has landed on (its warp point), or null: not landed, not in
        /// this level, or a world this level hasn't got.</summary>
        public WarpPoint LandedLeader()
        {
            var w = CoopWorld.Instance;
            var r = w != null && LeaderId != 0 ? w.RemoteOf(LeaderId) : null;
            if (r == null || r.Dead || string.IsNullOrEmpty(r.Surface)) return null;
            return WarpPoint.All.FirstOrDefault(p => p != null && p.kind != WarpPoint.Kind.Landmark
                                                     && string.Equals(p.displayName, r.Surface, System.StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Where it waits for a leader down on `world`: the world's high orbit (or the
        /// world itself, when it has none).</summary>
        public static WarpPoint WaitingPlace(WarpPoint world)
        {
            if (world == null) return null;
            string orbit = world.displayName + " High Orbit", equator = world.displayName + " Equatorial Orbit";
            return WarpPoint.All.FirstOrDefault(p => p != null && p.kind == WarpPoint.Kind.Landmark && (p.displayName == orbit || p.displayName == equator))
                   ?? world;
        }

        /// <summary>The world it waits over for its landed leader (null when it isn't waiting).</summary>
        public WarpPoint WaitingOver { get; private set; }

        /// <summary>What it says while it waits over a world.</summary>
        public string WaitingLine => WaitingOver == null ? "" :
            $"{LeaderName} has landed on {WaitingOver.displayName}. Holding {(WaitingPlace(WaitingOver) != WaitingOver ? "in high orbit over" : "over")} it";

        void WaitOver(CarrierController cc, WarpPoint world)
        {
            if (WaitingOver != world)
            {
                // (a new wait: a warp refused on the way after the leader doesn't hold this one up)
                WaitingOver = world;
                warpBlockedUntil = 0f;
                nextWarp = 0f;
            }
            var at = WaitingPlace(world);
            string doing = $"holding {(at != world ? "in high orbit over" : "over")} {world.displayName}: {LeaderName} has landed";
            if (Vector3.Distance(cc.transform.position, at.Position) > Mathf.Max(20000f, at.arrivalDistance * 2f))
            {
                // (a warp the drive won't make: it says why, and tries again in a while)
                TryWarp(at, $"warping to {at.displayName}: {LeaderName} has landed on {world.displayName}");
                return;
            }
            Steer(0f, 0f);
            cc.throttle = 0f;
            Doing = doing;
        }

        /// <summary>The nearest hostile ship or installation within `range` of a point (alive, in the level).</summary>
        public static Transform NearestEnemy(Vector3 from, float range)
        {
            Transform best = null;
            float bd = range * range;
            foreach (var t in Targets.All)
            {
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                if (!t.TryGetComponent(out TargetInfo info) || !info.Hostile) continue;
                if (t.TryGetComponent(out Health h) && h.Dead) continue;
                if (Sensors.Unseen(t)) continue;   // (one the sensors can't see: not known of)
                float d = (t.position - from).sqrMagnitude;
                if (d < bd) { bd = d; best = t; }
            }
            return best;
        }

        /// <summary>A target by name (part of its name, any case), the nearest that matches. Never
        /// another player's ship.</summary>
        public static Transform FindTarget(string name, Vector3 from)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            name = name.Trim().ToLowerInvariant();
            return Targets.All.Where(t => t != null && t.gameObject.activeInHierarchy && !IsPlayer(t) && NameOf(t).ToLowerInvariant().Contains(name))
                .OrderBy(t => (t.position - from).sqrMagnitude).FirstOrDefault();
        }

        /// <summary>A target as an attack order names it: as FindTarget, else the nearest enemy
        /// whose name is near enough word for word (NameMatch, loose: "Frank Hunter" for the
        /// Vrask Fang Hunter, as a recogniser hears it).</summary>
        public static Transform FindTargetLoose(string name, Vector3 from) =>
            FindTarget(name, from) ?? Pick(name, from, 1, true);

        /// <summary>An enemy a message names by itself ("Fang Hunter"): one whose whole name it
        /// is, or whose name has its words in a row; the nearest. Never loose, so talk isn't
        /// taken for an attack.</summary>
        public static Transform EnemyNamed(string words, Vector3 from)
        {
            if (NameWords(words).Sum(w => w.Count(char.IsLetter)) < 3) return null;
            return Pick(words, from, 2, false);
        }

        // the living enemy (never a player) whose name matches `name` best, at least `atLeast`
        // (NameMatch), the nearest of the best
        static Transform Pick(string name, Vector3 from, int atLeast, bool loose)
        {
            Transform best = null;
            int bestScore = 0;
            float bestDist = float.MaxValue;
            foreach (var t in Targets.All)
            {
                if (t == null || !t.gameObject.activeInHierarchy || IsPlayer(t)) continue;
                if (!t.TryGetComponent(out TargetInfo info) || !info.Hostile) continue;
                if (t.TryGetComponent(out Health h) && h.Dead) continue;
                int score = NameMatch(name, NameOf(t), loose);
                if (score < atLeast) continue;
                float d = (t.position - from).sqrMagnitude;
                if (score > bestScore || (score == bestScore && d < bestDist)) { best = t; bestScore = score; bestDist = d; }
            }
            return best;
        }

        /// <summary>Another player's ship (never a target for the wingman).</summary>
        static bool IsPlayer(Transform t) => t.GetComponentInParent<RemotePlayer>() != null;

        static readonly string[] Articles = { "the", "that", "this" };

        /// <summary>A name's words, lower case, without the punctuation ("Claim-jumper Drone A":
        /// claim, jumper, drone, a).</summary>
        public static List<string> NameWords(string s)
        {
            var words = new List<string>();
            var sb = new StringBuilder();
            foreach (char c in (s ?? "").ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) { sb.Append(c); continue; }
                if (sb.Length > 0) { words.Add(sb.ToString()); sb.Clear(); }
            }
            if (sb.Length > 0) words.Add(sb.ToString());
            return words;
        }

        /// <summary>How a name said matches a target's: 3, the whole name; 2, words of it in a
        /// row ("Fang Hunter" in "Vrask Fang Hunter"); 1 (only `loose`), word for word near
        /// enough (WordNear: "Frank Hunter"); 0, not at all. A "the" before it is left off.</summary>
        public static int NameMatch(string said, string name, bool loose)
        {
            var s = NameWords(said);
            while (s.Count > 1 && System.Array.IndexOf(Articles, s[0]) >= 0) s.RemoveAt(0);
            var n = NameWords(name);
            if (s.Count == 0 || n.Count == 0 || s.Count > n.Count) return 0;
            if (s.SequenceEqual(n)) return 3;
            int best = 0;
            for (int i = 0; i + s.Count <= n.Count; i++)
            {
                bool same = true, near = true;
                for (int j = 0; j < s.Count && near; j++)
                {
                    if (s[j] == n[i + j]) continue;
                    same = false;
                    near = loose && WordNear(s[j], n[i + j]);
                }
                if (same) return 2;
                if (near) best = 1;
            }
            return best;
        }

        /// <summary>Two words near enough for a name heard wrong: three letters or more each, and
        /// a letter off (two in a word of seven or more), or alike by sound (VoiceCommand.SoundsLike)
        /// and starting with the same sound: "frank" and "fang", not "skunk" and "fang".</summary>
        public static bool WordNear(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || a.Length < 3 || b.Length < 3) return false;
            if (char.IsDigit(a[0]) || char.IsDigit(b[0])) return false;
            if (VoiceCommand.Edits(a, b) <= (Mathf.Max(a.Length, b.Length) >= 7 ? 2 : 1)) return true;
            string ka = VoiceCommand.SoundKey(a), kb = VoiceCommand.SoundKey(b);
            if (ka.Length == 0 || kb.Length == 0 || ka[0] != kb[0]) return false;
            return VoiceCommand.SoundsLike(a, b) || VoiceCommand.SoundsLike(b, a);
        }

        /// <summary>The enemy a player has locked, as their ship's message says it
        /// (RemotePlayer.TargetId), alive and in this game; null for nothing locked, something
        /// that isn't an enemy, or a game too old to say.</summary>
        public static Transform LockOf(RemotePlayer r)
        {
            var w = CoopWorld.Instance;
            if (r == null || w == null || r.ContentVersion < CoopWire.ContentTarget) return null;
            string id = r.TargetId;
            if (string.IsNullOrEmpty(id) || id.StartsWith(CoopWorld.NotEnemyLock) || id.StartsWith(CoopWorld.OwnEnemyLock)) return null;
            var t = w.EntityTransform(id);
            return Gone(t) || !(t.TryGetComponent(out TargetInfo info) && info.Hostile) ? null : t;
        }

        /// <summary>How near a player an enemy is on them, for Defend (m).</summary>
        public const float DefendReach = 9000f;

        /// <summary>What's on a player: the enemy they've locked (within 30 km of them), else the
        /// nearest enemy within `reach` of their ship; null when nothing is, or they aren't seen.</summary>
        public static Transform ThreatTo(RemotePlayer r, float reach = DefendReach)
        {
            if (r == null || r.Hidden) return null;
            Vector3 at = r.transform.position;
            var locked = LockOf(r);
            if (locked != null && (locked.position - at).sqrMagnitude < 30000f * 30000f) return locked;
            return NearestEnemy(at, reach);
        }

        /// <summary>A target's name as the player sees it.</summary>
        public static string NameOf(Transform t) => Targets.NameOf(t);

        /// <summary>How much further than defendRange a fight goes on, once started.</summary>
        public const float DefendKeep = 1.25f;
        /// <summary>Seconds with no enemy in reach before a fight is over, while mining.</summary>
        public const float DefendCalm = 8f;
        float lastEnemySeen = float.NegativeInfinity;

        /// <summary>With no enemy in reach for `sinceSeen` seconds, is the fight still on? Mining,
        /// till the calm is out; otherwise, no.</summary>
        public static bool StillDefending(bool mining, float sinceSeen) => mining && sinceSeen < DefendCalm;

        /// <summary>Fights over while mining, back at the rocks (CoopBot says so on the channel).</summary>
        public int BackToMining { get; private set; }

        /// <summary>The fight over: back to the order.</summary>
        void EndDefence(CarrierController cc)
        {
            defending = false;
            Target = null;
            cc.SetTarget(null);
            if (Current == Order.Mine && Step <= MineStep.Cut) BackToMining++;
        }

        static bool Gone(Transform t) => t == null || !t.gameObject.activeInHierarchy || (t.TryGetComponent(out Health h) && h.Dead);

        // ------------------------------------------------------------ flying

        void Release()
        {
            if (demo == null) return;
            demo.autoRudder = demo.autoPitch = demo.autoElevator = demo.autoRoll = null;
        }

        void Update()
        {
            var cc = Ship;
            if (cc == null) return;
            if (cc.Destroyed) { Doing = "destroyed, waiting to come back"; return; }
            if (sized != cc)
            {
                sized = cc;
                var col = cc.GetComponentInChildren<Collider>();
                size = Mathf.Max(150f, col != null ? col.bounds.extents.magnitude : 150f);
            }
            if (drive == null || drive.gameObject != cc.gameObject) drive = cc.GetComponent<WarpDrive>();
            // (the warp drive, or the landing, flies the ship: hands off)
            if (drive != null && drive.Current != WarpDrive.State.Ready) { Steer(0f, 0f); Doing = "in warp"; return; }
            if (PlanetLanding.Busy) { Steer(0f, 0f); Doing = "down on a planet"; return; }

            // an enemy close while following, holding or mining: fight it, then carry on. A fight
            // starts inside defendRange and goes on while one is inside DefendKeep of that, and
            // until none has been seen for DefendCalm: so one at the edge of the sensors' reach
            // (which jamming moves about) doesn't start and stop a fight every second
            if (Time.time >= nextLook && Current != Order.Attack && Current != Order.Dock)
            {
                nextLook = Time.time + 1f;
                Vector3 pos = cc.transform.position;
                float keep = defendRange * DefendKeep;
                bool far = !Gone(Target) && (Target.position - pos).sqrMagnitude > keep * keep;
                // (defending a player: what's on them too, wherever they are)
                var near = NearestEnemy(pos, defending ? keep : defendRange) ?? (Current == Order.Defend ? ThreatTo(Leader()) : null);
                // (a nearer one in reach rather than one run off; none in reach, mining: not chased
                // off the field, but watched for)
                if (near != null) { defending = true; lastEnemySeen = Time.time; if (Gone(Target) || far) Target = near; }
                else if (defending && !StillDefending(Current == Order.Mine, Time.time - lastEnemySeen)) EndDefence(cc);
                else if (defending && far && Current == Order.Mine) { Target = null; cc.SetTarget(null); }
            }

            // defending a player who is far off, in a fight here: broken off as soon as the drive
            // will go (it won't under fire, or with hostiles engaged near), and off to them
            if (Current == Order.Defend && defending && drive != null)
            {
                var who = Leader();
                if (who != null && (who.transform.position - cc.transform.position).sqrMagnitude > warpAfter * warpAfter && drive.CanWarp(out _))
                {
                    defending = false;
                    Target = null;
                    cc.SetTarget(null);
                    warpBlockedUntil = 0f;
                    nextWarp = 0f;
                    if (TryWarpTo(who.transform, LeaderName)) return;
                }
            }

            if (Current == Order.Attack || defending)
            {
                // (mining: the laser off and the approach called off for the fight, then on again)
                if (Current == Order.Mine) MinePause();
                if (Gone(Target))
                {
                    // (mining and watching for more: the check each second finds the next one)
                    Target = Current == Order.Mine && defending ? null
                           : NearestEnemy(cc.transform.position, Current == Order.Attack ? 30000f : defendRange * DefendKeep)
                             ?? (Current == Order.Defend ? ThreatTo(Leader()) : null);
                    if (Target == null)
                    {
                        if (Current == Order.Attack)
                        {
                            Current = LeaderId != 0 ? Order.Follow : Order.Hold;
                            Doing = "target destroyed";
                            defending = false;
                            cc.SetTarget(null);
                            return;
                        }
                        // (nothing left to fight. Mining, it holds where it is till the calm is out,
                        // in case another comes, rather than going back to a rock and off it again;
                        // otherwise it carries on at once, not to lose its leader)
                        if (!StillDefending(Current == Order.Mine, Time.time - lastEnemySeen)) EndDefence(cc);
                        else { cc.SetTarget(null); Steer(0f, 0f); cc.throttle = 0f; Doing = "watching for more"; return; }
                    }
                }
                if (Target != null) { Fight(cc, Target); return; }
            }

            switch (Current)
            {
                case Order.Hold:
                    Steer(0f, 0f);
                    cc.throttle = 0f;
                    Doing = Docking.Docked ? "docked" : "holding position";
                    break;
                case Order.Follow:
                case Order.Defend:
                    FollowLeader(cc);
                    break;
                case Order.Mine:
                    MineUpdate(cc);
                    break;
                case Order.Goto:
                    if (Destination == null) { Current = Order.Hold; break; }
                    if (Vector3.Distance(cc.transform.position, Destination.Position) < Mathf.Max(20000f, Destination.arrivalDistance * 2f))
                    {
                        Doing = $"arrived at {Destination.displayName}";
                        Current = Order.Hold;
                        break;
                    }
                    // (a warp the drive refuses ends the order: it says why, once, and holds)
                    if (!TryWarp(Destination, $"warping to {Destination.displayName}")) { Current = Order.Hold; Released = true; }
                    break;
                case Order.Dock:
                {
                    if (Docking.Docked) { Steer(0f, 0f); cc.throttle = 0f; Doing = "docked"; Current = Order.Hold; break; }
                    var d = Docking.Instance;
                    // (navigation flying her in to the berth: hands off till she is in)
                    if (d != null && d.Current == Docking.State.Free && d.Candidate(out _) is Installation inn && d.FlyingIn(inn))
                    {
                        Release();
                        Doing = $"flying in to dock at {inn.displayName}";
                        break;
                    }
                    if (d != null && d.Current != Docking.State.Free) { Doing = "docking"; break; }   // (docking flies her in: wait for it)
                    Steer(0f, 0f);
                    cc.throttle = 0f;
                    if (d == null) break;
                    if (d.Candidate(out string why) == null) { Doing = $"can't dock: {why}"; Current = Order.Hold; break; }
                    if (d.Dock()) Doing = "docking";
                    break;
                }
            }
        }

        void Steer(float rudder, float pitch)
        {
            demo.autoRudder = rudder;
            demo.autoPitch = pitch;
            demo.autoElevator = 0f;
            demo.autoRoll = 0f;
        }

        /// <summary>Leave the station it is docked at (true while it is still docked or undocking).</summary>
        bool Undocking()
        {
            var d = Docking.Instance;
            if (d == null || d.Current == Docking.State.Free) return false;
            if (d.Current == Docking.State.Docked) d.Undock();
            // (flying in to dock: called off for the new order; undocking: docking flies her out)
            else if (d.Current == Docking.State.Docking) d.CancelApproach("new orders");
            return true;
        }

        /// <summary>Warp to a place (undocking first). False when the drive refuses (or did, in the
        /// last 20 s): Doing says why, and stays saying it rather than flicking back and forth.</summary>
        bool TryWarp(WarpPoint p, string doing)
        {
            if (Time.time < warpBlockedUntil) { Doing = warpWhy; return false; }
            Doing = doing;
            if (Undocking()) return true;
            string why = null;
            if (drive == null) why = "no warp drive";
            else if (Time.time >= nextWarp)
            {
                nextWarp = Time.time + 3f;
                if (!drive.Engage(p, out why) && string.IsNullOrEmpty(why)) why = "the drive refused";
            }
            if (string.IsNullOrEmpty(why)) return true;
            warpWhy = Doing = $"can't warp to {p.displayName}: {why}";
            warpBlockedUntil = Time.time + 20f;
            return false;
        }

        /// <summary>A mini-warp to a ship (the leader). False when the drive refuses (or did, in
        /// the last 20 s): Doing says why.</summary>
        bool TryWarpTo(Transform ship, string name)
        {
            if (drive != null && drive.Current != WarpDrive.State.Ready && drive.DestinationShip == ship) { Doing = $"warping to {name}"; return true; }
            if (Time.time < warpBlockedUntil) return false;
            if (Undocking()) { Doing = $"undocking to follow {name}"; return true; }
            if (Time.time < nextWarp) return false;
            nextWarp = Time.time + 3f;
            if (drive.EngageTo(ship, name, out string why)) { Doing = $"warping to {name}"; return true; }
            // (too close to bother, or locked: it flies after them, and tries again in a while)
            warpBlockedUntil = Time.time + 20f;
            warpWhy = $"can't warp to {name}: {why}";
            return false;
        }

        void FollowLeader(CarrierController cc)
        {
            var leader = Leader();
            // the leader landed: down on a surface no one else is on (each player's own), so it
            // waits for them over that world, in its high orbit, and forms up again when they're up
            var down = leader == null ? LandedLeader() : null;
            if (down != null) { WaitOver(cc, down); return; }
            WaitingOver = null;
            if (leader == null)
            {
                Steer(0f, 0f);
                cc.throttle = 0f;
                Doing = LeaderId != 0 ? $"waiting for {LeaderName}" : "holding position";
                return;
            }
            Vector3 lp = leader.transform.position;
            float far = Vector3.Distance(cc.transform.position, lp);
            // far behind: a mini-warp to the leader (dropping out close by them), if the drive will
            // go; otherwise fly after them
            if (far > warpAfter && drive != null && TryWarpTo(leader.transform, LeaderName)) return;
            if (Undocking()) { Doing = $"undocking to follow {LeaderName}"; return; }
            Vector3 to = lp + leader.transform.rotation * (slot * size);
            float d = Vector3.Distance(cc.transform.position, to);
            float leaderSpeed = leader.Velocity.magnitude;
            float matched = Mathf.Clamp01(leaderSpeed / Mathf.Max(1f, cc.maxSpeed));
            if (d < size * 1.2f)
            {
                // in the slot: the leader's heading and speed
                var s = ShipAutopilot.BowOn(cc, cc.transform.position + leader.transform.forward * 2000f);
                Steer(s.x, s.y);
                cc.throttle = matched;
                Doing = $"in formation on {LeaderName}";
            }
            else
            {
                // (round the leader and the others to its slot, not through them)
                var s = ShipAutopilot.Travel(cc, to, null, out float clear);
                Steer(s.x, s.y);
                float catchUp = Mathf.Clamp01((d - size) / 3000f);
                float aligned = Mathf.Clamp01(1f - ShipAutopilot.BowError(cc, to) / 90f);
                cc.throttle = Mathf.Clamp01(Mathf.Max(matched, catchUp) * (0.3f + 0.7f * aligned)) * clear;
                Doing = $"closing on {LeaderName} ({d / 1000f:F1} km)";
            }
        }

        void Fight(CarrierController cc, Transform t)
        {
            if (Undocking()) { Doing = $"undocking to engage {NameOf(t)}"; return; }
            if (cc.target != t) demo.SelectTarget(t);
            if (cc.mainTurrets != null) foreach (var g in cc.mainTurrets) if (g) g.autoFire = true;
            Vector3 p = t.position;
            float d = Vector3.Distance(cc.transform.position, p);
            string name = NameOf(t);
            var beam = demo.MainGun;
            if (beam != null && beam.Current != MainBeam.State.Idle)
            {
                // the bow beam charging or firing: keep the bow on
                var s = ShipAutopilot.BowOn(cc, p);
                Steer(s.x, s.y);
                cc.throttle = 0.3f;
                Doing = $"main gun on {name}";
                return;
            }
            // far off (an order to attack what a player far away has locked, or what's on them): a
            // mini-warp first, if the drive will go; to the player defended when it's on them
            // (dropping out by them, not under the enemy's guns), else to the target itself
            if (d > warpAfter && drive != null && drive.Current == WarpDrive.State.Ready)
            {
                var who = Current == Order.Defend ? Leader() : null;
                bool by = who != null && (who.transform.position - p).sqrMagnitude < 4f * DefendReach * DefendReach;
                if (by ? TryWarpTo(who.transform, LeaderName) : TryWarpTo(t, name)) return;
            }
            if (d > gunRange * 2.5f)
            {
                var s = ShipAutopilot.Travel(cc, p, t, out float clear);
                Steer(s.x, s.y);
                cc.throttle = clear;
                Doing = $"closing on {name} ({d / 1000f:F1} km)";
            }
            else
            {
                Steer(ShipAutopilot.Broadside(cc, p, 1, gunRange), 0f);
                cc.throttle = 0.55f;
                Doing = $"engaging {name} ({d / 1000f:F1} km)";
                if (beam != null && d < beam.range * 0.85f && ShipAutopilot.BowError(cc, p) < 12f) demo.ChargeMainGun();
            }
            if (cc.HasMissiles && Time.time >= nextMissiles && d < 4500f)
            {
                nextMissiles = Time.time + 12f;
                demo.FireMissiles();
            }
        }
    }
}
