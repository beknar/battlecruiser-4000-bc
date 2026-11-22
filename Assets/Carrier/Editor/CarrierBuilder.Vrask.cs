using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CarrierGame;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Carrier.EditorTools
{
    /// <summary>
    /// The Vrask, an alien faction, and their four ships (Data/vrask.json): the Snare (capture),
    /// the Fang (assault), the Maw (siege) and the Skink (escort), from the AI-made models
    /// Models/Lizard_Frigate1..4.fbx in their own paint, converted by Tools/Blender/convert_lizards.py.
    ///
    /// Each is built as a faction's ship is (CarrierBuilder.Faction): its turrets, fixed guns,
    /// engines, a shield (the Vrask were the first enemies with one), armour plating, systems,
    /// breakup and target information (hostile, of the Vrask). The places on the hull the rest
    /// of their fighting will use (the tractor, the breaching prow, the boarding pods, the silos)
    /// are kept as points (VraskShip).
    /// The Maw's spinal gun is its artillery, with its silos (CarrierBuilder.Artillery).
    ///
    /// The raiding group goes in the star system with VraskRaiders; the siege group over a city
    /// with VraskSiege; each fights as a fleet (VraskFleet), the Skinks as its escorts and eyes
    /// (CarrierBuilder.Escort).
    /// </summary>
    public static partial class CarrierBuilder
    {
        static VraskTable vrask;

        static VraskTable LoadVrask() => vrask = LoadFaction<VraskTable>(VraskTable.DataPath);

        /// <summary>The Vrask models, for the import settings.</summary>
        static IEnumerable<string> VraskModelPaths() => FactionModelPaths(LoadVrask());

        /// <summary>Their shield and bolts, their boarding pod, then each of their ships' prefabs
        /// (into p.vrask, by id).</summary>
        static void MakeVrask(Prefabs p)
        {
            if (!File.Exists(VraskTable.DataPath)) { Log("vrask: no " + VraskTable.DataPath); return; }
            LoadVrask();
            var bolts = MakeFactionKit(p, vrask);
            // their boarding pod (CarrierBuilder.Boarding)
            vraskPod = MakeBoardingPod(p);
            foreach (var s in vrask.ships) p.vrask[s.id] = MakeVraskShip(p, s, bolts);
        }

        static GameObject MakeVraskShip(Prefabs p, FactionShipStats s, Dictionary<string, Projectile> bolts) =>
            MakeFactionShip(p, vrask, s, bolts,
                root =>
                {
                    var vs = root.AddComponent<VraskShip>();
                    vs.role = (VraskShip.Role)Enum.Parse(typeof(VraskShip.Role), s.id);
                    return vs;
                },
                (root, ship, turrets) =>
                {
                    var vs = (VraskShip)ship;
                    // the Snare's tractor beam, at its projector, and how it fights with it
                    if (s.tractorRange > 0f) AddTractor(root, vs, s);
                    // boarding pods (the Fang's, with its tactics; the Snare's, at what it holds)
                    if (s.pods > 0) AddBoarding(root, vs, s);
                    // the Maw's spinal artillery, its nuclear silos and its tactics (CarrierBuilder.Artillery)
                    if (s.artillery != null && s.artillery.range > 0f) AddArtillery(p, root, vs, s);
                    // the Skink's sensors, point defence and escort tactics (CarrierBuilder.Escort)
                    if (s.interceptRange > 0f) AddEscort(root, vs, s, turrets);
                });

        /// <summary>The Snare's tractor beam, from its projector, drawn as a green line while it
        /// holds a ship, and its tactics with it.</summary>
        static void AddTractor(GameObject root, VraskShip vs, FactionShipStats s)
        {
            var beam = AddTractorBeam(root, vs, vrask, s.tractorRange);
            root.AddComponent<SnareTactics>();
            Log($"{s.displayName}: tractor beam from {(beam.emitter != root.transform ? beam.emitter.name : "the hull (no tractor point)")}, {s.tractorRange / 1000f:0.#} km");
        }

        /// <summary>
        /// The Vrask raiding group (Data/vrask.json "group"): lying in wait round a place in the
        /// star system (Comet Mirren: far out, away from the corsairs' haunts, where ships come to
        /// mine ice), hidden till a ship comes within its spotting range, then all of them attack.
        /// It is an EncounterGroup like the raiders in the Trojans, so the SpawnDirector brings its
        /// destroyed ships back, and its pilots are CombatPilots (the warp lock, the music, co-op).
        /// The Maw holds well back, bow on; the Snares stand off at medium range, bow on; the Fang
        /// makes fast attack runs; the Skinks screen the Maw.
        /// </summary>
        static EncounterGroup VraskRaiders(Prefabs p, Transform sys,
            Func<GameObject, string, string, Vector3, Vector3, Action<CombatPilot>, float, GameObject> enemy)
        {
            if (vrask?.group?.slots == null || p.vrask.Count == 0) { Log("vrask: no raiding group"); return null; }
            var g = vrask.group;
            var at = sys.GetComponentsInChildren<WarpPoint>(true).FirstOrDefault(w => w.displayName == g.at);
            if (at == null) { Log($"vrask: no place '{g.at}' for the raiders"); return null; }
            if (!string.IsNullOrEmpty(g.note) && (at.description == null || !at.description.Contains(g.note))) at.description = (at.description + " " + g.note).Trim();
            at.dangerNear = true;
            Vector3 c = at.transform.position;
            // round the place, from the side away from the star (the star is at the system's centre)
            Vector3 outward = Vector3.ProjectOnPlane(c - sys.position, Vector3.up);
            outward = outward.sqrMagnitude > 1f ? outward.normalized : Vector3.forward;
            var pilots = new List<(CombatPilot pilot, FactionShipStats s)>();
            foreach (var slot in g.slots)
            {
                var s = vrask.Find(slot.ship);
                if (s == null || !p.vrask.TryGetValue(s.id, out var prefab) || prefab == null) { Log($"vrask: no ship '{slot.ship}'"); continue; }
                Vector3 pos = c + Quaternion.AngleAxis(slot.angle, Vector3.up) * outward * slot.distance + Vector3.up * slot.height;
                string name = string.IsNullOrEmpty(slot.name) ? s.displayName : $"{s.displayName} {slot.name}";
                var go = enemy(prefab, name, name, pos, c, pl => VraskPilot(pl, s), 180f);
                go.GetComponent<Health>().respawnDelay = s.respawn;
                pilots.Add((go.GetComponent<CombatPilot>(), s));
            }
            // the escorts screen the ship they guard (each to its own side of it)
            int side = 0;
            foreach (var (pl, s) in pilots.Where(x => !string.IsNullOrEmpty(x.s.guard)))
            {
                var ward = pilots.FirstOrDefault(x => x.s.id == s.guard).pilot;
                if (ward == null) continue;
                pl.guard = ward.transform;
                pl.guardLeash = s.guardLeash;
                pl.circleSpeed = Mathf.Abs(pl.circleSpeed) * (side++ % 2 == 0 ? 1f : -1f);
            }
            var eg = new GameObject(g.name).AddComponent<EncounterGroup>();
            eg.groupName = g.name;
            eg.pilots = pilots.Select(x => x.pilot).ToArray();
            eg.spotRange = g.spotRange;
            // how they fight together (CarrierBuilder.Escort)
            AddFleet(eg.gameObject, pilots.Select(x => x.pilot.GetComponent<VraskShip>()), eg, null);
            float nearest = pilots.Count > 0 ? pilots.Min(x => Vector3.Distance(x.pilot.transform.position, c)) : 0f;
            Log($"{g.name}: {pilots.Count} ships lying in wait round {g.at} ({string.Join(", ", pilots.Select(x => x.pilot.name))}), " +
                $"the nearest {nearest / 1000f:F1} km from it, the arrival {at.arrivalDistance / 1000f:F1} km out; spotting at {g.spotRange / 1000f:F1} km");
            return eg;
        }

        /// <summary>How a Vrask ship flies, from its numbers.</summary>
        static void VraskPilot(CombatPilot pl, FactionShipStats s) => FactionPilot(pl, s);
    }
}
