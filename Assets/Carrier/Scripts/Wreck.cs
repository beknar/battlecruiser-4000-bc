using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// What is left of a destroyed ship, gun or station, to salvage (in combat mode): a few of
    /// its scorched pieces drifting where it died, holding refined materials and now and then a
    /// ship part (Salvage decides what). A wreck lasts `life` seconds, or until it has been
    /// stripped. Its biggest pieces burn for a few minutes (FireFx), where fires are shown. It is only in this game: in co-op each player has their own wrecks. While it
    /// lasts it is a point of interest (on the system map and in the navigation list, to warp
    /// to); salvaged or gone, it is off them.
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
        /// <summary>What it was the wreck of (one that respawns leaves one wreck at a time).</summary>
        public Health From;
        public float life = 900f;

        /// <summary>How big a wreck's fire is (times the pack's own size: its blaze is about a metre
        /// tall) per metre of the wreck.</summary>
        public const float WreckFirePerMetre = 0.35f;

        float dieAt;
        // (what it held at first: the beam takes a share of that each time, so a wreck is
        // stripped in the time it is meant to be, not ever more slowly)
        readonly List<float> start = new List<float>();
        bool empty;
        Vector3 drift, spinAxis;
        float spinRate;

        public string Name => $"Wreck of {Of}";
        public bool Empty => empty;
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
            if (Time.time >= dieAt || (empty && Contents.Count > 0)) { Destroy(gameObject); return; }
            transform.position += drift * Time.deltaTime;
            transform.Rotate(spinAxis, spinRate * Time.deltaTime, Space.World);
        }

        /// <summary>Move what will fit into the hold, a share at a time (`share` of what each
        /// item came to at first, at least a tonne or a part): returns a line saying what came
        /// aboard (null if nothing could).</summary>
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
                float whole = i < start.Count ? start[i] : left;
                float want = part ? Mathf.Max(1f, Mathf.Floor(whole * share)) : Mathf.Max(Mathf.Min(1f, left), whole * share);
                float took = hold.Load(id, Mathf.Min(want, left));
                if (took <= 0f) continue;
                Contents[i] = (id, left - took);
                got.Add(part ? $"{took:F0} {table.Name(id).ToLowerInvariant()}" : $"{took:F1} t {table.Name(id).ToLowerInvariant()}");
            }
            empty = Contents.All(c => c.amount <= 1e-4f);
            if (empty && TryGetComponent(out WarpPoint poi)) poi.enabled = false;
            return got.Count > 0 ? string.Join(", ", got) : null;
        }

        /// <summary>A wreck where `dead` died, built from a few of its pieces (its breakup's
        /// chunks, or its own meshes), darkened.</summary>
        public static Wreck Make(Transform dead, string of, IEnumerable<(string id, float amount)> contents, float size, Health from = null)
        {
            var go = new GameObject($"Wreck of {of}");
            go.transform.position = dead.position;
            go.SetActive(false);
            var w = go.AddComponent<Wreck>();
            w.Of = of;
            w.Size = size;
            w.From = from;
            w.Contents.AddRange(contents.Where(c => c.amount > 0f));
            w.start.AddRange(w.Contents.Select(c => c.amount));
            w.empty = w.Contents.Count == 0;
            // a point of interest while it lasts (its WarpPoint goes with it when it is stripped)
            var poi = go.AddComponent<WarpPoint>();
            poi.displayName = w.Name;
            poi.kind = WarpPoint.Kind.Wreck;
            poi.kindLabel = "wreck";
            poi.description = $"What is left of a {of}, drifting: salvage it (Z) for its materials.";
            poi.mapColor = new Color(0.62f, 0.56f, 0.5f);
            poi.arrivalDistance = Mathf.Max(900f, size * 4f);
            poi.surface = SurfaceKind.None;
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
            // its two biggest pieces burning for a while (if the fires are there to show)
            var fires = FireKit.Get();
            if (fires != null)
            {
                // (its pieces, biggest first, before any fire joins them)
                var pieces = go.GetComponentsInChildren<MeshRenderer>().Take(2).ToArray();
                for (int i = 0; i < pieces.Length; i++)
                {
                    // (on top of the piece, not lost inside it)
                    var b = pieces[i].bounds;
                    FireFx.Kindle(i == 0 ? fires.blaze : fires.groundFire, go.transform, b.center + Vector3.up * b.extents.y * 0.8f,
                                  Mathf.Clamp(size * WreckFirePerMetre, 2f, 120f) * (i == 0 ? 1f : 0.5f), fires.wreckBurn);
                }
            }
            return w;
        }
    }
}
