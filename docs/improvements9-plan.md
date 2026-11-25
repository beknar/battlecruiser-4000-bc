# Improvements 9: the plan

This plan covers `docs/improvements9.txt`. It has sixteen requests, made in twelve PRs in the order
below. After the last one is merged, the shipping build is made.

1. **The skybox** looks like a large flat texture, not a real skyscape. Look in the Unreal assets
   and `F:\non-unreal-assets` for a better one.
2. **Salvage:** the wingman can salvage.
3. **Rename:** the wingman's name can be changed.
4. **Moving ore:** ore can go from the player's ship to the wingman's, and from the wingman's to
   the player's.
5. **A chosen ore:** the wingman can be told to go after a given kind of ore.
6. **The most valuable ore:** the wingman can be told to go after the most valuable ore in range.
7. **Closing the buy/sell screen** with a key other than Esc.
8. **Attack my target:** the wingman attacks the player's target.
9. **The mining list scrolls** with the mouse wheel without zooming the ship's view.
10. **Sorting and filtering the mining list** by value or by mineral.
11. **Panning the system map** left, right, up and down.
12. **Planets with moons on the map:**
    - Clicking one selects the planet, not a random moon.
    - The moons and the other points of interest show as the planet's points of interest.
    - The planet can be warped to.
    - From there, a LOCAL MAP button shows the planet, its moons and their points of interest.
    - From the local map you can warp to a moon or fly to a nearby point of interest.
13. **Choosing the microphone.**
14. **Fixing the voice mishearings.**
15. **The co-op session:** compile the latest session's conversation and add any functions it
    points to.
16. **The shipping build.** "newest improvements file" in the list names the file itself and asks
    for nothing.

The plan was reviewed against the code before any work started. "Changed after review" marks what
that review changed.

## What was found

### The sky (item 1)
- **Today's sky** is one material: `Skybox/Panoramic` showing `TC_Space.exr`. That's a 4096 × 2048
  equirectangular EXR from the CargoShip pack, made by `CarrierBuilder.BuildScene`.
- **Why it looks flat:**
  - **It's dim and low in contrast.** The median brightness is 0.035 and the brightest point 1.5,
    so the stars are faint smudges that bloom never catches.
  - **It has one subject:** a single nebula. The rest is an even haze.
  - **It's stretched.** It has about 11 pixels per degree where a 1080p view needs 18, so it shows
    at 1.6 times its size.
  - **It shimmers.** It has no mipmaps.
  - **It has no depth.** It's one layer at infinity, with no star layer apart from it.
- **No better sky exists ready-made on either drive:**
  - The Unreal export (`I:\ue-assets`) has deep-space cubemaps, but the exporter skipped every
    TextureCube (115 of them, including SpaceColony2086's `T_Space2086` and Wasteland's
    `T_HDRI_Space`). Only their names are in its manifest.
  - `F:\non-unreal-assets` has only flat 2D space backdrops, none of them 360°.
- **What is on disk:**
  - The OrbitalBase pack's night-sky masks (`T_StarsMasks01/02.png`, 4096², 16-bit). They hold
    star points, bright knots and nebula clouds in separate channels: the layers of Unreal's
    `M_NightSky`, which wasn't exported.
  - A crisp star speckle (`Spaceship_Kit/.../T_star.png`).
  - All of these are licensed. They're staged into the ignored `Assets/Carrier/Kit` like
    `TC_Space`, never committed.

### The map and the moons (items 11, 12)
- **The map is `SystemMap`.**
  - The wheel zooms, and a right- or middle-drag pans. The pad's right stick pans.
  - The arrows and WASD step the selection.
  - So nothing on the keyboard pans, and a left-drag only selects. The help says "right-drag:
    move", which is easy to miss.
- **The random moon:**
  - Moons are placed at their real distance from the planet (1,400 to 5,000 km), but the planets'
    orbits are scaled up 20,000 times.
  - So at the whole-system view every moon sits within a fraction of a pixel of its planet.
  - `PointAt` takes the place nearest the click, so whichever moon happens to be a hair closer
    wins.
  - When the map opens, it selects the nearest place that isn't here, which at a planet is one of
    its moons.
- **Points of interest:**
  - A planet's points of interest (`Places.PointsOfInterest`) are the landmarks, stations and
    bases within its reach.
  - Moons are never among them, because a moon is a place of its own.
  - Nothing draws points of interest on the map; only the info panel names them.
