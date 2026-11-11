using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CarrierGame
{
    /// <summary>The floating origin: the world moved back toward `center` when the ship strays far from it.</summary>
    public partial class PlanetSurface
    {
        void ShiftIfFar()
        {
            Vector3 d = Ship.position - Center;
            if (Mathf.Abs(d.x) < shiftDistance && Mathf.Abs(d.z) < shiftDistance) return;
            var delta = new Vector3(-Mathf.Round(d.x / T) * T, 0f, -Mathf.Round(d.z / T) * T);
            ShiftBy(delta);
        }

        /// <summary>Move everything down here by `delta` (whole areas), keeping the planet where it was.</summary>
        public void ShiftBy(Vector3 delta)
        {
            if (delta == Vector3.zero) return;
            var moved = new List<Transform>();
            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            float reach2 = 60000f * 60000f;
            foreach (var r in roots)
            {
                if (r == gameObject) continue;
                if ((r.transform.position - Center).sqrMagnitude > reach2) continue;
                r.transform.position += delta;
                moved.Add(r.transform);
            }
            foreach (Transform c in transform)
            {
                c.position += delta;
                moved.Add(c);
            }
            OriginU -= delta.x;
            OriginV -= delta.z;
            Shifts++;
            TotalShift += delta;
            foreach (var t in moved)
            {
                if (t == null) continue;
                foreach (var s in t.GetComponentsInChildren<IOriginShift>(true)) s.OriginShifted(delta);
                foreach (var tr in t.GetComponentsInChildren<TrailRenderer>(true)) tr.Clear();
            }
            Physics.SyncTransforms();
            ringDirty = true;
            Shifted?.Invoke(delta);
        }
    }
}
