using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The target panel: a live camera view of the ship the carrier is targeting, its hull bar
    /// and stats (damage taken, hits, times destroyed, range, speed, who is attacking it, missiles
    /// inbound), plus brackets around the target on screen and markers on the other hostiles.
    /// </summary>
    public class TargetDisplay : MonoBehaviour
    {
        public CarrierController carrier;
        public Transform[] targets;

        /// <summary>Screen area the panel covers (GUI coordinates), so clicks there don't pick ships.</summary>
        public Rect PanelRect { get; private set; }

        static readonly Color Red = new Color(1f, 0.35f, 0.3f);
        static readonly Color Amber = new Color(1f, 0.75f, 0.25f);

        Camera viewCam;
        RenderTexture view;
        Transform shown;
        Health health;
        TargetInfo info;
        IVelocity mover;
        Vector3 localCenter;
        float radius = 50f, length;
        bool wasDead;
        float lastSeenHit, flashUntil;

        string ShownName => info != null ? info.Name : shown != null ? shown.name : "";

        void Start()
        {
            view = new RenderTexture(640, 400, 24) { name = "TargetView" };
            var go = new GameObject("TargetViewCamera");
            viewCam = go.AddComponent<Camera>();
            viewCam.targetTexture = view;
            viewCam.fieldOfView = 30f;
            viewCam.nearClipPlane = 1f;
            viewCam.farClipPlane = 30000f;
            viewCam.depth = -10;
            viewCam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            viewCam.enabled = false;
        }

        void OnDestroy()
        {
            if (view != null) view.Release();
        }

        void LateUpdate()
        {
            if (carrier == null || viewCam == null) return;
            var t = carrier.target;
            if (t != shown) Retarget(t);
            viewCam.enabled = t != null;
            if (t == null) return;

            // slow orbit around the target, framed to its size
            Vector3 c = t.TransformPoint(localCenter);
            Vector3 dir = Quaternion.Euler(16f, Time.time * 14f, 0f) * Vector3.forward;
            float dist = radius / Mathf.Sin(viewCam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.6f;
            viewCam.transform.SetPositionAndRotation(c - dir * dist, Quaternion.LookRotation(dir));

            if (health == null) return;
            if (health.Dead && !wasDead)
            {
                SoundFX.Ui(Sfx.UiKill);
                PadSupport.Rumble(0.8f, 0.6f, 0.45f);
                CarrierDemoInput.Toast($"{ShownName} destroyed");
            }
            wasDead = health.Dead;
            if (health.LastHitTime > lastSeenHit)
            {
                lastSeenHit = health.LastHitTime;
                flashUntil = Time.time + 0.1f;
            }
        }

        void Retarget(Transform t)
        {
            shown = t;
            health = t ? t.GetComponentInParent<Health>() : null;
            info = t ? t.GetComponentInParent<TargetInfo>() : null;
            mover = Motion.Of(t);
            wasDead = health != null && health.Dead;
            lastSeenHit = health != null ? health.LastHitTime : -100f;
            if (t == null) return;
            var box = t.GetComponent<BoxCollider>();
            if (box != null)
            {
                localCenter = box.center;
                Vector3 size = Vector3.Scale(box.size, t.lossyScale);
                radius = size.magnitude * 0.5f;
                length = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            }
            else
            {
                var rs = t.GetComponentsInChildren<Renderer>();
                var b = rs.Length > 0 ? rs[0].bounds : new Bounds(t.position, Vector3.one * 20f);
                foreach (var r in rs) b.Encapsulate(r.bounds);
                localCenter = t.InverseTransformPoint(b.center);
                radius = b.extents.magnitude;
                length = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            }
            radius = Mathf.Max(radius, 5f);
        }

        // ------------------------------------------------------------------ drawing

        void OnGUI()
        {
            if (carrier == null) return;
            float s = Hud.S;
            var cam = Camera.main;
            if (cam != null && targets != null)
                foreach (var t in targets)
                    if (t != null && t != carrier.target) Marker(cam, t, s);
            if (cam != null && carrier.target != null && carrier.target == shown) Brackets(cam, s);
            Panel(s);
        }

        /// <summary>Small dim brackets and a name on hostiles that aren't targeted.</summary>
        void Marker(Camera cam, Transform t, float s)
        {
            var h = t.GetComponent<Health>();
            if (h != null && h.Dead) return;
            if (CombatPilot.IsHidden(t)) return;   // an ambusher still lying in wait isn't on the screen
            Vector3 sp = cam.WorldToScreenPoint(t.position);
            if (sp.z <= 0f || sp.x < 0 || sp.x > Screen.width || sp.y < 0 || sp.y > Screen.height) return;
            var gp = new Vector2(sp.x, Screen.height - sp.y);
            if (CarrierDemoInput.HelpRect.Contains(gp) || PanelRect.Contains(gp)) return;
            var ti = t.GetComponent<TargetInfo>();
            float half = 11f * s;
            var r = new Rect(sp.x - half, Screen.height - sp.y - half, half * 2f, half * 2f);
            var c = new Color(1f, 0.45f, 0.4f, 0.6f);
            Hud.Corners(r, half * 0.6f, Mathf.Max(1f, 1.5f * s), c);
            var st = Hud.Text(13);
            GUI.Label(new Rect(r.xMax + 4f * s, r.y - 2f * s, 400f * s, 30f * s),
                      $"<color=#ff8a80aa>{(ti ? ti.Name : t.name)}</color>", st);
        }

        /// <summary>Corner brackets around the target, or an arrow at the screen edge if it's off screen.</summary>
        void Brackets(Camera cam, float s)
        {
            Vector3 c = shown.TransformPoint(localCenter);
            Vector3 sp = cam.WorldToScreenPoint(c);
            float km = Vector3.Distance(carrier.transform.position, c) / 1000f;
            string label = $"{ShownName}   {km:F1} km";
            bool dead = health != null && health.Dead;
            Color col = dead ? new Color(0.7f, 0.7f, 0.7f, 0.8f) : Red;
            bool onScreen = sp.z > 0f && sp.x > 0 && sp.x < Screen.width && sp.y > 0 && sp.y < Screen.height;
            if (onScreen)
            {
                float half = radius / (sp.z * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) * Screen.height * 0.5f;
                half = Mathf.Max(half, 18f * s);
                var r = new Rect(sp.x - half, Screen.height - sp.y - half, half * 2f, half * 2f);
                Hud.Corners(r, Mathf.Max(8f * s, half * 0.3f), Mathf.Max(2f, 2.5f * s), col);
                var ls = Hud.Text(15, true);
                var lc = new GUIContent($"<color=#{ColorUtility.ToHtmlStringRGB(col)}>{label}</color>");
                Vector2 lsz = ls.CalcSize(lc);
                var lr = new Rect(r.x, r.yMax + 4f * s, lsz.x, lsz.y);
                // keep the label readable: never under the (see-through) panels
                if (lr.Overlaps(PanelRect)) lr.x = PanelRect.x - lsz.x - 8f * s;
                if (lr.Overlaps(CarrierDemoInput.HelpRect)) lr.x = CarrierDemoInput.HelpRect.xMax + 8f * s;
                GUI.Label(lr, lc, ls);
                return;
            }
            // off screen: point toward it from the screen edge
            Vector2 centre = new Vector2(Screen.width, Screen.height) * 0.5f;
            Vector2 d = new Vector2(sp.x, sp.y) - centre;
            if (sp.z < 0f) d = -d;
            if (d.sqrMagnitude < 1f) d = Vector2.up;
            float margin = 50f * s;
            float k = Mathf.Min((centre.x - margin) / Mathf.Max(1e-3f, Mathf.Abs(d.x)),
                                (centre.y - margin) / Mathf.Max(1e-3f, Mathf.Abs(d.y)));
            Vector2 p = centre + d * k;
            string arrow = Mathf.Abs(d.x) * centre.y > Mathf.Abs(d.y) * centre.x
                ? (d.x > 0 ? "▶" : "◀")
                : (d.y > 0 ? "▲" : "▼");
            var st = Hud.Text(16, true, TextAnchor.MiddleCenter);
            var content = new GUIContent($"<color=#{ColorUtility.ToHtmlStringRGB(col)}>{arrow}  {label}</color>");
            Vector2 size = st.CalcSize(content);
            var box = new Rect(p.x - size.x * 0.5f, Screen.height - p.y - size.y * 0.5f, size.x, size.y);
            box.x = Mathf.Clamp(box.x, 8f, Screen.width - box.width - 8f);
            // the panels sit at the top corners: slide the pointer down below them rather than under them
            var padded = new Rect(box.x - 6f * s, box.y - 3f * s, box.width + 12f * s, box.height + 6f * s);
            if (padded.Overlaps(PanelRect)) box.y = PanelRect.yMax + 10f * s;
            else if (padded.Overlaps(CarrierDemoInput.HelpRect)) box.y = CarrierDemoInput.HelpRect.yMax + 10f * s;
            Hud.Panel(new Rect(box.x - 6f * s, box.y - 3f * s, box.width + 12f * s, box.height + 6f * s));
            GUI.Label(box, content, st);
        }

        void Panel(float s)
        {
            float pad = 12f * s;
            var body = Hud.Text(16);
            if (carrier.target == null || shown == null)
            {
                var c = new GUIContent(PadSupport.UsingPad
                    ? "<b>NO TARGET</b>\nGuns holding fire\nY next target"
                    : "<b>NO TARGET</b>\nGuns holding fire\nT next target, or click a ship");
                Vector2 sz = body.CalcSize(c);
                var r = new Rect(Screen.width - sz.x - pad * 3f, pad, sz.x + pad * 2f, sz.y + pad * 2f);
                PanelRect = r;
                Hud.Panel(r);
                GUI.Label(new Rect(r.x + pad, r.y + pad, sz.x, sz.y), c, body);
                return;
            }

            float w = 400f * s, inner = w - pad * 2f;
            float x = Screen.width - w - pad, y = pad;
            var head = Hud.Text(13, true);
            var title = Hud.Text(21, true);
            float headH = head.CalcSize(new GUIContent("T")).y;
            float titleH = title.CalcSize(new GUIContent("T")).y;
            float viewH = inner * 0.62f;
            float barH = 24f * s;
            var stats = new GUIContent(StatsText());
            float statsH = body.CalcHeight(stats, inner);
            float gap = pad * 0.6f;
            float h = pad + headH + titleH + gap + viewH + gap + barH + gap + statsH + pad;
            var panel = new Rect(x, y, w, h);
            PanelRect = panel;
            Hud.Panel(panel);

            float cy = y + pad;
            bool dead = health != null && health.Dead;
            GUI.Label(new Rect(x + pad, cy, inner, headH),
                      dead ? "<color=#bbbbbb>TARGET DESTROYED</color>" : "<color=#ff8a80>TARGET LOCKED</color>", head);
            cy += headH;
            GUI.Label(new Rect(x + pad, cy, inner, titleH), ShownName, title);
            cy += titleH + gap;

            // live view of the target
            var vr = new Rect(x + pad, cy, inner, viewH);
            if (view != null) GUI.DrawTexture(vr, view, ScaleMode.ScaleAndCrop, false);
            if (dead)
            {
                Hud.Fill(vr, new Color(0f, 0f, 0f, 0.6f));
                float left = health.respawnDelay > 0f ? Mathf.Max(0f, health.RespawnAt - Time.time) : 0f;
                string msg = health.respawnDelay > 0f ? $"DESTROYED\n<size={Mathf.RoundToInt(15 * s)}>respawning in {left:F0} s</size>" : "DESTROYED";
                GUI.Label(vr, msg, Hud.Text(24, true, TextAnchor.MiddleCenter));
            }
            if (health != null && !dead && Time.time - health.LastHitTime < 0.8f)
                GUI.Label(new Rect(vr.x + 8f * s, vr.y + 6f * s, vr.width - 16f * s, 30f * s),
                          $"<color=#ff6e60>HIT  -{health.LastHitAmount:N0}</color>", Hud.Text(16, true, TextAnchor.UpperRight));
            Hud.Corners(vr, 14f * s, Mathf.Max(1f, 2f * s), dead ? new Color(0.6f, 0.6f, 0.6f) : Red);
            if (!dead && Time.time < flashUntil) Hud.Outline(vr, Mathf.Max(2f, 3f * s), new Color(1f, 0.3f, 0.2f, 0.8f));
            cy += viewH + gap;

            // hull bar
            var bar = new Rect(x + pad, cy, inner, barH);
            Hud.Fill(bar, new Color(0.1f, 0.12f, 0.16f, 0.9f));
            float frac = health != null ? Mathf.Clamp01(health.Current / Mathf.Max(1f, health.maxHealth)) : 1f;
            Color fill = frac > 0.5f ? Color.Lerp(Amber, new Color(0.3f, 0.85f, 0.4f), (frac - 0.5f) * 2f)
                                     : Color.Lerp(Red, Amber, frac * 2f);
            Hud.Fill(new Rect(bar.x, bar.y, bar.width * frac, bar.height), fill * new Color(1f, 1f, 1f, 0.85f));
            Hud.Outline(bar, Mathf.Max(1f, s), new Color(1f, 1f, 1f, 0.25f));
            string hull = health != null ? $"HULL  {health.Current:N0} / {health.maxHealth:N0}   ({frac:P0})" : "HULL  unknown";
            GUI.Label(bar, $"<color=#ffffff>{hull}</color>", Hud.Text(14, true, TextAnchor.MiddleCenter));
            cy += barH + gap;

            GUI.Label(new Rect(x + pad, cy, inner, statsH), stats, body);
        }

        string StatsText()
        {
            var sb = new System.Text.StringBuilder();
            string cls = info != null ? info.shipClass : "Unknown";
            string faction = info != null ? info.faction : "Unknown";
            sb.Append($"Class  <b>{cls}</b>   ({faction})\n");
            if (health != null)
            {
                float frac = health.Current / Mathf.Max(1f, health.maxHealth);
                string status = health.Dead ? "<color=#bbbbbb>Destroyed</color>"
                    : frac >= 0.999f ? "<color=#7ee08a>Undamaged</color>"
                    : frac > 0.75f ? "<color=#c8e07e>Light damage</color>"
                    : frac > 0.4f ? "<color=#ffc04d>Damaged</color>"
                    : frac > 0.15f ? "<color=#ff8a50>Heavy damage</color>"
                    : "<color=#ff5040>Critical</color>";
                sb.Append($"Status  {status}\n");
                sb.Append($"Damage taken  <b>{health.DamageTaken:N0}</b>  ({health.DamageTaken / Mathf.Max(1f, health.maxHealth):P0} of hull)\n");
                sb.Append($"Hits taken  {health.Hits:N0}      Destroyed  {health.TimesDestroyed}×\n");
                sb.Append($"All damage dealt to it  {health.TotalDamage:N0}\n");
            }
            Vector3 c = shown.TransformPoint(localCenter);
            float km = Vector3.Distance(carrier.transform.position, c) / 1000f;
            float speed = mover != null ? mover.Velocity.magnitude : 0f;
            sb.Append($"Range  {km:F2} km      Speed  {speed:F0} m/s\n");
            sb.Append($"Length  {length:F0} m\n");
            if (info != null) sb.Append($"Weapons  {info.weapons}      Threat  {info.threat}\n");

            string guns = carrier.target == shown ? carrier.GunsBearingText() : "";
            if (guns.Length > 0) sb.Append($"In arc  {(guns.StartsWith("none") ? "<color=#ffb060>none</color>" : guns)}\n");
            if (carrier.HasHangar)
            {
                int fighters = 0, drones = 0;
                foreach (var craft in carrier.Wing)
                {
                    if (craft == null || craft.order != Craft.Order.Attack || craft.target != shown) continue;
                    if (craft.kind == Craft.Kind.Drone) drones++; else fighters++;
                }
                string attackers = fighters + drones == 0 ? "none" : $"{fighters} fighters, {drones} drones";
                sb.Append($"Attacking it  {attackers}\n");
            }
            if (carrier.HasMissiles)
            {
                int missiles = 0;
                foreach (var m in Missile.Active) if (m != null && m.target == shown) missiles++;
                sb.Append($"Missiles inbound  {missiles}\n");
            }
            return sb.ToString().TrimEnd('\n');
        }
    }
}
