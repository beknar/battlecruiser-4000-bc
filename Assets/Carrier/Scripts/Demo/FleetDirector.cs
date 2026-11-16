using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The player's fleet: the other ships they have built, flying with the one they command, up
    /// to ten in all (IndustryLedger.Fleet). A ship berthed at a shipyard joins the fleet there
    /// (services, the shipyard page): crewed, with recruits signed on at the yard and, if they
    /// are too few, crew sent across from the ship commanded (it needs half its complement to
    /// sail); or towed, with nobody aboard, which halves the leader's speed and makes its warps
    /// take twice as long; or left in the yard for later. In fleet mode (5) the fleet takes
    /// orders, the ship chosen in the menu or all of them: follow, guard (protect me), form up,
    /// attack my target, hold. The fleet warps with the leader and keeps up in cruise; it can't
    /// be sent off on its own elsewhere in the system (that's an AI player). A fleet ship
    /// destroyed is lost, and its crew. The fleet is only in this game: in co-op the others don't
    /// see it.
    /// </summary>
    public class FleetDirector : MonoBehaviour, IMenuSource
    {
        public CarrierDemoInput demo;

        public static FleetDirector Instance { get; private set; }

        public readonly List<FleetPilot> Ships = new List<FleetPilot>();
        /// <summary>The ship orders go to (null: all of them).</summary>
        public FleetPilot Selected { get; private set; }
        /// <summary>Ships lost since the level started (for the self-test).</summary>
        public int Lost { get; private set; }

        CarrierController Leader => demo != null ? demo.carrier : null;
        WarpDrive.State driveWas;
        float nextCrewSave;
        FleetPilot viewing;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        bool spawned;

        void Start() => IndustryDirector.LoadSaved();

        /// <summary>The saved fleet made, once there is a leader to fly with: a ship that can't
        /// be made (its class gone from the roster) is put back in its yard, not kept unseen.</summary>
        void SpawnSaved()
        {
            spawned = true;
            foreach (var e in IndustryLedger.Fleet.ToList())
                if (Spawn(e) == null)
                {
                    Debug.LogWarning($"[Fleet] couldn't make the {e.id}: berthed at {e.site} again");
                    IndustryLedger.LeaveAt(e, e.site);
                }
        }

        // ------------------------------------------------------------ the ships

        /// <summary>A fleet ship made where it keeps station by the leader.</summary>
        public FleetPilot Spawn(FleetShip e)
        {
            var leader = Leader;
            var entry = FrigateRoster.Get()?.Find(e.id);
            if (leader == null || entry == null || entry.prefab == null) return null;
            // (the roster's prefabs are the bare ships: the leader's own systems, the drive, the
            // flight model, the strike, are added to the ship commanded, not to these)
            var go = Instantiate(entry.prefab, leader.transform.position - leader.transform.forward * 800f, leader.transform.rotation);
            go.name = $"{entry.displayName} (fleet)";
            var cc = go.GetComponent<CarrierController>();
            if (cc == null) { Destroy(go); return null; }
            cc.shipName = entry.displayName;
            var pilot = go.AddComponent<FleetPilot>();
            pilot.ship = cc;
            pilot.leader = leader;
            pilot.entry = e;
            pilot.displayName = entry.displayName;
            pilot.slot = Ships.Count;
            var h = go.GetComponent<Health>();
            if (h != null)
            {
                h.respawnDelay = 0f;
                h.onDestroyed.AddListener(() => Destroyed(pilot));
            }
            var crew = go.GetComponent<Crew>();
            if (crew != null) crew.SetAboard(e.towed ? 0f : IndustryLedger.CrewOf(e.id) ?? 0f);
            Ships.Add(pilot);
            pilot.PlaceOnStation();
            return pilot;
        }

        void Destroyed(FleetPilot p)
        {
            if (p == null || !Ships.Contains(p)) return;
            Ships.Remove(p);
            Lost++;
            IndustryLedger.LoseFleetShip(p.entry);
            if (Selected == p) Selected = null;
            if (viewing == p) View(null);
            Renumber();
            SoundFX.Ui(Sfx.UiAlert);
            Notices.Post($"The {p.displayName} is lost, with all her crew");
        }

        void Renumber()
        {
            for (int i = 0; i < Ships.Count; i++) Ships[i].slot = i;
        }

        /// <summary>The crew a berthed ship needs to sail: half its complement.</summary>
        public static int CrewToSail(string id) => Mathf.CeilToInt(Complement(id) * 0.5f);

        public static int Complement(string id)
        {
            var e = FrigateRoster.Get()?.Find(id);
            return e != null && e.prefab != null && e.prefab.TryGetComponent(out Crew c) ? c.complement : 0;
        }

        /// <summary>Join the fleet with a ship berthed at `site`: crewed (recruits signed on there,
        /// paid from the hold, up to its complement; then crew sent across from the leader, to
        /// half its complement at least), or towed with nobody aboard. False, and why, if it can't.</summary>
        public bool Join(string id, string site, bool towed, out string why)
        {
            why = "";
            var leader = Leader;
            if (leader == null) { why = "no ship to fly with"; return false; }
            if (!IndustryLedger.FleetHasRoom(out why)) return false;
            int need = CrewToSail(id), full = Complement(id);
            var hold = demo.Cargo;
            var own = leader.GetComponent<Crew>();
            int aboard = Mathf.RoundToInt(IndustryLedger.CrewOf(id) ?? 0f);
            // (worked out before anything is paid or moved: if she can't sail, nothing changes)
            int signable = towed ? 0 : IndustryLedger.Recruitable(site, full - aboard, hold);
            int spare = own != null ? Mathf.Max(0, own.Aboard - Mathf.CeilToInt(own.complement * 0.5f)) : 0;
            if (!towed && aboard + signable + spare < need)
            {
                why = $"not enough crew: she needs {need} to sail, {aboard + signable} can sign on here and {spare} can come across from the {leader.shipName}. Recruit more, tow her, or leave her here";
                return false;
            }
            if (!IndustryLedger.JoinFleet(id, site, towed, out why)) return false;
            var e = IndustryLedger.Fleet.LastOrDefault(f => string.Equals(f.id, id, System.StringComparison.OrdinalIgnoreCase));
            if (e == null || Spawn(e) == null)
            {
                if (e != null) IndustryLedger.LeaveAt(e, site);
                why = "she couldn't be got under way";
                return false;
            }
            if (!towed)
            {
                // the recruits signed on (paid from the hold), and the rest from the leader
                IndustryLedger.RecruitInto(site, id, full, hold, full, out _);
                aboard = Mathf.RoundToInt(IndustryLedger.CrewOf(id) ?? 0f);
                if (aboard < need && own != null)
                {
                    int sent = need - aboard;
                    own.SetAboard(own.Current - sent);
                    IndustryLedger.SetCrew(id, aboard + sent);
                    aboard += sent;
                }
                var pilot = Ships.LastOrDefault(s => s != null && s.entry == e);
                if (pilot != null && pilot.TryGetComponent(out Crew theirs)) theirs.SetAboard(aboard);
            }
            return true;
        }

        /// <summary>Leave a fleet ship at a yard (berthed there, crew and all).</summary>
        public void LeaveAt(FleetPilot p, string site)
        {
            if (p == null) return;
            var crew = p.GetComponent<Crew>();
            if (crew != null && !p.Towed) IndustryLedger.SetCrew(p.entry.id, crew.Current);
            IndustryLedger.LeaveAt(p.entry, site);
            Ships.Remove(p);
            if (Selected == p) Selected = null;
            if (viewing == p) View(null);
            Destroy(p.gameObject);
            Renumber();
        }

        public bool AnyTowed => Ships.Any(s => s != null && s.Towed);

        void Update()
        {
            var leader = Leader;
            if (leader == null) return;
            if (!spawned) SpawnSaved();
            Ships.RemoveAll(s => s == null);
            foreach (var s in Ships) s.leader = leader;   // (another ship taken command of)
            // towing: half speed, twice as long a warp
            bool towing = AnyTowed;
            leader.towScale = towing ? 0.5f : 1f;
            var drive = demo.Warp;
            if (drive != null)
            {
                drive.slowScale = towing ? 2f : 1f;
                // the fleet warps with the leader: there on its stations as it arrives
                if (drive.Current == WarpDrive.State.Ready && driveWas == WarpDrive.State.Arriving)
                    foreach (var s in Ships) s.PlaceOnStation();
                driveWas = drive.Current;
            }
            // (their crews, as they are, kept for the save)
            if (Time.time >= nextCrewSave)
            {
                nextCrewSave = Time.time + 5f;
                foreach (var s in Ships)
                    if (!s.Towed && s.TryGetComponent(out Crew c) && !Mathf.Approximately(IndustryLedger.CrewOf(s.entry.id) ?? -1f, c.Current))
                        IndustryLedger.SetCrew(s.entry.id, c.Current);
            }
        }

        // ------------------------------------------------------------ orders

        IEnumerable<FleetPilot> Addressed => Selected != null ? (Selected.Towed ? new FleetPilot[0] : new[] { Selected }) : Ships.Where(s => !s.Towed);

        public void Order(FleetPilot.Order o)
        {
            if (Ships.Count == 0) { SoundFX.Ui(Sfx.UiError); Notices.Post("No fleet: build a ship at a shipyard, and have her join you there"); return; }
            Transform target = null;
            if (o == FleetPilot.Order.Attack)
            {
                target = Leader != null ? Leader.target : null;
                if (target == null) { SoundFX.Ui(Sfx.UiError); Notices.Post("Attack: target something first (combat mode, T)"); return; }
                if (Targets.IsFriendly(target)) { SoundFX.Ui(Sfx.UiError); Notices.Post($"The fleet won't attack {Targets.NameOf(target)}: it is friendly"); return; }
            }
            var to = Addressed.ToList();
            if (to.Count == 0) { SoundFX.Ui(Sfx.UiError); Notices.Post("The ships under tow can't take orders"); return; }
            foreach (var s in to) s.Give(o, target);
            SoundFX.Ui(Sfx.UiLock);
            string who = Selected != null ? $"The {Selected.displayName}" : "The fleet";
            Notices.Post(o == FleetPilot.Order.Follow ? $"{who}: following you"
                       : o == FleetPilot.Order.Guard ? $"{who}: guarding you"
                       : o == FleetPilot.Order.FormUp ? $"{who}: forming up on you"
                       : o == FleetPilot.Order.Attack ? $"{who}: attacking {Wingman.NameOf(target)}"
                       : $"{who}: holding position");
        }

        /// <summary>The camera on the next ship of the fleet, and back to the leader.</summary>
        public void ViewNext()
        {
            if (Ships.Count == 0) { View(null); return; }
            int i = viewing != null ? Ships.IndexOf(viewing) + 1 : 0;
            View(i < Ships.Count ? Ships[i] : null);
        }

        void View(FleetPilot p)
        {
            viewing = p;
            var cam = demo != null ? demo.orbitCamera : null;
            if (cam == null) return;
            if (p != null) { cam.target = p.transform; cam.offset = Vector3.zero; cam.distance = 900f; Notices.Post($"Viewing the {p.displayName}: B for the next"); }
            else { cam.target = Leader.transform; cam.offset = demo.ShipViewOffset; cam.distance = demo.ShipViewDistance; }
        }

        // ------------------------------------------------------------ the mini-HUD

        /// <summary>Its screen area (GUI coordinates), while shown: a click there isn't on a ship.</summary>
        public static Rect PanelsRect { get; private set; }

        /// <summary>How a fleet ship stands, for its panel: under fire, fighting, under tow, or clear.</summary>
        public static string Alert(FleetPilot p, out Color c)
        {
            var h = p.Hull;
            var sh = p.ShieldGen;
            float hit = Mathf.Max(h != null ? h.LastHitTime : -100f, sh != null ? sh.LastHitTime : -100f);
            if (p.Towed) { c = new Color(0.6f, 0.65f, 0.72f); return "UNDER TOW"; }
            if (Time.time - hit < 3f) { c = new Color(1f, 0.35f, 0.28f); return "UNDER FIRE"; }
            if (p.Fighting != null) { c = new Color(1f, 0.7f, 0.25f); return "ENGAGED"; }
            c = new Color(0.45f, 0.85f, 0.5f);
            return "ALL CLEAR";
        }

        /// <summary>Its guns, in a few words: how many, how many firing now.</summary>
        public static string Guns(FleetPilot p)
        {
            int count = 0, firing = 0;
            if (p.ship != null)
                foreach (var t in p.ship.mainTurrets)
                {
                    if (t == null) continue;
                    count++;
                    if (Time.time - t.LastFiredTime < 2.5f) firing++;
                }
            if (count == 0) return "no guns";
            return firing > 0 ? $"guns {count}: {firing} firing" : p.Fighting != null ? $"guns {count}: bearing on {Wingman.NameOf(p.Fighting)}" : $"guns {count}: ready";
        }

        void OnGUI()
        {
            PanelsRect = Rect.zero;
            if (Ships.Count == 0 || !HudSettings.FleetPanels || Hud.ReelMode || Leader == null) return;
            // (not over a screen that covers the game: the map, the menu, a site's screen)
            if (SystemMap.IsOpen || GameMenu.IsOpen || IndustryPanel.IsOpen) return;
            GUI.depth = 5;   // (behind the other panels, should any meet)
            float s = Hud.S;
            float w = 250f * s, h = 76f * s, gap = 6f * s, pad = 8f * s;
            var display = demo != null ? demo.GetComponent<TargetDisplay>() : null;
            float top = display != null && display.PanelRect.height > 0f ? display.PanelRect.yMax + 10f * s : 12f * s;
            float x = Screen.width - w - 12f * s;
            // as many as fit down the right side, above the bottom of the screen (and a line for
            // the rest)
            float moreH = 18f * s;
            int fits = Mathf.Max(1, Mathf.FloorToInt((Screen.height - 12f * s - moreH - top) / (h + gap)));
            int shown = Mathf.Min(fits, Ships.Count);
            PanelsRect = new Rect(x, top, w, shown * (h + gap) + (Ships.Count > fits ? moreH : 0f));
            // (the rest only drawn on a repaint: the strings and bars aren't needed for the others)
            if (Event.current.type != EventType.Repaint) return;
            var small = Hud.Text(11);
            for (int i = 0; i < shown; i++)
            {
                var p = Ships[i];
                var r = new Rect(x, top + i * (h + gap), w, h);
                Hud.Panel(r);
                string alert = Alert(p, out Color ac);
                Hud.Fill(new Rect(r.x, r.y, Mathf.Max(2f, 3f * s), r.height), ac);
                bool chosen = Selected == p;
                GUI.Label(new Rect(r.x + pad, r.y + 3f * s, w - pad * 2f, 18f * s),
                          $"<b>{(chosen ? "<color=#ffc070>> </color>" : "")}{p.displayName.ToUpperInvariant()}</b>  <color=#{ColorUtility.ToHtmlStringRGB(ac)}>{alert}</color>", Hud.Text(12));
                var hp = p.Hull;
                var sh = p.ShieldGen;
                float y = r.y + 23f * s;
                MiniBar(new Rect(r.x + pad, y, w - pad * 2f, 9f * s), "HULL", hp != null && hp.maxHealth > 0f ? hp.Current / hp.maxHealth : 0f, new Color(0.9f, 0.75f, 0.35f), s);
                y += 13f * s;
                MiniBar(new Rect(r.x + pad, y, w - pad * 2f, 9f * s), "SHIELD", sh != null && sh.Up ? sh.Fraction : 0f, new Color(0.35f, 0.8f, 1f), s);
                y += 13f * s;
                GUI.Label(new Rect(r.x + pad, y, w - pad * 2f, 16f * s), $"<color=#9aa4b0>{Guns(p)}  ·  {p.Doing}</color>", small);
            }
            if (Ships.Count > fits)
                GUI.Label(new Rect(x, top + fits * (h + gap), w, 16f * s), $"<color=#9aa4b0>and {Ships.Count - fits} more (Tab in fleet mode)</color>", small);
        }

        static void MiniBar(Rect r, string label, float f, Color c, float s)
        {
            float lw = 46f * s;
            GUI.Label(new Rect(r.x, r.y - 3f * s, lw, r.height + 6f * s), $"<color=#9aa4b0>{label}</color>", Hud.Text(9));
            var bar = new Rect(r.x + lw, r.y, r.width - lw, r.height);
            Hud.Fill(bar, new Color(1f, 1f, 1f, 0.08f));
            Hud.Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(f), bar.height), c);
        }

        // ------------------------------------------------------------ the menu

        public string Title => $"FLEET: {Ships.Count + 1} OF {IndustryLedger.MaxFleet} SHIPS";

        public string Hint(bool pad) => pad ? "D-pad choose   A give orders to it   B close   (A follow, X guard, Y form up, RB attack, B hold)"
                                            : "Up/Down choose   Enter give orders to it   F follow  G guard  R form up  V attack  Z hold  B view";

        public void Rows(List<MenuRow> rows)
        {
            var leader = Leader;
            rows.Add(new MenuRow { header = true, text = Ships.Count == 0 ? "NO FLEET: BUILD A SHIP AT A SHIPYARD" : "ORDERS GO TO" });
            if (Ships.Count > 0)
                rows.Add(new MenuRow { text = $"{(Selected == null ? "<color=#ffc070>> </color>" : "")}All ships", detail = $"{Ships.Count(s => !s.Towed)} crewed, {Ships.Count(s => s.Towed)} towed", tag = "all" });
            foreach (var s in Ships)
            {
                float d = leader != null ? Vector3.Distance(leader.transform.position, s.transform.position) : 0f;
                var h = s.GetComponent<Health>();
                var c = s.GetComponent<Crew>();
                string hull = h != null && h.maxHealth > 0f ? $"hull {Mathf.CeilToInt(100f * h.Current / h.maxHealth)}%" : "";
                string crew = s.Towed ? "towed" : c != null ? $"crew {c.Aboard}/{c.complement}" : "";
                rows.Add(new MenuRow
                {
                    text = $"{(Selected == s ? "<color=#ffc070>> </color>" : "")}{s.displayName}  <color=#9aa4b0>{s.Doing}</color>",
                    detail = $"{CombatMode.Distance(d)}   {hull}   {crew}",
                    tag = s,
                });
            }
            var berthed = IndustryLedger.Berthed;
            if (berthed.Count > 0)
            {
                rows.Add(new MenuRow { header = true, text = "BERTHED AT THE YARDS" });
                foreach (var b in berthed)
                    rows.Add(new MenuRow { text = FrigateRoster.Get()?.Find(b.id)?.displayName ?? b.id, detail = b.site, enabled = false, tag = b });
            }
        }

        public void Act(MenuRow row)
        {
            if (row.tag is FleetPilot p) { Selected = p; Notices.Post($"Orders to the {p.displayName}{(p.Towed ? " (under tow: none to give)" : "")}"); }
            else if (row.tag as string == "all") { Selected = null; Notices.Post("Orders to the whole fleet"); }
        }

        public bool Keys(Keyboard k, Gamepad pad, MenuRow row)
        {
            if (k == null) return false;
            // (the orders, to the row's ship, or all)
            FleetPilot.Order? o = k.fKey.wasPressedThisFrame ? FleetPilot.Order.Follow : k.gKey.wasPressedThisFrame ? FleetPilot.Order.Guard
                                : k.rKey.wasPressedThisFrame ? FleetPilot.Order.FormUp : k.vKey.wasPressedThisFrame ? FleetPilot.Order.Attack
                                : k.zKey.wasPressedThisFrame ? FleetPilot.Order.Hold : (FleetPilot.Order?)null;
            if (o == null) { if (k.bKey.wasPressedThisFrame) { ViewNext(); return true; } return false; }
            if (row?.tag is FleetPilot p) Selected = p;
            else if (row?.tag as string == "all") Selected = null;
            Order(o.Value);
            return true;
        }
    }
}
