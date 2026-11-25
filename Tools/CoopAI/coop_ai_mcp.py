"""An MCP server that lets Claude (or any MCP client) fly a ship in a co-op game as another player.

The ship is a game of its own running in bot mode: the Star System build started with -coopbot
(see launch_bot), connected to the co-op server like any player, flown by its wingman pilot
(follow, hold, attack, go to a place, regroup, dock, mine and sell) and talking on the game's comms channel.
The bot game answers on a small HTTP API on this machine (127.0.0.1, port 7781 by default); this
server turns that API into MCP tools, over stdio:

    status          the bot's ship, its orders, what is near, who is playing
    read_messages   the comms channel since the last read (players' messages, the bot's own)
    wait_messages   the same, but waiting (up to a minute) for a new message if there is none yet
    cargo           what is in the bot's hold, how full it is, and its credits
    give_credits    hand the bot's credits to a player (their game adds them to their hold)
    give_ore        hand ore from the bot's hold to a player's (all of it, a mineral, so many tonnes)
    say             send a message on the comms channel (every player in its level sees it)
    order           give the pilot an order: follow <player>, hold, attack [target], goto <place>,
                    regroup, dock, mine [mineral | the most valuable ore] [at field], sell,
                    defend [player], assist [player] (attack
                    what that player has locked), rename <name> (a new name for the bot)
    destinations    the places a ship can warp to, with distances
    get_chat_mode   who answers talk to the bot: llm (this listener), personality (the bot's own) or paused
    set_chat_mode   switch it: llm|personality|paused, or on|off|pause|resume
    launch_bot      start the bot game (headless) and connect it to a co-op server
    stop_bot        close the bot game started here

Players talk to the bot in the game: a message starting with its name ("Wingman, follow me",
"@Wingman attack") is an order it carries out by itself; anything else is left for whoever is
reading the channel through this server (read_messages and wait_messages tell the bot a listener
is there; with nobody listening for 90 s, its built-in personality answers talk instead). That is
the bot's chat mode "llm"; in "personality" its built-in personality always answers talk, and in
"paused" nobody does. read_messages and wait_messages say the mode (chat_mode): answer talk only
when it is llm.

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
ORDERS = ["follow", "hold", "attack", "goto", "regroup", "dock", "mine", "sell", "defend", "assist", "rename"]
CHAT_MODES = ["llm", "personality", "paused", "on", "off", "pause", "resume", "unpause"]
WAIT_MAX = 55

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
    r = call("/messages?listener=1&after=" + str(cursor if after is None else int(after)))
    if after is None and restarted(r):
        r = call("/messages?listener=1&after=0")
    if isinstance(r, dict) and "last" in r:
        cursor = max(cursor, int(r["last"]))
    return r


def restarted(r):
    """The bot game has started again since the last read (its messages are numbered from 0 again):
    the cursor goes back to the start."""
    global cursor
    if isinstance(r, dict) and "last" in r and int(r["last"]) < cursor:
        cursor = 0
        return True
    return False


def t_wait_messages(args):
    global cursor
    t = args.get("timeout")
    wait = max(1, min(WAIT_MAX, int(25 if t is None else t)))
    # (a restarted game would never get past the old cursor: start again from its first message)
    if restarted(call(f"/messages?listener=1&after={cursor}")):
        pass
    r = call(f"/messages?listener=1&after={cursor}&wait={wait}", timeout=wait + 10)
    if isinstance(r, dict) and "last" in r:
        cursor = max(cursor, int(r["last"]))
    return r


def t_cargo(_):
    return call("/cargo")


def t_give_credits(args):
    amount = str(args.get("amount") if args.get("amount") is not None else "all").strip().lower().replace(",", "")
    if amount not in ("all", "half"):
        try:
            n = float(amount)
        except ValueError:
            raise ValueError(f"amount must be a number, 'all' or 'half', not '{amount}'")
        if not (1 <= n < 1e12):
            raise ValueError("give at least 1 credit (and a believable number of them)")
        amount = str(int(n))
    to = str(args.get("to", "") or "").strip()
    return call("/order", {"order": "give", "target": f"{amount} to {to}" if to else amount})


def t_give_ore(args):
    amount = str(args.get("amount") if args.get("amount") is not None else "all").strip().lower().replace(",", "")
    if amount not in ("all", "half"):
        try:
            n = float(amount)
        except ValueError:
            raise ValueError(f"amount must be a number of tonnes, 'all' or 'half', not '{amount}'")
        if not (0 < n < 1e6):
            raise ValueError("give more than 0 t (and a believable number of tonnes)")
        amount = f"{n:g}"
    item = " ".join(str(args.get("item") or "ore").split())
    if " to " in f" {item} ":
        raise ValueError("item is the ore or a mineral; say who it goes to with 'to'")
    to = str(args.get("to", "") or "").strip()
    return call("/order", {"order": "giveore", "target": f"{amount} {item}" + (f" to {to}" if to else "")})


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


def t_get_chat_mode(_):
    return call("/chatmode")


def t_set_chat_mode(args):
    mode = str(args.get("mode", "")).strip().lower()
    if mode not in CHAT_MODES:
        raise ValueError(f"unknown chat mode '{mode}': one of {', '.join(CHAT_MODES)}")
    return call("/chatmode", {"mode": mode})


def t_launch_bot(args):
    global bot_process, cursor
    if bot_process is not None and bot_process.poll() is None:
        return {"ok": True, "note": "already running", "pid": bot_process.pid}
    cursor = 0
    exe = args.get("game") or GAME
    if not os.path.exists(exe):
        raise RuntimeError(f"no game at {exe}: set COOP_GAME_EXE or give 'game'")
    server = str(args.get("server") or "127.0.0.1:7777")
    port = urllib.parse.urlparse(API).port or 7781
    cmd = [exe, "-coopbot", "-connect", server, "-name", str(args.get("name") or "Wingman"), "-botport", str(port)]
    # (the server's API key for this AI player: given, or COOP_KEY in this server's environment)
    key = str(args.get("apikey") or os.environ.get("COOP_KEY", "")).strip()
    if key:
        cmd += ["-apikey", key]
    if args.get("ship"):
        cmd += ["-frigate", str(args["ship"])]
    if args.get("chatmode"):
        mode = str(args["chatmode"]).strip().lower()
        if mode not in ("llm", "personality", "paused"):
            raise ValueError(f"chatmode must be llm, personality or paused, not '{mode}'")
        cmd += ["-chatmode", mode]
    if args.get("headless", True):
        cmd += ["-batchmode", "-nographics"]
    else:
        cmd += ["-screen-fullscreen", "0", "-screen-width", "1280", "-screen-height", "720"]
    cmd += ["-logFile", os.path.join(os.path.dirname(os.path.abspath(exe)), "coopbot.log")]
    bot_process = subprocess.Popen(cmd, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    return {"ok": True, "pid": bot_process.pid, "command": [c if c != key else key[:9] + "..." for c in cmd] if key else cmd,
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
                      "out simple orders in those by itself and says so, and marks those handled: "
                      "never order a handled message again.",
     {"after": {"type": "integer", "description": "read from after this message number instead"}}, [], t_read_messages),
    ("wait_messages", "Like read_messages, but if nothing new has been said, waits up to 'timeout' seconds for "
                      "the next message before answering. Use it to listen for players talking to the wingman.",
     {"timeout": {"type": "integer", "description": f"seconds to wait, 1 to {WAIT_MAX} (default 25)"}}, [], t_wait_messages),
    ("cargo", "What is in the bot's hold: each item (ore by mineral, refined goods, parts) with its amount, the tonnes "
              "used of the hold's capacity, how full it is, and the credits it has. 'line' is the same as one sentence.",
     {}, [], t_cargo),
    ("give_credits", "Hand the bot's credits to a player: their game adds them to their ship's hold. With no 'to', the "
                     "last player to speak (or the nearest). The bot can't give more than it has.",
     {"amount": {"type": "string", "description": "a number of credits, 'all' (the default) or 'half'"},
      "to": {"type": "string", "description": "the player's name (or part of it)"}}, [], t_give_credits),
    ("give_ore", "Hand ore from the bot's hold to a player's ship: their game loads what fits and sends the rest back, "
                 "and the bot says on the comms what came back. With no 'to', the last player to speak (or the nearest). "
                 "Only to a person in the bot's level, within 50 km of it. (A player hands their own ore to the bot "
                 "themselves: 'Wingman, take my ore' on the comms, or G in their mining menu.)",
     {"item": {"type": "string", "description": "'ore' (every mineral aboard, the default) or a mineral by name or symbol ('platinum', 'Pt', 'He-3')"},
      "amount": {"type": "string", "description": "tonnes, 'all' (the default) or 'half'"},
      "to": {"type": "string", "description": "the player's name (or part of it)"}}, [], t_give_ore),
    ("say", "Send a message on the co-op comms channel, as the bot. Every player in its level sees it.",
     {"text": {"type": "string", "description": "the message (400 characters at most)"}}, ["text"], t_say),
    ("order", "Order the bot's pilot. follow [player name] (no name: the last player to speak): fly in formation, "
              "warping after them; hold: stop where it is; attack [target name]: fight the target or the nearest "
              "enemy; goto <place>: warp to a destination; regroup: form up on the nearest player; dock: dock at "
              "the station it is at; mine [asteroid field]: mine asteroids (the nearest field if none is named), "
              "selling the ore at the nearest market whenever the hold is full, over and over; the target can also say "
              "which rocks, as a player would: a mineral by name or symbol ('platinum', 'Pt', 'He-3': the rocks richest "
              "in it, or the most valuable if none in range has any), 'the most valuable ore' (worth most a tonne), and "
              "either with a field ('gold at Inner Belt'); status then has ore, ore_missing and rock_mix; sell: take the ore "
              "aboard to the nearest market now; defend [player name]: go to that player from wherever it is and fight "
              "what's on them; assist [player name]: attack what that player has locked (their game says it). Without "
              "a name, defend and assist mean the last player to speak. rename <new name>: the bot asks the co-op server "
              "for the name, and says on the comms (in its new name) whether it has it; it keeps the name for its next "
              "start, and from then on players order it by that name.",
     {"order": {"type": "string", "enum": ORDERS},
      "target": {"type": "string", "description": "a player's name (follow, defend, assist), a target's (attack), a place's (goto), an asteroid field, a mineral or both for mine ('Inner Belt', 'platinum', 'gold at Inner Belt', 'the most valuable ore'), or the bot's new name (rename)"}},
     ["order"], t_order),
    ("destinations", "The places a ship can warp to, nearest first, with their kinds and distances.", {}, [], t_destinations),
    ("get_chat_mode", "Who answers players' talk to the bot (anything addressed to it that isn't an order): 'llm', "
                      "whoever reads the channel here (the bot's built-in personality while nobody has read it for "
                      "90 s); 'personality', the bot's built-in personality, always; 'paused', nobody (it says it is "
                      "standing by, at most every 30 s). Orders work in every mode. Also how many seconds since a "
                      "listener last read the channel (listener_seen_s, -1 never).",
     {}, [], t_get_chat_mode),
    ("set_chat_mode", "Switch who answers talk to the bot: llm (or on), personality (or off: the LLM is off), paused "
                      "(or pause), or resume (unpause: back to the mode before the pause). Kept when the bot restarts.",
     {"mode": {"type": "string", "enum": CHAT_MODES}}, ["mode"], t_set_chat_mode),
    ("launch_bot", "Start the bot game (the Star System build in bot mode, headless by default) and connect it to a "
                   "co-op server as a player.",
     {"server": {"type": "string", "description": "host:port of the co-op server (default 127.0.0.1:7777)"},
      "name": {"type": "string", "description": "the bot's name in the game (default Wingman); if it was renamed since "
                                                "(order rename), the name it was given is kept for this one and wins"},
      "ship": {"type": "string", "description": "the frigate to fly: Sentinel (the default), Lancer, Warden, Talon or Vanguard. A destroyer or the cruiser only once built at a shipyard and taken command of in that game's save"},
      "headless": {"type": "boolean", "description": "no window (default true)"},
      "chatmode": {"type": "string", "enum": ["llm", "personality", "paused"],
                   "description": "who answers talk to it at the start (default: what it was when it last ran, or llm)"},
      "game": {"type": "string", "description": "path to the game's executable"},
      "apikey": {"type": "string", "description": "the API key the co-op server issued for this AI player "
                                                  "(CoopServer --new-key <name> --ai); default COOP_KEY from the environment"}}, [], t_launch_bot),
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
                                            "Listen on the comms channel (wait_messages, or read_messages) and answer players "
                                            "with say, in short radio lines (400 characters at most). Orders addressed to the "
                                            "bot by name are carried out by it already (marked handled: never order those again); "
                                            "answer the talk, but only while the "
                                            "chat_mode in read_messages/wait_messages is llm (personality: the bot answers "
                                            "talk itself; paused: nobody does). Carry out what they ask "
                                            "with order, give_credits or give_ore; facts come from status and cargo, never made up. "
                                            "Tools/CoopAI/WINGMAN.md has the voice."})
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
