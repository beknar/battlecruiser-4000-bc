# Improvements 8: the plan

This plan covers `docs/improvements8.txt`: seven changes to the game, made in eleven PRs, done in
order. After the last one is merged, the shipping build is made.

1. In the paint screen, the logo and lettering can be moved around the hull.
2. The mine order to the wingman is sometimes late, and sometimes goes away and comes back.
3. In the trade screen, the mouse wheel scrolls the list and also zooms the camera.
4. A separate screen to buy, and one to sell, in the services screen.
5. Quitting the web game throws errors in the browser console.
6. Microphone access and voice commands for the wingman.
7. The smallest possible voice for the wingman's replies: a hybrid of stock phrases recorded
   ahead of time and a live voice for everything else.

The plan was reviewed against the code before any work started. "Changed after review" marks what
that review changed.

**Assumptions:**
- "The wingman" is the co-op AI wingman: the bot game started with `-coopbot` that joins as a
  player and takes typed chat orders (`CoopBot`). The player's own built ships (`FleetDirector`)
  have no mine order, so the bug can only be the bot's.
- "A separate buy/sell screen for services" (changed after review):
  - Services is already a mode of play here (`ModeOfPlay.Services`), and the station panel is its
    screen.
  - That screen's TRADE page puts what can be sold and what can be bought in one list.
  - So the request is read as splitting that list into a SELL page and a BUY page. It is not read
    as charging for repairs, which nobody asked for.
- "The smallest possible TTS" (changed after review):
  - On desktop, the smallest engine the game can carry, the same on Windows and Linux, so the
    wingman has one voice.
  - On the web, the browser's own voice, which adds nothing to the download.
- The last line of the list ("If there are opportunities to do parallel actions.") is read as:
  run work in parallel where it can be. See "Parallel work" below.

## What the code does today

| Item | Today |
|---|---|
| Paint | `StartMenu.Paint.cs` draws the screen in IMGUI. The logo and lettering aren't decals. The hull shader (`ShipPaintPatterns.hlsl`, `DecalMask`) draws them on both sides, at a place fixed for each hull class (`ShipPaint.Placement`, a table of fractions of the hull's bounds). `PaintScheme` has no placement fields. Its code string (`1.pattern.scale...TEXT`, version 1) is saved per class and sent to co-op peers. |
| Mine order | The player types "Wingman, mine" in chat. The bot parses it (`CoopBot.ParseOrder`), and its `Wingman` pilot runs ToField, Scan, Approach, Cut, ToMarket, Docking, Sell and Leave (`Wingman.Mining.cs`). The player sees only chat lines (the last six, fading after about 20 s, all shown again whenever chat is opened) and the one-line notice. |
| Trade wheel | `OrbitCamera` zoomed on the wheel unless the pause menu or the system map was up, and didn't check `IndustryPanel.IsOpen`. The panel's `Update` read the same wheel to move the selection. (Fixed in PR 1.) |
| Trade page | One list: "sell all the ore", then a `*sell:` row for everything aboard, then, at a market, a `*buy:` row for every refined material, part and mineral (`IndustryPanel.BuildRows`). |
| Web quit | Quit in the menu calls `CarrierPageQuit`, which calls `instance.Quit()` from inside the running frame. The page then only hides the canvas. `Quit()` sets a flag, and the engine tears itself down in place. Its own main loop and mouse, key and focus listeners are still attached afterwards. The next frame gives "memory access out of bounds", and the next mouse event gives "null function". The game's own JS never calls into C#. The page doesn't set `autoSyncPersistentDataPath`, and nothing calls `syncfs`, so files the game writes on the web may never reach the browser's storage. |
| Voice in | Nothing. No `Microphone`, speech recogniser or Web Speech code anywhere. |
| Voice out | Nothing. The wingman is text only: about 150 personality lines in `wingman_dialogue.json` (54 with blanks), order replies built in `CoopBot.cs` with names, places and numbers, and free text from an LLM. The game has no audio mixer, and Unity's audio filters don't run on the web. |

## The PRs

Each PR goes like this:
1. Branch off main.
2. Write the code and compile it.
3. Run the unit tests, and the in-game test where there is one (windowed, one run at a time).
4. Review the diff, fix what the review finds, and merge.

