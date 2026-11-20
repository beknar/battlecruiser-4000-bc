# Improvements 6: the plan

This plan covers the list in `docs/improvements6.txt`. It splits the work into eight PRs (PR 6
is in two parts), done in order. After the last one is merged, the shipping build is made.

The plan was reviewed against the code before any work started. "Changed after review" below
marks what that review changed.

**Done before the plan:** the web game, the co-op server and the AI wingman on 192.168.1.58 are
shut down. nginx, coopserver and coopbot are stopped and not enabled at boot, and the wingman
listener is off.

**Not in the plan:** the line "there is no" in the list is unfinished, so it has nothing to act
on. It's raised with the user.

## Answers to the questions in the list

**How is armour shown on Vrask ships?** It isn't shown. Each Vrask ship has an `Armor` component
with a rating and plating, for example a Snare has 0.32 and 900 (`Data/vrask.json`). Each hit
that gets through the shield is cut by `rating x integrity x (1 - pierce)`, and the plating wears
down by what it stopped (`Scripts/Armor.cs`). Penetrator rounds go through it.
- **Recovery:** armour never comes back during a fight. It comes back only on docking, landing at
  a repair base, or respawning. So in effect it is already ablative.
- **Visuals:** nothing shows it. No plates come off, and nothing marks a hit on the armour.
- **PR 7** makes it look ablative: plates blow outwards as they're hit. It also adds the missing
  repair out of combat for Vrask ships.

**Can the wingman reach the player on a planet?** No.
- A landed player is hidden from other players' games (`RemotePlayer.Hidden`), and the surface is
  a separate world for each player. So the wingman loses its leader and stops in space, saying
  "waiting for ...".
- It also never follows a player into another star system.
- **PR 1** has it fly to the planet's high orbit and wait there, and say so on comms. Landing with
  the player isn't possible, because co-op surfaces aren't shared.

**Why is salvage of a targeted wreck refused in combat mode?** A wreck is never "the target".
- When a ship dies, the dead ship stays targeted, and its wreck is a separate object. The target
  list holds only ships and installations.
- Z salvages the wreck chosen in the combat menu, or else the nearest wreck within 20 km. It
  never looks at the target. So:
  - Z with the dead ship targeted can salvage a different wreck, or say "No wreck near" when the
    wreck is 20 to 30 km away. The combat menu lists wrecks out to 30 km.
  - Z on another wreck during a salvage flight cancels the flight instead of changing course.
  - Enter on a wreck row only chooses it. (On the pad, X on a wreck row already flies there and
    salvages it.)
- **Other refusals** come from the hold being full, the ship having no hold, or the ship being
  held. The ship is held during warp, docking, landing, chat, the system map or the industry
  panel.
- **PR 5** fixes these.

## The PRs

Each PR goes: branch off main, code, compile, test in the game (windowed, one run at a time),
review the diff, fix what the review finds, merge.

### PR 1: Co-op comms and the wingman on planets

- **The whole reply on the comms line:**
  - `CoopChat.OnGUI` wraps each message over as many lines as it needs, measured with
    `CalcHeight`. Today one clipped line holds about 60 characters.
  - It scales with the player's text size (`Hud.S`).
  - The comms box grows upward and keeps showing the last 6 messages.
- **No doubled name:** the bot's replies are said as `"Wingman: ..."` and shown as
  `"Wingman: Wingman: ..."`. The bot stops adding its own name.
  - The 240-character limit is raised to 400 in the game, the MCP tool and WINGMAN.md, so a
    full LLM reply fits.
- **The comms box height:** six long messages could be taller than the screen, so the box is
  capped at a third of the screen's height, dropping the oldest first. It also blocks clicks on
  ships, so it must stay small.
- **The notice toast:** it wraps too, so a long message doesn't run off the screen. It stays up
  longer for a long message.
- **The bot's name stays on the wire** (changed after review). `Tools/CoopAI/aibot_test.py`
  finds the bot's answers by their "Wingman:" start, so the game shows a message that starts with
  its sender's name without adding the name again, instead of the bot dropping it.
- **A landed leader:**
  - The co-op ship state already carries the body the player is on (`surf`). The wingman uses it
    to warp to that body's High Orbit and wait there. It keeps its leader while it waits (the
    Goto order would drop them), and tells landing apart from warp by `RemotePlayer.Surface`.
    A body with no High Orbit (an asteroid field) has it wait at the body itself.
  - It says once "<name> has landed on <body>. Holding in high orbit".
  - It picks the player up again when they take off.
  - `status` reports it.
- **Tests:** edit-mode tests for the waiting place (High Orbit, or the body itself), a
  330-character reply kept whole, and the name shown once.

### PR 2: The keyboard help screen

