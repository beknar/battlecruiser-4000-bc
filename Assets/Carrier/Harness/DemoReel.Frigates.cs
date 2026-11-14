using System.Collections;
using System.Linq;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The frigates reel (-reel &lt;folder&gt; -frigates, in the Star System): the start menu,
    /// stepping through the four frigates and the captains; then each frigate chosen in turn and
    /// launched, shown close up with its turrets tracking and firing on a target drone circling
    /// it; and to finish, a fight: the pirate ambush in the Outer Drift (the feature reel's).
    /// The menu swaps each frigate in where the ship is (StartMenu.LiveSwap), so it all runs in
    /// one level.
    /// </summary>
    /// <summary>A reel's moving prop that tells the guns how fast it is going (so they lead it).</summary>
    public class ReelMover : MonoBehaviour, IVelocity
    {
        public Vector3 Velocity { get; set; }
    }

    public partial class DemoReel
    {
        public static bool Frigates;

        IEnumerator FrigateReel()
        {
            recording = false;
            while (frame < Warmup) yield return null;
            landing = PlanetLanding.Instance;
            Arm();
            SideGuns(false);
            var roster = FrigateRoster.Get();
            var menu = FindAnyObjectByType<StartMenu>();
            if (roster == null || roster.frigates.Length == 0 || menu == null) { Note("no frigate roster or start menu"); finished = true; yield break; }
            StartMenu.LiveSwap = true;
            Note($"frigates reel: {string.Join(", ", roster.frigates.Select(f => f.displayName))}");

            // ---- the menu: the frigates, then the captains
            Place(cc.transform.position, cc.transform.position + cc.transform.forward * 1000f);
            menu.Open();
            menu.Select(0);
            yield return FadeIn();
            Caption("FOUR FRIGATES", "Choose a ship and a captain as the game starts", 5f);
            yield return Wait(3.5f);
            for (int i = 1; i < roster.frigates.Length; i++)
            {
                menu.Select(i);
                yield return Wait(3.2f);
            }
            menu.Select(0);
            yield return Wait(1.6f);
            int captain = Captain.Index;
            for (int k = 1; k <= Captain.Roster.Length; k++)
            {
                Captain.Index = (captain + k) % Captain.Roster.Length;
                yield return Wait(0.9f);
            }
            yield return Wait(0.8f);

            // ---- each frigate in turn: chosen, launched, its guns at work
            for (int i = 0; i < roster.frigates.Length; i++)
            {
                if (!StartMenu.IsOpen)
                {
                    menu.Open();
                    menu.Select(i == 0 ? 0 : i - 1);
                    yield return FadeIn(0.4f);
                    yield return Wait(0.8f);
                }
                menu.Select(i);
                yield return Wait(2.2f);
                menu.Launch();
                Arm();
                Note($"launched the {roster.frigates[i].displayName}");
                yield return Showcase(i, roster.frigates[i]);
                yield return FadeOut(0.4f);
            }

            // ---- and a fight, in the last one
            StartMenu.LiveSwap = false;
            Arm();
            yield return Ambush();
            Note($"the end: flying the {PlayerFrigate.Flying}");
            finished = true;
        }

        /// <summary>The ship as it is now (after a swap): its hull can't be lost in the reel.</summary>
        void Arm()
        {
            hullF = cc.GetComponent<Health>();
            shieldF = cc.GetComponentInChildren<Shield>(true);
            if (hullF != null) hullF.cannotDie = true;
            sysDrive = cc.GetComponent<WarpDrive>();
        }

        /// <summary>A ship close up, its turrets tracking and firing on a target drone that
        /// circles it (and a missile salvo at it, from a ship that carries them); `first` and
        /// `second` seconds on each of the two shots.</summary>
        IEnumerator Showcase(int index, FrigateRoster.Entry e, float first = 5f, float second = 4.5f)
        {
            Repair();
            Place(cc.transform.position, cc.transform.position + cc.transform.forward * 1000f);
            cc.throttle = 0.12f;
            var drone = TargetDrone();
            var mover = drone.GetComponent<ReelMover>();
            float t0 = Time.time, heading = cc.Heading;
            const float radius = 1300f;
            perFrame = () =>
            {
                float age = Time.time - t0;
                float a = (heading + 25f + age * 12f) * Mathf.Deg2Rad;
                var p = cc.transform.position + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * radius + Vector3.up * (220f + 120f * Mathf.Sin(age * 0.7f));
                var v = p - drone.position;
                drone.position = p;
                if (v.sqrMagnitude > 0.01f) drone.rotation = Quaternion.LookRotation(v);
                if (Time.deltaTime > 0f) mover.Velocity = v / Time.deltaTime;   // so the guns lead it
                cam.yaw += 5f * Time.deltaTime;
            };
            cc.SetTarget(drone);
            SideGuns(true);
            Shot(300 + index * 2, cc.transform, demo.ShipViewOffset, D * 0.48f, heading + 145f, 14f, 6f);
            var st = FrigateStats.Of(cc.gameObject);
            Caption(e.displayName.ToUpperInvariant(), ShipLine(e, st), first + 1f);
            yield return FadeIn(0.3f);
            yield return Wait(first);
            Shot(301 + index * 2, cc.transform, demo.ShipViewOffset, D * 0.42f, heading + 55f, 8f, -5f);
            int salvo = 0;
            if (cc.HasMissiles && cc.missiles.Stock != 0)
            {
                int before = cc.missiles.Stock;
                demo.FireMissiles();
                yield return Wait(1.5f);
                salvo = before - cc.missiles.Stock;
                yield return Wait(Mathf.Max(0f, second - 1.5f));
            }
            else yield return Wait(second);
            var fired = cc.mainTurrets.Where(x => x != null && x.ShotsFired > 0).Select(x => x.Label);
            var dh = drone.GetComponent<Health>();
            Note($"  the {e.displayName}: turrets that fired: {string.Join(", ", fired)}; {dh.Hits} hits on the drone" +
                 (salvo > 0 ? $"; a salvo of {salvo} missiles ({cc.missiles.Stock} left)" : ""));
            SideGuns(false);
            cc.SetTarget(null);
            perFrame = null;
            Destroy(drone.gameObject);
        }

        /// <summary>Something for the guns to shoot at: the look of one of the level's enemy drones,
        /// with a target's hull that can't be shot down.</summary>
        Transform TargetDrone()
        {
            var go = new GameObject("Target drone");
            go.SetActive(false);
            var src = FindObjectsByType<CombatPilot>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(p => p != null && p.name.Contains("Drone"));
            if (src != null)
            {
                var model = StartMenu.ModelOf(src.gameObject);
                model.transform.SetParent(go.transform, false);
                foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
            }
            go.AddComponent<SphereCollider>().radius = 18f;
            go.AddComponent<ReelMover>();
            var h = go.AddComponent<Health>();
            h.maxHealth = 20000f;     // more than a frigate's guns take off it in a showcase
            h.cannotDie = true;
            go.SetActive(true);
            return go.transform;
        }
    }
}
