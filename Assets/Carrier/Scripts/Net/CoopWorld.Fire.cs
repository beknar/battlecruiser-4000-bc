using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarrierGame.Demo
{
    /// <summary>Shots, missiles and beams in co-op: sent as they are fired, and shown (harmless) in the
    /// other games.</summary>
    public partial class CoopWorld
    {
        // ------------------------------------------------------------------ shots, missiles, beams

        /// <summary>Who fired: "p&lt;id&gt;" for my ship, or an enemy's id if I'm the host.</summary>
        string SourceOf(Transform owner)
        {
            if (owner == null || me == null) return null;
            if (owner.root == me.transform.root) return "p" + Client.MyId;
            if (!IsHost) return null;
            var e = EntOf(owner);
            return e != null ? e.id : null;
        }

        /// <summary>No more than so many shots a second from one ship (a fast gun or the CIWS).</summary>
        bool Budget(string src)
        {
            fxBudget.TryGetValue(src, out float next);
            if (Time.unscaledTime < next) return false;
            fxBudget[src] = Time.unscaledTime + 1f / 25f;
            return true;
        }

        static string BaseName(string n) => n.EndsWith("(Clone)") ? n.Substring(0, n.Length - 7).Trim() : n;

        void OnShot(Projectile p)
        {
            if (p == null) return;
            p.damage *= EnemyFire(p.owner);
            if (!On || p.damage <= 0f) return;          // a shown shot (no damage) isn't news
            string src = SourceOf(p.owner);
            if (src == null || !Budget(src)) return;
            var np = ToNet(p.transform.position);
            Double3 pos = np.p;
            Vector3 d = p.transform.forward;
            Client.Send(new FireMsg { src = src, k = CoopWire.Shot, pf = BaseName(p.name), rf = np.rf, x = pos.x, y = pos.y, z = pos.z, dx = d.x, dy = d.y, dz = d.z });
            ShotsSent++;
        }

        void OnMissile(Missile m)
        {
            if (m == null) return;
            m.damage *= EnemyFire(m.owner);
            if (!On || m.damage <= 0f) return;
            string src = SourceOf(m.owner);
            if (src == null) return;
            var np = ToNet(m.transform.position);
            Double3 pos = np.p;
            Vector3 d = m.transform.forward;
            Client.Send(new FireMsg { src = src, k = CoopWire.Missile, pf = BaseName(m.name), tg = IdOf(m.target), rf = np.rf, x = pos.x, y = pos.y, z = pos.z, dx = d.x, dy = d.y, dz = d.z });
            ShotsSent++;
        }

        void OnBeam(MainBeam b)
        {
            if (b == null) return;
            b.damageScale = EnemyFire(b.transform);      // (its owner is the top of the level, for a station's)
            if (!On) return;
            string src = SourceOf(b.transform);
            if (src == null) return;
            Vector3 d = b.BeamDirection;
            Client.Send(new FireMsg { src = src, k = CoopWire.Beam, dx = d.x, dy = d.y, dz = d.z });
            ShotsSent++;
        }

        /// <summary>A target as the others know it: a player ("p&lt;id&gt;") or an enemy's id.</summary>
        string IdOf(Transform t)
        {
            if (t == null) return "";
            if (me != null && t.root == me.transform.root) return "p" + Client.MyId;
            if (remoteByRoot.TryGetValue(t.root, out var rp)) return "p" + rp.Id;
            var e = EntOf(t);
            return e != null ? e.id : "";
        }

        /// <summary>A ship from its id: mine, another player's, or an enemy.</summary>
        Transform Resolve(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (id.StartsWith("p") && int.TryParse(id.Substring(1), out int pid))
            {
                if (pid == Client.MyId) return me != null ? me.transform : null;
                return remotes.TryGetValue(pid, out var r) && r != null && !r.Hidden ? r.transform : null;
            }
            return ents.TryGetValue(id, out var e) ? e.t : null;
        }

        /// <summary>The shot or missile a ship fires, by name, from its own guns and launchers.</summary>
        T PrefabOf<T>(Transform ship, string name) where T : UnityEngine.Object
        {
            string key = $"{ship.GetInstanceIDSafe()}|{typeof(T).Name}|{name}";
            if (prefabCache.TryGetValue(key, out var cached)) return cached as T;
            T found = null;
            foreach (var tu in ship.GetComponentsInChildren<CarrierTurret>(true))
                if (tu.projectilePrefab is T a && (found == null || tu.projectilePrefab.name == name)) { found = a; if (tu.projectilePrefab.name == name) break; }
            foreach (var w in ship.GetComponentsInChildren<EnemyWeapons>(true))
            {
                if (w.gunPrefab is T g && (found == null || w.gunPrefab.name == name)) found = g;
                if (w.missilePrefab is T mm && (found == null || w.missilePrefab.name == name)) found = mm;
            }
            foreach (var l in ship.GetComponentsInChildren<MissileLauncher>(true))
                if (l.missilePrefab is T lm && (found == null || l.missilePrefab.name == name)) found = lm;
            prefabCache[key] = found;
            return found;
        }

        void ShowFire(CoopMsg m)
        {
            var ship = Resolve(m.src);
            if (ship == null || (me != null && ship == me.transform)) return;
            Vector3 pos = FromNet(m.rf, new Double3(m.x, m.y, m.z)), dir = new Vector3(m.dx, m.dy, m.dz);
            if (dir.sqrMagnitude < 1e-6f) dir = ship.forward;
            switch (m.k)
            {
                case CoopWire.Shot:
                {
                    var prefab = PrefabOf<Projectile>(ship, m.pf);
                    if (prefab == null) return;
                    var p = Pool.Take(prefab, pos, Quaternion.LookRotation(dir));
                    p.damage = 0f;                                      // for show: the real one is in their game
                    p.owner = ship.root;
                    ShotsShown++;
                    break;
                }
                case CoopWire.Missile:
                {
                    var prefab = PrefabOf<Missile>(ship, m.pf);
                    if (prefab == null) return;
                    var mi = Instantiate(prefab, pos, Quaternion.LookRotation(dir));
                    mi.damage = 0f;
                    mi.owner = ship.root;
                    mi.target = Resolve(m.tg);
                    ShotsShown++;
                    break;
                }
                case CoopWire.Beam:
                {
                    var beam = ship.GetComponentInChildren<MainBeam>(true);
                    if (beam == null) return;
                    if (!beam.enabled) beam.enabled = true;
                    beam.NetFire(dir);
                    ShotsShown++;
                    break;
                }
            }
        }
    }
}
