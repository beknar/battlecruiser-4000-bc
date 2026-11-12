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

    /// <summary>One site's storage and its two production lines (the refinery's and the factory's).</summary>
    [Serializable]
    public class SiteState
    {
        public string name;
        public Stock stock = new Stock();
        public List<IndustryJob> refining = new List<IndustryJob>();
        public List<IndustryJob> building = new List<IndustryJob>();
    }

    /// <summary>
    /// The state of the industry: each site's storage and queues, and what each frigate has had
    /// fitted. It outlives the level (a restart keeps it) and is saved to disk (IndustrySave).
    /// A job takes its inputs from the site's storage when it is queued (cancelling it puts them
    /// back), runs for its recipe's seconds of game time, and puts what it makes into the same
    /// storage. A site's refinery and factory each work through their own queue, one job at a time,
    /// whether or not anyone is there.
    /// </summary>
    public static class IndustryLedger
    {
        /// <summary>Jobs waiting at one line, at most.</summary>
        public const int QueueLimit = 12;

        static readonly Dictionary<string, SiteState> sites = new Dictionary<string, SiteState>();
        static readonly Dictionary<string, List<string>> fittings = new Dictionary<string, List<string>>();

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
            return (new List<SiteState>(sites.Values), fs);
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
            Changed?.Invoke();
        }
    }
}
