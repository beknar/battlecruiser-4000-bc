using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CarrierGame.Demo
{
    /// <summary>The players' ships in co-op: sending mine, showing theirs, and their names over them.</summary>
    public partial class CoopWorld
    {
        // ------------------------------------------------------------------ my ship, and theirs

        void SendShip()
        {
            if (me == null) return;
            var shield = demo.ShieldGen;
            var warp = demo.Warp;
            var landing = PlanetLanding.Instance;
            var t = me.transform;
            var np = ToNet(t.position);
            Double3 p = np.p;
            Vector3 v = me.Velocity;
            var q = t.rotation;
            Client.Send(new ShipMsg
            {
                ts = Client.ServerNow,
                rf = np.rf, x = p.x, y = p.y, z = p.z, qx = q.x, qy = q.y, qz = q.z, qw = q.w, vx = v.x, vy = v.y, vz = v.z,
                th = me.throttle,
                sh = shield != null ? shield.Fraction : 0f, su = shield != null && shield.Up,
                hu = myHealth != null ? myHealth.Current / Mathf.Max(1f, myHealth.maxHealth) : 1f,
                dead = me.Destroyed,
                warp = warp != null ? (int)warp.Current : 0,
                dock = Docking.Docked,
                surf = landing != null && landing.Current != PlanetLanding.State.InSpace && landing.Body != null ? landing.Body.displayName : "",
                cap = Captain.Index,
                fr = PlayerFrigate.Flying,
            });
        }

        /// <summary>The ship showing another player, made the first time they are heard from: their
        /// frigate (from the roster, or a copy of mine if theirs is unknown, as from an older
        /// game), with its controls and weapons taken out. Made again if they change frigates.</summary>
        RemotePlayer Remote(int id, string frigate = null)
        {
            if (remotes.TryGetValue(id, out var r) && r != null)
            {
                if (string.IsNullOrEmpty(frigate) || string.Equals(r.Frigate, frigate, StringComparison.OrdinalIgnoreCase)) return r;
                RemoveRemote(id);
            }
            if (me == null) return null;
            var roster = FrigateRoster.Get();
            var entry = roster != null ? roster.Find(frigate) : null;
            var template = entry != null && entry.prefab != null ? entry.prefab : me.gameObject;
            var holder = new GameObject("co-op ship (being made)");
            holder.SetActive(false);
            var go = Instantiate(template, holder.transform);
            for (int pass = 0; pass < 4; pass++)
                foreach (var b in go.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (b == null) continue;
                    var type = b.GetType();
                    if (Keep.Contains(type) && type != typeof(NetPuppet)) continue;
                    if (b is CarrierTurret turret) { turret.enabled = false; turret.autoFire = false; continue; }   // kept for its muzzles
                    DestroyImmediate(b);
                }
            foreach (var l in go.GetComponentsInChildren<AudioListener>(true)) DestroyImmediate(l);
            foreach (var c in go.GetComponentsInChildren<Camera>(true)) DestroyImmediate(c.gameObject);
            string name = Client.NameOf(id);
            var shipName = template.TryGetComponent(out CarrierController tcc) ? tcc.shipName : me.shipName;
            go.name = $"{name}'s {shipName.ToLower()}";
            go.transform.SetParent(null, false);
            Destroy(holder);
            r = go.AddComponent<RemotePlayer>();
            r.Init(id, name);
            // (the id they sent, even one this game doesn't know: so their next message matches)
            r.Frigate = !string.IsNullOrEmpty(frigate) ? frigate : entry != null ? entry.id : "";
            remotes[id] = r;
            remoteByRoot[go.transform] = r;
            PlayerShips.Add(go.transform);
            Debug.Log($"[Co-op] {go.name} is here");
            return r;
        }

        void RemoveRemote(int id)
        {
            if (!remotes.TryGetValue(id, out var r)) return;
            remotes.Remove(id);
            if (r != null)
            {
                remoteByRoot.Remove(r.transform);
                PlayerShips.Remove(r.transform);
                Destroy(r.gameObject);
            }
        }

        void ClearRemotes()
        {
            foreach (var id in remotes.Keys.ToArray()) RemoveRemote(id);
        }

        // ------------------------------------------------------------------ names over the other ships

        void OnGUI()
        {
            if (!On || Camera.main == null) return;
            var cam = Camera.main;
            float s = Hud.S;
            foreach (var r in remotes.Values)
            {
                if (r == null || r.Hidden) continue;
                Vector3 sp = cam.WorldToScreenPoint(r.transform.position + Vector3.up * 40f);
                if (sp.z <= 0f) continue;
                var st = Hud.Text(13, true, TextAnchor.LowerCenter);
                string text = $"<color=#7ee08a>{r.Name}</color>  <color=#9aa4b0>{Vector3.Distance(cam.transform.position, r.transform.position) / 1000f:F1} km</color>";
                var size = st.CalcSize(new GUIContent(text));
                var rect = new Rect(sp.x - size.x * 0.5f, Screen.height - sp.y - size.y - 8f * s, size.x, size.y);
                GUI.Label(rect, text, st);
                // shield and hull
                float w = 60f * s, h = 3f * s;
                var bar = new Rect(sp.x - w * 0.5f, rect.yMax + 2f * s, w, h);
                Hud.Fill(bar, new Color(1f, 1f, 1f, 0.15f));
                Hud.Fill(new Rect(bar.x, bar.y, w * Mathf.Clamp01(r.Shield), h), new Color(0.35f, 0.78f, 1f));
                bar.y += h + 2f * s;
                Hud.Fill(bar, new Color(1f, 1f, 1f, 0.15f));
                Hud.Fill(new Rect(bar.x, bar.y, w * Mathf.Clamp01(r.Hull), h), new Color(0.45f, 0.9f, 0.55f));
            }
        }
    }
}
