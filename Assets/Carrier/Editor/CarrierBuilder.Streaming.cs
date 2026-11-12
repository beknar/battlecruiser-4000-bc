using System.IO;
using System.Linq;
using CarrierGame;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Carrier.EditorTools
{
    /// <summary>
    /// The places loaded only when the frigate is near (StreamedVisual): a place's looks (its
    /// meshes, materials and lights) are made a prefab in Resources/Streamed, and the place keeps
    /// what it does: its warp point, berth, crew, industry, its rock or pad if it stands on one.
    /// The heavy ones are streamed: the settlements and the industrial sites, built from big kit
    /// meshes and their 4K textures.
    /// </summary>
    public static partial class CarrierBuilder
    {
        const string StreamDir = Root + "/Resources/Streamed";

        /// <summary>Everything under `root` but the children named in `keep` made one prefab,
        /// loaded by a StreamedVisual on `root` within about `radius` × 15 of it (never less than
        /// 25 km). `body`: what its anchored pieces stand on.</summary>
        static StreamedVisual StreamOut(Transform root, string place, float radius, Transform body = null, params string[] keep)
        {
            var vis = new GameObject("Visual");
            vis.transform.SetParent(root, false);
            foreach (Transform c in root.Cast<Transform>().ToArray())
            {
                if (c == vis.transform || keep.Contains(c.name)) continue;
                var outer = PrefabUtility.GetOutermostPrefabInstanceRoot(c.gameObject);
                if (outer != null) PrefabUtility.UnpackPrefabInstance(outer, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                c.SetParent(vis.transform, true);
            }
            if (vis.transform.childCount == 0) { Object.DestroyImmediate(vis); return null; }
            Directory.CreateDirectory(StreamDir);
            string file = place.Replace(' ', '_');
            PrefabUtility.SaveAsPrefabAsset(vis, $"{StreamDir}/{file}.prefab");
            Object.DestroyImmediate(vis);
            var sv = root.gameObject.AddComponent<StreamedVisual>();
            sv.pieces = new[] { new StreamedVisual.Piece { path = "Streamed/" + file, parent = root } };
            sv.body = body;
            sv.loadDistance = Mathf.Max(25000f, radius * 15f);
            sv.unloadDistance = sv.loadDistance * 1.3f;
            return sv;
        }

        /// <summary>A berth of the place's own (in the level), where one inside its looks is.</summary>
        static Transform OwnBerth(Transform root, Transform inside)
        {
            if (inside == null) return null;
            return Berth(root, root.InverseTransformPoint(inside.position), root.InverseTransformDirection(inside.forward));
        }
    }
}
