using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// Mining on screen: a marker on each deposit the mineral sensor found (what it is, its main
    /// minerals, how much is left and how far), brackets on the one the mining laser is cutting,
    /// and the cargo list (I).
    /// </summary>
    public class MiningDisplay : MonoBehaviour
    {
        public CarrierController carrier;

        MineralSensor sensor;
        MiningLaser laser;
        CargoHold hold;
        bool looked;

        /// <summary>Screen area of the cargo list (GUI coordinates), while it is open.</summary>
        public static Rect ManifestRect { get; private set; }

        void Find()
        {
            if (looked || carrier == null) return;
            looked = true;
            sensor = carrier.GetComponentInChildren<MineralSensor>();
            laser = carrier.GetComponentInChildren<MiningLaser>();
            hold = carrier.GetComponent<CargoHold>();
        }

        void OnGUI()
        {
            if (carrier == null || SystemMap.IsOpen || GameMenu.IsOpen) return;
            Find();
            var cam = Camera.main;
            if (cam == null) return;
            float s = Hud.S;
            if (!CarrierDemoInput.ReelMode && sensor != null && sensor.Showing && !carrier.Destroyed) Markers(cam, s);
            if (laser != null && laser.Cutting && laser.Source != null) Cutting(cam, s);
            ManifestRect = Rect.zero;
            if (CarrierDemoInput.ManifestOpen && hold != null) Manifest(s);
        }

        void Markers(Camera cam, float s)
        {
            var label = Hud.Text(14);
            Vector3 ship = carrier.transform.position;
            int shown = 0;
            foreach (var f in sensor.Finds)
            {
                if (shown >= 14) break;
                var src = f.source;
                if (src == null || !src.Valid || src.Depleted) continue;
                Vector3 p = src.Position;
                float dist = Mathf.Max(0f, Vector3.Distance(ship, p) - src.Radius);
                if (dist > sensor.ScanRange * 1.5f) continue;
                Vector3 sp = cam.WorldToScreenPoint(p);
                if (sp.z <= 0f) continue;
                var gp = new Vector2(sp.x, Screen.height - sp.y);
                if (gp.x < 0f || gp.y < 0f || gp.x > Screen.width || gp.y > Screen.height) continue;
                shown++;
                var prize = Minerals.Prize(src.Mix);
                Color c = Minerals.Get(prize).color;
                float size = Mathf.Lerp(16f, 9f, Mathf.Clamp01(dist / sensor.ScanRange)) * s;
                Diamond(gp, size, new Color(c.r, c.g, c.b, 0.95f), 2f * s);
                string text = $"{Minerals.Summary(src.Mix, 2)}\n<color=#9aa4b0>{src.Remaining:F0} t  {dist / 1000f:F1} km</color>";
                var content = new GUIContent(text);
                Vector2 sz = label.CalcSize(content);
                var r = new Rect(gp.x + size + 4f * s, gp.y - sz.y * 0.5f, sz.x + 8f * s, sz.y + 4f * s);
                Hud.Panel(r);
                GUI.Label(new Rect(r.x + 4f * s, r.y + 2f * s, sz.x, sz.y), content, label);
            }
        }

        void Cutting(Camera cam, float s)
        {
            Vector3 sp = cam.WorldToScreenPoint(laser.HitPoint);
            if (sp.z <= 0f) return;
            var gp = new Vector2(sp.x, Screen.height - sp.y);
            float r = 22f * s;
            Hud.Corners(new Rect(gp.x - r, gp.y - r, r * 2f, r * 2f), 8f * s, 2f * s, new Color(1f, 0.75f, 0.35f, 0.95f));
            var src = laser.Source;
            var label = Hud.Text(14);
            var content = new GUIContent($"<color=#ffc070>{src.Label}</color>\n{Minerals.Summary(src.Mix, 3)}\n<color=#9aa4b0>{src.Remaining:F0} of {src.Initial:F0} t left</color>");
            Vector2 sz = label.CalcSize(content);
            var box = new Rect(gp.x + r + 6f * s, gp.y + r * 0.2f, sz.x + 10f * s, sz.y + 6f * s);
            Hud.Panel(box);
            GUI.Label(new Rect(box.x + 5f * s, box.y + 3f * s, sz.x, sz.y), content, label);
        }

        void Manifest(float s)
        {
            var list = hold.Manifest();
            var sb = new System.Text.StringBuilder();
            sb.Append($"<b>CARGO HOLD</b>   {hold.Used:F1} / {hold.capacity:F0} t\n");
            if (list.Count == 0) sb.Append("<color=#9aa4b0>empty: cut ore with the mining laser</color>");
            foreach (var (m, t) in list)
                sb.Append($"\n<color={Minerals.Hex(m)}>{Minerals.Name(m)}</color>  {(t >= 10f ? t.ToString("F0") : t.ToString("F2"))} t   <color=#9aa4b0>{t * Minerals.Value(m):N0} cr</color>");
            if (list.Count > 0)
            {
                var cheap = hold.Cheapest();
                sb.Append($"\n\nWorth <b>{hold.Value:N0} cr</b>");
                if (cheap != null) sb.Append($"\n<color=#9aa4b0>J jettisons the cheapest: {Minerals.Name(cheap.Value).ToLower()}</color>");
            }
            if (hold.TotalLost > 0f) sb.Append($"\n<color=#ff6050>{hold.TotalLost:F0} t lost with the ship</color>");
            var style = Hud.Text(15);
            var content = new GUIContent(sb.ToString());
            Vector2 sz = style.CalcSize(content);
            float pad = 12f * s;
            var r = new Rect(Screen.width - sz.x - pad * 3f, Screen.height - sz.y - pad * 9f, sz.x + pad * 2f, sz.y + pad * 2f);
            ManifestRect = r;
            Hud.Panel(r);
            GUI.Label(new Rect(r.x + pad, r.y + pad, sz.x, sz.y), content, style);
        }

        /// <summary>A small diamond outline (four short strokes round a point).</summary>
        static void Diamond(Vector2 c, float r, Color col, float th)
        {
            int steps = Mathf.Max(4, Mathf.RoundToInt(r / Mathf.Max(1f, th)));
            for (int i = 0; i < steps; i++)
            {
                float k = i / (float)steps;
                float a = r * k, b = r * (1f - k);
                Hud.Fill(new Rect(c.x + a - th * 0.5f, c.y - b - th * 0.5f, th, th), col);
                Hud.Fill(new Rect(c.x + b - th * 0.5f, c.y + a - th * 0.5f, th, th), col);
                Hud.Fill(new Rect(c.x - a - th * 0.5f, c.y + b - th * 0.5f, th, th), col);
                Hud.Fill(new Rect(c.x - b - th * 0.5f, c.y - a - th * 0.5f, th, th), col);
            }
        }
    }
}
