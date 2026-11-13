"""An MCP server that lets Claude (or any MCP client) fly a ship in a co-op game as another player.

The ship is a game of its own running in bot mode: the Star System build started with -coopbot
(see launch_bot), connected to the co-op server like any player, flown by its wingman pilot
(follow, hold, attack, go to a place, regroup, dock) and talking on the game's comms channel.
The bot game answers on a small HTTP API on this machine (127.0.0.1, port 7781 by default); this
server turns that API into MCP tools, over stdio:

    status          the bot's ship, its orders, what is near, who is playing
    read_messages   the comms channel since the last read (players' messages, the bot's own)
    say             send a message on the comms channel (every player in its level sees it)
    order           give the pilot an order: follow <player>, hold, attack [target], goto <place>,
                    regroup, dock
    destinations    the places a ship can warp to, with distances
    launch_bot      start the bot game (headless) and connect it to a co-op server
    stop_bot        close the bot game started here

Players talk to the bot in the game: a message starting with its name ("Wingman, follow me",
"@Wingman attack") is an order it carries out by itself; anything else is just read, by whoever
is reading the channel through this server.

No packages are needed (Python 3.8+). Add it to Claude Code with:

    claude mcp add coop-ai -- python "<repo>/Tools/CoopAI/coop_ai_mcp.py"

Environment: COOP_BOT_API (default http://127.0.0.1:7781), COOP_GAME_EXE (the Star System build's
executable, for launch_bot; default <repo>/../Builds/StarSystem/StarSystem.exe).
"""
import io
import json
import os
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request

API = os.environ.get("COOP_BOT_API", "http://127.0.0.1:7781").rstrip("/")
HERE = os.path.dirname(os.path.abspath(__file__))
GAME = os.environ.get("COOP_GAME_EXE", os.path.normpath(os.path.join(HERE, "..", "..", "..", "Builds", "StarSystem",
                                                                     "StarSystem.exe" if os.name == "nt" else "StarSystem")))
PROTOCOLS = ["2025-06-18", "2025-03-26", "2024-11-05"]
ORDERS = ["follow", "hold", "attack", "goto", "regroup", "dock"]

bot_process = None
cursor = 0


