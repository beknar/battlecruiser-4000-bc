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
                if (t.position.y < -50000f || t.GetComponentInParent<PlanetSurface>() != null) continue;   // on a planet: each game's own
                string id = PathOf(t);
                string unique = id;
                for (int n = 2; ents.ContainsKey(unique); n++) unique = $"{id}#{n}";
                var e = new Ent { id = unique, t = t, h = t.GetComponent<Health>(), pilot = t.GetComponent<CombatPilot>(), shields = t.GetComponentsInChildren<Shield>(true) };
                ents[unique] = e;
                entByTransform[t] = e;
                if (e.h != null) entByHealth[e.h] = e;
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
                foreach (var e in ents.Values)
                {
                    if (e.t == null) continue;
                    foreach (var b in e.t.GetComponentsInChildren<MonoBehaviour>(true))
                        if (b != null && b.enabled && !Keep.Contains(b.GetType())) { b.enabled = false; e.off.Add(b); }
                    e.puppet = e.t.gameObject.AddComponent<NetPuppet>();
                    e.ts0 = e.ts1 = -1;
                }
                // the things that start fights (ambushes, defence grids) are the host's too
                foreach (var b in FindObjectsByType<EncounterGroup>(FindObjectsSortMode.None)) Off(b);
                foreach (var b in FindObjectsByType<DefenseGrid>(FindObjectsSortMode.None)) Off(b);
                foreach (var b in FindObjectsByType<AmbushDirector>(FindObjectsSortMode.None)) Off(b);
                Debug.Log("[Co-op] the enemies are the host's: puppets here");
            }
            else
            {
                foreach (var e in ents.Values)
                {
                    foreach (var b in e.off) if (b != null) b.enabled = true;
                    e.off.Clear();
                    // a main gun that was only showing the host's shots does damage again
                    if (e.t != null) foreach (var beam in e.t.GetComponentsInChildren<MainBeam>(true)) beam.cosmetic = false;
                    if (e.puppet != null) Destroy(e.puppet);
                    e.puppet = null;
                    // one the host had destroyed comes back in its own time, as it would have
                    if (e.h != null && e.h.Dead && e.h.respawnDelay > 0f) StartCoroutine(RespawnLater(e.h, e.h.respawnDelay));
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
                if (e.h != null) e.h.ScaleTo(k);
                if (e.shields != null) foreach (var s in e.shields) if (s != null) s.ScaleTo(k);
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

        void SendEntities()
        {
            var list = new List<CoopEntity>(ents.Count);
            foreach (var e in ents.Values)
            {
                if (e.t == null) { list.Add(new CoopEntity { id = e.id, gone = true, dead = true }); continue; }
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
                });
            }
            Client.Send(new EntitiesMsg { ts = Client.ServerNow, e = list.ToArray() });
        }

        void ApplyEntities(CoopMsg m)
        {
            if (m.e == null) return;
            foreach (var s in m.e)
            {
                if (s == null || s.id == null || !ents.TryGetValue(s.id, out var e) || e.t == null) continue;
                if (e.h != null)
                {
                    if (s.dead) e.h.NetKill();
                    else
                    {
                        if (e.h.Dead) e.h.NetRespawn();
                        e.h.NetSetHull(s.hu * e.h.maxHealth);
                    }
                }
                if (s.gone || s.dead) continue;
                if (e.pilot != null) e.pilot.NetLurk(s.lurk);
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
