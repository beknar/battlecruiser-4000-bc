using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A Vrask hull's armour, seen: ablative plates over the hull, as many as the armour is whole
    /// (Armor.Integrity). As hits wear the armour, plates blow off one by one, the one nearest the
    /// hit first: torn off glowing hot, tumbling outwards and cooling as they go. The armour never
    /// comes back in a fight. Out of one (no hit for `quietSeconds`, and no ship of the player's
    /// side within `enemyRange`), the Vrask grow it back slowly (`mendPerSecond` of
    /// the plating a second), and the plates come back with it; a dock or a respawn makes it whole
    /// at once. The plates have no colliders and aren't targets: they show the armour, they don't
    /// stop shots (the armour's sums do that, Armor.Reduce). They are made once, a ship, and used
    /// again (no new objects as they come and go).
    /// </summary>
    [DisallowMultipleComponent]
    public class AblativePlates : MonoBehaviour
    {
        [Tooltip("How many plates cover the hull.")]
        public int count = 12;
        [Tooltip("No hit for this long (s), and out of the fight, before the armour starts to grow back.")]
        public float quietSeconds = 90f;
        [Tooltip("How fast it grows back, out of a fight: a share of the plating a second.")]
        public float mendPerSecond = 0.02f;
        [Tooltip("A ship of the player's side this near (m) is a fight: no growing back.")]
        public float enemyRange = 15000f;

        /// <summary>Plates torn off, in all (for the self-test).</summary>
        public static int Shed { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Shed = 0;

        /// <summary>How many are on the hull now.</summary>
        public int Attached { get; private set; }
        public int Count => plates != null ? plates.Length : 0;
        /// <summary>The armour is growing back now (out of a fight).</summary>
        public bool Mending { get; private set; }

        Armor armor;
        Health health;
        Transform[] plates;
        Vector3[] homePos, vel, spin;
        Quaternion[] homeRot;
        float[] flyUntil;
        bool[] on;
        MaterialPropertyBlock glow;
        float nextLook;
        static Material plateMat;
        static Mesh cube;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        void Start()
        {
            armor = GetComponent<Armor>();
            health = GetComponent<Health>();
            if (armor == null || count <= 0) { enabled = false; return; }
            Build();
        }

        void OnDestroy()
        {
            // (the ones flying loose aren't the ship's children)
            if (plates == null) return;
            foreach (var p in plates) if (p != null && p.parent != transform) Destroy(p.gameObject);
        }

        static Material PlateMaterial()
        {
            if (plateMat != null) return plateMat;
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) return null;
            plateMat = new Material(sh) { name = "Vrask armour plate", hideFlags = HideFlags.DontSave };
            plateMat.SetColor("_BaseColor", new Color(0.2f, 0.21f, 0.2f));
            plateMat.SetFloat("_Metallic", 0.75f);
            plateMat.SetFloat("_Smoothness", 0.35f);
            plateMat.EnableKeyword("_EMISSION");
            plateMat.SetColor(EmissionId, Color.black);
            plateMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return plateMat;
        }

        /// <summary>The plates, over the hull's bounds (in the ship's own frame), the same each time
        /// for a ship of this name.</summary>
        void Build()
        {
            var mat = PlateMaterial();
            if (cube == null) cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            if (mat == null || cube == null) { enabled = false; return; }
            // the hull's bounds in its own frame (not the shield bubble, nor any plume)
            var toLocal = transform.worldToLocalMatrix;
            bool any = false;
            var b = new Bounds();
            foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.GetComponentInParent<Shield>(true) != null || !mf.TryGetComponent(out MeshRenderer mr) || !mr.enabled) continue;
                var m = toLocal * mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            if (!any) { enabled = false; return; }
            Vector3 ext = b.extents;
            float size = Mathf.Max(2f, Mathf.Min(ext.x, ext.y, ext.z) * 0.9f + ext.magnitude * 0.1f);
            var rng = new System.Random(name.GetHashCode());
            plates = new Transform[count];
            homePos = new Vector3[count];
            homeRot = new Quaternion[count];
            vel = new Vector3[count];
            spin = new Vector3[count];
            flyUntil = new float[count];
            on = new bool[count];
            glow = new MaterialPropertyBlock();
            for (int i = 0; i < count; i++)
            {
                // round the hull, along its length, on its sides and back more than its belly
                float along = -0.8f + 1.6f * (i + (float)rng.NextDouble() * 0.8f) / count;
                float round = (float)rng.NextDouble() * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(round), Mathf.Sin(round) * 0.8f + 0.15f, 0f).normalized;
                var local = b.center + new Vector3(dir.x * ext.x, dir.y * ext.y, along * ext.z) * 0.98f;
                var normal = new Vector3(dir.x / Mathf.Max(1f, ext.x), dir.y / Mathf.Max(1f, ext.y), along * 0.3f / Mathf.Max(1f, ext.z)).normalized;
                var go = new GameObject($"Armour plate {i + 1}");
                go.layer = gameObject.layer;
                go.transform.SetParent(transform, false);
                homePos[i] = local;
                homeRot[i] = Quaternion.LookRotation(normal, Vector3.forward);
                go.transform.SetLocalPositionAndRotation(local, homeRot[i]);
                float s = size * (0.8f + 0.4f * (float)rng.NextDouble());
                go.transform.localScale = new Vector3(s, s * 0.7f, s * 0.08f);
                go.AddComponent<MeshFilter>().sharedMesh = cube;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                plates[i] = go.transform;
                on[i] = true;
            }
            Attached = count;
        }

        void Update()
        {
            if (plates == null) return;
            // the ones flying: outwards, tumbling, cooling from orange heat, gone after 3 s
            float now = Time.time, dt = Time.deltaTime;
            for (int i = 0; i < plates.Length; i++)
            {
                if (on[i] || plates[i] == null || !plates[i].gameObject.activeSelf) continue;
                if (now >= flyUntil[i]) { plates[i].gameObject.SetActive(false); continue; }
                plates[i].position += vel[i] * dt;
                plates[i].Rotate(spin[i] * dt, Space.Self);
                float heat = Mathf.Clamp01((flyUntil[i] - now) / 3f);
                glow.SetColor(EmissionId, new Color(4f, 1.6f, 0.4f) * heat * heat);
                plates[i].GetComponent<MeshRenderer>().SetPropertyBlock(glow);
            }
            if (health != null && health.Dead)
            {
                // (the hull breaks up: its plates go with it)
                for (int i = 0; i < plates.Length; i++) if (on[i] && plates[i] != null) { on[i] = false; plates[i].gameObject.SetActive(false); }
                Attached = 0;
                return;
            }
            // as many on as the armour is whole: torn off as it wears, put back as it is mended
            int want = Mathf.Clamp(Mathf.CeilToInt(armor.Integrity * plates.Length - 0.001f), 0, plates.Length);
            while (Attached > want) TearOff();
            for (int i = 0; i < plates.Length && Attached < want; i++) if (!on[i]) PutBack(i);
            Mend(dt);
        }

        /// <summary>The plate nearest the last hit, torn off and thrown out.</summary>
        void TearOff()
        {
            int best = -1;
            float bd = float.MaxValue;
            Vector3 hit = armor.HasHitPoint ? armor.LastHitPoint : transform.position + Random.onUnitSphere * 50f;
            for (int i = 0; i < plates.Length; i++)
            {
                if (!on[i] || plates[i] == null) continue;
                float d = (plates[i].position - hit).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            if (best < 0) { Attached = 0; return; }
            var p = plates[best];
            on[best] = false;
            Attached--;
            Shed++;
            Vector3 outward = (p.position - transform.position).normalized;
            var mover = Motion.Of(transform);
            p.SetParent(null, true);
            vel[best] = (mover != null ? mover.Velocity : Vector3.zero) + outward * Random.Range(25f, 45f) + Random.insideUnitSphere * 8f;
            spin[best] = Random.insideUnitSphere * 400f;
            flyUntil[best] = Time.time + 3f;
        }

        void PutBack(int i)
        {
            var p = plates[i];
            if (p == null) return;
            p.SetParent(transform, false);
            p.SetLocalPositionAndRotation(homePos[i], homeRot[i]);
            p.gameObject.SetActive(true);
            glow.SetColor(EmissionId, Color.black);
            p.GetComponent<MeshRenderer>().SetPropertyBlock(glow);
            on[i] = true;
            Attached++;
        }

        /// <summary>Out of a fight (a while since the last hit, no ship of the player's side
        /// near), the armour grows back.</summary>
        void Mend(float dt)
        {
            // (another game's ship, in co-op: its armour grows back there, and comes here with it)
            if (armor.Integrity >= 1f || health == null || armor.NetDriven) { Mending = false; return; }
            if (Time.time >= nextLook)
            {
                nextLook = Time.time + 1f;
                bool quiet = Time.time - health.LastHitTime > quietSeconds;
                if (quiet && enemyRange > 0f)
                    foreach (var t in PlayerShips.All)
                        if (t != null && t.gameObject.activeInHierarchy && (t.position - transform.position).sqrMagnitude < enemyRange * enemyRange) { quiet = false; break; }
                Mending = quiet;
            }
            if (Mending) armor.Mend(armor.plating * mendPerSecond * dt);
        }
    }
}
