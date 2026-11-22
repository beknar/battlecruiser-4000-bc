using CarrierGame;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Carrier.EditorTools
{
    /// <summary>
    /// The Vrask's boarding (Data/vrask.json: a ship's "pods", "podInterval", "podRange"): their
    /// boarding pod (Prefabs/VraskBoardingPod: the missile's model, bigger, with a green trail),
    /// the pod hatches (PodLauncher) at the Fang's boarding_pod points with its tactics
    /// (FangTactics), and at the Snare's tractor projector, where it boards the ship it holds.
    /// </summary>
    public static partial class CarrierBuilder
    {
        static BoardingPod vraskPod;

        /// <summary>The boarding pod: a missile (so co-op shows it and point defence can shoot it
        /// down), slower and tougher, that puts breachers aboard instead of exploding.</summary>
        static BoardingPod MakeBoardingPod(Prefabs p, string prefix = VraskShip.Faction, Color? trailGlow = null)
        {
            string name = prefix + "BoardingPod";
            var go = new GameObject(name);
            var model = Place(OwnModel("Missile"), go.transform, "Model");
            model.transform.localScale *= 1.8f;
            var pod = go.AddComponent<BoardingPod>();
            pod.explosion = p.explosionSmall;
            pod.boostTime = 0.8f;
            pod.boostSpeed = 90f;
            pod.maxSpeed = 420f;
            pod.acceleration = 220f;
            pod.turnRate = 90f;
            pod.lifetime = 24f;
            pod.proximity = 30f;
            pod.damage = 30f;          // (bursting on a shield)
            pod.toughness = 40f;       // (five CIWS bolts; a missile takes two)
            pod.hitRadius = 12f;
            var trail = Node("Trail", go.transform, new Vector3(0, 0, -8f)).AddComponent<TrailRenderer>();
            trail.time = 1.0f;
            trail.startWidth = 2.6f;
            trail.endWidth = 0f;
            trail.sharedMaterial = Additive($"FX_{prefix}PodTrail", trailGlow ?? new Color(0.45f, 1.4f, 0.55f) * 3f);
            trail.shadowCastingMode = ShadowCastingMode.Off;
            Log($"{prefix.ToLowerInvariant()} boarding pod: 420 m/s, toughness 40, breachers " + Boarders.PodMin + "-" + Boarders.PodMax);
            return SavePrefab<BoardingPod>(go, name);
        }

        /// <summary>A ship's pod hatches (and the Fang's tactics): at its boarding_pod points, or the
        /// Snare's at its tractor projector, launching at what its beam holds.</summary>
        static void AddBoarding(GameObject root, VraskShip vs, FactionShipStats s)
        {
            if (vraskPod == null) { Log($"{s.displayName}: no boarding pod prefab"); return; }
            var hatches = vs.PointsOf("boarding_pod");
            bool snare = vs.role == VraskShip.Role.Snare;
            if (hatches.Length == 0)
            {
                var at = vs.PointOf("tractor");
                hatches = new[] { at != null ? at : root.transform };
            }
            var pl = root.AddComponent<PodLauncher>();
            pl.podPrefab = vraskPod;
            pl.hatches = hatches;
            pl.perWave = s.pods;
            pl.waveInterval = s.podInterval > 0f ? s.podInterval : 20f;
            pl.range = s.podRange > 0f ? s.podRange : 3500f;
            pl.atHeld = snare;
            if (vs.role == VraskShip.Role.Fang) root.AddComponent<FangTactics>();
            Log($"{s.displayName}: {s.pods} boarding pods a wave every {pl.waveInterval:0} s, {pl.range / 1000f:0.#} km, from " +
                $"{hatches.Length} hatch(es){(snare ? ", at the ship its beam holds" : "")}{(vs.role == VraskShip.Role.Fang ? "; Fang tactics" : "")}");
        }
    }
}
