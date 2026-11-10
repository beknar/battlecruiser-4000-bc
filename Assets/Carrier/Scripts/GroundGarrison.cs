using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarrierGame
{
    /// <summary>One area's ground forces: they all go to action stations together when one of
    /// them sees a player's ship within `spotRange` (hills hide it), or one of them is hit; and
    /// stand down when every ship has gone `leash` away. In co-op any player's ship can be seen.</summary>
    public class GroundGarrison : MonoBehaviour, IOriginShift
    {
        public PlanetSurface surface;
        public readonly List<GroundUnit> units = new List<GroundUnit>();
        public Vector3 center;
        public float spotRange = 5500f;
        public float leash = 12000f;

        public bool Alert { get; private set; }
        public int TimesAlerted { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { lastToast = -100f; }
        static float lastToast = -100f;

        float nextLook;

        public int AliveCount
        {
            get
            {
                int n = 0;
                foreach (var u in units) if (u != null && u.Alive) n++;
                return n;
            }
        }

        public void OriginShifted(Vector3 d) => center += d;

        void Update()
        {
            if (PlayerShips.Count == 0) return;
            units.RemoveAll(u => u == null);
            if (Time.time < nextLook) return;
            nextLook = Time.time + 0.3f;
            if (!Alert)
            {
                if (Spotted()) Raise();
            }
            else if (!AnyShipWithin(center, leash))
            {
                Alert = false;
                foreach (var u in units) u.StandDown();
            }
        }

        bool Spotted()
        {
            foreach (var u in units)
            {
                if (!u.Alive) continue;
                if (u.WasHit) return true;
                Vector3 e = u.Eye;
                foreach (var ship in PlayerShips.All)
                {
                    if (!PlayerShips.Present(ship)) continue;
                    Vector3 pp = ship.position;
                    if ((e - pp).sqrMagnitude < spotRange * spotRange && Cover.Visible(e, pp)) return true;
                }
            }
            return false;
        }

        static bool AnyShipWithin(Vector3 p, float range)
        {
            foreach (var ship in PlayerShips.All)
                if (PlayerShips.Present(ship) && (ship.position - p).sqrMagnitude <= range * range) return true;
            return false;
        }

        public void Raise()
        {
            if (Alert) return;
            Alert = true;
            TimesAlerted++;
            foreach (var u in units) u.Engage();
            if (Time.time - lastToast > 10f)
            {
                lastToast = Time.time;
                SoundFX.Ui(Sfx.UiAlert);
                Demo.CarrierDemoInput.Toast("Ground defences have spotted us!");
            }
        }
    }
}