The key help in `ShipHud` is now one block of text, several bindings to a line with three spaces
between them.
- **New layout:**
  - A heading for each section: the current mode, then General.
  - One binding to a row: the key in a coloured "key cap" column, then the action, aligned.
  - Two columns when the screen is wide enough; it wraps to one column otherwise.
  - A maximum height, with the general section folding first.
- **The pad help** uses the same layout.
- `HelpRect` is kept accurate, so other panels still stay clear of it.
- `HelpText(pad)` stays a flat string, because `SystemTest.Modes` checks it for each mode.
- **Tests:** the HUD self-test checks that the help fits on screen above the console, at the one
  windowed size the test runs at. Every binding in the old text must still be there.

### PR 3: Pan and zoom on the system map

- **Mouse:** the wheel zooms about the cursor (from the whole system down to about 20 km across).
  Dragging with the right or middle button pans.
- **Keys:** `+`/`-` zoom, and Home resets. WASD and the arrows already move the selection, so
  they're left alone.
- **Pad:** the right stick pans, and the triggers zoom.
- **Drawing:**
  - Everything is clipped to the map's square (`GUI.BeginGroup`).
  - The orbit circles get more segments when zoomed in.
  - Labels thin out by distance on screen, not by a fixed size.
- **Selection:** Tab or a stick move to a place off-screen pans to keep it in view.
- **The camera behind the map** (changed after review): `OrbitCamera` reads right-drag, the
  wheel, the right stick and the triggers whenever no game menu is open, so it would turn and zoom
  behind the map. It stops while the map is open.
- **Precision:** zoomed in to 20 km, positions are about 1e7 px from the star. So everything is
  drawn relative to the view's centre, and only the visible arc of an orbit is drawn.
- **Tests:** zooming keeps the point under the cursor fixed. Selecting a place off-screen brings
  it into view. Home resets the view.

### PR 4: The paint screen

- **Turning the ship:** drag on the picture to turn it (yaw and pitch). The wheel zooms in and
  out. A double-click puts it back on the turntable.
  - The automatic turn stops while dragging, and starts again 4 s later (unscaled time: the
    paint screen runs with the game paused).
  - On the pad, the right stick turns the ship and the triggers zoom.
- **A factory finish with a logo and lettering:**
  - The factory finish now means no paint scheme at all, so a logo and lettering can't go on it.
  - A new "factory" pattern in `ShipPaint.shader` keeps the hull's own base colour and texture,
    and draws only the decals. The shader already has the base map times the base colour before
    the paint goes on.
  - In the paint code it is pattern -1 (changed after review). Patterns are clamped to 0..99
    and the code's version is checked, so the version stays the same, the clamp lets -1 through,
    and random choices and the pattern row leave it out. An older co-op game shows pattern 0.
  - The paint shader has no emission or metallic-map variants, so a hull that uses them looks a
    little different under the factory pattern. The hull materials are checked, and what's
    missing is copied over or noted.
  - With Factory finish chosen, the logo and lettering choices stay open:
    - With a logo or lettering chosen, it saves as a scheme with the factory pattern.
    - Without either, it stays the plain factory finish, as before.
  - The save code keeps working: old codes load the same.
- **Tests:**
  - The paint code makes a round trip with the factory pattern.
  - A factory finish with lettering puts the decal on the hull material.
  - Dragging changes the preview's yaw.

### PR 5: Salvage and landing at the Inner Belt

- **Salvage:**
  - Z with a destroyed ship targeted salvages that ship's wreck (`Wreck.From`), ahead of the
    nearest one.
  - The 20 km limit becomes 30 km, the same as the menu.
  - In the combat menu, Z or Enter on a wreck row salvages it, flying there first if needed.
  - Z on another wreck during a flight changes course.
  - Every silent refusal posts a notice that says why.
- **Landing next to a wreck** (changed after review): landing on an asteroid field is a
  documented feature (the Jovara Trojans ground battle is reached that way), so it stays.
  - When a wreck is targeted, chosen or in reach, K tells you that Z salvages it, and doesn't
    land.
  - K names the body it is about to land on before the fade ("Landing on the Inner Belt").
- **Tests:**
  - Destroy a ship, keep it targeted, press Z: its own wreck is salvaged, not a nearer one.
  - K at the Inner Belt with a wreck in reach doesn't land, and says Z salvages it.

### PR 6: Every Nine Hells place has what it should

The audit of every Nine Hells destination found that the High Orbit and Reach points are
navigation landmarks, made when the game starts. They're empty by design, in both systems.
**The Forge has nothing but a smuggler patrol 660 km out.** The work is in two parts.

#### PR 6a: Landmarks and hostile flags