- **Travel:**
  - Warping goes to any `WarpPoint` (`WarpDrive.Engage`).
  - Flying to a point of interest at the place the ship is at is `NavigationMode.TravelTo`, which
    refuses anywhere else ("warp to X first").
- **There's no local map.**

### The mining list and the services screen (items 7, 9, 10)
- **The mining list** is the mining mode's Tab menu (`MiningMode.Rows`, drawn by `ModeMenu`).
  - It lists the 20 nearest finds.
  - It has no wheel handling, and `OrbitCamera` holds the wheel back only for the services screen.
  - The 20 are cut after sorting by distance, so sorting by value has to come before the cut.
- **The services screen** already closes on U as well as Esc, and its hints say so.
  - But Tab opens it from the controls box ("Tab or U"), and inside it Tab only turns pages.
  - So a player who opened it with Tab can't close it with Tab. Everywhere else (`ModeMenu`), Tab
    closes.

### The wingman (items 2 to 6, 8, 15)
- **Salvage:**
  - The player salvages with `Salvage` (combat mode only). It's a beam on a wreck within 1.5 km.
  - It takes steel plate, aluminium sheet and copper wire, and sometimes a part, at a sixth of the
    wreck a second.
  - Every game has its own copy of each wreck, so the bot salvaging takes nothing from a player.
  - Each wreck is also a warp point ("Wreck of X").
  - The goods aren't ore, so selling ore (`CargoHold.SellAll`) leaves them aboard.
- **Moving ore:**
  - Credits already move by `gift` / `giftok`. The sender holds the gift for 10 s for a receipt,
    and the receiver refuses one older than 6 s, so a gift can't be both refunded and received.
  - Only an AI player's gift is taken, and only the bot calls the refund tick.
  - The hold keeps ore by mineral (`CargoHold`, 600 t).
- **Mining:**
  - The wingman mines the nearest find in a field (`Wingman.Mining` scan step).
  - There are 27 minerals, from silicates (2 cr/t) to diamonds (20,000 cr/t).
  - A rock is a mix, and the laser takes the whole mix, so "mine platinum" can only mean the rocks
    richest in platinum.
  - Today "mine gold" looks for a field called "gold".
- **The player's target:**
  - It's `CarrierController.target`. The ship message (`ps`) doesn't carry it.
  - Enemies have the same id in every game (`CoopWorld.IdOf` / `EntityTransform`).
- **The name:** the server takes it from the hello only, so a new one needs a server message,
  shaped like the captain change (`captain` → roster).

### Voice (items 13, 14)
- **Choosing the microphone:**
  - Windows' recognisers (`DictationRecognizer`, `KeywordRecognizer`) always use the default
    recording device. They can't be pointed at another one or fed audio.
  - Linux has no voice commands at all.
  - In the browser, `SpeechRecognition.start(track)` takes a chosen microphone's track. That's
    Chrome and Edge 135 and later; other browsers ignore it and use the default.
  - A real choice on Windows and Linux means recording the chosen device with Unity's `Microphone`
    and recognising it on the player's machine.
  - The smallest engine that does that well for commands is **Vosk**, with its small English model
    (40 MB, Apache-2.0). It takes a phrase list (a grammar), which also cuts the mishearings.
- **The mishearings:** all from the last co-op session, on the web (Chrome).
  - "Wingman" was heard as "women", "only man" and "hey man".
  - "sell ore" was heard as "cellar", "cell or" and "sell the or"; "mining" as "morning"; "attack"
    as "pack"; "Fang Hunter" as "Frank Hunter". "amen", in the name's place, is the name again
    *(changed after review: it was first taken for "and")*.
  - When no wingman is named, the voice code puts one first, so "women mine" was sent as
    "Wingman, women mine". The bot couldn't read that, and left it to its LLM listener.

### The co-op session (item 15)
The last session (24–25 Nov, one player on the web, all by voice) is compiled in
`docs/coop-session-2026-11-24.md`, with names made anonymous. What it points to:
- **Asked for and missing:**
  - Salvage (asked three times).
  - "Attack my target" (three times).
  - "Defend me" / "I'm under attack": come to me and fight what's on me, from anywhere.
  - "Where are you?"
  - A target's name alone as an attack order ("Wingman, Fang Hunter").
  - "Attack" when the enemies are round the leader, not round the wingman.
- **Worked:** status, mine, give credits (with "how many?" and then "all"), and the mishearings
  above when an LLM was listening.

## The PRs