README and the Harness README are updated in the PR that changes what they describe.

### PR 1: The wheel in the trade screen (#128)

- `OrbitCamera` takes no wheel input while the station panel is open, or on the frame it shuts.
  The right button still orbits, so the player can look the ship over.
- The panel's list keeps moving on the wheel from anywhere on the screen. Its review found that
  limiting it to the panel left dead bands round the panel where the wheel did nothing.
- **Test:** `CameraWheelTests` (PlayMode, with the Input System's test fixture). No zoom under the
  panel, and a zoom once it's shut. It fails with the camera fix taken out.

### PR 2: Quitting the web game

The game stops tearing the engine down inside a page that stays open. The page leaves instead.
- **The save comes first:**
  - `GameMenu.Quit` saves the industry and `PlayerPrefs` before it calls the page.
  - The page config sets `autoSyncPersistentDataPath`, Unity's own way to keep
    `persistentDataPath` in the browser's storage (changed after review).
  - `CarrierPageQuit` then waits for a final `FS.syncfs` before the page leaves. If a sync is
    already running, it tries again shortly after. It never waits more than 2 s.
- **The quit is put off until after the frame** (`setTimeout`), so nothing is torn down inside a
  running frame.
- **`index.html`'s `carrierQuit`:**
  - It stops the main loop (`Module.pauseMainLoop`).
  - It replaces the page with the same address plus `ended`. That page shows "Thanks for
    playing" and never starts the engine.
  - "Play again" goes back to the address without `ended`.
  - `instance.Quit()` is no longer called, so no listener is ever left pointing at a freed engine.
- **The test** (changed after review):
  - The web self-test never quits, so a new `?quittest` switch flies for a few seconds with
    mouse moves and clicks, then quits through the pause menu, as a player would.
  - A new `Tools/WebTest/quittest.sh`, modelled on `padtest.sh`, fails if the browser log shows
    "memory access out of bounds" or "null function" at any time in the session, or if the ended
    page doesn't load.
  - It also checks that a file written before the quit is still there after a reload.
  - The browser runs headless on a sim host (.61), not on the laptop. No host has Chrome, so
    Chrome for Testing's headless shell is unpacked in storm's home there, with nothing installed
    system-wide.

### PR 3a: The wingman's orders: phrasing, and orders given twice

- **The order given twice.**
  - The LLM listener sees the orders the bot already carried out, and can issue them again
    through `/order`. A second `Mine()` restarts the whole cycle and zeroes the sale counters.
  - Every message the bot handled as an order is marked `handled: true` in `/messages`.
    `listen.py` and the MCP brief skip those.
  - `Mine()` on the field it's already mining is a no-op, apart from its reply.
- **Phrasing.**
  - The parser takes a leading "hey", "ok" or "okay" before the name.
  - It drops "please", "can you", "could you", "would you" and "go".
  - After "mine" it ignores words that aren't a place, so "mine some asteroids please" mines the
    nearest field. A field is looked up only when the words name one.
  - This also helps the voice commands (PR 6).
- **Tests:**
  - EditMode `CoopChatTests` cover the new phrasing and the `handled` flag.
  - `aibot_test.py` checks that `handled` lines are skipped.

### PR 3b: The wingman's orders: the mining pilot (changed after review)

- **The field it never reaches** (found in review).
  - ToField counts the bot as arrived when `Places.At(me)` is the field. `Places.At` prefers a
    planet or moon within its reach (60 km or more).
  - So for a field near a world, the bot never "arrives". It warps again, or the drive refuses
    ("already there"), and each refusal holds warps for 20 s. The bot keeps leaving and coming
    back.
  - Arrival becomes a distance check against the field itself.
  - Every asteroid field in both systems is checked for this in a test.
- **Fight flicker.**
  - While mining, the bot looks for an enemy within 6 km every second. It stops mining when one
    is seen and starts again from the scan when it's gone. Sensor range swings with jamming, so a
    ship near the edge turns the fight on and off.
  - The fix adds hysteresis: a fight starts inside 6 km, and ends only after 8 s with nothing
    inside 7.5 km.
  - The fight's other exit, the target lost (`Gone(Target)`), gets the same 8 s rule.
  - A hostile installation counts only if it can reach the bot, so a hostile base near a field
    doesn't keep the bot "defending" forever.
  - After a fight the approach goes back to the same rock, if it's still there, rather than the
    nearest in the old scan.
  - The bot says "back to mining" when it goes back.
- **Wandering.** Rocks skipped as uncuttable are cleared on every new scan, not only at the next
  field, so the bot doesn't wander saying "looking for deposits".
- **Tests:** PlayMode tests for a mining wingman:
  - with an enemy moving in and out of range: one fight, not a flicker, and back to the same rock;
  - arriving at a field that sits near a moon.

### PR 3c: The wingman's order on the HUD (changed after review)

- The bot's player state gets a `doing` field, for example "mining the Outer Drift: 34 t
  aboard". It's sent when it changes, not with every update.
- A line under the wingman's name on the HUD shows it, so the order doesn't go away when its chat
  line fades or scrolls off.
- Older peers ignore the field, and the server passes messages on untouched.
- **Test:** the `coop` stage checks that the line shows what the bot is doing.

### PR 4: The BUY and SELL pages (changed after review)

- The station panel's TRADE page becomes two pages, SELL and BUY, beside each other in the tab
  strip.
- **SELL:** "sell all the ore" and a row for everything aboard. This is what's sold, so the list
  is what the wheel scrolls (PR 1).
- **BUY:**
  - The market's refined materials, parts and minerals, each with its price and what the hold can
    afford.
  - Shown only at a market. A refinery that isn't a market has SELL only, as today.
- Keys, the pad, the page hints and the details pane follow the split. Enter sells or buys one,
  and Shift+Enter sells all or buys ten, as today.
- **Tests:** `SystemTest.Services` (the services screen test) checks both pages, and that a
  refinery has no BUY page. Its baseline is blessed.

### PR 5: Moving the logo and lettering

- `PaintScheme` gets placement for the logo and for the lettering:
  - along the hull and up it, as fractions of the hull, added to the class's default place;
  - size, as a percentage;
  - rotation;
  - which side: both sides, port only or starboard only.
- **The code string** (changed after review):
  - A scheme with its decals where they've always been still writes version 1, so the presets,
    the existing tests and older peers see no change.
  - A moved decal writes version 2, with the placement fields before TEXT. `TryParse` reads both,
    and its limit on the number of parts goes up.
  - A peer on an older build shows the factory finish for a v2 code, as it does today for any
    code it doesn't know.
- **The shader** (`DecalMask`):
  - It rotates the logo and the lettering about their centres.
  - It gates each one on the side.
  - `ShipPaint.Decals` adds the player's offsets to the class place, still clamped inside the
    hull's bounds, and passes rotation and side through a new `_DecalXform` vector.
  - The lettering still runs along the side. The top and belly aren't possible: the projection
    runs across the ship, and changing that is a bigger shader job.
- **The UI:**
  - The right-hand column is full, so the paint screen gets a PLACE page beside its colours.
    Moving to it is a tab, as the station panel's pages are.
  - The page has sliders for each field, with keyboard and pad support, and a reset.
  - On the preview, the left mouse button on the hull drags the selected decal, logo or
    lettering. The ship turns with the right button while the PLACE page is up.
  - A drag maps the mouse onto the side of the hull's bounds, so no collider is needed.
  - The preview updates live, as it does for colours.
- **Tests:** `ShipPaintTests`:
  - a v2 code round trip, v1 still read and still written for unmoved decals, and clamping;
  - a PlayMode check that a moved logo lands at the new place on the hull, read back from the
    material's `_LogoRect`.

### PR 6: Voice commands

- **Microphone access.**
  - A pause-menu setting: Voice commands, off or push-to-talk. Off by default, and saved.
  - Push-to-talk is held:
    - On the keyboard, a key that's free in every mode, chosen in the PR.
    - On the pad, an input that isn't part of a chord. The View button is out, because it's the
      chord modifier for half the pad's controls (changed after review).
    - Both show on the controls page.
  - The first press asks for the microphone:
    - On Windows, if the system's privacy setting blocks it, a notice says where to allow it.
    - On the web, the browser's permission prompt is the ask. A page served over plain http
      can't use the microphone, and the setting says so (README already says to serve over
      https).
