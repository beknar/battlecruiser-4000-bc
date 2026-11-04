using UnityEngine;

namespace CarrierGame
{
    /// <summary>A smooth latitude-longitude sphere (radius 0.5, like Unity's own) built when the
    /// level loads, with enough segments that a planet's outline stays round however large it
    /// looms. Its texture coordinates are equirectangular, for a planet map.</summary>
    [RequireComponent(typeof(MeshFilter))]
    public class ProceduralSphere : MonoBehaviour
    {
        public int segments = 160;

        static Mesh shared;
        static int sharedSegments;

        void Awake() => Build();

        public void Build()
        {
            if (shared == null || sharedSegments != segments) { shared = Make(segments); sharedSegments = segments; }
            GetComponent<MeshFilter>().sharedMesh = shared;
        }

        public void Clear() => GetComponent<MeshFilter>().sharedMesh = null;

        public static void ClearCache()
        {
            if (shared != null) { if (Application.isPlaying) Destroy(shared); else DestroyImmediate(shared); }
            shared = null;
        }

        static Mesh Make(int seg)
        {
            int rings = seg / 2;
            var v = new Vector3[(seg + 1) * (rings + 1)];
            var n = new Vector3[v.Length];
            var uv = new Vector2[v.Length];
            for (int r = 0; r <= rings; r++)
            {
                float lat = Mathf.PI * r / rings - Mathf.PI / 2f;
                for (int s = 0; s <= seg; s++)
                {
                    float lon = 2f * Mathf.PI * s / seg;
                    var d = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                    int i = r * (seg + 1) + s;
                    v[i] = d * 0.5f;
                    n[i] = d;
                    uv[i] = new Vector2(1f - s / (float)seg, r / (float)rings);
                }
            }
            var t = new int[seg * rings * 6];
            int k = 0;
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = r * (seg + 1) + s, b = a + seg + 1;
                    t[k++] = a; t[k++] = b; t[k++] = a + 1;
                    t[k++] = a + 1; t[k++] = b; t[k++] = b + 1;
                }
            var m = new Mesh { name = "PlanetSphere", hideFlags = HideFlags.DontSave };
            m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.vertices = v;
            m.normals = n;
            m.uv = uv;
            m.triangles = t;
            m.RecalculateTangents();
            m.RecalculateBounds();
            // make sure the faces point outward (tested on a triangle at the equator; the poles are degenerate)
            int e = rings / 2 * seg * 6;
            if (Vector3.Dot(Vector3.Cross(v[t[e + 1]] - v[t[e]], v[t[e + 2]] - v[t[e]]), v[t[e]] + v[t[e + 1]] + v[t[e + 2]]) < 0f)
            {
                for (int i = 0; i < t.Length; i += 3) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
                m.triangles = t;
            }
            return m;
        }
    }
}