Each PR is reviewed, its errors fixed, and merged before the next starts.
- **Written in parallel:** the code for PRs that touch different files is written in parallel, in
  separate worktrees: PR 3 and PR 4 with PR 2, and PR 11 with any of them.
- **One at a time:** PRs 6 to 10 share `CoopBot.cs` and the wingman's pilot, so they go one after
  another.
- **Tested and merged in order:** only one game or editor runs on the laptop at once.
- **Changed after review:** PRs 3 and 7 were split, the mishearings moved up to PR 5, and Vosk
  (the microphone on Windows and Linux) moved to last.

1. **PR 1: the plan.** This file and the compiled session. Docs only.

2. **PR 2: the services screen and the mining list** (items 7, 9, 10).
   - **Tab closes the services screen,** as it opened it.
     - Q/E, LB/RB and the tabs still turn pages. Shift+Tab no longer turns back; Q does.
     - The key hints, the class comment and the controls box say so.
     - *(Changed after review: there's also a CLOSE button in the screen's corner, for the mouse.
       The screen has none, and on the web the first Esc only leaves full screen.)*
   - **The wheel moves the selection in any mode's Tab menu** (`ModeMenu`), the mining list among
     them, while the pointer is over it, as it does in the services screen.
     - *(Changed after review: `ModeMenu` puts its scroll back each frame to keep the selected row
       in view, so the wheel has to move the selection, not the scroll.)*
     - `OrbitCamera` holds the wheel only while the pointer is over the menu (`ModeMenu.Area`).
       *(Changed after review: holding it whenever the menu is up would stop zooming with the
       pointer elsewhere.)*
   - **The mining list's order:**
     - The O key cycles nearest → most valuable → richest.
       - "Most valuable" is credits per tonne × the tonnes the rock started with.
       - "Richest" is credits per tonne.
       - *(Changed after review: the starting tonnes are used, not what's left, so the rows don't
         move while the laser cuts.)*
     - The F key cycles a mineral filter: all → each mineral in the finds, most valuable first.
     - The list header shows both.
     - The order and filter are applied before the list is cut to 20.
     - T / Shift+T (the next deposit) follow the same order.
     - Both settings are kept between sessions. *(Changed after review: under keys of their own for
       automated runs, as `Captain.PrefKey` does, so a test never picks up the player's choice.)*
   - **Tests:** EditMode for the order and filter; PlayMode for the wheel (as `CameraWheelTests`).

3. **PR 3: moving round the system map** (item 11, and item 12's selection).
   - **Panning:**
     - Shift + arrows (or WASD) pan.
     - A left-drag pans once the pointer moves 6 px.
     - *(Changed after review: the click is moved from the press to the release, and selects only
       when the pointer moved less than 6 px. The WARP button and the double-click warp still work
       as they do now.)*
     - The help line says both.
   - **Clicking a planet selects the planet:**
     - A moon counts as part of its planet until the view is close enough to draw it apart.
       `PointAt`, hover, Tab / Shift+Tab (`Step`) and the arrows (`StepToward`) all pass over it
       until then. *(Changed after review: Tab and the arrows walked through the moons too.)*
     - The moons' labels are hidden until then as well.
     - When the map opens, it selects where the ship is (its planet, not a moon).
     - The system test's "Tab to Selene" step zooms in first.
   - **A planet's points of interest:** the info panel lists its moons first, then its landmarks,
     stations and bases, then each moon's, from one new query, `Places.Around(planet)`.
   - **Tests:** EditMode for the pick (the planet over its moon at system zoom, the moon when zoomed
     in) and for `Places.Around`.

4. **PR 4: the local map** (the rest of item 12).
   - **The button:** LOCAL MAP, beside WARP.
     - It's shown when a planet or moon is selected or the ship is at one.
     - L toggles it while the map is open, and Y on the pad. *(Changed after review: L is level-off
       outside the map, so it's read only while the map is up.)*
     - Back, Esc or L leaves it.
   - **What it shows:** the planet, its moons and every point of interest round them.
     - It's centred on the planet and kept there as it moves on its orbit, zoomed so the outermost
       moon fits.
     - High orbits, which are straight up, are offset so they don't sit on the planet.
   - **Moving off it:**
     - WARP on a moon or a point of interest warps there.
     - On a point of interest at the place the ship is at, FLY TO flies there
       (`NavigationMode.TravelTo`).
     - *(Changed after review: FLY TO clears the map's "switched mode" before closing it, or
       closing would put the player back in the mode they were in and stop the flight.)*
   - **Tests:** a system-test step that opens the local map at Thalassa in the Kestrel system and
     flies to Selene's high orbit. *(Changed after review: Gilgal's moons are pirate-held, so a
     fight could stop the warp.)*

5. **PR 5: the voice mishearings and the microphone on the web** (items 13 in part, 14).
   *(Changed after review: moved up from PR 8, as it's what the player had most trouble with, and
   small.)*
   - **More of what the browser heard:**
     - The browser's recogniser is asked for its 5 best guesses, not 1.
     - The first one that reads as an order to a wingman is sent. With none, the first guess is
       sent, as now.
     - This fixes most mishearings with no sound table.
   - **The wingman's name, near-missed:**
     - A first word that sounds like a wingman's name ("women", "amen", "wing man", "only man",
       "hey man") is replaced by the name on the sending side. *(Changed after review: the bot
       answers only its exact name, so skipping the prefix alone would reach no one; and "amen" is
       the name, not "and".)*
     - The match is by sound (Double Metaphone codes plus a short edit distance), not by a list, so
       other names work too.
     - When the message doesn't start with a sound-alike, the name is put first, as now.
   - **The orders, near-missed:**
     - A small table of the sound-alikes of order words, applied only to the first words after the
       name: "cellar" / "cell or" / "sell the or" → sell ore; "pack" / "tack" → attack …
     - Used only when what was heard isn't already an order.
     - Single words that are also talk aren't in it. *(Changed after review: "morning" → mining
       would turn "Wingman, morning" into an order. "morning" is matched only with an ore word or a
       field after it.)*
     - The game puts what it corrected in brackets after the message, so the bot's answer shows
       what it was taken as.
   - **The browser's microphone:**
     - Where `SpeechRecognition.start(track)` works (Chrome and Edge 135 and later, checked by
       version), the pause menu gets **Microphone: < name >**.
     - It lists the browser's audio inputs and is kept by device id.
     - Names appear once the browser has been allowed the microphone; before that it says "allow
       the microphone first".
     - Elsewhere it shows "Microphone: browser default".
     - *(Changed after review: `SpeechGrammarList` is dropped; Chrome ignores it.)*
   - **Tests:** EditMode for each mishearing from the session (the exact strings), the sound match,
     the choice among guesses, and no change to talk.
   - **Later PRs** (6 to 9) add their own new orders to the guess check and to the keyword phrases
     (`VoiceCommand.Orders`, `Phrases`).