- **Recognition**, on the player's own machine:
  - **Windows:** `UnityEngine.Windows.Speech`, which is built into Windows 10 and 11 and adds
    nothing to the download. It's guarded by `UNITY_STANDALONE_WIN`.
    - `DictationRecognizer` while the key is held, for free speech. It sends the audio to
      Microsoft to recognise it, so the setting says so (changed after review).
    - The keyword system is shut down before dictation starts, since the two can't run together.
    - "Listening" shows only once dictation is running.
    - If dictation isn't available (the "Online speech recognition" setting is off), a
      `KeywordRecognizer` takes over. It's offline. Its phrases are built from the order
      vocabulary, the bot's name, and the places and asteroid fields of the system the player is
      in.
  - **Web:** the browser's `SpeechRecognition`, through a new `CarrierVoice.jslib`.
    - C# polls it, as `CoopSocket` does, so nothing calls into C# from JS. That matters after
      PR 2.
    - Chrome and Edge have it. In Firefox and Safari the setting says voice isn't supported in
      this browser.
    - Chrome sends the audio to Google to recognise it, so the setting says so.
  - **Linux:** no recogniser. The setting says voice isn't supported there (changed after
    review).
- **Turning speech into an order** (`VoiceCommand.ToChat`, pure C#, unit tested):
  - It drops filler words.
  - It turns spoken numbers into figures ("two thousand credits").
  - It puts the bot's name at the front if the player didn't say it. With more than one bot, it
    uses the one the player last spoke to.
  - The result goes out through the same `CoopChat.Say` as typed chat, so the bot needs no
    changes.
  - What was heard shows in the chat line first, for half a second, so the player sees what went.
  - Nothing is sent if the recogniser's confidence is low. A notice says "didn't catch that".
- **A HUD mark** shows while the mic is open, and another while it's working on what it heard.
- **Tests:**
  - EditMode tests for `VoiceCommand.ToChat` over the whole order vocabulary, filler and numbers.
  - A `-voicetest` switch feeds heard phrases through the same path, so the in-game test runs
    without a microphone.
  - The recognisers themselves can only be tried by speaking to them. That is left for the user
    to try, and the PR says so.

### PR 7a: The wingman's replies as data (changed after review)

This is a refactor on its own, so the voice work after it has a fixed list of what the wingman
can say.
- The order replies, refusals and reports are moved from string literals in `CoopBot.cs` into
  `Resources/Data/wingman_replies.json`.
  - Each is a template with named blanks, in the style of `wingman_dialogue.json`.
  - `CoopBot` fills them through the same blank filler the personality uses.
- What the bot says doesn't change. Every existing `CoopChatTests` reply check still passes.
- **New test:** every reply key `CoopBot` uses is in the file, and every template's blanks are
  ones the filler knows.

### PR 7b: The wingman's voice, part one: stock phrases

- `WingmanVoice`, on the player's client, speaks every chat line that comes from a bot:
  - It uses the clean text, not the garbled text shown when jamming is on.
  - It strips the "Name:" prefix.
  - Lines queue, so they don't talk over each other. The queue drops a line older than 10 s
    unless it's an order reply.
  - A pause-menu setting turns the voice on or off, with a volume. On by default.
- **Stock phrases** are recorded ahead of time with Piper and kept in
  `Resources/WingmanVoice/` as small Ogg files, with an index from the normalised text to its
  clip:
  - every personality line that has no blanks;
  - every reply template from PR 7a that has no blanks;
  - the replies whose only blank is a place, recorded for each place and field in both systems.
- **The voice model** must be one whose licence lets the recordings ship in a public repo and
  game. The first choice is `en_US-libritts_r-medium` (its data is CC BY 4.0). Its model card is
  checked before any use, and the credit goes in README.
- **The radio sound** (band limit, a little grit, a squelch at each end) is put into the files
  when they're made, because Unity's audio filters don't run on the web.
- **The recording tool:** `Tools/Voice/render_stock.py`.
  - It reads `wingman_dialogue.json` and `wingman_replies.json`, writes the clips and the index,
    and fails if a line in the data has no clip.
  - It runs on the laptop's CPU, with no Unity and no game running.
- **A line that matches no clip** is shown as text only, until PR 8.
- **Tests:**
  - EditMode: the text is normalised the same way in C# and in the tool, checked against a shared
    list of cases; every data line has a clip; the index loads.
  - PlayMode: a bot chat line plays its clip, and two lines queue.

### PR 8: The wingman's voice, part two: the live voice

- **Desktop, Windows and Linux** (changed after review):
  - The engine is **Flite**, from CMU, built as a small native library with the `cmu_us_kal16`
    voice. Its licence is BSD-style, so it can ship, and it is a few MB on disk.
  - It's the same on both platforms, so the live voice sounds the same everywhere.
  - It's built with a portable `zig cc`: a Windows DLL, and a Linux `.so` targeting an old glibc,
    so it loads on older distributions. Nothing is installed on the laptop or the servers.
    - The build script and a pinned Flite version are kept in `Tools/Voice/flite/`.
    - The built libraries are committed under `Assets/Carrier/Plugins/`, with Flite's licence
      beside them.
    - The editor locks a native library once it's loaded, so a rebuilt library needs Unity
      restarted.
  - C# calls it through a small C wrapper: text in, 16-bit samples out.
  - The call runs on a worker thread, a sentence at a time, so the first words start in about a
    tenth of a second.
  - The same radio sound is put on in C#, on the samples, before they're played.
- **Web:** the browser's own `speechSynthesis`, through `CarrierVoice.jslib`. This adds nothing
  to the download.
  - It can't go through Unity's audio, so it has no radio sound. A deeper English voice is chosen
    where the browser has one.
  - If the browser has no voice, the line stays text only.
- **What the live voice speaks:**
  - personality lines with blanks;
  - order replies with names and numbers;
  - the LLM's free text, sentence by sentence as each line arrives.
  - It never speaks a line a stock clip covers.
- **Tests:**
  - EditMode: the wrapper turns a sentence into samples of a sensible length.
  - PlayMode: a line with no clip is spoken live and joins the queue behind a stock clip.
  - On the web, the `quittest` page from PR 2 also checks that `speechSynthesis` was called
    (headless Chrome has the API even with no voice installed).

### After PR 8: the shipping build

`BuildAll` runs in batch mode with `-nographics`, giving:
- `Builds/StarSystem.zip` (Windows)
- `Builds/StarSystemWeb.zip`
- `Builds/StarSystemLinux.tar.gz`

These are the files of the last release. The co-op server is rebuilt if its code changed.

Nothing is published or deployed to the server unless asked. The server's services have been
stopped since 23 November. So the wingman fixes reach the player only once a release with the
new bot is deployed (changed after review).

## Parallel work

- **Flite's native build** (PR 8) uses no Unity. It started with the plan, with `zig` in the
  scratchpad. A failure there is known early.
- **Recording the personality clips** (PR 7b) needs only Piper and `wingman_dialogue.json`, so it
  can start at any time. The reply clips wait for PR 7a (changed after review).
- **PR 2** is mostly the page and the jslib, so it's written while other PRs are tested. Only
  its build and its web test go in the serial queue.
- **Reviews:** each PR's diff is reviewed by a separate agent while the next PR's code starts.
- **The web test** (PR 2 and PR 8) runs headless Chrome on a sim host, alongside the Unity work
  on the laptop.
- **Kept serial:** only one Unity editor or game process runs on the laptop at a time (the GPU
  rule), so building and in-game tests stay serial, and the PRs merge in order.

## Risks

- **Recognition quality** depends on the player's machine and browser, and can't be tested here
  with a real voice. The keyword fallback and the "didn't catch that" notice keep a misheard
  command from doing something unexpected.
- **Two voices:** stock clips (Piper) and live speech (Flite or the browser) sound different.
  The radio sound brings them closer on desktop. A line is always spoken whole by one voice,
  never stitched from both.
- **The paint code:** a moved decal writes version 2. Peers on an older build show the factory
  finish for those ships until they update.
- **Flite on Windows** is built with a cross-compiler and can't be tried in Unity until the
  editor loads it. If it fails, PR 8 says so, and Windows players get text only for lines with no
  stock clip until it's fixed.
- **The mine-order symptoms** were seen on the deployed 22 November build. Each cause found in
  the code is fixed and tested, but whether those were the ones the user hit can only be checked
  in play after a deploy.
