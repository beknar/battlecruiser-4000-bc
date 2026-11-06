using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>Something the mining laser can work: a rock, moon or comet in space, or a vein in
    /// a planet's surface.</summary>
    public interface IMineralSource
    {
        /// <summary>What it is ("M-type asteroid (metallic)").</summary>
        string Label { get; }
        /// <summary>What it is made of, biggest share first.</summary>
        Share[] Mix { get; }
        float Remaining { get; }
        float Initial { get; }
        bool Depleted { get; }
        /// <summary>Its centre (a body), or the point on the ground (a vein).</summary>
        Vector3 Position { get; }
        /// <summary>How far its surface is from its centre (0 for a vein).</summary>
        float Radius { get; }
        /// <summary>Still there (not unloaded with its area of a planet).</summary>
        bool Valid { get; }
        /// <summary>Found by the mineral sensor.</summary>
        bool Discovered { get; set; }
        /// <summary>Mine up to `tonnes`; returns how much came out.</summary>
        float Take(float tonnes);
    }

    /// <summary>
    /// The minerals in a body in space: an asteroid, a moon, a comet's nucleus or a lump of its
    /// debris. What it is made of comes from its seed (an asteroid is carbonaceous, stony or
    /// metallic), and how much there is from its size. The mining laser takes it away a few
    /// tonnes at a time; the mineral sensor finds it.
    /// </summary>
    public class MineralDeposit : MonoBehaviour, IMineralSource
    {
        public BodyClass body = BodyClass.Asteroid;
        [Tooltip("What it is made of comes from this (0: from where it is).")]
        public int seed;
        [Tooltip("Tonnes there are to mine (0: from its size).")]
        public float reserve;

        public static readonly List<MineralDeposit> All = new List<MineralDeposit>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();
        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        Share[] mix;
        string label;
        float radius, taken;

        void Init()
        {
            if (mix != null) return;
            int s = seed != 0 ? seed : PositionSeed(transform.position);
            mix = Minerals.ForBody(body, s, out label);
            // the rock's size, from its mesh (made when the level loads)
            var mf = GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                Vector3 e = Vector3.Scale(mf.sharedMesh.bounds.extents, transform.lossyScale);
                radius = (Mathf.Abs(e.x) + Mathf.Abs(e.y) + Mathf.Abs(e.z)) / 3f;
            }
            else if (TryGetComponent(out Obstacle ob)) radius = ob.coreRadius;
            if (reserve <= 0f)
                reserve = body switch
                {
                    BodyClass.Moon => 60000f,
                    BodyClass.Comet => 40000f,
                    BodyClass.CometDebris => 30f + radius * 3f,
                    _ => 40f + radius * 2.2f,
                };
        }

        /// <summary>A seed from a position (rocks that share a shape still differ inside).</summary>
        public static int PositionSeed(Vector3 p)
        {
            unchecked
            {
                int h = Mathf.RoundToInt(p.x) * 73856093 ^ Mathf.RoundToInt(p.y) * 19349663 ^ Mathf.RoundToInt(p.z) * 83492791;
                return (h & 0x7fffffff) % 1000003 + 1;
            }
        }

        public string Label { get { Init(); return label; } }
        public Share[] Mix { get { Init(); return mix; } }
        public float Initial { get { Init(); return reserve; } }
        public float Remaining { get { Init(); return Mathf.Max(0f, reserve - taken); } }
        public bool Depleted => Remaining < 0.05f;
        public Vector3 Position => transform.position;
        public float Radius { get { Init(); return radius; } }
        public bool Valid => this != null && isActiveAndEnabled;
        public bool Discovered { get; set; }

        public float Take(float tonnes)
        {
            float t = Mathf.Min(Mathf.Max(0f, tonnes), Remaining);
            taken += t;
            return t;
        }

        /// <summary>A small rock (or lump of comet ice) that has been mined out falls apart and is
        /// gone; a big one stays, a husk. True if it broke up.</summary>
        public bool BreakUp()
        {
            if (!Depleted || Radius > 40f || body == BodyClass.Moon || body == BodyClass.Comet) return false;
            gameObject.SetActive(false);
            return true;
        }
    }
}
