using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// Makes a ship come apart when it is destroyed. The ship's model is a set of chunks (cut from
    /// its mesh by the builder); when Health runs out:
    ///   1. over `breakTime` seconds its parts snap off one after another, each with a small blast,
    ///      drifting away from the hull and starting to tumble;
    ///   2. then the main explosion goes off and every piece is thrown outward, spinning;
    ///   3. the wreckage drifts on and shrinks away, and is hidden after `duration` seconds.
    /// The pieces fly free of the ship (it may keep moving, e.g. on its patrol orbit, while it waits
    /// to respawn) and are put back together by Restore() when it respawns.
    /// </summary>
    public class ShipBreakup : MonoBehaviour
    {
        public enum Phase { Intact, Breaking, Blown }

        [Tooltip("The chunks the ship's model is built from.")]
        public Transform[] chunks;
        [Tooltip("Time from the first part breaking off to the main explosion.")]
        public float breakTime = 0.9f;
        [Tooltip("How long the wreckage stays before it is hidden.")]
        public float duration = 5f;
        [Tooltip("Speed parts drift off at before the blast (m/s).")]
        public float crackSpeed = 6f;
        [Tooltip("Speed the blast throws pieces outward (m/s, roughly).")]
        public float blastSpeed = 45f;
        public float spin = 50f;           // degrees per second, at most
        public Explosion smallBlast;
        public float smallBlastScale = 1f;

        public Phase State { get; private set; }
        public float BrokeAt { get; private set; } = -100f;
        public float BlewAt { get; private set; } = -100f;
        /// <summary>How many parts broke off before the main explosion (for the self-test).</summary>
        public int ReleasedBeforeBlast { get; private set; }

        struct Piece
        {
            public Transform t;
            public Transform home;
            public Vector3 localPos, localScale;
            public Quaternion localRot;
            public Vector3 velocity, angular, outward;
            public float releaseAt;
            public bool released;
        }

        Piece[] pieces;
        Transform debris;
        Explosion mainBlast;
        float mainScale;
        Vector3 inherited;

        void Awake()
        {
            if (chunks == null) chunks = new Transform[0];
            pieces = new Piece[chunks.Length];
            for (int i = 0; i < chunks.Length; i++)
            {
                var c = chunks[i];
                pieces[i] = new Piece { t = c, home = c ? c.parent : null, localPos = c ? c.localPosition : Vector3.zero,
                                        localRot = c ? c.localRotation : Quaternion.identity, localScale = c ? c.localScale : Vector3.one };
            }
        }

        /// <summary>Start coming apart; the main explosion follows after breakTime.</summary>
        public void Break(Explosion blast, float blastScale)
        {
            if (State != Phase.Intact || pieces.Length == 0)
            {
                if (pieces.Length == 0 && blast != null) Spawn(blast, transform.position, blastScale);
                return;
            }
            mainBlast = blast;
            mainScale = blastScale;
            State = Phase.Breaking;
            BrokeAt = Time.time;
            ReleasedBeforeBlast = 0;
            var mover = Motion.Of(transform);
            inherited = mover != null ? mover.Velocity : Vector3.zero;

            // the wreckage flies free of the ship itself
            if (debris == null) debris = new GameObject(name + " wreckage").transform;
            Vector3 centre = transform.position;
            float maxR = 1f;
            for (int i = 0; i < pieces.Length; i++)
                if (pieces[i].t) maxR = Mathf.Max(maxR, Vector3.Distance(pieces[i].t.position, centre));
            for (int i = 0; i < pieces.Length; i++)
            {
                ref var p = ref pieces[i];
                if (!p.t) continue;
                p.t.SetParent(debris, true);
                Vector3 off = p.t.position - centre;
                p.outward = off.sqrMagnitude > 1e-4f ? off.normalized : Random.onUnitSphere;
                // outer parts go first; the core holds until the blast
                float r = off.magnitude / maxR;
                p.releaseAt = BrokeAt + breakTime * Mathf.Clamp01(1f - r + Random.Range(-0.15f, 0.25f));
                p.released = false;
                p.velocity = inherited;
                p.angular = Vector3.zero;
            }
        }

        void Update()
        {
            if (State == Phase.Intact) return;
            float dt = Time.deltaTime, now = Time.time;

            if (State == Phase.Breaking)
            {
                for (int i = 0; i < pieces.Length; i++)
                {
                    ref var p = ref pieces[i];
                    if (!p.t || p.released || now < p.releaseAt) continue;
                    p.released = true;
                    ReleasedBeforeBlast++;
                    p.velocity = inherited + p.outward * crackSpeed * Random.Range(0.6f, 1.4f) + Random.insideUnitSphere * crackSpeed * 0.4f;
                    p.angular = Random.insideUnitSphere * spin * 0.5f;
                    if (smallBlast != null) Spawn(smallBlast, p.t.position, smallBlastScale * Random.Range(0.7f, 1.2f));
                }
                if (now - BrokeAt >= breakTime) Blow();
            }

            float age = now - BlewAt;
            for (int i = 0; i < pieces.Length; i++)
            {
                ref var p = ref pieces[i];
                if (!p.t) continue;
                p.t.position += p.velocity * dt;
                if (p.angular.sqrMagnitude > 0f) p.t.Rotate(p.angular * dt, Space.World);
                if (State == Phase.Blown)
                {
                    // gentle drag, then shrink away over the last part
                    p.velocity = Vector3.Lerp(p.velocity, inherited * 0.5f, dt * 0.2f);
                    float fade = Mathf.Clamp01((duration - age) / 1.5f);
                    p.t.localScale = p.localScale * fade;
                }
            }
            if (State == Phase.Blown && age >= duration)
                foreach (var p in pieces) if (p.t) p.t.gameObject.SetActive(false);
        }

        void Blow()
        {
            State = Phase.Blown;
            BlewAt = Time.time;
            if (mainBlast != null) Spawn(mainBlast, transform.position, mainScale);
            for (int i = 0; i < pieces.Length; i++)
            {
                ref var p = ref pieces[i];
                if (!p.t) continue;
                p.released = true;
                float kick = blastSpeed * Random.Range(0.5f, 1.3f);
                p.velocity += p.outward * kick + Random.insideUnitSphere * blastSpeed * 0.3f;
                p.angular = Random.insideUnitSphere * spin;
            }
        }

        /// <summary>Put the ship back together (on respawn).</summary>
        public void Restore()
        {
            for (int i = 0; i < pieces.Length; i++)
            {
                ref var p = ref pieces[i];
                if (!p.t) continue;
                p.t.gameObject.SetActive(true);
                p.t.SetParent(p.home, false);
                p.t.localPosition = p.localPos;
                p.t.localRotation = p.localRot;
                p.t.localScale = p.localScale;
                p.velocity = p.angular = Vector3.zero;
                p.released = false;
            }
            State = Phase.Intact;
        }

        void OnDestroy()
        {
            if (debris != null) Destroy(debris.gameObject);
        }

        static void Spawn(Explosion e, Vector3 at, float scale)
        {
            var x = Instantiate(e, at, Quaternion.identity);
            x.transform.localScale *= scale;
        }
    }
}
