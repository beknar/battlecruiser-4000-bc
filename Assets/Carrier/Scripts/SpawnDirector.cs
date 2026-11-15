using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// When the enemies come back, location by location. Each band lying in wait (an
    /// EncounterGroup: the raiders in a field, a pirate cruiser, a capital ship battle group) is
    /// a location. Every `spawnEvery` seconds (5 minutes) a location's destroyed enemies spawn
    /// there again, back in their hiding places, so it fills up again over time. But once every
    /// one of a location's enemies has been destroyed, none spawn there for `quietAfterCleared`
    /// seconds (10 minutes): the place has been cleared, and stays clear a while. Then they all
    /// come back, and the five-minute spawns go on.
    ///
    /// It replaces each of their own respawn timers (Health.respawnHeld). In co-op it runs in
    /// the host's game only, as the enemies do; the others see them come back as the host has them.
    /// </summary>
    public class SpawnDirector : MonoBehaviour
    {
        [Tooltip("Seconds between a location's spawns (its destroyed enemies back).")]
        public float spawnEvery = 300f;
        [Tooltip("Seconds a location stays empty once all its enemies have been destroyed.")]
        public float quietAfterCleared = 600f;

        public static SpawnDirector Instance { get; private set; }

        /// <summary>One location: its enemies, and when they next spawn.</summary>
        public class Site
        {
            public string name;
            public readonly List<Health> enemies = new List<Health>();
            public float nextSpawn;
            /// <summary>Cleared (all destroyed): no spawns till then.</summary>
            public float quietUntil = -1f;
            public int spawns, clearances;
            public bool Cleared => quietUntil >= 0f;
            public int Alive => enemies.Count(h => h != null && !h.Dead);
        }

        public readonly List<Site> Sites = new List<Site>();
        float nextLook;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            foreach (var g in FindObjectsByType<EncounterGroup>(FindObjectsSortMode.None))
            {
                if (g == null || g.pilots == null) continue;
                var site = new Site { name = g.groupName, nextSpawn = Time.time + spawnEvery };
                foreach (var p in g.pilots)
                {
                    var h = p != null ? p.GetComponent<Health>() : null;
                    // (only those that come back at all: the others are gone for good)
                    if (h == null || h.respawnDelay <= 0f) continue;
                    h.respawnHeld = true;
                    site.enemies.Add(h);
                }
                if (site.enemies.Count > 0) Sites.Add(site);
            }
            if (Sites.Count > 0)
                Debug.Log($"[Spawns] {Sites.Count} locations, every {spawnEvery / 60f:0.#} min, quiet {quietAfterCleared / 60f:0.#} min once cleared: " +
                          string.Join(", ", Sites.Select(s => $"{s.name} ({s.enemies.Count})")));
        }

        void Update()
        {
            if (Time.time < nextLook) return;
            nextLook = Time.time + 1f;
            float now = Time.time;
            foreach (var s in Sites)
            {
                s.enemies.RemoveAll(h => h == null);
                if (s.enemies.Count == 0) continue;
                int alive = s.Alive;
                if (alive == 0 && !s.Cleared)
                {
                    // every one destroyed: the place stays clear a while
                    s.quietUntil = now + quietAfterCleared;
                    s.clearances++;
                    Debug.Log($"[Spawns] {s.name} cleared: none for {quietAfterCleared / 60f:0.#} min");
                    if (NearPlayer(s, 60000f)) Notices.Post($"{s.name} destroyed: the area is clear for {quietAfterCleared / 60f:0} minutes");
                    continue;
                }
                if (s.Cleared)
                {
                    if (now < s.quietUntil) continue;
                    // the quiet is over: they all come back, and the spawns go on from now
                    s.quietUntil = -1f;
                    Spawn(s, now);
                    continue;
                }
                if (now >= s.nextSpawn) Spawn(s, now);
            }
        }

        void Spawn(Site s, float now)
        {
            s.nextSpawn = now + spawnEvery;
            int back = 0;
            foreach (var h in s.enemies)
                if (h != null && h.Dead) { h.RespawnNow(); back++; }
            if (back == 0) return;
            s.spawns++;
            Debug.Log($"[Spawns] {s.name}: {back} back");
            if (NearPlayer(s, 60000f)) Notices.Post($"{s.name}: {back} more hostile{(back == 1 ? "" : "s")} have arrived");
        }

        static bool NearPlayer(Site s, float range)
        {
            var me = PlayerShips.Primary;
            if (me == null) return false;
            foreach (var h in s.enemies)
                if (h != null && (h.transform.position - me.position).sqrMagnitude < range * range) return true;
            return false;
        }

        /// <summary>For the self-test: spawn time now, at every location (as if 5 minutes had passed).</summary>
        public void SpawnAllNow()
        {
            foreach (var s in Sites) if (!s.Cleared) Spawn(s, Time.time);
        }

        /// <summary>For the self-test: the quiet of every cleared location over now.</summary>
        public void EndQuietNow()
        {
            foreach (var s in Sites) if (s.Cleared) s.quietUntil = Time.time;
        }
    }
}
