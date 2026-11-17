using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// -vrasktest's capture by the Snares (from VraskTest, after the fight): the frigate, that
    /// can't be destroyed, holds still while their ion fire knocks out its shield generator and
    /// then its engines; a Snare locks its tractor beam on it and pulls it in; the warp drive is
    /// refused while it is held; and the beam lets go when the Snare holding it is destroyed.
    /// </summary>
    public partial class SystemTest
    {
        IEnumerator SnareCapture(List<VraskShip> ships, System.Action<bool, string> Check)
        {
            var snares = ships.Where(v => v != null && v.role == VraskShip.Role.Snare).ToList();
            var beams = snares.Select(v => v.GetComponent<TractorBeam>()).Where(b => b != null).ToList();
            var tactics = snares.Select(v => v.GetComponent<SnareTactics>()).Where(x => x != null).ToList();
            Check(snares.Count > 0 && beams.Count == snares.Count && tactics.Count == snares.Count && beams.All(b => b.emitter != null && b.range >= 2000f),
                  $"the Snares have their tractor beams and tactics: {beams.Count} of {snares.Count} " +
                  $"({string.Join(", ", beams.Select(b => $"{b.name} {b.range / 1000f:0.#} km from {(b.emitter != null ? b.emitter.name : "-")}"))})");
            if (beams.Count == 0) yield break;

            var frigate = cc.transform;
            var sys = cc.GetComponent<ShipSystems>();
            cc.throttle = 0f;
            float t0 = Time.time, shieldAt = -1f, enginesAt = -1f, lockAt = -1f, pulled0 = 0f, from0 = 0f;
            TractorBeam holder = null;
            float until = Time.time + 180f;
            while (Time.time < until)
            {
                if (shieldAt < 0f && sys != null && sys.IsDown(ShipSystem.Shields)) shieldAt = Time.time - t0;
                if (enginesAt < 0f && sys != null && sys.EnginesDown) enginesAt = Time.time - t0;
                if (lockAt < 0f && cc.Tractored)
                {
                    lockAt = Time.time - t0;
                    pulled0 = cc.TractorPulled;
                    holder = beams.FirstOrDefault(b => b.Holding == frigate);
                    from0 = holder != null ? Vector3.Distance(holder.From, frigate.position) : 0f;
                }
                // (and 12 s of being pulled, to see how far)
                if (enginesAt >= 0f && lockAt >= 0f && Time.time - t0 > lockAt + 12f) break;
                yield return null;
            }
            string duties = string.Join(", ", tactics.Select(x => $"{x.name} {x.Current}"));
            {
                // (what the Snares' guns did: how many shots, how many guns on the frigate)
                var guns = snares.SelectMany(v => v.GetComponentsInChildren<CarrierTurret>(true)).ToList();
                var sh = cc.GetComponentInChildren<Shield>();
                Log($"  the Snares' guns: {guns.Sum(g => g.ShotsFired)} shots from {guns.Count(g => g.ShotsFired > 0)} of {guns.Count}; " +
                    $"{guns.Count(g => g.target == frigate)} on the frigate; its shield {(sh != null ? sh.Fraction : -1f):P0}; " +
                    $"{(sys != null ? string.Join(", ", ShipSystems.Components.Select(c => $"{c} {sys.Condition(c):P0}")) : "no systems")}; " +
                    $"Snares {string.Join(", ", snares.Select(v => $"{Vector3.Distance(v.transform.position, frigate.position):F0} m"))}");
            }
            Check(shieldAt >= 0f && enginesAt >= 0f,
                  $"the Snares' ion fire knocks out the frigate's shield generator ({shieldAt:F0} s) and then its engines ({enginesAt:F0} s): " +
                  $"{duties}; aimed at its {ShipSystems.Label(ShipSystems.AimOf(snares[0].transform.root))}");
            Check(lockAt >= 0f && cc.Tractored && holder != null,
                  $"a tractor lock on the frigate after {lockAt:F0} s ({(holder != null ? holder.name : "none")}): {cc.TractorBeams} beam(s), held fast {cc.TractorFirm}");
            if (!cc.Tractored || holder == null) yield break;

            float pulled = cc.TractorPulled - pulled0;
            float fromNow = Vector3.Distance(holder.From, frigate.position);
            Log($"  the frigate pulled {pulled:F0} m in {Time.time - t0 - lockAt:F0} s: {from0:F0} m from the beam at the lock, {fromNow:F0} m now");
            Check(pulled > 100f, $"the tractor pulls the frigate in: {pulled:F0} m ({from0:F0} m from the beam, now {fromNow:F0} m)");

            // no warp while held
            bool canWarp = drive.CanWarp(out string why);
            Check(!canWarp && why == "held by a tractor beam", $"the warp drive is refused while held: {why}");

            // the Snare holding it destroyed: its beam lets go
            int before = TractorBeam.BeamsOn(frigate);
            var hh = holder.GetComponent<Health>();
            for (int i = 0; i < 4 && hh != null && !hh.Dead; i++) hh.Damage(hh.maxHealth * 20f, holder.transform.position, HitKind.Penetrator);
            yield return null;
            yield return null;
            Check(hh != null && hh.Dead && !holder.Locked && holder.LastRelease == "destroyed",
                  $"the Snare holding it destroyed, its beam lets go ({holder.LastRelease}): {before} beam(s) on the frigate, then {TractorBeam.BeamsOn(frigate)}");
            // and with all the Snares gone, the frigate is free
            foreach (var v in snares)
            {
                var h = v.GetComponent<Health>();
                for (int i = 0; i < 4 && h != null && !h.Dead; i++) h.Damage(h.maxHealth * 20f, v.transform.position, HitKind.Penetrator);
            }
            yield return new WaitForSeconds(0.6f);
            Check(!cc.Tractored && !TractorBeam.HeldTargets.Contains(frigate), $"with the Snares destroyed the frigate is free (held {cc.Tractored})");
        }
    }
}
