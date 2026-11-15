using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// What is left of a destroyed ship, gun or station, to salvage (in combat mode): a few of
    /// its scorched pieces drifting where it died, holding refined materials and now and then a
    /// ship part (Salvage decides what). A wreck lasts `life` seconds, or until it has been
    /// stripped. It is only in this game: in co-op each player has their own wrecks.
    /// </summary>
    public class Wreck : MonoBehaviour
    {
        public static readonly List<Wreck> All = new List<Wreck>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        /// <summary>What it was ("Pirate Cruiser").</summary>
        public string Of = "";
        /// <summary>What is left to salvage: item id and amount (tonnes, or whole parts).</summary>
        public readonly List<(string id, float amount)> Contents = new List<(string, float)>();
        /// <summary>Roughly how big it is (m), for how near the salvage beam must be.</summary>
        public float Size = 30f;
        public float life = 900f;

        float dieAt;
        Vector3 drift, spinAxis;
        float spinRate;

        public string Name => $"Wreck of {Of}";
        public bool Empty => Contents.All(c => c.amount <= 1e-4f);
        /// <summary>Tonnes left in it.</summary>
        public float Tonnes
        {
            get
            {
                var table = IndustryTable.Get();
                return table == null ? 0f : Contents.Sum(c => table.Mass(c.id, c.amount));
            }
        }

        /// <summary>A short list of what is in it ("12 t steel plate, 1 turret mount").</summary>
        public string Summary(int max = 3)
        {
            var table = IndustryTable.Get();
            if (table == null || Empty) return "nothing left";
            var parts = Contents.Where(c => c.amount > 1e-4f).Take(max).Select(c =>
                table.Part(c.id) != null ? $"{c.amount:F0} {table.Name(c.id).ToLowerInvariant()}" : $"{c.amount:F0} t {table.Name(c.id).ToLowerInvariant()}");
            int more = Contents.Count(c => c.amount > 1e-4f) - max;
            return string.Join(", ", parts) + (more > 0 ? $" and {more} more" : "");
        }

        void OnEnable()
        {
            All.Add(this);
            dieAt = Time.time + life;
            drift = Random.insideUnitSphere * 1.5f;
            spinAxis = Random.onUnitSphere;
            spinRate = Random.Range(2f, 6f);
        }

        void OnDisable() => All.Remove(this);

        void Update()
        {
            if (Time.time >= dieAt || (Empty && Contents.Count > 0)) { Destroy(gameObject); return; }
            transform.position += drift * Time.deltaTime;
            transform.Rotate(spinAxis, spinRate * Time.deltaTime, Space.World);
        }

        /// <summary>Move what will fit into the hold, a share at a time (`share` of what is
        /// left of each item, at least a tonne or a part): returns a line saying what came aboard
        /// (null if nothing could).</summary>
        public string TakeInto(CargoHold hold, float share)
        {
            var table = IndustryTable.Get();
            if (hold == null || table == null) return null;
            var got = new List<string>();
            for (int i = 0; i < Contents.Count; i++)
            {
                var (id, left) = Contents[i];
                if (left <= 1e-4f) continue;
                bool part = table.Part(id) != null;
                float want = part ? Mathf.Max(1f, Mathf.Floor(left * share)) : Mathf.Max(Mathf.Min(1f, left), left * share);
                float took = hold.Load(id, Mathf.Min(want, left));
                if (took <= 0f) continue;
                Contents[i] = (id, left - took);
                got.Add(part ? $"{took:F0} {table.Name(id).ToLowerInvariant()}" : $"{took:F1} t {table.Name(id).ToLowerInvariant()}");
            }
            return got.Count > 0 ? string.Join(", ", got) : null;
        }

        /// <summary>A wreck where `dead` died, built from a few of its pieces (its breakup's
        /// chunks, or its own meshes), darkened.</summary>
        public static Wreck Make(Transform dead, string of, IEnumerable<(string id, float amount)> contents, float size)
        {
            var go = new GameObject($"Wreck of {of}");
            go.transform.position = dead.position;
            go.SetActive(false);
            var w = go.AddComponent<Wreck>();
            w.Of = of;
            w.Size = size;
            w.Contents.AddRange(contents.Where(c => c.amount > 0f));
            // the biggest few of its pieces, scattered a little
            var meshes = new List<(MeshFilter f, MeshRenderer r)>();
            var breakup = dead.GetComponent<ShipBreakup>();
            IEnumerable<Transform> sources = breakup != null && breakup.chunks != null && breakup.chunks.Length > 0 ? breakup.chunks.Where(c => c != null) : new[] { dead };
            foreach (var src in sources)
                foreach (var f in src.GetComponentsInChildren<MeshFilter>(true))
                    if (f.sharedMesh != null && f.TryGetComponent(out MeshRenderer r) && r.sharedMaterial != null) meshes.Add((f, r));
            var dark = new MaterialPropertyBlock();
            dark.SetColor("_BaseColor", new Color(0.22f, 0.2f, 0.19f));
            dark.SetColor("_Color", new Color(0.22f, 0.2f, 0.19f));
            foreach (var (f, r) in meshes.OrderByDescending(m => m.f.sharedMesh.bounds.size.sqrMagnitude * m.f.transform.lossyScale.sqrMagnitude).Take(4))
            {
                var piece = new GameObject("piece");
                piece.transform.SetParent(go.transform, false);
                piece.transform.position = dead.position + Random.insideUnitSphere * size * 0.35f;
                piece.transform.rotation = Random.rotation;
                piece.transform.localScale = f.transform.lossyScale;
                piece.AddComponent<MeshFilter>().sharedMesh = f.sharedMesh;
                var pr = piece.AddComponent<MeshRenderer>();
                pr.sharedMaterials = r.sharedMaterials;
                pr.SetPropertyBlock(dark);
            }
            go.SetActive(true);
            return w;
        }
    }
}
