using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A place whose looks are loaded only while the frigate is near it. The place itself (its
    /// warp point, berth, crew, industry, the rest of what it does) stays in the level; its meshes,
    /// materials and lights are prefabs in Resources/Streamed, loaded when the ship comes within
    /// `loadDistance` (in the background; at once if it is suddenly close, after a warp) and thrown
    /// away beyond `unloadDistance`, so what they take of memory is only taken while they are seen.
    /// Pieces with a SurfaceAnchor are set on `body` (a rock, a planet) when they arrive.
    /// </summary>
    public class StreamedVisual : MonoBehaviour
    {
        [Serializable]
        public class Piece
        {
            public string path;
            public Transform parent;
            public Vector3 position, scale = Vector3.one;
            public Quaternion rotation = Quaternion.identity;
        }

        public Piece[] pieces;
        [Tooltip("What its anchored pieces stand on.")]
        public Transform body;
        public float loadDistance = 30000f, unloadDistance = 40000f;

        public bool Loaded { get; private set; }
        bool loading;
        readonly List<GameObject> made = new List<GameObject>();
        float nextLook;

        public static readonly List<StreamedVisual> All = new List<StreamedVisual>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { All.Clear(); viewerFrame = -1; viewer = null; }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); nextLook = Time.unscaledTime + UnityEngine.Random.value * 0.5f; }
        void OnDisable() => All.Remove(this);
        void OnDestroy() => Drop();

        // the viewer, looked up once a frame for all of them (the ship found again only when it has gone)
        static int viewerFrame = -1;
        static Transform viewer;
        static bool hasViewer;
        static Vector3 viewerAt;

        /// <summary>Where the ship (the camera, failing that) is, for the distances.</summary>
        static bool Viewer(out Vector3 p)
        {
            if (viewerFrame != Time.frameCount)
            {
                viewerFrame = Time.frameCount;
                if (viewer == null || !viewer.gameObject.activeInHierarchy)
                {
                    var ship = PlayerShips.Primary;
                    viewer = ship != null ? ship : (Camera.main != null ? Camera.main.transform : null);
                }
                hasViewer = viewer != null;
                viewerAt = hasViewer ? viewer.position : Vector3.zero;
            }
            p = viewerAt;
            return hasViewer;
        }

        /// <summary>Whether it should be loaded with the viewer `d` away (and loaded now or not).</summary>
        public static bool Want(float d, bool loaded, float load, float unload) => loaded ? d <= unload : d <= load;

        void Update()
        {
            if (!Viewer(out var p)) return;
            float d = Vector3.Distance(p, transform.position);
            // suddenly close (a warp's arrival, a jump): there now, not in a moment, even if it was
            // already coming in the background
            if (!Loaded && d < loadDistance * 0.25f) { LoadNow(); return; }
            if (Time.unscaledTime < nextLook) return;
            nextLook = Time.unscaledTime + 0.5f;
            if (!Want(d, Loaded || loading, loadDistance, unloadDistance)) { if (Loaded || loading) Drop(); return; }
            if (Loaded || loading) return;
            StartCoroutine(LoadInBackground());
        }

        public void LoadNow()
        {
            if (Loaded) return;
            StopAllCoroutines();
            loading = false;
            foreach (var pc in pieces) Make(pc, Resources.Load<GameObject>(pc.path));
            Done();
        }

        IEnumerator LoadInBackground()
        {
            loading = true;
            var reqs = new ResourceRequest[pieces.Length];
            for (int i = 0; i < pieces.Length; i++) reqs[i] = Resources.LoadAsync<GameObject>(pieces[i].path);
            foreach (var r in reqs) while (!r.isDone) yield return null;
            if (!loading) yield break;
            for (int i = 0; i < pieces.Length; i++) Make(pieces[i], reqs[i].asset as GameObject);
            loading = false;
            Done();
        }

        void Make(Piece pc, GameObject prefab)
        {
            if (prefab == null) { Debug.LogWarning($"[StreamedVisual] {name}: {pc.path} isn't there"); return; }
            var parent = pc.parent != null ? pc.parent : transform;
            var go = Instantiate(prefab, parent, false);
            go.name = prefab.name;
            go.transform.localPosition = pc.position;
            go.transform.localRotation = pc.rotation;
            go.transform.localScale = pc.scale;
            // (set on what it stands on before its Start settles it there)
            if (body != null) foreach (var a in go.GetComponentsInChildren<SurfaceAnchor>(true)) a.body = body;
            made.Add(go);
        }

        void Done()
        {
            Loaded = true;
            Physics.SyncTransforms();
        }

        /// <summary>Throw its looks away (and, a little later, what they took of memory: one
        /// clear-out for many places let go together, none if one is done meanwhile).</summary>
        public void Drop()
        {
            StopAllCoroutines();
            loading = false;
            if (!Loaded) return;
            foreach (var go in made) if (go != null) Destroy(go);
            made.Clear();
            Loaded = false;
            SceneMemory.CollectSoon("let go of the places left behind", 4f);
        }
    }
}
