using UnityEngine;

namespace CarrierGame.Demo
{
    /// <summary>
    /// The captain's portrait on the HUD, and what she makes of what is happening. She watches
    /// the ship: hits on the shield or hull, the shield going down, missiles coming in, enemies
    /// closing, the guns, missiles, fighters and bow beam firing, kills, warps, mining, scans,
    /// docking, landing, the ship lost and back again. Each gives her a face for a while (the more
    /// serious thing wins); between them, what the ship is doing sets her face (intent while
    /// mining or in a fight, awed in warp, calm when all is quiet).
    ///
    /// The face is alive: one expression cross-fades into the next, she breathes, blinks, mouths
    /// the messages on the HUD, and the picture jolts and flashes red when the ship is hit.
    /// </summary>
    public class CaptainPortrait : MonoBehaviour
    {
        public CarrierDemoInput demo;

        public static CaptainPortrait Instance { get; private set; }

        /// <summary>Her mood now (before blinks and talking).</summary>
        public Mood Mood { get; private set; } = Mood.Neutral;
        /// <summary>The picture on screen this moment (a blink or a word included).</summary>
        public Mood Shown { get; private set; } = Mood.Neutral;
        /// <summary>What she is reacting to, in a few words.</summary>
        public string Reason { get; private set; } = "ALL QUIET";
        /// <summary>How many times her mood has changed (for the self-test).</summary>
        public int Changes { get; private set; }
        /// <summary>Every mood she has shown, with its reason, latest last (for the self-test).</summary>
        public readonly System.Collections.Generic.List<(Mood mood, string reason, float at)> History =
            new System.Collections.Generic.List<(Mood, string, float)>();

        // a reaction: a face held for a while, unless something more serious comes along
        Mood reaction;
        string reactionWhy;
        int reactionPrio = -1;
        float reactionUntil;

        // what was seen last frame, to notice changes
        float hullHitSeen = -100f, shieldHitSeen = -100f;
        bool shieldWasUp = true, wasDestroyed, gunWasFiring, wasDocked, wasLanded, laserWasFull;
        int shotsSeen, ciwsSeen, cellsSeen = -1, wingSeen, minedOutSeen, killsSeen, missilesSeen, hostilesSeen, toastsSeen;
        WarpDrive.State warpWas = WarpDrive.State.Ready;
        float lastShotTime = -100f;

        // the animation
        Mood from = Mood.Neutral;
        float blendStart = -100f;
        const float BlendTime = 0.22f;
        float nextBlink, blinkUntil = -1f, talkUntil = -1f, nextMouth, hitFlash, shake;
        bool mouthOpen;

        static Texture2D scan;

        Health hull;
        Shield shield;
        WarpDrive warp;
        MissileLauncher missiles;
        CiwsDirector ciws;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            var cc = demo != null ? demo.carrier : null;
            if (cc != null)
            {
                hull = cc.GetComponent<Health>();
                shield = cc.GetComponentInChildren<Shield>(true);
                warp = cc.GetComponent<WarpDrive>();
                missiles = cc.missiles;
                ciws = cc.GetComponent<CiwsDirector>();
                if (hull != null) hullHitSeen = hull.LastHitTime;
                if (shield != null) shieldHitSeen = shield.LastHitTime;
                shotsSeen = Shots(cc.mainTurrets);
                ciwsSeen = ciws != null ? Shots(ciws.mounts) : 0;
                cellsSeen = missiles != null ? missiles.ReadyCells : -1;
                wingSeen = cc.Wing.Count;
            }
            toastsSeen = CarrierDemoInput.Toasts;
            nextBlink = Time.time + Random.Range(1.5f, 4f);
            Captain.Changed += OnCaptainChanged;
        }

        void OnDisable() => Captain.Changed -= OnCaptainChanged;

        void OnCaptainChanged()
        {
            // the new captain says hello
            React(Mood.Smile, 9, 1.8f, "REPORTING FOR DUTY");
            talkUntil = Time.time + 1.2f;
        }

        static int Shots(CarrierTurret[] guns)
        {
            int n = 0;
            if (guns != null) foreach (var g in guns) if (g != null) n += g.ShotsFired;
            return n;
        }

