using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarrierGame.Demo
{
    /// <summary>The enemies in co-op: which they are (the same in every game), handing them to the host
    /// and back, the host sending them and the others' puppets following, and making them
    /// tougher for more players.</summary>
    public partial class CoopWorld
    {
        // ------------------------------------------------------------------ the enemies

        /// <summary>Every enemy out in space, by its place in the level (the same in every game).</summary>
        void Register()
        {
            registered = true;
            foreach (var info in TargetInfo.All.ToArray())
            {
                if (info == null || !info.Hostile) continue;
                var t = info.transform;
                if (t.GetComponentInParent<PlanetSurface>() != null) continue;   // on a planet: each game's own
                string id = PathOf(t);
                string unique = id;
                for (int n = 2; ents.ContainsKey(unique); n++) unique = $"{id}#{n}";
                AddEnt(unique, t, null);
            }
            Debug.Log($"[Co-op] {ents.Count} enemies in '{level}'");
        }

        static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (var x = t; x != null; x = x.parent) parts.Add(x.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>The enemy a transform belongs to (it or a parent), if any.</summary>
        Ent EntOf(Transform t)
        {
            for (var x = t; x != null; x = x.parent) if (entByTransform.TryGetValue(x, out var e)) return e;
            return null;
        }

        /// <summary>Hand the enemies over to the host (puppets here), or take them back.</summary>
        void SetPuppeting(bool on)
        {
            if (on == puppeting) return;
            puppeting = on;
            if (on)
            {
                // (what this game made during play on its own is the host's to have now, not
                // ours: gone, or it would hang there, a target the host doesn't know of)
                foreach (var e in ents.Values.Where(x => !string.IsNullOrEmpty(x.key)).ToList())
                {
                    RemoveEnt(e);
                    if (e.t != null) Destroy(e.t.gameObject);
                }
                foreach (var e in ents.Values)
                {
                    if (e.t == null) continue;
                    Puppet(e);
                }
                // the things that start fights (ambushes, defence grids) are the host's too
                // (and each faction's fleet: HostOnly)
                foreach (var type in HostOnly)
                    foreach (var o in FindObjectsByType(type, FindObjectsSortMode.None)) Off(o as Behaviour);
                Debug.Log("[Co-op] the enemies are the host's: puppets here");
            }
            else
            {
                // (left co-op: the host's drones and the like are the host's, gone with it; made
                // host: they are this game's now, and fly on)
                if (!On) DropRuntimePuppets();
                foreach (var e in ents.Values)
                {
                    foreach (var b in e.off) if (b != null) b.enabled = true;
                    e.off.Clear();
                    // a main gun that was only showing the host's shots does damage again
                    if (e.t != null) foreach (var beam in e.t.GetComponentsInChildren<MainBeam>(true)) beam.cosmetic = false;
                    if (e.puppet != null) Destroy(e.puppet);
                    e.puppet = null;
                    // one the host had destroyed comes back in its own time, as it would have
                    // (one whose location spawns it is left to its SpawnDirector)
                    if (e.h != null && e.h.Dead && e.h.respawnDelay > 0f && !e.h.respawnHeld) StartCoroutine(RespawnLater(e.h, e.h.respawnDelay));
                }
                foreach (var b in directorsOff) if (b != null) b.enabled = true;
                directorsOff.Clear();
                Debug.Log("[Co-op] the enemies run here");
            }
        }

        void Off(Behaviour b) { if (b != null && b.enabled) { b.enabled = false; directorsOff.Add(b); } }

        // ------------------------------------------------------------------ more players, harder enemies

        /// <summary>Players out in space in this level, me included. One who has landed on a
        /// planet is left out: fights down there are each player's own. Every game sees the same
        /// list and the same ships, so they agree. (A handful of players: cheap every frame.)</summary>
        int PlayersHere()
        {
            var landing = PlanetLanding.Instance;
            int n = landing != null && landing.Current != PlanetLanding.State.InSpace ? 0 : 1;
            foreach (var p in Client.Players)
            {
                if (p.id == Client.MyId || p.level != level) continue;
                if (remotes.TryGetValue(p.id, out var r) && r != null && r.Surface.Length > 0) continue;
                n++;
            }
            return Mathf.Max(1, n);
        }

        /// <summary>Every enemy's hull and shields made as strong as `players` call for (see
        /// CoopScaling), each keeping how damaged it is.</summary>
        void ScaleEnemies(int players)
        {
            int was = scaledFor;
            scaledFor = players;
            float k = CoopScaling.Toughness(players);
            foreach (var e in ents.Values)
            {
                if (e.t == null) continue;
                ScaleOne(e, k);
            }
            Debug.Log($"[Co-op] {players} players here: the enemies' hull and shields x{k:0.##}, their hits x{CoopScaling.Firepower(players):0.##}");
            if (players > 1) Notices.Post($"{players} players here: the enemies are {k:0.#}x as tough and hit {CoopScaling.Firepower(players):0.##}x as hard");
            else if (was > 1) Notices.Post("Flying alone: the enemies are back to normal");
        }

        /// <summary>How much harder an enemy's shot, missile or beam is, set as it is fired. Only
        /// the game running the enemies (the host's, or one alone) fires them for real; the hits
        /// it passes on to other players' games are already scaled, so nothing is scaled twice.
        /// A guest's puppets only fire for show (no damage).</summary>
        float EnemyFire(Transform shooter) =>
            scaledFor > 1 && !puppeting && shooter != null && EntOf(shooter) != null ? CoopScaling.Firepower(scaledFor) : 1f;

        static System.Collections.IEnumerator RespawnLater(Health h, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (h != null) h.NetRespawn();
        }

        // ------------------------------------------------------------------ the host's enemies

        /// <summary>Beyond this from every player (m), an enemy is sent once a second, not ten
        /// times: no one is near enough to see it move.</summary>
        public const float FarRange = 60000f;
        int sends;
        int playersSentTo = -1;
        /// <summary>The enemies sent in the last update, and the bytes of the updates so far.</summary>
        public int LastSent { get; private set; }
        public long EntityBytes { get; private set; }
        public int EntityUpdates { get; private set; }
        readonly List<(string id, string key)> goneIds = new List<(string, string)>();

        void SendEntities()
        {
            // every enemy every tenth update, or when a player has come (they need them all at
            // once); the far ones are left out of the others
            int playersNow = Client.Players.Count;
            bool full = sends++ % 10 == 0 || playersNow != playersSentTo;
            playersSentTo = playersNow;
            if (full) MarkFar();
            // (one made during play that was destroyed outright, with no respawn, and never said
            // gone: gone now, said once)
            foreach (var e in ents.Values.Where(x => x.t == null && !string.IsNullOrEmpty(x.key)).ToList())
            {
                RemoveEnt(e);
                goneIds.Add((e.id, e.key));
            }
            var list = new List<CoopEntity>(ents.Count + goneIds.Count);
            foreach (var (id, key) in goneIds) list.Add(new CoopEntity { id = id, gone = true, dead = true, k = key });
            goneIds.Clear();
            foreach (var e in ents.Values)
            {
                if (e.t == null) { list.Add(new CoopEntity { id = e.id, gone = true, dead = true, k = e.key }); continue; }
                if (e.far && !full) continue;
                var np = ToNet(e.t.position);
                Double3 p = np.p;
                var q = e.t.rotation;
                var mover = Motion.Of(e.t);
                Vector3 v = mover != null ? mover.Velocity : Vector3.zero;
                list.Add(new CoopEntity
                {
                    id = e.id, rf = np.rf, x = p.x, y = p.y, z = p.z, qx = q.x, qy = q.y, qz = q.z, qw = q.w, vx = v.x, vy = v.y, vz = v.z,
                    hu = e.h != null ? e.h.Current / Mathf.Max(1f, e.h.maxHealth) : 1f,
                    dead = e.h != null && e.h.Dead,
                    lurk = e.pilot != null && e.pilot.Current == CombatPilot.State.Lurk,
                    sy = ShipSystems.ToWire(e.sys),
                    tr = TractorOf(e),
                    sf = e.shields != null && e.shields.Length > 0 && e.shields[0] != null ? 1f + e.shields[0].Fraction : 0f,
                    sd = e.shields != null && e.shields.Length > 0 && e.shields[0] != null && !e.shields[0].Up,
                    ar = e.t.TryGetComponent(out Armor arm) ? 1f + arm.Integrity : 0f,
                    k = e.key,
                    ck = e.t.TryGetComponent(out Cloak cl) ? (cl.Up ? 2 : 1) : 0,
                    pt = PaintOf(e),
                });
            }
            LastSent = list.Count;
            var msg = new EntitiesMsg { ts = Client.ServerNow, e = list.ToArray() };
            EntityBytes += JsonUtility.ToJson(msg).Length;
            EntityUpdates++;
            Client.Send(msg);
        }

        /// <summary>The player an enemy's targeting laser paints: 1 + their id (0: none).</summary>
        int PaintOf(Ent e)
        {
            if (!e.t.TryGetComponent(out Designator d) || d.Painting == null) return 0;
            if (me != null && d.Painting.root == me.transform.root) return 1 + Client.MyId;
            foreach (var r in remotes.Values) if (r != null && d.Painting.root == r.transform.root) return 1 + r.Id;
            return 0;
        }

        /// <summary>The host's enemy paints a player: painted here too (for my HUD's warning, and
        /// so the paint shows on the right ship).</summary>
        void ShowPaint(Ent e, int playerId)
        {
            Transform target = playerId == Client.MyId ? (me != null ? me.transform : null)
                             : remotes.TryGetValue(playerId, out var r) && r != null ? r.transform : null;
            if (target != null) Designation.Mark(e.t, target, FactionShip.Of(e.t)?.FactionName ?? "");
        }

        /// <summary>Which enemies are far from every player here (and so sent once a second).</summary>
        void MarkFar()
        {
            var near = new List<Vector3>();
            if (me != null) near.Add(me.transform.position);
            foreach (var r in remotes.Values) if (r != null && !r.Hidden) near.Add(r.transform.position);
            float far2 = FarRange * FarRange;
            foreach (var e in ents.Values)
            {
                if (e.t == null) { e.far = false; continue; }
                bool far = true;
                foreach (var p in near) if ((e.t.position - p).sqrMagnitude < far2) { far = false; break; }
                e.far = far;
            }
        }

        // ------------------------------------------------------------------ enemies made during play

        int madeHere;
        /// <summary>This game's own tag for the enemies it makes alone (another game's are never the same).</summary>
        static readonly string RunTag = "s" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
        /// <summary>Enemies made during play, here or as the host's puppets.</summary>
        public int RuntimeEntities => ents.Values.Count(e => !string.IsNullOrEmpty(e.key));

        /// <summary>Gameplay has made an enemy (CoopSpawn.Spawned): the host's (or one alone)
        /// is added to what is sent; a guest makes none of its own.</summary>
        void OnEnemySpawned(GameObject go, string key)
        {
            if (go == null || entByTransform.ContainsKey(go.transform)) return;
            // (alone, it is still listed, so it is sent if co-op starts)
            // (unique even when made alone, before co-op: this game's own tag, not a player id)
            string id = $"rt:{(On ? Client.MyId.ToString() : RunTag)}:{++madeHere}";
            var e = AddEnt(id, go.transform, key);
            if (scaledFor > 1) ScaleOne(e, CoopScaling.Toughness(scaledFor));
        }

        /// <summary>An enemy made during play is gone for good: the others are told, once.</summary>
        void OnEnemyGone(GameObject go)
        {
            if (go == null || !entByTransform.TryGetValue(go.transform, out var e)) return;
            RemoveEnt(e);
            if (IsHost) goneIds.Add((e.id, e.key));
        }

        Ent AddEnt(string id, Transform t, string key)
        {
            var e = new Ent { id = id, t = t, key = key, h = t.GetComponent<Health>(), pilot = t.GetComponent<CombatPilot>(), shields = t.GetComponentsInChildren<Shield>(true), sys = t.GetComponent<ShipSystems>(), beam = t.GetComponent<TractorBeam>() };
            ents[id] = e;
            entByTransform[t] = e;
            if (e.h != null) entByHealth[e.h] = e;
            return e;
        }

        void RemoveEnt(Ent e)
        {
            ents.Remove(e.id);
            // (by reference: a destroyed object compares as null, but is still the key)
            if ((object)e.t != null) entByTransform.Remove(e.t);
            if ((object)e.h != null) entByHealth.Remove(e.h);
        }

        void ScaleOne(Ent e, float k)
        {
            if (e.h != null) e.h.ScaleTo(k);
            if (e.shields != null) foreach (var s in e.shields) if (s != null) s.ScaleTo(k);
        }

        /// <summary>One the host made during play, which this game hasn't got: made here from its
        /// key, a puppet like the rest (null: no such prefab here).</summary>
        Ent MakePuppet(CoopEntity s)
        {
            var prefab = CoopSpawnables.Find(s.k);
            if (prefab == null) return null;
            var t = Instantiate(prefab, FromNet(s.rf ?? "", new Double3(s.x, s.y, s.z)), new Quaternion(s.qx, s.qy, s.qz, s.qw)).transform;
            var e = AddEnt(s.id, t, s.k);
            Puppet(e);
            if (scaledFor > 1) ScaleOne(e, CoopScaling.Toughness(scaledFor));
            return e;
        }

        /// <summary>An enemy made a puppet: everything it does on its own off, the host's game moving it.</summary>
        void Puppet(Ent e)
        {
            foreach (var b in e.t.GetComponentsInChildren<MonoBehaviour>(true))
                if (b != null && b.enabled && !Keep.Contains(b.GetType())) { b.enabled = false; e.off.Add(b); }
            e.puppet = e.t.gameObject.AddComponent<NetPuppet>();
            e.ts0 = e.ts1 = -1;
        }

        /// <summary>Out of co-op: the host's enemies made during play go with it.</summary>
        void DropRuntimePuppets()
        {
            foreach (var e in ents.Values.Where(x => !string.IsNullOrEmpty(x.key)).ToList())
            {
                RemoveEnt(e);
                if (e.t != null) Destroy(e.t.gameObject);
            }
        }

        void ApplyEntities(CoopMsg m)
        {
            if (m.e == null) return;
            foreach (var s in m.e)
            {
                if (s == null || s.id == null) continue;
                if (!ents.TryGetValue(s.id, out var e))
                {
                    // one the host made during play: made here (unless it has gone already)
                    if (string.IsNullOrEmpty(s.k) || s.gone || s.dead || (e = MakePuppet(s)) == null) continue;
                }
                else if (s.gone && !string.IsNullOrEmpty(e.key))
                {
                    // the host's own is gone for good: so is this one
                    RemoveEnt(e);
                    if (e.t != null) Destroy(e.t.gameObject);
                    continue;
                }
                else if (e.t == null && !string.IsNullOrEmpty(e.key) && !s.dead)
                {
                    // lost here (destroyed when the host said it died) but alive again there: again here
                    RemoveEnt(e);
                    if (string.IsNullOrEmpty(s.k) || (e = MakePuppet(s)) == null) continue;
                }
                if (e.t == null) continue;
                if (e.h != null)
                {
                    if (s.dead) e.h.NetKill();
                    else
                    {
                        if (e.h.Dead) e.h.NetRespawn();
                        e.h.NetSetHull(s.hu * e.h.maxHealth);
                    }
                }
                if (e.sys != null) e.sys.NetSet(s.sy);
                // (its armour as the host has it: a Vrask ship sheds the same plates here)
                if (s.ar > 0f && e.t.TryGetComponent(out Armor arm)) arm.NetSetIntegrity(s.ar - 1f);
                ShowTractor(e, s.gone || s.dead ? 0 : s.tr);
                if (s.sf > 0f && e.shields != null && e.shields.Length > 0 && e.shields[0] != null) e.shields[0].NetSet(s.sf - 1f, !s.sd);
                if (s.gone || s.dead) continue;
                if (e.pilot != null) e.pilot.NetLurk(s.lurk);
                if (s.ck > 0 && e.t.TryGetComponent(out Cloak cloak)) cloak.NetSet(s.ck == 2);
                if (s.pt > 0) ShowPaint(e, s.pt - 1);
                // the last two updates, to go between
                e.ts0 = e.ts1; e.p0 = e.p1; e.q0 = e.q1; e.rf0 = e.rf1;
                e.ts1 = m.ts; e.p1 = new Double3(s.x, s.y, s.z); e.rf1 = s.rf ?? ""; e.q1 = new Quaternion(s.qx, s.qy, s.qz, s.qw); e.v1 = new Vector3(s.vx, s.vy, s.vz);
                if (e.ts0 < 0) { e.ts0 = e.ts1; e.p0 = e.p1; e.q0 = e.q1; e.rf0 = e.rf1; }
            }
        }

        void DrivePuppets(double t)
        {
            foreach (var e in ents.Values)
            {
                if (e.t == null || e.ts1 < 0 || (e.h != null && e.h.Dead)) continue;
                Vector3 p;
                Quaternion q;
                Vector3 w1 = FromNet(e.rf1, e.p1);
                if (t >= e.ts1)
                {
                    float ahead = (float)Math.Min(0.4, t - e.ts1);
                    p = w1 + e.v1 * ahead;
                    q = e.q1;
                }
                else
                {
                    float k = e.ts1 > e.ts0 ? Mathf.Clamp01((float)((t - e.ts0) / (e.ts1 - e.ts0))) : 1f;
                    // (each end where its reach is now, then between them)
                    p = e.rf0 == e.rf1 ? Vector3.Lerp(FromNet(e.rf0, e.p0), w1, k) : w1;
                    q = Quaternion.Slerp(e.q0, e.q1, k);
                }
                e.t.SetPositionAndRotation(p, q);
                if (e.puppet != null) e.puppet.velocity = e.v1;
            }
        }
    }
}
