using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CarrierGame
{
    /// <summary>The ground streamed in around the ship: which areas are wanted, worked out off the main
    /// thread (or a few rows a frame on the web), put in place one a frame, thrown away when far.</summary>
    public partial class PlanetSurface
    {
        /// <summary>Build the areas within `radius` of a point straight away (behind the screen
        /// fade on landing), at full detail. Takes at most `maxSeconds`.</summary>
        public IEnumerator Preload(Vector3 p, float radius, float maxSeconds)
        {
            float until = Time.realtimeSinceStartup + maxSeconds;
            var want = new List<(int, int)>();
            TileOf(p, out int I0, out int J0);
            for (int dj = -2; dj <= 2; dj++)
                for (int di = -2; di <= 2; di++)
                    if (DistanceToTile(p, I0 + di, J0 + dj) < radius) want.Add((I0 + di, J0 + dj));
            want.Sort((a, b) => DistanceToTile(p, a.Item1, a.Item2).CompareTo(DistanceToTile(p, b.Item1, b.Item2)));
            var list = new List<TileJob>();
            foreach (var (i, j) in want)
            {
                // a settlement's layout, loaded now while the screen is dark rather than mid-flight
                World.CanonicalTile(i, j, out int ci, out int cj);
                var f = World.Features(ci, cj);
                if (f != null && f.baseName != null && f.site != SiteKind.Base) Layout(f.baseName);
                var job = new TileJob(World, i, j, 0);
                list.Add(job);
                if (Threads) System.Threading.ThreadPool.QueueUserWorkItem(_ => job.RunAll());
            }
            foreach (var job in list)
            {
                if (!Threads)
                {
                    // no threads in a web page: work it out now (the screen is faded out)
                    var it = job.Steps();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (true)
                    {
                        try { if (!it.MoveNext()) break; }
                        catch (System.Exception e) { job.error = e; break; }
                        if (sw.ElapsedMilliseconds > 200) { yield return null; sw.Restart(); }
                    }
                    job.done = true;
                }
                while (!job.done && Time.realtimeSinceStartup < until) yield return null;
                if (!job.done) { job.cancelled = true; continue; }   // it gives its buffers back when it stops
                if (job.error != null) { Debug.LogError($"[PlanetSurface] {World.name}: working out area {job.I},{job.J} ({job.ci},{job.cj}) failed: {job.error}"); job.Release(); continue; }
                Apply(job);
            }
            nextPlan = 0f;
        }

        void Update() => Tick();

        void Tick()
        {
            if (Ship == null || World == null || !Running) return;
            ShiftIfFar();
            CollectJobs();
            // put a finished area in place: one in full detail a frame, or a few distant ones
            if (ready.Count > 0)
            {
                Vector3 sp = Ship.position;
                ready.Sort((a, b) => DistanceToTile(sp, a.I, a.J).CompareTo(DistanceToTile(sp, b.I, b.J)));
                int budget = 3;
                while (ready.Count > 0 && budget > 0)
                {
                    var job = ready[0];
                    ready.RemoveAt(0);
                    if (job.error != null) { Debug.LogError($"[PlanetSurface] {World.name}: working out area {job.I},{job.J} ({job.ci},{job.cj}) failed: {job.error}"); job.Release(); continue; }
                    if (DistanceToTile(sp, job.I, job.J) > unloadRadius) { job.Release(); continue; }
                    if (tiles.TryGetValue(Key(job.I, job.J), out var have) && have.lod <= job.lod) { job.Release(); continue; }
                    Apply(job);
                    budget -= job.lod == 0 ? 3 : 1;
                }
            }
            if (Time.time >= nextPlan)
            {
                nextPlan = Time.time + 0.25f;
                Plan();
            }
            Follow();
        }

        /// <summary>How far a point is from the nearest edge of an area (0 inside it), across the ground.</summary>
        float DistanceToTile(Vector3 p, int I, int J)
        {
            Vector3 c = Corner(I, J);
            float dx = Mathf.Max(c.x - p.x, 0f, p.x - (c.x + T));
            float dz = Mathf.Max(c.z - p.z, 0f, p.z - (c.z + T));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Throw away the areas the ship has left behind, and start building the ones it
        /// is coming up to, nearest first.</summary>
        void Plan()
        {
            Vector3 p = Ship.position;
            var drop = new List<long>();
            foreach (var kv in tiles)
                if (DistanceToTile(p, kv.Value.I, kv.Value.J) > unloadRadius) drop.Add(kv.Key);
            foreach (var k in drop)
            {
                var gone = tiles[k];
                DestroyTile(gone);
                tiles.Remove(k);
                HandOverGarrison(gone);
                TilesUnloaded++;
                ringDirty = true;
            }
            var stale = new List<(long, int)>();
            foreach (var kv in jobs)
                if (DistanceToTile(p, kv.Value.I, kv.Value.J) > unloadRadius) { kv.Value.cancelled = true; stale.Add(kv.Key); }
            foreach (var k in stale)
            {
                var sj = jobs[k];
                jobs.Remove(k);
                // a web job only runs when stepped, so a cancelled one has stopped; a thread's job
                // gives its buffers back itself when it stops (or now, if it already has)
                if (!Threads) sj.done = true;
                sj.Release();
            }
            if (webJob != null && webJob.cancelled) { webJob = null; webSteps = null; }

            TileOf(p, out int I0, out int J0);
            int reach = Mathf.CeilToInt(farRadius / T) + 1;
            var wanted = new List<(float, int, int, int)>();
            for (int dj = -reach; dj <= reach; dj++)
                for (int di = -reach; di <= reach; di++)
                {
                    int I = I0 + di, J = J0 + dj;
                    float d = DistanceToTile(p, I, J);
                    int lod = d < nearRadius ? 0 : d < farRadius ? 1 : -1;
                    if (lod < 0) continue;
                    if (tiles.TryGetValue(Key(I, J), out var have) && have.lod <= lod) continue;
                    if (jobs.ContainsKey((Key(I, J), lod))) continue;
                    if (lod == 1 && jobs.ContainsKey((Key(I, J), 0))) continue;
                    if (ready.Exists(r => r.I == I && r.J == J && r.lod <= lod)) continue;
                    wanted.Add((d + (lod == 0 ? 0f : 2500f), I, J, lod));
                }
            wanted.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            foreach (var (_, I, J, lod) in wanted)
            {
                if (jobs.Count >= maxJobs) break;
                var job = new TileJob(World, I, J, lod);
                jobs[(Key(I, J), lod)] = job;
                if (Threads) System.Threading.ThreadPool.QueueUserWorkItem(_ => job.RunAll());
            }
        }

        void CollectJobs()
        {
            if (!Threads)
            {
                // no threads in a web page: a few milliseconds of the current job each frame
                if (webJob == null)
                    foreach (var j in jobs.Values) { if (!j.done && !j.cancelled) { webJob = j; webSteps = j.Steps(); break; } }
                if (webJob != null)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        while (sw.ElapsedMilliseconds < 6)
                            if (!webSteps.MoveNext()) { webJob.done = true; break; }
                    }
                    catch (System.Exception e) { webJob.error = e; webJob.done = true; }
                    if (webJob.done) { webJob = null; webSteps = null; }
                }
            }
            List<(long, int)> finished = null;
            foreach (var kv in jobs)
                if (kv.Value.done) (finished ??= new List<(long, int)>()).Add(kv.Key);
            if (finished == null) return;
            foreach (var k in finished)
            {
                var j = jobs[k];
                jobs.Remove(k);
                if (!j.cancelled) ready.Add(j);
                else j.Release();
            }
        }
    }
}