6. **PR 6: the wingman's fights** (items 8, 15).
   - **The player's target goes out:**
     - The ship message carries the player's target as an enemy's entity id (`tg`), or "" when
       nothing hostile is locked.
     - `CoopWire.Content` goes to 3, so the bot can tell "nothing locked" from "a game too old to
       say".
     - *(Changed after review: a player is never sent as a target, and the bot never attacks one.)*
   - **New orders:**
     - **"attack my target" / "my lock" / "what I'm shooting":** the speaker's target, warping to
       it if it's far.
       - With nothing locked: "you've nothing locked".
       - With a station or other non-enemy locked: "that's not an enemy".
       - From an old game: "your game doesn't say what you've locked".
     - **"defend me", "cover me", "I'm under attack", "help me":** come to the speaker and fight
       whatever is near them.
       - When it can't warp because it's in a fight of its own, it says "fighting here, coming
         when clear", breaks off as soon as the warp drive allows, and goes. *(Changed after
         review: the warp drive refuses under fire, and in the session it was fighting when
         called.)*
       - An unnamed "help" is still the help list, so only the phrases with "me" count.
     - **"where are you":** where the wingman is, how far from the speaker, and what it's doing. A
       report, answered at once.
     - **"attack" with nothing near the wingman but something near its leader:** it goes to the
       leader's fight, not "no enemies in sight".
     - **A target's name alone** ("Wingman, Fang Hunter") attacks it. That needs the name to match
       exactly, or to be contained in a target's name.
   - **Target names after an attack word match loosely:**
     - By sound or a letter or two off: "Frank Hunter" is the Fang Hunter.
     - The bot says the target's real name.
     - *(Changed after review: the loose match only follows an attack word, so talk isn't taken
       for an attack; "Skink" already works by containment.)*
   - **Also updated:** the replies, the help text, the MCP order list and `WINGMAN.md`.
   - **Tests:** EditMode for the parsing and the loose match; the AI bot test for "attack my
     target" and "defend me".