def call(path, body=None, timeout=5.0):
    """The bot game's API: GET, or POST with a JSON body. Returns the parsed reply."""
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(API + path, data=data, method="GET" if body is None else "POST",
                                 headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return json.loads(r.read().decode() or "{}")
    except urllib.error.HTTPError as e:
        try:
            return json.loads(e.read().decode())
        except ValueError:
            return {"error": f"HTTP {e.code}"}
    except (urllib.error.URLError, OSError) as e:
        raise RuntimeError(f"the bot game isn't answering at {API} ({getattr(e, 'reason', e)}): start it with launch_bot")


def text(obj):
    return obj if isinstance(obj, str) else json.dumps(obj, indent=1)


# ------------------------------------------------------------------ the tools

def t_status(_):
    return call("/status")


def t_read_messages(args):
    global cursor
    after = args.get("after")
    r = call("/messages?after=" + str(cursor if after is None else int(after)))
    if isinstance(r, dict) and "last" in r:
        cursor = max(cursor, int(r["last"]))
    return r


def t_say(args):
    msg = str(args.get("text", "")).strip()
    if not msg:
        raise ValueError("nothing to say")
    return call("/say", {"text": msg})


def t_order(args):
    order = str(args.get("order", "")).strip().lower()
    if order not in ORDERS:
        raise ValueError(f"unknown order '{order}': one of {', '.join(ORDERS)}")
    return call("/order", {"order": order, "target": str(args.get("target", "") or "")})


def t_destinations(_):
    return call("/destinations")


def t_launch_bot(args):
    global bot_process
    if bot_process is not None and bot_process.poll() is None:
        return {"ok": True, "note": "already running", "pid": bot_process.pid}
    exe = args.get("game") or GAME
    if not os.path.exists(exe):
        raise RuntimeError(f"no game at {exe}: set COOP_GAME_EXE or give 'game'")
    server = str(args.get("server") or "127.0.0.1:7777")
    port = urllib.parse.urlparse(API).port or 7781
    cmd = [exe, "-coopbot", "-connect", server, "-name", str(args.get("name") or "Wingman"), "-botport", str(port)]
    if args.get("ship"):
        cmd += ["-frigate", str(args["ship"])]
    if args.get("headless", True):
        cmd += ["-batchmode", "-nographics"]
    else:
        cmd += ["-screen-fullscreen", "0", "-screen-width", "1280", "-screen-height", "720"]
    cmd += ["-logFile", os.path.join(os.path.dirname(os.path.abspath(exe)), "coopbot.log")]
    bot_process = subprocess.Popen(cmd, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    return {"ok": True, "pid": bot_process.pid, "command": cmd,
            "note": "give it 20-60 s to load and connect, then call status"}


def t_stop_bot(_):
    global bot_process
    if bot_process is None or bot_process.poll() is not None:
        bot_process = None
        return {"ok": False, "note": "no bot game started here is running"}
    try:
        call("/quit", {})
    except RuntimeError:
        pass
    try:
        bot_process.wait(15)
    except subprocess.TimeoutExpired:
        bot_process.kill()
    bot_process = None
    return {"ok": True}


TOOLS = [
    ("status", "The bot's ship (class, hull, shield, crew, missiles, speed, warp, docked or landed), its pilot's "
               "current order and what it is doing, the nearest place, what is near it (enemies first) and who is playing.",
     {}, [], t_status),
    ("read_messages", "Messages on the co-op comms channel since the last read (or since 'after'): from players, "
                      "and the bot's own. A message addressed to the bot by name is marked to_me; the bot carries "
                      "out simple orders in those by itself and says so.",
     {"after": {"type": "integer", "description": "read from after this message number instead"}}, [], t_read_messages),
    ("say", "Send a message on the co-op comms channel, as the bot. Every player in its level sees it.",
     {"text": {"type": "string", "description": "the message (240 characters at most)"}}, ["text"], t_say),
    ("order", "Order the bot's pilot. follow [player name] (no name: the last player to speak): fly in formation, "
              "warping after them; hold: stop where it is; attack [target name]: fight the target or the nearest "
              "enemy; goto <place>: warp to a destination; regroup: form up on the nearest player; dock: dock at "
              "the station it is at.",
     {"order": {"type": "string", "enum": ORDERS},
      "target": {"type": "string", "description": "a player's name (follow), a target's (attack) or a place's (goto)"}},
     ["order"], t_order),
    ("destinations", "The places a ship can warp to, nearest first, with their kinds and distances.", {}, [], t_destinations),
    ("launch_bot", "Start the bot game (the Star System build in bot mode, headless by default) and connect it to a "
                   "co-op server as a player.",
     {"server": {"type": "string", "description": "host:port of the co-op server (default 127.0.0.1:7777)"},
      "name": {"type": "string", "description": "the bot's name in the game (default Wingman)"},
      "ship": {"type": "string", "description": "the ship class to fly (default the starting frigate)"},
      "headless": {"type": "boolean", "description": "no window (default true)"},
      "game": {"type": "string", "description": "path to the game's executable"}}, [], t_launch_bot),
    ("stop_bot", "Close the bot game started by launch_bot.", {}, [], t_stop_bot),
]


# ------------------------------------------------------------------ MCP over stdio (JSON-RPC 2.0, a message a line)

def result(id_, res):
    return {"jsonrpc": "2.0", "id": id_, "result": res}


def error(id_, code, message):
    return {"jsonrpc": "2.0", "id": id_, "error": {"code": code, "message": message}}


def handle(msg):
    """The reply to one JSON-RPC message (None for a notification, which gets none)."""
    if not isinstance(msg, dict):
        return error(None, -32600, "not a request")
    method, id_, params = msg.get("method"), msg.get("id"), msg.get("params") or {}
    if id_ is None:
        return None          # a notification (initialized, cancelled): nothing to answer
    if method == "initialize":
        want = params.get("protocolVersion")
        return result(id_, {"protocolVersion": want if want in PROTOCOLS else PROTOCOLS[0],
                            "capabilities": {"tools": {}},
                            "serverInfo": {"name": "coop-ai", "version": "1.0"},
                            "instructions": "You are flying a ship in a co-op space game, as the players' wingman. "
                                            "Read the comms channel (read_messages) often and answer players with say; "
                                            "carry out what they ask with order. Check status before acting."})
    if method == "ping":
        return result(id_, {})
    if method == "tools/list":
        return result(id_, {"tools": [{"name": n, "description": d,
                                       "inputSchema": {"type": "object", "properties": p, "required": r}}
                                      for n, d, p, r, _ in TOOLS]})
    if method == "tools/call":
        name, args = params.get("name"), params.get("arguments") or {}
        tool = next((t for t in TOOLS if t[0] == name), None)
        if tool is None:
            return error(id_, -32602, f"no tool {name}")
        try:
            out = tool[4](args)
            bad = isinstance(out, dict) and (bool(out.get("error")) or out.get("ok") is False)
            return result(id_, {"content": [{"type": "text", "text": text(out)}], "isError": bad})
        except Exception as e:  # a tool's failure is the tool's answer, not the protocol's
            return result(id_, {"content": [{"type": "text", "text": str(e)}], "isError": True})
    return error(id_, -32601, f"no method {method}")


def serve(lines, write):
    """Answer each line (a JSON-RPC message or a batch of them) with write(reply line)."""
    for line in lines:
        line = line.strip()
        if not line:
            continue
        try:
            msg = json.loads(line)
        except ValueError:
            write(json.dumps(error(None, -32700, "not JSON")))
            continue
        if isinstance(msg, list):
            replies = [r for r in (handle(m) for m in msg) if r is not None]
            if replies:
                write(json.dumps(replies))
        else:
            r = handle(msg)
            if r is not None:
                write(json.dumps(r))


def main():
    stdin = io.TextIOWrapper(sys.stdin.buffer, encoding="utf-8")
    out = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", newline="\n")

    def write(s):
        out.write(s + "\n")
        out.flush()

    serve(stdin, write)


if __name__ == "__main__":
    main()
