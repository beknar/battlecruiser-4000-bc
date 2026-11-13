using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>A refining or building job in a site's queue: the recipe, and the seconds left.</summary>
    [Serializable]
    public class IndustryJob
    {
        public string recipe;
        public float left, total;

        public float Progress => total > 0f ? Mathf.Clamp01(1f - left / total) : 1f;
    }

    /// <summary>One site's storage and its production lines (the refinery's, the factory's and,
    /// where there is one, the shipyard's).</summary>
    [Serializable]
    public class SiteState
    {
        public string name;
        public Stock stock = new Stock();
        public List<IndustryJob> refining = new List<IndustryJob>();
        public List<IndustryJob> building = new List<IndustryJob>();
        public List<IndustryJob> shipyard = new List<IndustryJob>();
    }

    /// <summary>A ship built at a shipyard (a destroyer or the cruiser, by its class) and where it
    /// is berthed.</summary>
    [Serializable]
    public class BuiltShip
    {
        public string id, site;
    }

    /// <summary>
    /// The state of the industry: each site's storage and queues, what each frigate has had
    /// fitted, and the ships the player has built: berthed at their shipyards, or the one they
    /// are in command of. It outlives the level (a restart keeps it) and is saved to disk (IndustrySave).
    /// A job takes its inputs from the site's storage when it is queued (cancelling it puts them
    /// back), runs for its recipe's seconds of game time, and puts what it makes into the same
    /// storage. A site's refinery and factory each work through their own queue, one job at a time,
    /// whether or not anyone is there.
    /// </summary>
    public static class IndustryLedger
    {
        /// <summary>Jobs waiting at one line, at most.</summary>
        public const int QueueLimit = 12;
        /// <summary>Ships on a shipyard's slips at once, at most.</summary>
        public const int YardLimit = 2;

        static readonly Dictionary<string, SiteState> sites = new Dictionary<string, SiteState>();
        static readonly Dictionary<string, List<string>> fittings = new Dictionary<string, List<string>>();
        // the recruits waiting at each place that recruits, and each frigate class's crew aboard
        static readonly Dictionary<string, float> pools = new Dictionary<string, float>();
        static readonly Dictionary<string, float> crews = new Dictionary<string, float>();
        // the ships built: berthed at their shipyards, and the one the player commands (its class
        // and the shipyard it goes back to when the player flies another)
        static readonly List<BuiltShip> berthed = new List<BuiltShip>();
        static BuiltShip commanded;

        /// <summary>How much faster than game time the lines run (the self-test speeds them up).</summary>
        public static float SpeedUp = 1f;

        /// <summary>A job is done: the site, what it made, and how much.</summary>
        public static event Action<string, string, float> Finished;
        /// <summary>Anything changed (storage, queues, fittings): time to save.</summary>
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            sites.Clear();
            fittings.Clear();
            pools.Clear();
            crews.Clear();
            berthed.Clear();
            commanded = null;
            SpeedUp = 1f;
            Finished = null;
            Changed = null;
        }

        public static IEnumerable<SiteState> Sites => sites.Values;

        /// <summary>A site's state (made empty the first time).</summary>
        public static SiteState Site(string name)
        {
            if (!sites.TryGetValue(name, out var s)) sites[name] = s = new SiteState { name = name };
            return s;
        }

        public static void Touch() => Changed?.Invoke();

        // ------------------------------------------------------------ production

        /// <summary>Whether `site` could refine (or build) one batch of `recipe` now, and if not why.</summary>
        public static bool CanQueue(string site, string recipe, bool build, out string why)
        {
            var table = IndustryTable.Get();
            var spec = table.Site(site);
            var inputs = build ? table.Part(recipe)?.inputs : table.Refined(recipe)?.inputs;
            why = "";
            if (spec == null) why = "not an industrial site";
            else if (inputs == null) why = "no such recipe";
            else if (build ? !spec.Builds(recipe) : !spec.Refines(recipe)) why = build ? "not built here" : "not refined here";
            else if ((build ? Site(site).building : Site(site).refining).Count >= QueueLimit) why = "the queue is full";
            else
            {
                var stock = Site(site).stock;
                foreach (var i in inputs)
                    if (!stock.Has(i.item, i.tonnes))
                    {
                        why = $"needs {table.Amount(i.item, i.tonnes)} of {table.Name(i.item).ToLower()} in storage (has {table.Amount(i.item, stock.Amount(i.item))})";
                        break;
                    }
            }
            return why == "";
        }

        /// <summary>Queue one batch (refined) or one part (built): its inputs leave storage now.</summary>
        public static bool Queue(string site, string recipe, bool build, out string why)
        {
            if (!CanQueue(site, recipe, build, out why)) return false;
            var table = IndustryTable.Get();
            var s = Site(site);
            var inputs = build ? table.Part(recipe).inputs : table.Refined(recipe).inputs;
            foreach (var i in inputs) s.stock.Take(i.item, i.tonnes);
            float secs = build ? table.Part(recipe).seconds : table.Refined(recipe).seconds;
            (build ? s.building : s.refining).Add(new IndustryJob { recipe = recipe, left = secs, total = secs });
            Changed?.Invoke();
            return true;
        }

        /// <summary>Cancel a queued job (the last one by default): its inputs go back into storage.</summary>
        public static bool Cancel(string site, bool build, int index = -1)
        {
            var s = Site(site);
            var q = build ? s.building : s.refining;
            if (q.Count == 0) return false;
            if (index < 0 || index >= q.Count) index = q.Count - 1;
            var job = q[index];
            q.RemoveAt(index);
            var table = IndustryTable.Get();
            var inputs = build ? table.Part(job.recipe)?.inputs : table.Refined(job.recipe)?.inputs;
            if (inputs != null) foreach (var i in inputs) s.stock.Add(i.item, i.tonnes);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Run every line for `dt` seconds of game time.</summary>
        public static void Advance(float dt)
        {
            dt *= SpeedUp;
            if (dt <= 0f) return;
            bool any = false;
            foreach (var s in sites.Values)
            {
                any |= Run(s, s.refining, false, dt);
                any |= Run(s, s.building, true, dt);
                any |= RunYard(s, dt);
            }
            // (the recruiting places' pools refill, whole people at a time as far as anyone sees)
            foreach (var p in CrewTable.Get().places)
            {
                if (!p.Recruits || p.refillPerMinute <= 0f) continue;
                float was = Pool(p.name);
                float now = Mathf.Min(p.recruits, was + p.refillPerMinute * dt / 60f);
                pools[p.name] = now;
                if (Mathf.FloorToInt(now) != Mathf.FloorToInt(was)) any = true;
            }
            if (any) Changed?.Invoke();
        }

        static bool Run(SiteState s, List<IndustryJob> q, bool build, float dt)
        {
            bool done = false;
            var table = IndustryTable.Get();
            while (dt > 0f && q.Count > 0)
            {
                var job = q[0];
                float step = Mathf.Min(dt, job.left);
                job.left -= step;
                dt -= step;
                if (job.left > 1e-4f) break;
                q.RemoveAt(0);
                float made = build ? 1f : table.Refined(job.recipe)?.makes ?? 0f;
                if (made > 0f) s.stock.Add(job.recipe, made);
                done = true;
                Finished?.Invoke(s.name, job.recipe, made);
            }
            return done;
        }

        // ------------------------------------------------------------ shipyards and the ships built

        /// <summary>The ships berthed at shipyards.</summary>
        public static IReadOnlyList<BuiltShip> Berthed => berthed;
        /// <summary>The built ship the player commands (its class), or "" when flying a frigate.</summary>
        public static string Commanded => commanded != null ? commanded.id : "";

        /// <summary>The player has a ship of this class: berthed, or in command of it.</summary>
        public static bool Owns(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (commanded != null && string.Equals(commanded.id, id, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var b in berthed) if (string.Equals(b.id, id, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>A ship of this class is on a shipyard's slips.</summary>
        public static bool Building(string id)
        {
            foreach (var s in sites.Values)
                foreach (var j in s.shipyard)
                    if (string.Equals(j.recipe, id, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Whether `site` could lay down a ship of class `id` now, and if not why. A player
        /// has one ship of each class at most.</summary>
        public static bool CanQueueShip(string site, string id, out string why)
        {
            var table = IndustryTable.Get();
            var spec = table.Site(site);
            var recipe = table.Ship(id);
            why = "";
            if (spec == null) why = "not an industrial site";
            else if (!spec.shipyard) why = "no shipyard here";
            else if (recipe == null) why = "no such ship";
            else if (Owns(id)) why = "you already have one";
            else if (Building(id)) why = "one is already being built";
            else if (Site(site).shipyard.Count >= YardLimit) why = "the slips are full";
            else
            {
                // (an item listed twice is needed twice over)
                var stock = Site(site).stock;
                foreach (var kv in Totals(recipe.inputs))
                    if (!stock.Has(kv.Key, kv.Value))
                    {
                        why = $"needs {table.Amount(kv.Key, kv.Value)} of {table.Name(kv.Key).ToLower()} in storage (has {table.Amount(kv.Key, stock.Amount(kv.Key))})";
                        break;
                    }
            }
            return why == "";
        }

        /// <summary>A recipe's inputs added up by item.</summary>
        static Dictionary<string, float> Totals(Ingredient[] inputs)
        {
            var t = new Dictionary<string, float>();
            if (inputs != null)
                foreach (var i in inputs) t[i.item] = (t.TryGetValue(i.item, out float n) ? n : 0f) + i.tonnes;
            return t;
        }

        /// <summary>Lay down a ship: its parts and materials leave storage now.</summary>
        public static bool QueueShip(string site, string id, out string why)
        {
            if (!CanQueueShip(site, id, out why)) return false;
            var recipe = IndustryTable.Get().Ship(id);
            var s = Site(site);
            foreach (var i in recipe.inputs) s.stock.Take(i.item, i.tonnes);
            s.shipyard.Add(new IndustryJob { recipe = recipe.id, left = recipe.seconds, total = recipe.seconds });
            Changed?.Invoke();
            return true;
        }

        /// <summary>Stop building a ship (the last one by default): its parts go back into storage.</summary>
        public static bool CancelShip(string site, int index = -1)
        {
            var s = Site(site);
            if (s.shipyard.Count == 0) return false;
            if (index < 0 || index >= s.shipyard.Count) index = s.shipyard.Count - 1;
            var job = s.shipyard[index];
            s.shipyard.RemoveAt(index);
            var recipe = IndustryTable.Get().Ship(job.recipe);
            if (recipe != null) foreach (var i in recipe.inputs) s.stock.Add(i.item, i.tonnes);
            Changed?.Invoke();
            return true;
        }

        static bool RunYard(SiteState s, float dt)
        {
            bool done = false;
            while (dt > 0f && s.shipyard.Count > 0)
            {
                var job = s.shipyard[0];
                float step = Mathf.Min(dt, job.left);
                job.left -= step;
                dt -= step;
                if (job.left > 1e-4f) break;
                s.shipyard.RemoveAt(0);
                Launch(job.recipe, s.name);
                done = true;
                Finished?.Invoke(s.name, job.recipe, 1f);
            }
            return done;
        }

        /// <summary>A ship is finished: it is berthed at its yard, with nobody aboard yet (its crew
        /// comes over from the ship the player commands, or is recruited).</summary>
        public static void Launch(string id, string site)
        {
            berthed.Add(new BuiltShip { id = id, site = site });
            if (CrewOf(id) == null) crews[id] = 0f;
            Changed?.Invoke();
        }

        /// <summary>Take command of a ship of class `id` berthed at `site` (anywhere when null).
        /// The ship commanded until now (if it was a built one) is berthed in its place, at that
        /// yard (or, from anywhere, at its own). False if there is no such ship.</summary>
        public static bool Command(string id, string site = null)
        {
            if (commanded != null && string.Equals(commanded.id, id, StringComparison.OrdinalIgnoreCase)) return true;
            int i = berthed.FindIndex(b => string.Equals(b.id, id, StringComparison.OrdinalIgnoreCase) && (site == null || b.site == site));
            if (i < 0) return false;
            var ship = berthed[i];
            berthed.RemoveAt(i);
            if (commanded != null) berthed.Add(new BuiltShip { id = commanded.id, site = site ?? commanded.site });
            commanded = ship;
            Changed?.Invoke();
            return true;
        }

        /// <summary>The player flies a frigate again: the ship commanded (if a built one) goes back
        /// to its berth (at `site`, or the yard it came from).</summary>
        public static void ReleaseCommand(string site = null)
        {
            if (commanded == null) return;
            berthed.Add(new BuiltShip { id = commanded.id, site = site ?? commanded.site });
            commanded = null;
            Changed?.Invoke();
        }

        /// <summary>The ships built, to save: those berthed and the one commanded.</summary>
        public static (List<BuiltShip> berthed, BuiltShip commanded) FleetSnapshot()
        {
            var b = new List<BuiltShip>();
            foreach (var x in berthed) b.Add(new BuiltShip { id = x.id, site = x.site });
            return (b, commanded != null ? new BuiltShip { id = commanded.id, site = commanded.site } : null);
        }

        public static void RestoreFleet(List<BuiltShip> savedBerthed, BuiltShip savedCommanded)
        {
            berthed.Clear();
            commanded = null;
            if (savedBerthed != null) foreach (var b in savedBerthed) if (b != null && !string.IsNullOrEmpty(b.id)) berthed.Add(b);
            if (savedCommanded != null && !string.IsNullOrEmpty(savedCommanded.id)) commanded = savedCommanded;
        }

        // ------------------------------------------------------------ crew

        /// <summary>People lost at a place (a strike from orbit): fewer recruits waiting there.</summary>
        public static void Lose(string place, float n)
        {
            if (string.IsNullOrEmpty(place) || n <= 0f) return;
            pools[place] = Mathf.Max(0f, Pool(place) - n);
            Changed?.Invoke();
        }

        /// <summary>Recruits waiting at a place (its whole pool the first time; 0 where none recruit).</summary>
        public static float Pool(string place)
        {
            if (pools.TryGetValue(place ?? "", out float n)) return n;
            var p = CrewTable.Get().Place(place);
            return p != null && p.Recruits ? p.recruits : 0f;
        }

        /// <summary>Recruit up to `want` at a place into a crew, paid from the hold's credits: as
        /// many as there are recruits, room aboard and credits for. Returns how many signed on.</summary>
        public static int Recruit(string place, Crew crew, CargoHold hold, int want, out string why)
        {
            var table = CrewTable.Get();
            var p = table.Place(place);
            why = "";
            if (p == null || !p.Recruits) { why = "nobody recruits here"; return 0; }
            if (crew == null) { why = "no crew to join"; return 0; }
            if (hold == null) { why = "no hold to pay from"; return 0; }
            int n = Mathf.Min(want, Mathf.Min(crew.Missing, Mathf.FloorToInt(Pool(place) + 1e-4f)));
            // (as many as the credits cover, by the same reckoning Spend pays by)
            if (table.recruitCost > 0f) n = Mathf.Min(n, Mathf.FloorToInt((hold.Credits + 1e-3f) / table.recruitCost));
            if (n <= 0)
            {
                why = crew.Missing == 0 ? "the crew is complete" : Pool(place) < 1f ? "nobody is waiting to sign on" : "not enough credits";
                return 0;
            }
            if (!hold.Spend(n * table.recruitCost)) { why = "not enough credits"; return 0; }
            n = crew.Add(n);
            pools[place] = Pool(place) - n;
            Changed?.Invoke();
            return n;
        }

        /// <summary>The crew a frigate class had aboard (what was saved), or null.</summary>
        public static float? CrewOf(string frigate) => crews.TryGetValue(frigate ?? "", out float n) ? n : (float?)null;

        public static void SetCrew(string frigate, float aboard)
        {
            crews[frigate ?? ""] = aboard;
            Changed?.Invoke();
        }

        // ------------------------------------------------------------ fittings

        /// <summary>The parts fitted to a frigate class (empty if none).</summary>
        public static List<string> FittedTo(string frigate)
        {
            frigate ??= "";
            if (!fittings.TryGetValue(frigate, out var list)) fittings[frigate] = list = new List<string>();
            return list;
        }

        public static IEnumerable<KeyValuePair<string, List<string>>> AllFittings => fittings;

        // ------------------------------------------------------------ saving

        /// <summary>Everything, to save.</summary>
        public static (List<SiteState> sites, List<IndustrySave.Fitting> fittings) Snapshot()
        {
            var fs = new List<IndustrySave.Fitting>();
            foreach (var kv in fittings) fs.Add(new IndustrySave.Fitting { frigate = kv.Key, parts = new List<string>(kv.Value) });
            // (a site with nothing stored and nothing queued needn't be saved)
            var kept = new List<SiteState>();
            foreach (var s in sites.Values) if (s.stock.items.Count > 0 || s.refining.Count > 0 || s.building.Count > 0 || s.shipyard.Count > 0) kept.Add(s);
            return (kept, fs);
        }

        /// <summary>The recruit pools and the frigates' crews, to save.</summary>
        public static (List<IndustrySave.Named> pools, List<IndustrySave.Named> crews) PeopleSnapshot()
        {
            var p = new List<IndustrySave.Named>();
            foreach (var kv in pools) p.Add(new IndustrySave.Named { name = kv.Key, value = kv.Value });
            var c = new List<IndustrySave.Named>();
            foreach (var kv in crews) c.Add(new IndustrySave.Named { name = kv.Key, value = kv.Value });
            return (p, c);
        }

        public static void RestorePeople(List<IndustrySave.Named> savedPools, List<IndustrySave.Named> savedCrews)
        {
            pools.Clear();
            crews.Clear();
            if (savedPools != null) foreach (var n in savedPools) if (n != null && n.name != null) pools[n.name] = n.value;
            if (savedCrews != null) foreach (var n in savedCrews) if (n != null && n.name != null) crews[n.name] = n.value;
        }

        /// <summary>Put back what was saved (replacing everything).</summary>
        public static void Restore(List<SiteState> saved, List<IndustrySave.Fitting> fitted)
        {
            sites.Clear();
            fittings.Clear();
            if (saved != null)
                foreach (var s in saved)
                {
                    if (s == null || string.IsNullOrEmpty(s.name)) continue;
                    s.stock ??= new Stock();
                    s.stock.items ??= new List<Stock.Entry>();
                    s.refining ??= new List<IndustryJob>();
                    s.building ??= new List<IndustryJob>();
                    s.shipyard ??= new List<IndustryJob>();
                    sites[s.name] = s;
                }
            if (fitted != null)
                foreach (var f in fitted)
                    if (f != null && f.frigate != null) fittings[f.frigate] = f.parts ?? new List<string>();
        }

        /// <summary>Forget everything (a new game).</summary>
        public static void Clear()
        {
            sites.Clear();
            fittings.Clear();
            pools.Clear();
            crews.Clear();
            berthed.Clear();
            commanded = null;
            Changed?.Invoke();
        }
    }
}
