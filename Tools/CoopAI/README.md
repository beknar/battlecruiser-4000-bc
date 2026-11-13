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
| `launch_bot` | Starts the bot game (headless by default) and connects it to a server (`server` is `host:port`, default `127.0.0.1:7777`), with a `name` and a `ship` class. Give it 20 to 60 s to load. |
| `status` | The ship: hull, shield, crew, missiles, speed, warp, docked or landed. Also the pilot's order and what it is doing, what is near it (enemies first), and the players. |
| `read_messages` | The comms channel since the last read (or since `after`). Messages addressed to the bot are marked `to_me`. |
| `say` | Sends a message to every player in the bot's level. |
| `order` | `follow [player]`, `hold`, `attack [target]`, `goto <place>`, `regroup` (the nearest player) or `dock`. |
| `destinations` | The places to warp to, nearest first. |
| `stop_bot` | Closes the bot game that `launch_bot` started. |

Players can also order the bot themselves. A message that starts with its name, such as
`Wingman, follow me` or `@Wingman attack the corsair`, is an order it carries out and answers
by itself. Any other message is left for whoever reads the channel through this server. For
example, an assistant connected here can answer questions, or turn "can you cover the station
while I mine?" into `order goto`.

## Without MCP

The bot game's API is plain HTTP with JSON, so a script can use it directly:

```
StarSystem.exe -coopbot -connect 127.0.0.1:7777 -name Wingman -botport 7781 -batchmode -nographics
curl http://127.0.0.1:7781/status
curl -X POST -d "{\"order\":\"follow\",\"target\":\"Ann\"}" http://127.0.0.1:7781/order
```

## Tests

- `Tools/Tests/test_coop_ai_mcp.py` tests this server without the game. It runs in the test
  runner's tools stage.
- `aibot_test.py` is the live test, which the runner's `aibot` stage runs: a test player, the
  bot game and a server.
