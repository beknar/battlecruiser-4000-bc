using UnityEngine;

namespace CarrierGame
{
    /// <summary>Held by tractor beams (TractorBeam; in co-op, the host's beams, by message): the
    /// ship is pulled toward them, its speed held down, and turned slowly stern to them. Not
    /// while something else is flying it (externalMotion: a warp, the landing, docking, a tow).
    /// While held it can't warp, dock or land (WarpDrive.InCombat).</summary>
    public partial class CarrierController
    {
        /// <summary>The beams' pull this frame (m/s, toward them): added to the ship's own motion,
        /// through Collide, so rocks still stop it.</summary>
        [System.NonSerialized] public Vector3 tractorPull;
        /// <summary>The ship's top speed while held (m/s; infinite when free).</summary>
        [System.NonSerialized] public float tractorCap = float.PositiveInfinity;

        Vector3 tractorFrom;
        int tractorBeams;
        bool tractorFirm, tractorWas;
        float tractorUntil = -1f;

        /// <summary>Held by a tractor beam now.</summary>
        public bool Tractored => tractorBeams > 0 && Time.time < tractorUntil && !Destroyed;
        /// <summary>How many beams hold it (0: free).</summary>
        public int TractorBeams => Tractored ? tractorBeams : 0;
        /// <summary>Held fast (two beams or more, or its engines knocked out): it can't fly free.</summary>
        public bool TractorFirm => Tractored && (tractorFirm || tractorBeams >= 2 || engineScale < 1f);
        /// <summary>Where the beams pull it toward.</summary>
        public Vector3 TractorFrom => tractorFrom;
        /// <summary>How far the beams have pulled it in all (m, for the self-test).</summary>
        public float TractorPulled { get; private set; }

        /// <summary>Held by `beams` tractor beams pulling toward `from` (`firm`: held fast whatever
        /// its engines), for `seconds` unless held again: the beams renew it every frame, co-op's
        /// message every half second.</summary>
        public void Tractor(Vector3 from, int beams, bool firm, float seconds)
        {
            bool was = Tractored;
            tractorFrom = from;
            tractorBeams = Mathf.Max(1, beams);
            tractorFirm = firm;
            tractorUntil = Time.time + seconds;
            if (!was && !Destroyed && transform == PlayerShips.Primary)
            {
                Notices.Post(TractorFirm ? "Tractor lock! We're held fast" : "Tractor lock! Full thrust away to break free");
                SoundFX.Ui(Sfx.UiAlert);
            }
        }

        /// <summary>Let go (co-op: the host's beams have let go).</summary>
        public void Untractor() => tractorUntil = -1f;

        /// <summary>The beams' hold this frame: their pull, the speed they leave it, and the slow
        /// turn stern to them (the pilot can turn against one beam; not against two).</summary>
        void UpdateTractor(float dt)
        {
            if (!Tractored)
            {
                tractorPull = Vector3.zero;
                tractorCap = float.PositiveInfinity;
                if (tractorWas && transform == PlayerShips.Primary) Notices.Post("Free of the tractor beam");
                tractorWas = false;
                return;
            }
            tractorWas = true;
            Vector3 to = tractorFrom - transform.position;
            float d = to.magnitude;
            bool firm = TractorFirm;
            TractorBeam.Strength(tractorBeams, firm, maxSpeed * speedScale * engineScale, d, out float pull, out tractorCap);
            tractorPull = d > 1f ? to / d * pull : Vector3.zero;
            TractorPulled += pull * dt;
            if (d > 1f && !holdLevel)
            {
                float turn = TractorBeam.TurnSpeed * Mathf.Min(tractorBeams, TractorBeam.MaxBeams) * (firm ? 1.5f : 1f) * dt;
                Aim(-to, out float h, out float p);
                heading = Mathf.MoveTowardsAngle(heading, h, turn);
                pitchAngle = Mathf.MoveTowards(pitchAngle, p, turn);
            }
        }
    }
}