        /// <summary>Hold a face for `seconds`, unless something more pressing (a higher
        /// priority) is already showing.</summary>
        public void React(Mood m, int priority, float seconds, string why)
        {
            if (Time.time < reactionUntil && priority < reactionPrio) return;
            reaction = m;
            reactionWhy = why;
            reactionPrio = priority;
            reactionUntil = Time.time + seconds;
        }

        void Update()
        {
            var cc = demo != null ? demo.carrier : null;
            if (cc == null) return;
            Watch(cc);

            // what to show: a reaction while it lasts, else what the ship is doing
            Mood m;
            string why;
            if (cc.Destroyed) { m = Mood.Sad; why = "SHIP LOST"; }
            else if (Time.time < reactionUntil) { m = reaction; why = reactionWhy; }
            else { reactionPrio = -1; Steady(cc, out m, out why); }
            Set(m, why);

            // blinks: now and then, when her face is at rest enough for it
            if (Time.time >= nextBlink)
            {
                if (Mood == Mood.Neutral || Mood == Mood.Focused || Mood == Mood.Smile || Mood == Mood.Determined)
                    blinkUntil = Time.time + 0.13f;
                nextBlink = Time.time + Random.Range(2.4f, 6f);
            }
            // a new message on the HUD: she says it
            if (CarrierDemoInput.Toasts != toastsSeen)
            {
                toastsSeen = CarrierDemoInput.Toasts;
                if (!cc.Destroyed) talkUntil = Time.time + 1.1f;
            }
            bool canTalk = Mood == Mood.Neutral || Mood == Mood.Focused || Mood == Mood.Smile || Mood == Mood.Determined;
            if (Time.time < talkUntil && canTalk)
            {
                if (Time.time >= nextMouth) { mouthOpen = !mouthOpen; nextMouth = Time.time + Random.Range(0.08f, 0.16f); }
            }
            else mouthOpen = false;
            Shown = Time.time < blinkUntil ? Mood.Blink : mouthOpen ? Mood.Talk : Mood;

            hitFlash = Mathf.Max(0f, hitFlash - Time.deltaTime * 2.6f);
            shake = Mathf.Max(0f, shake - Time.deltaTime * 3f);
        }

        void Set(Mood m, string why)
        {
            Reason = why;
            if (m == Mood) return;
            from = Mood;
            Mood = m;
            blendStart = Time.time;
            Changes++;
            History.Add((m, why, Time.time));
            if (History.Count > 200) History.RemoveAt(0);
        }

        /// <summary>What the ship is doing, when nothing has just happened.</summary>
        void Steady(CarrierController cc, out Mood m, out string why)
        {
            var gun = demo.MainGun;
            var landing = PlanetLanding.Instance;
            var dock = Docking.Instance;
            var laser = demo.Laser;
            var sensor = demo.Sensor;
            m = Mood.Neutral;
            why = PlanetLanding.OnSurface ? "ON THE SURFACE" : "ALL QUIET";
            if (gun != null && gun.Current == MainBeam.State.Firing) { m = Mood.Determined; why = "MAIN GUN FIRING"; return; }
            if (warp != null && warp.Current == WarpDrive.State.Warping) { m = Mood.Awe; why = "IN WARP"; return; }
            if (warp != null && warp.Current == WarpDrive.State.Charging) { m = Mood.Focused; why = "WARP DRIVE CHARGING"; return; }
            if (landing != null && landing.Current == PlanetLanding.State.Descending) { m = Mood.Focused; why = "GOING DOWN"; return; }
            if (landing != null && landing.Current == PlanetLanding.State.Ascending) { m = Mood.Focused; why = "TAKING OFF"; return; }
            if (dock != null && (dock.Current == Docking.State.Docking || dock.Current == Docking.State.Undocking)) { m = Mood.Focused; why = "DOCKING"; return; }
            if (dock != null && dock.Current == Docking.State.Docked) { m = Mood.Smile; why = "DOCKED"; return; }
            if (hull != null && hull.Current < hull.maxHealth * 0.25f) { m = Mood.Alarmed; why = "HULL CRITICAL"; return; }
            if (gun != null && gun.Current == MainBeam.State.Charging) { m = Mood.Focused; why = "MAIN GUN CHARGING"; return; }
            if (Time.time - lastShotTime < 1.5f) { m = Mood.Determined; why = "GUNS FIRING"; return; }
            if (ShipConsole.HostilesNear > 0) { m = Mood.Focused; why = ShipConsole.HostilesNear == 1 ? "HOSTILE NEARBY" : $"{ShipConsole.HostilesNear} HOSTILES NEARBY"; return; }
            if (laser != null && laser.Cutting) { m = Mood.Focused; why = "MINING"; return; }
            if (sensor != null && sensor.Sweeping) { m = Mood.Focused; why = "SCANNING"; return; }
            if (cc.target != null && warp == null) { m = Mood.Focused; why = "TARGET LOCKED"; return; }
        }

