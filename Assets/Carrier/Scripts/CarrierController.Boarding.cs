using UnityEngine;

namespace CarrierGame
{
    /// <summary>Boarded, clamped and captured. A Vrask Fang clamped to the hull (FangTactics) holds
    /// the ship to a crawl and, as boarders aboard do, keeps the warp drive, docking and landing
    /// locked (WarpDrive.InCombat). Taken by boarders (Boarders, Health.Capture), the ship stops
    /// where it is with everything aboard shut down, its hold seized (CargoHold) and those of the
    /// crew the boarders took prisoners; the survivors bring it back to its respawn point (the
    /// last place it warped to), whole, after the usual respawn delay.</summary>
    public partial class CarrierController
    {
        /// <summary>The most a ship with a boarding ship clamped to its hull can make (m/s).</summary>
        public const float ClampSpeed = 4f;

        float clampUntil = -1f;
        Transform clampedBy;
        bool clampWas;

        /// <summary>Taken by boarders, and waiting to come back.</summary>
        public bool Captured => health != null && health.Captured;
        /// <summary>The faction that took it (the Vrask's if no boarders say).</summary>
        public string CapturedBy => Boarders.FactionOr(GetComponent<Boarders>()?.Faction);
        /// <summary>A boarding ship is clamped to the hull now.</summary>
        public bool Clamped => Time.time < clampUntil && !Destroyed;
        public Transform ClampedBy => Clamped ? clampedBy : null;
        /// <summary>The crew taken prisoner when the ship was last captured.</summary>
        public int LastTaken { get; private set; }
        /// <summary>Where the ship comes back after being destroyed or captured.</summary>
        public Vector3 SpawnPoint => spawnFrame != null ? spawnFrame.position + spawnFrame.rotation * spawnOffset : spawnPosition;

        /// <summary>A boarding ship (`by`) clamped to the hull, for `seconds` unless clamped again
        /// (it renews it every frame).</summary>
        public void Clamp(Transform by, float seconds)
        {
            bool was = Clamped;
            clampedBy = by;
            clampUntil = Time.time + seconds;
            if (!was && !Destroyed && transform == PlayerShips.Primary)
            {
                var info = by != null ? by.GetComponentInParent<TargetInfo>() : null;
                SoundFX.Ui(Sfx.UiAlert);
                Notices.Post($"{(info != null ? info.Name : "A boarding ship")} has clamped onto our hull!");
            }
        }

        /// <summary>The boarding ship has cast off.</summary>
        public void Unclamp() => clampUntil = -1f;

        /// <summary>Clamped: held to a crawl (on top of any tractor beams' hold).</summary>
        void UpdateClamp()
        {
            if (!Clamped)
            {
                if (clampWas && transform == PlayerShips.Primary && !Destroyed) Notices.Post("The boarding ship has cast off");
                clampWas = false;
                return;
            }
            clampWas = true;
            tractorCap = Mathf.Min(tractorCap, ClampSpeed);
        }

        /// <summary>Taken by boarders: everything aboard shuts down and the ship stops, in one piece.</summary>
        void OnShipCaptured()
        {
            if (PlayerShips.Primary == transform) WorldState.ShipTaken();
            OnShipDestroyed();
            Untractor();
            clampUntil = -1f;
            clampWas = false;
            var b = GetComponent<Boarders>();
            LastTaken = b != null ? b.Prisoners : 0;
            if (transform == PlayerShips.Primary)
            {
                SoundFX.Ui(Sfx.UiAlert);
                Notices.Post($"Captured by the {CapturedBy}: {LastTaken} of your crew taken");
            }
        }
    }
}