7. **PR 7: the wingman's name** (item 3).
   - **The server:**
     - A new `name` message from a game renames its player. It's cleaned as the hello's name is,
       logged, and the roster goes out.
     - Some names are refused: one another player has; "all ships", "everyone" or "wingmen"; or
       one starting with a greeting word.
     - Every request gets a `renamed` reply to the one who asked: done, or why not. *(Changed after
       review: a `reject` ends the connection in the client.)*
     - `renamed` is one of the messages only the server sends, with its sample in
       `Server/protocol`.
     - An old server passes `name` on to the room, so games act only on `renamed` and the roster.
   - **The game:** `CoopClient.Rename` sends it and takes the new name from the roster.
   - **The bot:**
     - "your name is Ace", "call yourself Ace" and "rename to Ace" rename it.
     - Only its leader or the game's host may, as with the chat mode.
     - It answers in its new name.
     - The name is kept for its next start (with its chat mode, which is stored under the name it
       was started with). A kept name wins over `-name`. *(Changed after review: the MCP server
       starts it with `-name Wingman`, so the setting has to say which wins.)*
   - **The player's side:**
     - The co-op panel lists the AI wingmen with RENAME. It takes the new name in a text field and
       sends the order.
     - Windows' keyword phrases are rebuilt for the new name.
   - **Tests:** server tests for the rename, its refusals and the roster; EditMode for the
     parsing; the AI bot test renames it and orders it by the new name.

8. **PR 8: the wingman's choice of ore** (items 5, 6). *(Changed after review: split from moving
   ore, which is PR 9.)*
   - **"mine platinum", "mine for gold", "go after helium-3":**
     - The wingman mines the rocks richest in that mineral, in the field it's at or the nearest.
     - A mineral's name is matched (with symbols: "Pt", "He-3") before a field's name.
   - **"mine the most valuable ore", "mine the best rocks":** the rocks worth most per tonne in
     sensor range.
   - **Either choice can go with a field:** "mine gold at the Inner Belt".
   - **The choice is part of the order:**
     - A repeat of the same order still doesn't restart it.
     - The choice is shown in the doing line and the status.
   - **Tests:** EditMode for the parsing and the choice of rock; the AI bot test mines a mineral.