- **The Forge High Orbit:** a smugglers' derelict waits there to be salvaged, made when the
  level starts like the landmark itself. Its cargo is what the Forge smugglers run. The Forge
  smugglers' patrol stays as it is, so the patrol count the tests check stays 7.
- **Landmarks:**
  - The map and navigation text for a landmark say what it is, for example "a parking orbit over
    the pole". Anything at it or within its range is listed.
  - Gas giants (Rephaim, Gilgal, and Jovara in the Testing System) also get the Equatorial Orbit
    the README promises. Landing on a gas giant's cloud tops is a documented feature, so its
    Reach sites stay (changed after review).
- **Bands that mark the wrong place hostile:**
  - A band lying in wait no longer flags the body or station it waits by as hostile. That flag
    turned Paradise's and Anak's surfaces into pirate worlds, and marked Rephaim Fuel Station red
    on the map. The Testing System's Vrask raiders (`CarrierBuilder.Vrask.cs`) do the same.
  - The warp point gets a separate "danger near" mark instead. The system map, the navigation
    list and the wingman's `/destinations` show it, so the warnings stay.
  - Only `hostile` turns a surface into a pirate world. It means the body itself is held.
- **Tests:**
  - `SystemTest.NineHells`: a friendly body or station is not flagged hostile, the bands' danger
    marks are there, and The Forge High Orbit has its derelict within its range.
  - `SystemTest.Landing`: the hostile-flag check (line 132) is updated to match.
  - The Nine Hells baseline is updated if the warp-point count changes.

#### PR 6b: Things not where they should be

- **The stream bands:** the Havilah and Sheba bands sit in the stream. Today they're 10 km to one
  side of a stream 1.8 km wide.
- **The Outer Belt:** its easy band moves clear of the Vrask brood, so the easy fight doesn't
  pull in the boss.
- **The Gilgal picket:** it is placed in the ring, as its description says.
- **Bastion and the K'rzyn satellites:** they no longer vanish silently if an industry prefab is
  missing; the build logs it.
- **Kept as they are** (changed after review):
  - Bastion stays at L4. At this scale L1 is about 12 km from K'rzyn's centre, inside the planet,
    and L1 and L2 are equally close anyway. Its description says L4.
  - Deep Watch stays on the night side. Nineveh turns, so a station built over the observatory
    would drift off it.
- **Tests:** `SystemTest.NineHells` checks that each moved band is where it should be.

### PR 7: Ablative armour on Vrask ships

- **Plates:**
  - A Vrask hull gets 8 to 16 armour plates by class, placed over the hull's bounds. They're
    made from the hull mesh's own material, slightly darker.
  - As the armour wears, plates blow off one by one: a flash, the plate flying outwards,
    tumbling, with a few sparks. Which plate goes depends on where the hit landed.
  - Once a section's plate is gone, that's where it shows.
- **Mechanics:** the damage maths stays as now. Plating wears by what it stops, and never comes
  back in a fight.
  - The one change: armour comes back only out of combat. That means docking, landing at a
    repair base, respawning, or, for the Vrask, 90 s with no hit and no enemy within 15 km.
    Then it comes back at 2% a second, and the plates come back with it.
- **Co-op** (changed after review): `Armor.NetSetIntegrity` exists but nothing calls it, so
  armour isn't sent today. The owner of a Vrask ship sends its armour integrity with its state,
  and the other games set it and shed the same plates.
- **The plates have no colliders,** and aren't on any target layer, so they don't absorb shots.
  They're pooled and capped, because the web build pays for every renderer.
- **Simulations, on the four hosts** (this laptop, .35, .61, .58):
  - Run the `-simduo` study against the four Sheol picket mixes, before and after the change, 6
    times each.
  - The armour maths isn't changed, so the results should match within noise. A band that
    withdraws could meet the quiet time in a long battle, so the sims also show whether the
    repair ever happens in one.
  - Any difference beyond noise is looked into before merging.
  - The before/after results go into `docs/frigates-section.md`.
- **Tests:**
  - Hits that wear the armour shed plates.
  - The plates come back only after the quiet time.
  - The plates don't come back during a fight.

## What runs in parallel

- **The before sims** run on the three remote hosts while PRs 1 to 6 are being written (started
  before any change). They use
  a Linux sim build made from main before any change. The laptop joins for the after sims.
- **Reviews:** each PR's code review runs in the background while the next PR is started. A fix
  the review asks for lands before that PR is merged.
- **One thing at a time:** builds and in-game tests run on the laptop, one at a time. Two Unity
  processes don't share the project, and only one game window runs on the laptop.

## After the last merge

Build the shipping binaries from main: BuildAll, then StarSystem for Windows, web and Linux,
then zip them in `E:\code\claude_unity\Builds`. They aren't deployed, because the services on
192.168.1.58 are shut down as asked.
