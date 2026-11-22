using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The Greys raiding a place for people (the Goliath cell's raids on Nineveh's research
    /// station): every `intervalMinutes` of game time (WorldState's clock, saved; whether or not
    /// anyone is near), while its band isn't in a fight and its Reaper is alive, `share` of the
    /// people there are taken (IndustryLedger: the place's people), a notice says so, and co-op
    /// passes it on as the Vrask siege's strikes are (VraskBombardment.Tell: an order with no
    /// blast). Only the game running the enemies raids (co-op: the host's).
    /// </summary>
    public class GreysRaid : MonoBehaviour
    {
        public string place = "Nineveh Observatory";
        public string body = "Nineveh";
        public float intervalMinutes = 180f;
        [Range(0f, 1f)] public float share = 0.03f;
        public EncounterGroup group;

        public int Raids { get; private set; }
        public int Taken { get; private set; }

        string Timer => "greys raid:" + place;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void HostOnly() => CoopWorld.HostOnlyBehaviour(typeof(GreysRaid));

        void Update()
        {
            if (!WorldState.TryTimer(Timer, out double at)) { WorldState.SetTimer(Timer, WorldState.Clock + intervalMinutes * 60.0); return; }
            if (WorldState.Clock < at) return;
            WorldState.SetTimer(Timer, WorldState.Clock + intervalMinutes * 60.0);
            if (group != null && group.Active) return;              // (busy: no raid this time)
            if (!ReaperAlive()) return;                             // (no Reaper: no one to take them)
            RaidNow();
        }

        bool ReaperAlive()
        {
            if (group == null || group.pilots == null) return true;
            foreach (var p in group.pilots)
                if (p != null && p.TryGetComponent(out GreysShip g) && g.role == GreysShip.Role.Reaper && g.Alive) return true;
            return false;
        }

        /// <summary>A raid now (the tests): people taken from the place.</summary>
        public int RaidNow()
        {
            // (at least one taken: a small place's share would round down to none)
            float pool = IndustryLedger.Pool(place);
            float take = pool > 0f ? Mathf.Max(share, 1f / pool) : share;
            var o = new VraskBombardment.Order { body = body, place = place, weapon = "the Greys", radius = 0f, population = take };
            var r = VraskBombardment.Apply(o, false);
            Raids++;
            Taken += r.casualties;
            if (r.casualties > 0)
            {
                Notices.Post($"{place}: {r.casualties} people taken by the Greys in the night");
                VraskBombardment.Tell(o);
            }
            return r.casualties;
        }
    }
}
