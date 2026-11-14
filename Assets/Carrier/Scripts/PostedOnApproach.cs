using CarrierGame.Demo;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A capital ship battle group posted on the way in to a place (a planet): on station a few
    /// kilometres on from where a warp from the player's side drops the ship, straight ahead along
    /// its way in and a little below it (their turrets, on top, can look up at the ship; it noses
    /// down toward the planet as it arrives), so the player comes out of warp with them in front. (Ahead
    /// in the ship's own frame, as it will be on arrival: by a real-size planet "up" is out from
    /// it, and the planet itself is far below.) The planets go round over days and
    /// weeks, so the way in from anywhere turns with them: whenever the player's warp drive charges
    /// for the place, the group (lying in wait, not fighting, out of sight) moves to the station on
    /// that warp's way in. `slots` are the group's ships' places in the formation (in the order
    /// of its EncounterGroup's pilots): x across, y up and z along the way in, from the station.
    /// </summary>
    public class PostedOnApproach : MonoBehaviour
    {
        public WarpPoint at;
        [Tooltip("How far past the arrival point, toward the planet, the group waits (m).")]
        public float towardBody = 6000f;
        [Tooltip("How far below the arrival point it waits, as a share of `towardBody`.")]
        public float below = 0.25f;
        public Vector3[] slots;
        [Tooltip("Not moved while the player is this near it (m): it would be seen to.")]
        public float unseenBeyond = 40000f;

        /// <summary>Times it has moved onto a way in (for the self-test).</summary>
        public int Moves { get; private set; }

        WarpDrive drive;
        bool doneThisWarp;

        /// <summary>Where a ship in `slot` waits for a warp from `from`, and which way they face
        /// (the builder lays the group out with it too).</summary>
        public Vector3 SlotPosition(Vector3 from, Vector3 slot, out Quaternion face)
        {
            Station(from, out Vector3 station, out Vector3 along, out Vector3 across, out Vector3 up);
            face = Quaternion.LookRotation(-along, up);
            return station + across * slot.x + up * slot.y + along * slot.z;
        }

        /// <summary>Where the ships would wait for a warp from `from`, and which way they face.</summary>
        public void Station(Vector3 from, out Vector3 station, out Vector3 along, out Vector3 across) =>
            Station(from, out station, out along, out across, out _);

        public void Station(Vector3 from, out Vector3 station, out Vector3 along, out Vector3 across, out Vector3 up)
        {
            Vector3 arrive = at.ArrivalPoint(from);
            // the ship's "up" there (out from the planet, near it), and its way in, level
            up = Demo.PlanetFlight.UpAt(arrive, at, out _);
            along = Vector3.ProjectOnPlane(arrive - from, up);
            if (along.sqrMagnitude < 1f) along = Vector3.ProjectOnPlane(at.Position - arrive, up);
            if (along.sqrMagnitude < 1f) along = Vector3.Cross(up, Vector3.right);
            along.Normalize();
            across = Vector3.Cross(up, along).normalized;
            station = arrive + along * towardBody - up * (towardBody * below);
        }

        void Update()
        {
            if (at == null || slots == null) return;
            if (drive == null)
            {
                var ship = PlayerShips.Primary;
                drive = ship != null ? ship.GetComponent<WarpDrive>() : null;
                if (drive == null) return;
            }
            if (drive.Current == WarpDrive.State.Ready) { doneThisWarp = false; return; }
            if (doneThisWarp || drive.Current != WarpDrive.State.Charging || drive.Destination != at) return;
            doneThisWarp = true;
            MoveOnto(drive.transform.position);
        }

        /// <summary>Onto the way in of a warp from `from`, if the group is lying in wait unseen.</summary>
        public bool MoveOnto(Vector3 from)
        {
            // (in co-op the host runs the enemies: a guest's copies follow it)
            if (CoopClient.Active && !CoopClient.Instance.IsHost) return false;
            var group = GetComponent<EncounterGroup>();
            var pilots = group != null ? group.pilots : null;
            if (pilots == null || group.Active) return false;
            foreach (var p in pilots)
            {
                if (p == null) continue;
                bool alive = p.Alive;
                if (alive && (p.Current != CombatPilot.State.Lurk || !p.AtHome)) return false;
                if (alive && Vector3.Distance(p.transform.position, from) < unseenBeyond) return false;
            }
            var orbits = OrbitalSystem.Instance;
            for (int i = 0; i < pilots.Length && i < slots.Length; i++)
            {
                var p = pilots[i];
                if (p == null) continue;
                Vector3 pos = SlotPosition(from, slots[i], out Quaternion face);
                // (kept far off in doubles by the orbits: let go of, and taken up again from here)
                if (orbits != null) orbits.Forget(p.transform);
                p.transform.SetPositionAndRotation(pos, face);
                p.SetHome(pos, face);
            }
            Moves++;
            return true;
        }
    }
}
