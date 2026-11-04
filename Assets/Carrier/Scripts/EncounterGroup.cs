using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// A band of enemies lying in wait somewhere in the star system (in an asteroid field). They
    /// all attack together as soon as one of them can see the player within `spotRange`, or one of
    /// them is shot. When the player leaves (warps out, or runs further than the pilots' leash)
    /// they go back into hiding, so the place is dangerous again on the next visit.
    /// </summary>
    public class EncounterGroup : MonoBehaviour
    {
        public string groupName = "Hostiles";
        public CombatPilot[] pilots;
        public float spotRange = 4000f;

        public bool Active { get; private set; }
        public int TimesSprung { get; private set; }
        Transform player;

        void Start()
        {
            var cc = FindAnyObjectByType<CarrierController>();
            if (cc != null) player = cc.transform;
        }

        /// <summary>They break cover and attack now.</summary>
        public void Spring()
        {
            if (Active || pilots == null) return;
            Active = true;
            TimesSprung++;
            foreach (var p in pilots) if (p) p.Engage();
            SoundFX.Ui(Sfx.UiAlert);
            Demo.CarrierDemoInput.Toast($"{groupName} attacking!");
        }

        void Update()
        {
            if (player == null || pilots == null) return;
            bool anyEngaged = false, anyLurking = false;
            foreach (var p in pilots)
            {
                if (p == null) continue;
                if (p.Current == CombatPilot.State.Lurk) anyLurking = true; else anyEngaged = true;
            }
            if (!Active)
            {
                Vector3 pp = player.position;
                bool spotted = anyEngaged;   // one of them was shot (and has already come out)
                foreach (var p in pilots)
                {
                    if (spotted || p == null || p.Current != CombatPilot.State.Lurk) continue;
                    var h = p.GetComponent<Health>();
                    if (h != null && h.Dead) continue;
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
