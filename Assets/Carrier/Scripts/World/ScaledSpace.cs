using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarrierGame
{
    /// <summary>
    /// A planet, moon or the star drawn however far off it is. The camera sees out to a little
    /// over a thousand kilometres (further, and the depth buffer can't tell near from far); a body
    /// beyond `Near` is drawn, for each camera, nearer and smaller in the same proportion: shrunk
    /// toward the camera, which looks exactly the same from there. The further it really is, the
    /// further back it is drawn (up to `Far`), so nearer bodies still pass in front of further
    /// ones. It goes back to its real place and size as soon as the camera has drawn it.
    ///
    /// A planet's shaders light it from where the star is: each body is given the direction to
    /// the star from where it really is (`_CarrierLightDir`), so the drawn-nearer copy is lit the
    /// same.
    /// </summary>
    [DefaultExecutionOrder(300)]   // after StarLight
    public class ScaledSpace : MonoBehaviour
    {
        /// <summary>Bodies nearer than this (m) are drawn where they are.</summary>
        public static float Near = 500000f;
        /// <summary>However far a body is, it is drawn nearer than this (m): inside the camera's reach.</summary>
        public static float Far = 1100000f;
        /// <summary>Off while the frigate is down on a surface (the system isn't in view there).</summary>
        public static bool Active = true;

        static readonly List<ScaledSpace> all = new List<ScaledSpace>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Active = true;
        static bool hooked;
        static readonly int LightDirId = Shader.PropertyToID("_CarrierLightDir");
        static MaterialPropertyBlock block;

        [Tooltip("Lit from the star by the Carrier/Planet shaders (planets, their atmospheres): given the real direction to the star.")]
        public bool starLit = true;

        Vector3 truePos, trueScale;
        bool moved;
        Renderer[] lit;
        Transform star;

        /// <summary>Where it really is (even while it is drawn nearer).</summary>
        public Vector3 RealPosition => moved ? truePos : transform.position;

        /// <summary>For the self-test: drawn nearer than it is at the last camera.</summary>
        public bool Compressed { get; private set; }
        public static int Count => all.Count;

        void OnEnable()
        {
            all.Add(this);
            if (!hooked)
            {
                hooked = true;
                RenderPipelineManager.beginCameraRendering += Begin;
                RenderPipelineManager.endCameraRendering += End;
                RenderPipelineManager.endContextRendering += (ctx, cams) => RestoreAll();
            }
            if (starLit) lit = GetComponentsInChildren<Renderer>(true);
        }

        void OnDisable()
        {
            Restore();
            all.Remove(this);
        }

        /// <summary>How far back a body `d` away is drawn: where it is up to Near, then further
        /// and further back, never past Far.</summary>
        public static float Drawn(float d)
        {
            if (d <= Near) return d;
            float span = Far - Near;
            return Near + span * (1f - Mathf.Exp(-(d - Near) / span));
        }

        void LateUpdate()
        {
            if (!starLit || lit == null) return;
            if (star == null) { var sl = FindAnyObjectByType<StarLight>(); if (sl != null) star = sl.transform; }
            if (star == null || star == transform) return;
            Vector3 dir = (star.position - transform.position).normalized;
            block ??= new MaterialPropertyBlock();
            foreach (var r in lit)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetVector(LightDirId, new Vector4(dir.x, dir.y, dir.z, 1f));
                r.SetPropertyBlock(block);
            }
        }

        // (only for the camera that looks out at the system, and the editor's scene view: the
        // other cameras, a target's close-up or a portrait, see only what is near; and every body is
        // back in its real place as soon as the camera has drawn, before the game looks at it again)
        static void Begin(ScriptableRenderContext ctx, Camera cam)
        {
            RestoreAll();
            if (!Active || cam == null) return;
            if (cam.cameraType != CameraType.SceneView && cam != Camera.main) return;
            Vector3 c = cam.transform.position;
            bool main = cam.cameraType != CameraType.SceneView;
            foreach (var s in all) if (s != null) s.Compress(c, main);
        }

        static void End(ScriptableRenderContext ctx, Camera cam) => RestoreAll();

        static void RestoreAll()
        {
            foreach (var s in all) if (s != null) s.Restore();
        }

        void Compress(Vector3 cam, bool main)
        {
            Restore();
            Vector3 p = transform.position;
            Vector3 off = p - cam;
            float d = off.magnitude;
            if (main) Compressed = d > Near;
            if (d <= Near) return;
            float k = Drawn(d) / d;
            truePos = p;
            trueScale = transform.localScale;
            moved = true;
            transform.position = cam + off * k;
            transform.localScale = trueScale * k;
        }

        void Restore()
        {
            if (!moved) return;
            moved = false;
            transform.position = truePos;
            transform.localScale = trueScale;
        }
    }
}
