using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// Not drawn while the ship is still up in space: the ground built ahead of it as it comes down
    /// lies far below the system (`center`), and from over a planet, where the ship's "up" is out
    /// from the planet, not the system's, that can be in view, in the sky, as a square of
    /// half-built ground. Its renderers and its areas' terrains (which draw themselves) are
    /// switched off, and so is what has been put down near its centre (the roots ShiftBy moves),
    /// never the players' ships or the camera. Exactly what was switched off is switched back on
    /// at the hand-over, or when the surface goes.
    /// </summary>
    public partial class PlanetSurface
    {
        bool hidden;
        int hiddenCount = -1;
        float nextRootLook;
        readonly HashSet<Renderer> hiddenRenderers = new HashSet<Renderer>();
        readonly HashSet<Terrain> hiddenTerrains = new HashSet<Terrain>();
        readonly List<Renderer> rendererBuffer = new List<Renderer>();
        readonly List<Terrain> terrainBuffer = new List<Terrain>();
        readonly List<GameObject> rootBuffer = new List<GameObject>();

        public bool Hidden
        {
            get => hidden;
            set
            {
                if (value != hidden)
                {
                    hidden = value;
                    if (hidden) { HideUnder(transform); HideRootsNear(); }
                    else ShowAll();
                    return;
                }
                if (!hidden) return;
                // (set each frame while hidden: what has been built under it since is hidden at once,
                // or it would show; what has been put down near the centre, twice a second)
                if (transform.hierarchyCount != hiddenCount) HideUnder(transform);
                if (Time.unscaledTime >= nextRootLook) HideRootsNear();
            }
        }

        void HideUnder(Transform root)
        {
            if (root == transform) hiddenCount = transform.hierarchyCount;
            root.GetComponentsInChildren(true, rendererBuffer);
            foreach (var r in rendererBuffer)
                if (r != null && !r.forceRenderingOff) { r.forceRenderingOff = true; hiddenRenderers.Add(r); }
            root.GetComponentsInChildren(true, terrainBuffer);
            foreach (var t in terrainBuffer)
                if (t != null && t.drawHeightmap) { t.drawHeightmap = false; hiddenTerrains.Add(t); }
        }

        void HideRootsNear()
        {
            nextRootLook = Time.unscaledTime + 0.5f;
            if (!gameObject.scene.IsValid()) return;
            gameObject.scene.GetRootGameObjects(rootBuffer);
            var cam = Camera.main != null ? Camera.main.transform.root : null;
            float reach2 = 60000f * 60000f;
            foreach (var r in rootBuffer)
            {
                if (r == null || r == gameObject || r.transform == cam || IsPlayerShip(r.transform)) continue;
                if ((r.transform.position - Center).sqrMagnitude > reach2) continue;
                HideUnder(r.transform);
            }
        }

        static bool IsPlayerShip(Transform root)
        {
            foreach (var s in PlayerShips.All) if (s != null && s.root == root) return true;
            return root.GetComponent<CarrierController>() != null;
        }

        void ShowAll()
        {
            foreach (var r in hiddenRenderers) if (r != null) r.forceRenderingOff = false;
            foreach (var t in hiddenTerrains) if (t != null) t.drawHeightmap = true;
            hiddenRenderers.Clear();
            hiddenTerrains.Clear();
            hiddenCount = -1;
        }
    }
}
