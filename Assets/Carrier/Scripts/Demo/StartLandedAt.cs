using System.Collections;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// A level whose game starts down at a place (the Nine Hells: in Heaven, K'rzyn's capital):
    /// the frigate begins over it, and once the start menu is closed it lands there. Not in an
    /// automated run (the self-tests and reels start in the air over it), and only on a new
    /// start, not when the level is opened again with the frigate already about her business.
    /// </summary>
    public class StartLandedAt : MonoBehaviour
    {
        public string place = "Heaven";
        static bool done;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => done = false;

        IEnumerator Start()
        {
            if (done || LaunchArgs.Automated || Hud.ReelMode) yield break;
            // (after the start menu: the ship and captain chosen, the paint done)
            while (StartMenu.WillOpen || StartMenu.IsOpen) yield return null;
            yield return null;
            var landing = PlanetLanding.Instance;
            var at = WarpPoint.All.Find(w => w != null && w.displayName == place);
            if (landing == null || at == null) yield break;
            done = true;
            if (landing.LandOn(at)) Notices.Post($"Down to {place}");
        }
    }
}