        /// <summary>Notice what has just happened to the ship, and react.</summary>
        void Watch(CarrierController cc)
        {
            // lost, and back
            if (cc.Destroyed != wasDestroyed)
            {
                wasDestroyed = cc.Destroyed;
                if (!cc.Destroyed) React(Mood.Determined, 8, 2.5f, "BACK IN ACTION");
                else { hitFlash = 1f; shake = 1f; }
            }
            if (cc.Destroyed) return;

            // hits
            if (hull != null && hull.LastHitTime > hullHitSeen)
            {
                hullHitSeen = hull.LastHitTime;
                if (hull.LastHitAmount >= 5f)
                {
                    bool hard = hull.LastHitAmount >= hull.maxHealth * 0.04f || hull.Current < hull.maxHealth * 0.35f;
                    React(Mood.Pain, hard ? 7 : 6, hard ? 1.6f : 1.1f, "HULL HIT");
                    hitFlash = hard ? 1f : 0.7f;
                    shake = hard ? 1f : 0.6f;
                }
            }
            if (shield != null)
            {
                if (shield.LastHitTime > shieldHitSeen)
                {
                    shieldHitSeen = shield.LastHitTime;
                    if (Time.time - lastShotTime < 1.5f) React(Mood.Determined, 5, 1f, "RETURNING FIRE");
                    else React(Mood.Alarmed, 5, 1.1f, "SHIELDS TAKING FIRE");
                    hitFlash = Mathf.Max(hitFlash, 0.35f);
                    shake = Mathf.Max(shake, 0.25f);
                }
                if (shieldWasUp && !shield.Up) React(Mood.Alarmed, 8, 2.4f, "SHIELDS DOWN");
                shieldWasUp = shield.Up;
            }

            // threats
            if (ShipConsole.MissilesInbound > missilesSeen) React(Mood.Alarmed, 6, 1.5f, "MISSILE INBOUND");
            missilesSeen = ShipConsole.MissilesInbound;
            if (hostilesSeen == 0 && ShipConsole.HostilesNear > 0) React(Mood.Alarmed, 4, 1.6f, "CONTACT");
            hostilesSeen = ShipConsole.HostilesNear;
            if (ShipConsole.Kills > killsSeen) React(Mood.Smile, 5, 2.4f, "TARGET DESTROYED");
            killsSeen = ShipConsole.Kills;

            // our own fire
            int shots = Shots(cc.mainTurrets);
            if (shots > shotsSeen) { lastShotTime = Time.time; React(Mood.Determined, 3, 1.2f, "GUNS FIRING"); }
            shotsSeen = shots;
            if (ciws != null)
            {
                int c = Shots(ciws.mounts);
                if (c > ciwsSeen) React(Mood.Determined, 2, 0.8f, "POINT DEFENCE FIRING");
                ciwsSeen = c;
            }
            if (missiles != null)
            {
                int cells = missiles.ReadyCells;
                if (cellsSeen >= 0 && cells < cellsSeen) React(Mood.Determined, 4, 1.5f, "MISSILES AWAY");
                cellsSeen = cells;
            }
            if (cc.Wing.Count > wingSeen) React(Mood.Determined, 3, 1.5f, "LAUNCHING THE WING");
            wingSeen = cc.Wing.Count;
            var gun = demo.MainGun;
            bool firing = gun != null && gun.Current == MainBeam.State.Firing;
            if (firing && !gunWasFiring) { React(Mood.Determined, 6, 2f, "MAIN GUN FIRING"); shake = Mathf.Max(shake, 0.3f); }
            gunWasFiring = firing;

            // travel
            if (warp != null)
            {
                if (warpWas != WarpDrive.State.Ready && warp.Current == WarpDrive.State.Ready && warpWas == WarpDrive.State.Arriving)
                    React(Mood.Smile, 3, 2f, "ARRIVED");
                warpWas = warp.Current;
            }
            var landing = PlanetLanding.Instance;
            bool landed = landing != null && landing.Current == PlanetLanding.State.OnSurface;
            if (landed && !wasLanded) React(Mood.Smile, 3, 2f, "TOUCHDOWN");
            wasLanded = landed;
            var dock = Docking.Instance;
            bool docked = dock != null && dock.Current == Docking.State.Docked;
            if (docked && !wasDocked) React(Mood.Smile, 4, 3f, "DOCKED: REPAIRED");
            wasDocked = docked;

            // mining
            var laser = demo.Laser;
            if (laser != null)
            {
                if (laser.DepositsMinedOut > minedOutSeen) React(Mood.Smile, 3, 2f, "ROCK MINED OUT");
                minedOutSeen = laser.DepositsMinedOut;
            }
            var cargo = demo.Cargo;
            bool full = cargo != null && cargo.Fraction > 0.999f;
            if (full && !laserWasFull) React(Mood.Smile, 3, 2.5f, "HOLD FULL");
            laserWasFull = full;
        }

