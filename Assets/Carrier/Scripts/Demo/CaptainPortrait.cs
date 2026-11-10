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
    /// The face is alive. Each expression has three steps (a hint of it, clearly it, all of it):
    /// a new one grows through them, and on the way to another she relaxes back down through the
    /// steps of the one she is leaving (quicker for a sudden shock), each step blended into the
    /// next. She breathes and sways a little, blinks (eyelids half, then closed, then open),
    /// mouths the messages on the HUD, and the picture jolts and flashes red when the ship is hit.
    ///
    /// She also speaks: a short line for what has just happened (CaptainLines), typed out on the
    /// console's comms line while her mouth moves. Urgent news (a hit, the shield down, a
    /// missile, contact, a kill) can cut in quickly; anything else waits its turn, and she
    /// doesn't say the same kind of thing again too soon. When all is quiet she remarks on it
    /// now and then.
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

        // the animation: the expression on screen and how far into it (0 calm to its last step),
        // and the blink and talk tracks over it (0 none, 1 half, 2 full)
        Mood face = Mood.Neutral;
        float level;
        float blink, talk, blinkStart = -100f, talkUntil = -1f, talkTarget, nextMouth, nextBlink, hitFlash, shake;
        float swayPhase;
        /// <summary>The expression showing and how strongly, 0 to 3 (for the self-test).</summary>
        public Mood Face => face;
        public float Level => level;

        // ---- what she says
        /// <summary>Her latest line, and the event it was about.</summary>
        public string Quote { get; private set; } = "";
        public string QuoteEvent { get; private set; } = "";
        public float QuoteAt { get; private set; } = -100f;
        /// <summary>Every line she has said (for the self-test).</summary>
        public readonly System.Collections.Generic.List<(string ev, string line, float at)> Said =
            new System.Collections.Generic.List<(string, string, float)>();
        public const float LettersPerSecond = 48f;
        string lastEvent = "";
        bool greeted;
        float nextIdle;
        readonly System.Collections.Generic.Dictionary<string, float> lastSaid = new System.Collections.Generic.Dictionary<string, float>();
        static readonly System.Collections.Generic.HashSet<string> Urgent = new System.Collections.Generic.HashSet<string>
        {
            "HULL HIT", "SHIELDS DOWN", "HULL CRITICAL", "MISSILE INBOUND", "SHIP LOST", "CONTACT", "TARGET DESTROYED",
            "MAIN GUN FIRING", "BACK IN ACTION", "REPORTING FOR DUTY", "ARRIVED",
        };

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
                hull = demo.Hull;
                shield = demo.ShieldGen;
                warp = demo.Warp;
                missiles = cc.missiles;
                ciws = demo.Ciws;
                if (hull != null) hullHitSeen = hull.LastHitTime;
                if (shield != null) shieldHitSeen = shield.LastHitTime;
                shotsSeen = Shots(cc.mainTurrets);
                ciwsSeen = ciws != null ? Shots(ciws.mounts) : 0;
                cellsSeen = missiles != null ? missiles.ReadyCells : -1;
                wingSeen = cc.Wing.Count;
            }
            toastsSeen = CarrierDemoInput.Toasts;
            nextBlink = Time.time + Random.Range(1.5f, 4f);
            swayPhase = Random.Range(0f, 10f);
            nextIdle = Time.time + Random.Range(25f, 45f);
        }

        void OnEnable() => Captain.Changed += OnCaptainChanged;
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

            // taking command as the level starts
            if (!greeted && Time.timeSinceLevelLoad > 2f && !cc.Destroyed) { greeted = true; Speak("REPORTING FOR DUTY", true); }
            // all quiet for a while: a remark now and then
            if ((lastEvent == "ALL QUIET" || lastEvent == "ON THE SURFACE") && Time.time >= nextIdle)
            {
                Speak(lastEvent, true);
                nextIdle = Time.time + Random.Range(35f, 65f);
            }
            else if (lastEvent != "ALL QUIET" && lastEvent != "ON THE SURFACE") nextIdle = Mathf.Max(nextIdle, Time.time + 20f);
            Animate(cc);

            hitFlash = Mathf.Max(0f, hitFlash - Time.deltaTime * 2.6f);
            shake = Mathf.Max(0f, shake - Time.deltaTime * 3f);
        }

        void Set(Mood m, string why)
        {
            Reason = why;
            // something new to talk about
            string ev = CaptainLines.EventOf(why);
            if (ev != lastEvent)
            {
                lastEvent = ev;
                if (ev != "ALL QUIET" && ev != "ON THE SURFACE") Speak(ev, false);
            }
            if (m == Mood) return;
            Mood = m;
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
            if (cc.Destroyed)
            {
                // (the hit that destroyed it is not news when it comes back)
                if (hull != null) hullHitSeen = hull.LastHitTime;
                if (shield != null) shieldHitSeen = shield.LastHitTime;
                return;
            }

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
                if (warp.Current == WarpDrive.State.Ready && warpWas == WarpDrive.State.Arriving)
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

        // ------------------------------------------------------------------ speech

        /// <summary>Say a line about `ev`, if it is her turn to speak.</summary>
        void Speak(string ev, bool idle)
        {
            var cc = demo != null ? demo.carrier : null;
            if (cc == null || CaptainLines.CountFor(ev) == 0) return;
            bool urgent = Urgent.Contains(ev);
            float sinceLine = Time.time - QuoteAt;
            float sinceSame = lastSaid.TryGetValue(ev, out float t) ? Time.time - t : 999f;
            // still saying the last line (unless this can't wait), or said this kind of thing just now
            float busy = Quote.Length / LettersPerSecond + 1.2f;
            if (!idle && sinceLine < (urgent ? 1.5f : Mathf.Max(4.5f, busy))) return;
            if (sinceSame < (urgent ? 5f : 12f)) return;
            string line = CaptainLines.Pick(ev, Blank);
            if (line == null) return;
            Quote = line;
            QuoteEvent = ev;
            QuoteAt = Time.time;
            lastSaid[ev] = Time.time;
            Said.Add((ev, line, Time.time));
            if (Said.Count > 300) Said.RemoveAt(0);
            talkUntil = Time.time + line.Length / LettersPerSecond + 0.25f;
            nextIdle = Mathf.Max(nextIdle, Time.time + 20f);
        }

        /// <summary>Fill in a blank in a line: the captain, the ship, where it is, how many hostiles.</summary>
        string Blank(string key)
        {
            var cc = demo.carrier;
            switch (key)
            {
                case "name": return Captain.Current.name;
                case "ship": return cc.shipName;
                case "place":
                    var here = WarpPoint.Nearest(cc.transform.position);
                    return here != null ? here.displayName : null;
                case "count": return ShipConsole.HostilesNear > 1 ? ShipConsole.HostilesNear.ToString() : null;
                default: return null;
            }
        }

        /// <summary>Her line as far as she has got through it, and how visible it still is (it
        /// fades a few seconds after she finishes).</summary>
        public string QuoteShown(out float alpha)
        {
            float age = Time.time - QuoteAt;
            float typed = Quote.Length / LettersPerSecond;
            alpha = Mathf.Clamp01(1f - (age - typed - 4f) / 1.2f);
            if (Quote.Length == 0 || alpha <= 0f) { alpha = 0f; return ""; }
            int n = Mathf.Clamp(Mathf.CeilToInt(age * LettersPerSecond), 0, Quote.Length);
            return Quote.Substring(0, n);
        }

        // ------------------------------------------------------------------ animation

        /// <summary>Step the face toward her mood, and run the blinks and words over it.</summary>
        void Animate(CarrierController cc)
        {
            float dt = Time.deltaTime;
            // a shock comes on fast; anything else at an easy pace (steps a second)
            bool sudden = Mood == Mood.Pain || Mood == Mood.Alarmed;
            float rate = sudden ? 14f : 6f;
            if (face != Mood && Mood != Mood.Neutral && level > 0f)
                level = Mathf.MoveTowards(level, 0f, rate * 1.4f * dt);          // relax out of the old one
            else
            {
                if (level <= 0f && Mood != Mood.Neutral) face = Mood;
                float want = Mood == Mood.Neutral ? 0f : Captain.Steps(face);
                level = Mathf.MoveTowards(level, want, rate * dt);               // and into the new one
                if (level <= 0f) face = Mood.Neutral;
            }

            // blinks: eyelids half, closed, half, open, every few seconds (when the face is near rest)
            if (Time.time >= nextBlink)
            {
                if (!(face == Mood.Pain && level > 1.2f)) blinkStart = Time.time;
                nextBlink = Time.time + Random.Range(2.2f, 5.5f) * (Random.value < 0.15f ? 0.25f : 1f);   // now and then a double blink
            }
            float b = (Time.time - blinkStart) / 0.2f;                           // a blink takes 0.2 s
            blink = b < 0f || b > 1f ? 0f : 2f * (1f - Mathf.Abs(b * 2f - 1f));
            blink = Mathf.Min(2f, blink * 1.25f);

            // a new message on the HUD: she says it, the mouth opening and closing through its steps
            if (CarrierDemoInput.Toasts != toastsSeen)
            {
                toastsSeen = CarrierDemoInput.Toasts;
                if (!cc.Destroyed) talkUntil = Mathf.Max(talkUntil, Time.time + 1.2f);
            }
            bool mouthBusy = level > 1.2f && (face == Mood.Alarmed || face == Mood.Pain || face == Mood.Awe);
            if (Time.time < talkUntil && !mouthBusy)
            {
                if (Time.time >= nextMouth)
                {
                    talkTarget = talkTarget > 0.5f ? Random.Range(0f, 0.4f) : Random.Range(1.2f, 2f);
                    nextMouth = Time.time + Random.Range(0.07f, 0.14f);
                }
            }
            else talkTarget = 0f;
            talk = Mathf.MoveTowards(talk, talkTarget, 16f * dt);

            Shown = blink > 1f ? Mood.Blink : talk > 1f ? Mood.Talk : level > 0.5f ? face : Mood.Neutral;
        }

        // ------------------------------------------------------------------ drawing

        /// <summary>Draw part of a picture (`band`, 0 to 1 from the top left) over the same part
        /// of what is there, `alpha` of the way.</summary>
        static void LayerBand(Rect pic, Texture2D tex, Rect band, float alpha)
        {
            if (tex == null || alpha <= 0.002f) return;
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            var at = new Rect(pic.x + band.x * pic.width, pic.y + band.y * pic.height, band.width * pic.width, band.height * pic.height);
            GUI.DrawTextureWithTexCoords(at, tex, new Rect(band.x, 1f - band.y - band.height, band.width, band.height));
            GUI.color = old;
        }

        /// <summary>Draw a picture over what is there, `alpha` of the way.</summary>
        static void Layer(Rect pic, Texture2D tex, float alpha)
        {
            if (tex == null || alpha <= 0.002f) return;
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            GUI.DrawTexture(pic, tex, ScaleMode.ScaleAndCrop);
            GUI.color = old;
        }

        /// <summary>Draw her in `r` (GUI coordinates), with a frame in the alert colour.</summary>
        public void Draw(Rect r, Color frame)
        {
            float s = Hud.S;
            Hud.Fill(r, new Color(0.01f, 0.02f, 0.05f, 0.95f));
            int who = Captain.Index;
            var now = Captain.Face(who, Mood.Neutral, 0);
            if (now == null)
            {
                GUI.Label(r, "<color=#6a7a8a>NO SIGNAL</color>", Hud.Text(14, true, TextAnchor.MiddleCenter));
                Hud.Outline(r, Mathf.Max(1f, 2f * s), frame);
                return;
            }
            GUI.BeginGroup(r);
            // breathing, a slow sway of the head, and a jolt when hit
            float t = Time.time;
            float breath = Mathf.Sin(t * Mathf.PI * 2f / 4.3f);
            float zoom = 1.045f + 0.01f * breath;
            float sway = t + swayPhase;
            Vector2 drift = new Vector2(Mathf.Sin(sway * 0.83f) * 1.6f, Mathf.Sin(sway * 0.61f + 1f) * 1.1f - 1.4f * breath) * s;
            Vector2 jolt = shake > 0f ? new Vector2(Mathf.Sin(t * 91f), Mathf.Cos(t * 77f)) * (7f * s * shake) : Vector2.zero;
            float w = r.width * zoom, h = r.height * zoom;
            var pic = new Rect((r.width - w) * 0.5f + drift.x + jolt.x, (r.height - h) * 0.5f + drift.y + jolt.y, w, h);
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Sin(sway * 0.47f) * 0.6f + (shake > 0f ? Mathf.Sin(t * 60f) * 1.5f * shake : 0f),
                                         new Vector2(r.width * 0.5f, r.height * 0.8f));
            // the expression: the step it is at, blended into the next one up
            int i = Mathf.FloorToInt(level);
            float f = level - i;
            GUI.DrawTexture(pic, Captain.Face(who, face, i), ScaleMode.ScaleAndCrop);
            if (f > 0.002f) Layer(pic, Captain.Face(who, face, i + 1), f);
            // eyelids and words over it (half, then full): the whole face when it is near rest,
            // just the eyes or the mouth over a stronger expression (which stays as it is)
            var info = Captain.Roster[who];
            bool whole = level <= 1.2f;
            if (blink > 0f)
            {
                var half = Captain.Face(who, Mood.Blink, 1);
                var shut = Captain.Face(who, Mood.Blink, 2);
                if (whole) { Layer(pic, half, Mathf.Min(1f, blink)); Layer(pic, shut, blink - 1f); }
                else { LayerBand(pic, half, info.eyes, Mathf.Min(1f, blink)); LayerBand(pic, shut, info.eyes, blink - 1f); }
            }
            if (talk > 0f)
            {
                var part = Captain.Face(who, Mood.Talk, 1);
                var open = Captain.Face(who, Mood.Talk, 2);
                if (whole && blink <= 0f) { Layer(pic, part, Mathf.Min(1f, talk)); Layer(pic, open, talk - 1f); }
                else { LayerBand(pic, part, info.mouth, Mathf.Min(1f, talk)); LayerBand(pic, open, info.mouth, talk - 1f); }
            }
            GUI.matrix = matrix;
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
