using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A band of enemies lying in wait somewhere in the star system (in an asteroid field). They
    /// all attack together as soon as one of them can see the player within `spotRange`, or one of
    /// them is shot. When the player leaves (warps out, or runs further than the pilots' leash)
    /// they go back into hiding, so the place is dangerous again on the next visit. A band told to
    /// hold off (a Vrask group that has withdrawn: VraskFleet) doesn't spring until it is let go.
    /// </summary>
    public class EncounterGroup : MonoBehaviour
    {
        public string groupName = "Hostiles";
        public CombatPilot[] pilots;
        public float spotRange = 4000f;
        /// <summary>Don't spring (VraskFleet: withdrawn, until its lost ships are back).</summary>
        [System.NonSerialized] public bool holdOff;

        public bool Active { get; private set; }
        public int TimesSprung { get; private set; }
        Transform player;
        float nextLook;

        void Start() => player = PlayerShips.Primary;

        /// <summary>Back to lying in wait, as before any fight (the battle simulator's, between battles).</summary>
        public void ResetBand()
        {
            Active = false;
            holdOff = false;
        }

        /// <summary>They break cover and attack now.</summary>
        public void Spring()
        {
            if (Active || pilots == null) return;
            Active = true;
            TimesSprung++;
            foreach (var p in pilots) if (p) p.Engage();
            SoundFX.Ui(Sfx.UiAlert);
            Notices.Post($"{groupName} attacking!");
        }

        void Update()
        {
            if (pilots == null) return;
            // with more than one player ship about (co-op): the one nearest the group (and another
            // at once if the one it had is gone: a player who left, a ship destroyed)
            if (player == null && PlayerShips.Count == 0)
            {
                // (no player ship about: looked for once a second, not every frame)
                if (Time.time < nextLook) return;
                nextLook = Time.time + 1f;
                player = PlayerShips.Primary;
            }
            else if (pilots.Length > 0 && pilots[0] != null)
                player = PlayerShips.Retarget(player, pilots[0].transform.position, ref nextLook, 0f);
            if (player == null) return;
            bool anyEngaged = false, anyLurking = false;
            foreach (var p in pilots)
            {
                if (p == null || !p.Alive) continue;    // the dead wait to respawn, they aren't fighting
                if (p.Current == CombatPilot.State.Lurk) anyLurking = true; else anyEngaged = true;
            }
            if (!Active && !holdOff)
            {
                Vector3 pp = player.position;
                bool spotted = anyEngaged;   // one of them was shot (and has already come out)
                foreach (var p in pilots)
                {
                    // only from the hiding places: not while one is still flying back to its own
                    if (spotted || p == null || !p.Alive || p.Current != CombatPilot.State.Lurk || !p.AtHome) continue;
                    Vector3 e = p.transform.position;
                    if ((e - pp).sqrMagnitude < spotRange * spotRange && Cover.Visible(e, pp)) spotted = true;
                }
                if (spotted) Spring();
            }
            else if (!anyEngaged && anyLurking)
            {
                Active = false;    // they've all gone back into hiding
            }
        }
    }
}