        // ------------------------------------------------------------------ drawing

        /// <summary>Draw her in `r` (GUI coordinates), with a frame in the alert colour.</summary>
        public void Draw(Rect r, Color frame)
        {
            float s = Hud.S;
            Hud.Fill(r, new Color(0.01f, 0.02f, 0.05f, 0.95f));
            int who = Captain.Index;
            var now = Captain.Face(who, Shown);
            if (now == null)
            {
                GUI.Label(r, "<color=#6a7a8a>NO SIGNAL</color>", Hud.Text(14, true, TextAnchor.MiddleCenter));
                Hud.Outline(r, Mathf.Max(1f, 2f * s), frame);
                return;
            }
            GUI.BeginGroup(r);
            // breathing, and a jolt when hit
            float t = Time.time;
            float zoom = 1.035f + 0.012f * Mathf.Sin(t * Mathf.PI * 2f / 4.3f);
            Vector2 jolt = shake > 0f ? new Vector2(Mathf.Sin(t * 91f), Mathf.Cos(t * 77f)) * (7f * s * shake) : Vector2.zero;
            float w = r.width * zoom, h = r.height * zoom;
            var pic = new Rect((r.width - w) * 0.5f + jolt.x, (r.height - h) * 0.5f + jolt.y - 1.5f * s * Mathf.Sin(t * Mathf.PI * 2f / 4.3f), w, h);
            var old = GUI.color;
            // the expression she is leaving, fading under the new one (blinks and words cut quickly)
            float k = Mathf.Clamp01((t - blendStart) / BlendTime);
            if (k < 1f && Shown == Mood)
            {
                var was = Captain.Face(who, from);
                if (was != null) GUI.DrawTexture(pic, was, ScaleMode.ScaleAndCrop);
                GUI.color = new Color(1f, 1f, 1f, k);
            }
            GUI.DrawTexture(pic, now, ScaleMode.ScaleAndCrop);
            GUI.color = old;
            GUI.EndGroup();

            // the look of a comms screen: scan lines, and tints for the moment
            if (scan == null)
            {
                scan = new Texture2D(1, 4) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
                scan.SetPixels(new[] { new Color(0, 0, 0, 0.22f), Color.clear, Color.clear, new Color(1, 1, 1, 0.03f) });
                scan.Apply();
            }
            GUI.DrawTextureWithTexCoords(r, scan, new Rect(0f, t * 0.6f, 1f, r.height / (3f * s)));
            var warpState = warp != null ? warp.Current : WarpDrive.State.Ready;
            if (warpState == WarpDrive.State.Warping || warpState == WarpDrive.State.Charging)
                Hud.Fill(r, new Color(0.3f, 0.55f, 1f, 0.1f + 0.06f * Mathf.Sin(t * 6f)));
            if (Mood == Mood.Sad) Hud.Fill(r, new Color(0f, 0f, 0f, 0.35f));
            if (hitFlash > 0f) Hud.Fill(r, new Color(1f, 0.15f, 0.1f, 0.38f * hitFlash));
            Hud.Outline(r, Mathf.Max(1f, 2f * s), frame);
            Hud.Corners(new Rect(r.x - 3f * s, r.y - 3f * s, r.width + 6f * s, r.height + 6f * s), 12f * s, Mathf.Max(1f, 2f * s), frame);
        }
    }
}
