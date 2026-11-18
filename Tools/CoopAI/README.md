# Co-op AI: an MCP server for the AI wingman

`coop_ai_mcp.py` lets Claude, or any MCP client, fly a ship in a co-op game as another player.

The ship is a copy of the Star System game running in bot mode (`-coopbot`). It connects to the
co-op server like any player. Its built-in wingman pilot flies it, and it talks on the game's
comms channel. The bot game answers a small HTTP API on `127.0.0.1`, port 7781 by default.
This script turns that API into MCP tools over stdio. It needs Python 3.8+ and no packages.

## Adding it to Claude Code

```
claude mcp add coop-ai -- python "<repo>/Tools/CoopAI/coop_ai_mcp.py"
```

Two environment variables change where it looks. Add them with `-e NAME=value` before the `--`:

- `COOP_BOT_API` is the bot game's API (default `http://127.0.0.1:7781`). Use it to run more
  than one bot, each on its own `-botport`.
- `COOP_GAME_EXE` is the Star System build that `launch_bot` starts (default
  `<repo>/../Builds/StarSystem/StarSystem.exe`).

## The tools

| Tool | What it does |
| --- | --- |
| `launch_bot` | Starts the bot game (headless by default) and connects it to a server (`server` is `host:port`, default `127.0.0.1:7777`), with a `name`, a `ship` class, a `chatmode` and the `apikey` the server issued for it (`CoopServer --new-key Wingman --ai`; default `COOP_KEY` from the MCP server's environment). Give it 20 to 60 s to load. |
| `status` | The ship: hull, shield, crew, missiles, speed, warp, docked or landed. Also the pilot's order and what it is doing, what is near it (enemies first), and the players. |
| `read_messages` | The comms channel since the last read (or since `after`). Messages addressed to the bot are marked `to_me`. |
| `wait_messages` | The same, but if nothing new has been said, it waits up to `timeout` seconds (55 at most) for the next message. |
| `cargo` | The hold: each item and its amount, tonnes used of the capacity, how full it is, and the credits. |
| `give_credits` | Transfers credits from the bot's hold to a player (`amount`: a number, `all` or `half`; `to`: a name, or the last player to speak). |
| `say` | Sends a message to every player in the bot's level. |
| `order` | `follow [player]`, `hold`, `attack [target]`, `goto <place>`, `regroup` (the nearest player), `dock`, `mine [field]` (mine asteroids and sell the ore whenever the hold is full) or `sell` (the ore aboard, now). |
| `destinations` | The places to warp to, nearest first. |
| `get_chat_mode` | Who answers talk to the bot (`llm`, `personality` or `paused`), and `listener_seen_s`: the seconds since a listener last read the channel (-1 if never). |
| `set_chat_mode` | Switches it: `llm` (or `on`), `personality` (or `off`), `paused` (or `pause`), or `resume` (or `unpause`) to go back to the mode before the pause. |
| `stop_bot` | Closes the bot game that `launch_bot` started. |

Players can also order the bot themselves. A message that starts with its name, such as
`Wingman, follow me` or `@Wingman attack the corsair`, is an order it carries out and answers
by itself. Any other message is left for whoever reads the channel through this server. For
example, an assistant connected here can answer questions, or turn "can you cover the station
while I mine?" into `order goto`. Reading with `read_messages` or `wait_messages` tells the bot
that a listener is there. When nothing has read for 90 s, the bot's built-in personality answers
talk instead.

## The chat mode

The bot's chat mode decides who answers talk to it, that is, a message to it that isn't an order.
Orders work in every mode.

| Mode | Who answers talk |
| --- | --- |
| `llm` (the default) | Whoever reads the channel through this server. While nothing has read it for 90 s, the bot's built-in personality answers. |
| `personality` | The bot's built-in personality, always: the LLM is off. `read_messages` still works, but the bot never leaves talk for it. |
| `paused` | Nobody. The bot says it is standing by, at most once every 30 s. |

There are four ways to set it:

- The `set_chat_mode` tool, or `POST /chatmode {"mode": "..."}`. `GET /chatmode` reads it.
- A player in the game: `Wingman, chat on`, `chat off`, `chat pause` or `chat resume`, or
  `chat mode?` to ask. Only the bot's leader or the game's host may switch it. If it has neither,
  anyone may.
- `-chatmode llm|personality|paused` on the bot game's command line.
- The bot keeps the mode between runs, per bot name, in the player prefs of the machine it runs
  on. The command line flag wins over the saved mode.

`/status` and `/messages` (so `read_messages` and `wait_messages`) say the mode as `chat_mode`.
Answer talk only while it is `llm`. `listen.py` keeps listening in every mode. While the mode
isn't `llm`, it prints a line when the mode changes but doesn't print messages, so nothing wakes
the session to answer them.

**The built-in personality.** It is `Assets/Carrier/Scripts/Net/WingmanPersonality.cs`, and its
lines are in `Assets/Carrier/Resources/Data/wingman_dialogue.json`. It speaks in the same voice as
`WINGMAN.md`. It finds the topic by keywords: greetings, how it is, the last battle, the ship, the
hold and credits, where it is, the Vrask, the corsairs, home, the captain, jokes, thanks, goodbye,
and more. Its lines are filled in from its live state (`{hull}`, `{shield}`, `{near}`,
`{hostiles}`, `{credits}`, `{hold}`, `{leader}`, `{order}` and others). Some answers ask a
question with numbered choices, such as `(1) long (2) short`. The player's next line, a number or
a word, picks the branch. Branches can set flags that change later lines. Each player has a
conversation of their own, which is forgotten after 2 minutes of quiet, and it never says the
same line twice running. It only talks, and never carries out orders.

## Talking to the wingman from Claude Code

1. If the bot runs on a server, tunnel its API here: `ssh -N -L 7781:127.0.0.1:7781 root@<server>`.
2. `claude mcp add coop-ai -- python "<repo>/Tools/CoopAI/coop_ai_mcp.py"`.
3. Run `python Tools/CoopAI/listen.py` as a background monitor in the session. It prints one
   line per message addressed to the wingman (`--all`: every player message), which wakes the
   session to answer with `say`.
4. `WINGMAN.md` is the voice to answer in.

## Without MCP

The bot game's API is plain HTTP with JSON, so a script can use it directly:

```
StarSystem.exe -coopbot -connect 127.0.0.1:7777 -name Wingman -apikey <key> -botport 7781 -batchmode -nographics
curl http://127.0.0.1:7781/status
curl -X POST -d "{\"order\":\"follow\",\"target\":\"Ann\"}" http://127.0.0.1:7781/order
```

## Tests

- `Tools/Tests/test_coop_ai_mcp.py` tests this server without the game. It runs in the test
  runner's tools stage.
- `aibot_test.py` is the live test, which the runner's `aibot` stage runs: a test player, the
  bot game and a server.
