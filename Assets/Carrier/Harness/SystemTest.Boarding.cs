using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// -vrasktest's boarding (from VraskTest, after the Snares' capture): the Fang has its pod
    /// hatches and tactics, and the Snares launched pods at the frigate they held. With the other
    /// Vrask holding their fire, pods are launched at the frigate (that can't be destroyed) from
    /// 1.8 km off its beam: its CIWS engage them and shoot at least one down, and with its shield
    /// up none gets aboard. With the shield down and the CIWS off, a pod breaches the hull, its
    /// breachers kill crew, and a full crew repels them; a crew already near its survivors is
    /// overrun by three pods: the frigate is captured (the crew left at its survivors, the cargo
    /// seized, the credits kept, the prisoners counted) and comes back at its respawn point. Last,
    /// the Fang left to fight its own way: it comes in, its guns on the frigate's weapons,
    /// launches its pods and clamps onto the hull, holding the frigate (no warp), and casts off
    /// when its boarders are done.
    /// </summary>
    public partial class SystemTest
    {
        IEnumerator Boarding(List<VraskShip> ships, System.Action<bool, string> Check, Shield shieldF, float cap0)
        {
            var fang = ships.FirstOrDefault(v => v != null && v.role == VraskShip.Role.Fang);
            var fangPods = fang != null ? fang.GetComponent<PodLauncher>() : null;
            var tactics = fang != null ? fang.GetComponent<FangTactics>() : null;
            var snares = ships.Where(v => v != null && v.role == VraskShip.Role.Snare).ToList();
            var snarePods = snares.Select(v => v.GetComponent<PodLauncher>()).Where(x => x != null).ToList();
            Check(fangPods != null && fangPods.podPrefab != null && fangPods.hatches != null && fangPods.hatches.Length >= 3 && tactics != null &&
                  snarePods.Count == snares.Count && snarePods.All(x => x.atHeld && x.podPrefab != null),
                  $"the Fang has its boarding pods ({fangPods?.hatches?.Length ?? 0} hatches, {fangPods?.perWave ?? 0} a wave every {fangPods?.waveInterval ?? 0:0} s) " +
                  $"and its tactics {tactics != null}; the Snares' pods, launched at the ship they hold: {snarePods.Count} of {snares.Count}");
            // (the Snares held the frigate a while ago, its shield down: they boarded it)
            Check(snarePods.Sum(x => x.Launched) > 0, $"the Snares launched {snarePods.Sum(x => x.Launched)} pods at the frigate they held");
            if (fangPods == null || fangPods.podPrefab == null || shieldF == null) yield break;
            var prefab = fangPods.podPrefab;

            var frigate = cc.transform;
            var hull = cc.GetComponent<Health>();
            var crew = cc.GetComponent<Crew>();
            var hold = demo.Cargo;
            var ciws = cc.GetComponent<CiwsDirector>();
            if (crew == null || hold == null) { Check(false, $"the frigate has a crew ({crew != null}) and a hold ({hold != null})"); yield break; }
            var leftOver = Boarders.On(frigate);
            if (leftOver != null) leftOver.Clear();

            // the Vrask hold their fire, and the Fang its own tactics, while the pods are counted
            var arms = ships.Where(v => v != null).Select(v => v.GetComponent<EnemyWeapons>()).Where(w => w != null).ToList();
            foreach (var w in arms) w.weaponsFree = false;
            if (tactics != null) tactics.enabled = false;
            cc.throttle = 0f;
            yield return new WaitForSeconds(2f);

            // pods from off the frigate's beam, level with it (where its CIWS bear), spread along it
            IEnumerator Wave(int n, float km)
            {
                for (int i = 0; i < n; i++)
                {
                    Vector3 at = frigate.position + frigate.right * km * 1000f + frigate.up * 60f + frigate.forward * (i - (n - 1) * 0.5f) * 150f;
                    PodLauncher.Fire(prefab, at, Quaternion.LookRotation(frigate.position - at), fang.transform, frigate);
                    yield return new WaitForSeconds(0.4f);
                }
            }
            bool PodsFlying() => Missile.Active.Any(m => m is BoardingPod && m != null);

            // ---- point defence: the CIWS shoot the pods down; the shield up, none gets aboard
            shieldF.SetCapacity(1e7f);
            shieldF.SetOffline(false);
            if (ciws != null) ciws.active = true;
            int down0 = Missile.ShotDownCount, breached0 = BoardingPod.Breached, burst0 = BoardingPod.Burst;
            bool engaged = false;
            for (int w = 0; w < 3 && Missile.ShotDownCount == down0; w++)
            {
                yield return Wave(3, 1.8f);
                float until = Time.time + 20f;
                while (Time.time < until && PodsFlying())
                {
                    var on = ciws != null ? ciws.EngagedTarget : null;
                    if (on != null && on.GetComponent<BoardingPod>() != null) engaged = true;
                    yield return null;
                }
            }
            Check(engaged && Missile.ShotDownCount > down0,
                  $"the CIWS engage the boarding pods and shoot down {Missile.ShotDownCount - down0}; {BoardingPod.Burst - burst0} burst on the shield");
            Check(BoardingPod.Breached == breached0 && Boarders.CountOn(frigate) == 0,
                  $"with the shield up no pod gets aboard ({BoardingPod.Breached - breached0} breached)");

            // ---- the shield down, the CIWS off: a pod breaches, and a full crew repels its breachers
            if (ciws != null) ciws.active = false;
            shieldF.SetCapacity(cap0);
            shieldF.SetOffline(true);
            crew.SetAboard(crew.complement);
            float crew0 = crew.Current;
            yield return Wave(1, 0.6f);
            Boarders b = null;
            float t0 = Time.time, wait = Time.time + 15f;
            while (Time.time < wait && ((b = Boarders.On(frigate)) == null || b.Aboard == 0)) yield return null;
            string hud = Boarders.HudText(frigate);
            Check(b != null && b.Aboard >= Boarders.PodMin - 1 && BoardingPod.Breached > breached0 && hud.StartsWith("BOARDERS ABOARD:"),
                  $"with the shield down a pod breaches the hull after {Time.time - t0:F1} s: {b?.Aboard ?? 0} boarders aboard; the HUD: \"{hud.Replace("\n", " / ")}\"");
            bool warpLocked = !drive.CanWarp(out string lockWhy);
            Check(warpLocked, $"the warp drive is locked with boarders aboard ({lockWhy})");
            wait = Time.time + 60f;
            while (Time.time < wait && b != null && b.Result == Boarders.Outcome.Fighting) yield return null;
            Check(b != null && b.Result == Boarders.Outcome.Repelled && !crew.AtFloor && crew.Current < crew0 && !hull.Dead,
                  $"a full crew repels them in {(b != null ? b.Ended - b.Began : 0f):F0} s: {b?.Result}, crew {crew0:F0} -> {crew.Current:F0} " +
                  $"({b?.CrewKilled ?? 0f:F0} killed, {b?.Freed ?? 0} freed), {b?.BoardersKilled ?? 0f:F0} boarders killed");

            // ---- a crew near its survivors, three pods: captured, the hold seized, back at the respawn point
            crew.SetAboard(Mathf.Ceil(crew.Floor) + 12f);
            hold.Add(Mineral.Iron, Mathf.Min(40f, hold.Free));
            hold.Earn(500f);
            float used0 = hold.Used, credits0 = hold.Credits;
            int prisoners0 = WorldState.Prisoners;
            Vector3 spawn = cc.SpawnPoint;
            string notice = null;
            void Listen(string s) { if (s.StartsWith("Captured by the Vrask")) notice = s; }
            Notices.Posted += Listen;
            yield return Wave(3, 0.6f);
            wait = Time.time + 60f;
            while (Time.time < wait && !hull.Captured) yield return null;
            Notices.Posted -= Listen;
            b = Boarders.On(frigate);
            Check(hull.Captured && hull.Dead && cc.Captured && Mathf.Abs(crew.Current - crew.Floor) < 0.01f && b != null && b.Result == Boarders.Outcome.Captured,
                  $"a crew near its survivors is overrun: the frigate is captured ({hull.Captured}), the crew left at its survivors ({crew.Current:F1} of {crew.complement}, floor {crew.Floor:F1})");
            Check(used0 > 0f && hold.Used == 0f && Mathf.Approximately(hold.Credits, credits0),
                  $"the cargo seized ({used0:F0} t -> {hold.Used:F0} t), the credits kept ({credits0:F0} -> {hold.Credits:F0})");
            Check(b != null && b.Prisoners > 0 && WorldState.Prisoners == prisoners0 + b.Prisoners && notice != null && notice.Contains($"{b.Prisoners} of your crew taken"),
                  $"the prisoners: {b?.Prisoners ?? 0} taken, the Vrask hold {WorldState.Prisoners}; \"{notice}\"");
            wait = Time.time + hull.respawnDelay + 4f;
            while (Time.time < wait && hull.Dead) yield return null;
            yield return new WaitForSeconds(0.5f);
            float off = Vector3.Distance(frigate.position, spawn);
            Check(!hull.Dead && !hull.Captured && off < 100f && hull.Current >= hull.maxHealth - 0.5f && Mathf.Abs(crew.Current - crew.Floor) < 0.01f,
                  $"she comes back at her respawn point ({off:F0} m from it), whole ({hull.Current:F0} of {hull.maxHealth:F0}), the survivors aboard ({crew.Current:F0})");

            // ---- the Fang's own way: in from an odd angle, guns on the weapons, pods, the clamp
            crew.SetAboard(crew.complement);
            shieldF.SetOffline(true);
            int waves0 = fangPods.Waves, clamps0 = tactics != null ? tactics.Clamps : 0;
            if (tactics != null) tactics.enabled = true;
            var fw = fang.GetComponent<EnemyWeapons>();
            if (fw != null) fw.weaponsFree = true;
            var fp = fang.GetComponent<CombatPilot>();
            if (fp != null && fp.Current == CombatPilot.State.Lurk) fp.Engage();
            string vector = "";
            bool suppress = false, held = false, noWarp = false;
            string why = "";
            t0 = Time.time;
            wait = Time.time + 150f;
            while (Time.time < wait && tactics != null && tactics.Clamps == clamps0)
            {
                if (tactics.Vector.Length > 0) vector = tactics.Vector;
                if (ShipSystems.AimOf(fang.transform.root) == ShipSystem.Weapons) suppress = true;
                yield return null;
            }
            float clampAt = Time.time - t0;
            if (tactics != null && tactics.IsClamped)
            {
                yield return new WaitForSeconds(1f);
                held = cc.Clamped && cc.ClampedBy == fang.transform;
                noWarp = !drive.CanWarp(out why);
            }
            Check(tactics != null && tactics.Clamps > clamps0 && fangPods.Waves > waves0 && suppress && vector.Length > 0,
                  $"the Fang comes in {vector}, its guns on the frigate's weapons ({suppress}), launches {fangPods.Waves - waves0} wave(s) of pods " +
                  $"and clamps onto the hull after {clampAt:F0} s ({tactics?.Clamps - clamps0 ?? 0} clamp(s))");
            Check(held && noWarp, $"clamped, it holds the frigate (clamped {held}, top speed {CarrierController.ClampSpeed} m/s) and the warp drive is refused ({why})");
            // hold and extract: it stays while its boarders fight, and casts off when they are done
            int castOffs0 = tactics != null ? tactics.CastOffs : 0;
            wait = Time.time + 120f;
            while (Time.time < wait && tactics != null && tactics.IsClamped) yield return null;
            var fb = Boarders.On(frigate);
            Check(tactics != null && tactics.CastOffs > castOffs0 && !tactics.IsClamped && !cc.Clamped,
                  $"it stays clamped while its boarders fight ({fb?.Result}) and then casts off: {tactics?.LastCastOff}");

            // ---- back as it was: the frigate whole and free, its shield and CIWS on, the Vrask firing
            wait = Time.time + hull.respawnDelay + 4f;
            while (Time.time < wait && hull.Dead) yield return null;
            fb = Boarders.On(frigate);
            if (fb != null) fb.Clear();
            crew.SetAboard(crew.complement);
            shieldF.SetCapacity(1e7f);
            shieldF.SetOffline(false);
            if (ciws != null) ciws.active = true;
            foreach (var w in arms) if (w != null) w.weaponsFree = true;
        }
    }
}