9. **PR 9: moving ore** (item 4).
   - **New messages, `cargo` and `cargook`,** carry items by the hold's own item ids: ore by
     mineral, and later goods and parts. *(Changed after review: item ids from the start, so PR 10
     reuses it.)*
   - **They work as the credits gift does:**
     - The sender takes the items out with `Unload` and holds them for 10 s.
     - The receiver refuses a message older than 6 s, and `Load`s what fits.
     - The receipt says what fitted, and the rest goes back.
     - Both games run the refund tick.
     - Only between a person and an AI player, in the same level, within 50 km.
   - **No duplicating through the save:** the player's game saves the hold at once when items go
     out and when any come back. *(Changed after review: the hold is saved 3 s after a change, so
     closing the game in those 3 s would keep the ore that had gone.)*
   - **The orders:**
     - "give me your ore", "give me the platinum", "give me 100 t of iron": the wingman sends it.
     - "take my ore", "take the iron": the player's own game reads its own outgoing order and sends
       the ore with it.
     - *(Changed after review: no request from the bot to the player's game, so nothing a bot sends
       can take ore out of a player's hold.)*
   - **The player's side:** the mining menu's hold rows get G (give to the wingman) beside J
     (eject), when a wingman is near. *(Changed after review: the services screen's Storage page is
     only there when docked at an industrial site; the mining menu is where the ore is handed over,
     out at the field.)*
   - **Tests:** EditMode for the parsing and the hold arithmetic; the AI bot test moves ore both
     ways and checks a partial receipt.

10. **PR 10: the wingman salvages** (item 2).
    - **The order:** "salvage", "scavenge", "salvage the wrecks", "salvage that wreck".
    - **The loop:**
      - Pick the nearest wreck in reach of the sensor (30 km), or the one named.
      - Warp to it if it's far, then fly in.
      - Turn the salvage beam on (in combat mode, as the beam needs).
      - When the wreck is empty, take the next one.
      - With nothing left, say so and go back to the order before.
    - **In a fight:** it stops the beam, fights, and goes back to salvaging, as mining does.
    - **A full hold:**
      - It takes the goods and parts to the nearest market and sells them.
      - The sale is pulled out of the services screen into a static one that both the screen and
        the bot use. *(Changed after review: the screen's sale needs the screen open; and parts
        kept for ever would fill the hold, so they're sold too.)*
      - "give me the parts" hands parts over first, with PR 9's message.
    - **Also:** the doing line and the status show what it's taking.
    - **Tests:** a PlayMode test for the loop against wrecks made in the test; the AI bot test
      orders salvage after a kill.

11. **PR 11: the sky** (item 1).
    - **A new sky shader,** `Carrier/SpaceSky`. It's one material on `RenderSettings.skybox`, so
      landing's swap and its test still work. It draws three layers:
      - **The far nebula:** `TC_Space`, with contrast and exposure (a curve on its brightness). It
        picks its own mip level from the view, as mipmaps on an equirect leave a seam at the wrap.
        *(Changed after review.)*
      - **The OrbitalBase masks:** the nebula clouds and bright knots, tinted, each at its own
        rotation and scale, and turned very slowly, so the layers part as the camera turns.
      - **Point stars:**
        - Generated per cell on the sky direction, so there's no texture to stretch. A few
          thousand of them.
        - Bright enough (HDR above 1) for bloom to make them crisp points.
        - They twinkle slightly, and a few bright ones are coloured.
    - **Reflections:** ships' reflections of the brighter sky are checked and capped if they're
      too bright. *(Changed after review.)*
    - **Per system:** the Nine Hells gets its own tints and rotation, so the two systems differ.
    - **Staging:** the masks are staged into `Assets/Carrier/Kit` by `stage_carrier_assets.py`
      (`EXTRA_TEXTURES`), never committed. Without them the shader draws the nebula and the stars
      alone.
    - **Tests:** a system-test close-up of the sky in each system, compared by eye before and
      after; the landing test still passes.

12. **PR 12: a microphone of one's own on Windows and Linux** (item 13). *(Changed after review:
    moved to last, as the largest and riskiest; the session's player was on the web, which PR 5
    covers.)*
    - **Vosk:** the small English model, and `libvosk` for Windows (with its libgcc, libstdc++ and
      winpthread DLLs) and Linux.
      - They're Apache-2.0, kept in LFS, with their licence beside the players as Flite's is.
      - The model goes beside the Windows and Linux players only, never into the web build.
        *(Changed after review: StreamingAssets would ship it to the web too.)*
    - **The pause menu's Microphone:**
      - **Windows:** "Windows default" (today's dictation, unchanged) and each of Unity's
        `Microphone.devices`.
      - **Linux:** the devices only, so voice commands come to Linux.
    - **With a device chosen:**
      - The device is kept recording while voice commands are on, and push-to-talk takes from a
        short ring buffer, so the first word isn't clipped. *(Changed after review: starting the
        microphone on the key is slow.)*
      - Vosk recognises it on a worker thread. Its grammar is the order phrases and the wingmen's
        names, plus "[unk]" for anything else.
      - The result goes through the same `VoiceInput.Heard`.
    - **Limit:** the small model knows only its own words. Place names it hasn't heard (Thalassa,
      Gilgal, Vrask) and unusual wingman names come out as "[unk]". Such orders are said to need
      typing, and "Windows default" is still the choice for them. *(Changed after review.)*
    - **Size:** the model loads on first use, not at start.
    - **Tests:** EditMode for the grammar, and for a recorded WAV of each order (made with the
      Piper voice the stock lines use) being recognised as that order.

**Then:** the shipping build (Windows, the web and Linux), as before: BuildAll, the three players,
the archives, and the web quit test on .61.

## Not done, and why
- **Deployment.** *(Changed after review.)*
  - The server's services were shut down on 25 Nov, at the user's request.
  - PR 7's server change, and the new bot for PRs 6 to 10, reach players only when they're
    deployed again. That isn't part of this plan unless asked for.
- **The Unreal packs' own space cubemaps** (SpaceColony2086, Wasteland and others) would need a
  re-export with an exporter that handles TextureCube. The sky in PR 11 doesn't depend on them, and
  they can be swapped in later as its far layer.
- **Windows' own dictation on a chosen device** isn't possible: Windows doesn't offer it. Vosk
  does that job (PR 12).
- **A known hole in gifts, credits and items alike.** A receipt that arrives after the 10 s refund,
  or is lost when the connection drops, leaves the gift both refunded and kept. It's rare and is
  left as it is.
