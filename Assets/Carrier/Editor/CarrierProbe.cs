using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Carrier.EditorTools
{
    /// <summary>Logs size, orientation hints and material names of the carrier's source models.</summary>
    public static class CarrierProbe
    {
        [MenuItem("Tools/Carrier/Probe Models")]
        public static void Probe()
        {
            AssetDatabase.Refresh();
            var sb = new StringBuilder();
            var guids = AssetDatabase.FindAssets("t:Model", new[] { "Assets/Carrier" });
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                var inst = Object.Instantiate(go);
                var rs = inst.GetComponentsInChildren<Renderer>();
                var b = rs.Length > 0 ? rs[0].bounds : new Bounds();
                foreach (var r in rs) b.Encapsulate(r.bounds);
                var mats = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Select(m => m.name);
                var kids = string.Join(",", inst.GetComponentsInChildren<Transform>().Select(t => t.name + "@" + t.localPosition.ToString("F1")).Take(8));
                sb.AppendLine($"PROBE {path} | center {b.center:F1} size {b.size:F1} | rot {inst.transform.GetChild(0).localEulerAngles:F0} | mats [{string.Join(",", mats)}] | {kids}");
                Object.DestroyImmediate(inst);
            }
            File.WriteAllText("Temp/carrier_probe.txt", sb.ToString());
            Debug.Log(sb.ToString());
        }
    }
}
