using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// More points of interest at each planet, moon and asteroid field than the bases built
    /// there, made as the level starts (WarpPoint.Kind.Landmark): on a planet or moon with a
    /// surface, a site in its northern and in its southern lands (flown to, and landed at, from
    /// navigation mode) and a high orbit over its pole; round a gas giant, a high orbit over its
    /// pole and one over its equator; in an asteroid field, its thickest cluster and its outer
    /// rocks. Each goes round with its body (its place), and is listed in navigation mode's menu
    /// with the bases there, and by the system map. They are only in this game (not saved, not
    /// sent in co-op: each game makes the same ones).
    /// </summary>
    public class Landmarks : MonoBehaviour
    {
        /// <summary>The landmarks made (for the self-test).</summary>
        public static readonly List<WarpPoint> All = new List<WarpPoint>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        void Start()
        {
            All.RemoveAll(w => w == null);
            if (All.Count > 0) return;   // (made already: one set a level)
            foreach (var b in WarpPoint.All.ToList())
            {
                if (b == null) continue;
                switch (b.kind)
                {
                    case WarpPoint.Kind.Planet:
                    case WarpPoint.Kind.Moon:
                        if (b.bodyRadius <= 0f) break;
                        if (b.surface != SurfaceKind.None)
                        {
                            Surface(b, $"{b.displayName} Northern Reach", 48f, 35f, "the northern lands");
                            Surface(b, $"{b.displayName} Southern Reach", -44f, 215f, "the southern lands");
                            Orbit(b, $"{b.displayName} High Orbit", Vector3.up, "high over its north pole");
                            // (a gas giant: its cloud tops are landed on at the Reaches, and it has
                            // an orbit over its equator too)
                            if (b.surface == SurfaceKind.GasGiant)
                                Orbit(b, $"{b.displayName} Equatorial Orbit", Vector3.right, "high over its equator");
                        }
                        else
                        {
                            Orbit(b, $"{b.displayName} High Orbit", Vector3.up, "high over its north pole");
                            Orbit(b, $"{b.displayName} Equatorial Orbit", Vector3.right, "high over its equator");
                        }
                        break;
                    case WarpPoint.Kind.AsteroidField:
                        Field(b, $"{b.displayName} Dense Cluster", new Vector3(0.45f, 0.02f, 0.2f), "the thickest of its rocks");
                        Field(b, $"{b.displayName} Outer Rocks", new Vector3(-0.3f, -0.03f, -0.75f), "its outer edge, the rocks spread thin");
                        break;
                }
            }
            // what waits at some of them
            Derelict("The Forge High Orbit", "Smuggler", "Smugglers' runabout",
                     "A smugglers' runabout, gutted and left in the parking orbit over The Forge: what the Forge smugglers didn't take is still aboard. Salvage it (Z).");
            if (All.Count > 0) Debug.Log($"[Landmarks] {All.Count} made: {string.Join(", ", All.Select(w => w.displayName))}");
        }

        static WarpPoint Make(WarpPoint body, string name, Vector3 offset, string kindLabel, string about)
        {
            var go = new GameObject("Landmark: " + name);
            go.SetActive(false);
            var w = go.AddComponent<WarpPoint>();
            w.displayName = name;
            w.kind = WarpPoint.Kind.Landmark;
            w.kindLabel = kindLabel;
            w.description = $"{about} of {body.displayName}.";
            w.mapColor = body.mapColor;
            w.follow = body;
            w.followOffset = offset;
            w.surface = SurfaceKind.None;
            go.AddComponent<LandmarkPose>().point = w;
            go.transform.position = w.Position;
            go.SetActive(true);
            All.Add(w);
            return w;
        }

        /// <summary>A site on the surface, by latitude and longitude (degrees, in the body's frame).</summary>
        static void Surface(WarpPoint body, string name, float lat, float lon, string about)
        {
            Vector3 dir = Quaternion.Euler(-lat, lon, 0f) * Vector3.forward;
            var w = Make(body, name, dir * body.bodyRadius, "surface site", $"A site in {about}");
            // dropped out of warp high over it, as over a base (the ground is built on the way down)
            w.overhead = true;
            w.overheadSide = 0.1f;
            w.arrivalDistance = 12000f;
        }

        /// <summary>A high orbit over the body, along a direction in its frame (within the
        /// reach of its points of interest): a parking orbit, with nothing built there.</summary>
        static void Orbit(WarpPoint body, string name, Vector3 dir, string about)
        {
            var w = Make(body, name, OrbitOffset(body, dir), "orbit", $"An orbit {about}");
            w.description += " A parking orbit: nothing is built here.";
            w.arrivalDistance = 4000f;
        }

        /// <summary>Where a body's orbit landmark is, in its frame (its high orbit: straight up).</summary>
        public static Vector3 OrbitOffset(WarpPoint body, Vector3 dir)
        {
            float height = Mathf.Min(body.bodyRadius * 0.8f, Places.Reach(body) * 0.6f);
            return dir.normalized * (body.bodyRadius + height);
        }

        /// <summary>A derelict to salvage at a landmark (if the level has it): a wreck that stays,
        /// going round with the landmark, made of the pieces of a ship whose name starts with
        /// `likeShip` (its kind of ship), holding what such a ship's wreck holds.</summary>
        static void Derelict(string at, string likeShip, string of, string about)
        {
            var w = All.FirstOrDefault(x => x != null && x.displayName == at);
            if (w == null) return;
            var like = Object.FindObjectsByType<TargetInfo>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                             .FirstOrDefault(i => i != null && !string.IsNullOrEmpty(i.displayName) && i.displayName.StartsWith(likeShip));
            if (like == null) { Debug.LogWarning($"[Landmarks] no {likeShip} to make the derelict at {at} of"); return; }
            var contents = Salvage.ContentsFor(like.transform, like, out float size, out _);
            if (contents.Count == 0) return;
            var wreck = Wreck.Make(like.transform, of, contents, size, null, burning: false);
            wreck.Stays = true;
            wreck.transform.SetParent(w.transform, false);
            wreck.transform.localPosition = Vector3.zero;
            if (wreck.TryGetComponent(out WarpPoint poi)) poi.description = about;
            w.description = $"{w.description.Replace(" A parking orbit: nothing is built here.", "")} A smugglers' derelict drifts here, to salvage.";
        }

        /// <summary>A part of an asteroid field, as a share of its radius in its frame.</summary>
        static void Field(WarpPoint field, string name, Vector3 share, string about)
        {
            float r = Mathf.Max(field.bodyRadius, 20000f);
            var w = Make(field, name, share * r, "in the field", $"Among the rocks: {about}");
            w.arrivalDistance = 3000f;
        }
    }

    /// <summary>Keeps a landmark's object where the landmark is (it goes round with its body),
    /// for anything that looks at its transform.</summary>
    public class LandmarkPose : MonoBehaviour
    {
        public WarpPoint point;

        void LateUpdate()
        {
            if (point == null || point.follow == null) return;
            transform.SetPositionAndRotation(point.Position, point.follow.transform.rotation);
        }
    }
}
