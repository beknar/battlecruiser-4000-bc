"""The AI wingman, live: a test player and the bot game (the Star System build with -coopbot) on one
co-op server. Run by Tools/Tests/run_tests.py (the aibot stage), which starts the server and the
bot game; this script is the other player, and checks the bot through the comms channel, through
its HTTP API, and through the MCP server (coop_ai_mcp.py):

    python aibot_test.py <bot API, http://127.0.0.1:7781> <server, ws://127.0.0.1:7792/coop> [bot name]

  - the bot game connects, its pilot takes its ship, and it greets a player who joins
  - orders by name on the comms channel (status, hold, follow me) are carried out and answered;
    talk that isn't to it is left alone
  - the API: /say reaches the players, /order is carried out (and a nonsense one refused),
    /messages has the channel (who it was to), /destinations has the places to warp to
  - talk to it is answered by its built-in personality while nobody listens, and left alone once a
    listener reads the channel; a waiting /messages call answers as soon as something is said
  - the chat mode: /chatmode reads it, "Wingman, chat off" makes the personality answer (a branch
    picked by number), paused answers talk with "standing by" while orders still work, resume
    goes back to the mode before the pause
  - the MCP server's status and say tools work against the live bot
  - told to mine platinum: its answer, /status (ore) and its line say so, and it goes for a rock
    with platinum in it, or says there's none in range and mines the most valuable; the API's
    mine order takes 'the most valuable ore'
  - after selling ore: 'cargo' reports the hold, 'give me N credits' sends a gift the player's game
    acknowledges, a gift with no receipt comes back to the bot, and it won't give more than it has
  - ore both ways, with a ship of the test player's beside the bot's: cargo from the player's game
    is loaded and receipted, more than its hold takes is loaded as far as it fits (the receipt says
    how much), a message sent more than 6 s ago is refused; 'give me 100 t of iron' sends it and a
    partial receipt puts the rest back aboard, 'give me your ore' hands over all of it, and 'take my
    ore' is left to the player's game, which sends the ore with it
  - the fights, with a ship of the test player's beside the bot's: 'attack my target' from an older
    game (no lock in its ship message) says the game doesn't say, with nothing locked says so, and
    with an enemy of the bot's own list locked attacks it; 'defend me' sends it to the player;
    'where are you' says how far it is from them, at once, and is marked handled
  - its name: 'your name is <name>' renames it (its answer, in the new name, its /status and the
    server's roster have it), orders by the new name are carried out and by the old one aren't, a
    name another player has is refused, and the API's rename order gives it its first name back
    (so the name it keeps for its next start is the one it was launched with; a name left from a
    run that stopped part way is put back at the start)
  - /quit closes it (the runner checks the game ends)

Prints a line a check ("[AIBot] PASS ..." or "[AIBot] FAIL ...") and exits 1 if any failed.
Needs the 'websockets' package.
"""
import asyncio, json, os, subprocess, sys, time, urllib.error, urllib.request
from pathlib import Path

import websockets

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "CoopTest"))
from protocol import PROTOCOL, hello  # noqa: E402

API = sys.argv[1].rstrip("/") if len(sys.argv) > 1 else "http://127.0.0.1:7781"
URL = sys.argv[2] if len(sys.argv) > 2 else "ws://127.0.0.1:7792/coop"
BOT = sys.argv[3] if len(sys.argv) > 3 else "Wingman"
ME = "Tester"
NEW = "Kestrel"          # the name the test gives the bot, and takes back
renamed = False          # the bot has the new name (given back at the end, whatever happens)

checks, failed = 0, 0


def log(s):
    print(f"[AIBot] {s}", flush=True)


def check(ok, what, detail=""):
    global checks, failed
    checks += 1
    if not ok:
        failed += 1
    log(f"{'PASS' if ok else 'FAIL'} {what}{': ' + detail if detail else ''}")
    return ok


def http(path, body=None, timeout=5):
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(API + path, data=data, method="GET" if body is None else "POST",
                                 headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return json.loads(r.read().decode() or "{}")
    except urllib.error.HTTPError as e:
        return json.loads(e.read().decode() or "{}")


async def api(path, body=None):
    return await asyncio.to_thread(http, path, body)


class Player:
    """The test player: in the bot's level, reading the comms channel and keeping the server's ping."""

    def __init__(self, ws):
        self.ws = ws
        self.my_id = 0
        self.heard = []          # (who, text) of each chat message from someone else
        self.gifts = []          # credits received
        self.receipts = True     # answer a gift with a receipt, as a game does
        self.cargo_in = []       # the items of each cargo the bot sent
        self.cargo_load = None   # what to say was loaded of cargo that comes (None: all of it)
        self.cargo_receipts = {} # the bot's receipts for cargo sent to it, by number
        self.clock0, self.local0 = 0.0, time.monotonic()   # the server's clock at the welcome
        self.bot_ship = None     # the bot's last ship message
        self.enemies = {}        # the host's (the bot game's) living enemies: id -> (rf, x, y, z)
        self.ship = None         # my own ship message's fields, sent while set (None: no ship)
        self.pos = None          # ... and where it is (rf, x, y, z)
        self.last_ship = 0.0
        self.roster = {}         # the server's last players list: id -> name

    def now(self):
        return self.clock0 + (time.monotonic() - self.local0)

    async def send_ship(self):
        """My ship, beside the bot's (2 km off), as a game sends it: with its lock (tg) and what
        the game has in it (cv), or as an older game, without either."""
        b = self.bot_ship
        if self.ship is None or b is None:
            return
        # (where the bot was when my ship first went out, and there it stays)
        if self.pos is None:
            self.pos = (b.get("rf", ""), b.get("x", 0) + 2000, b.get("y", 0), b.get("z", 0))
        rf, x, y, z = self.pos
        m = {"t": "ps", "ts": self.now(), "rf": rf, "x": x, "y": y, "z": z,
             "qx": 0, "qy": 0, "qz": 0, "qw": 1, "vx": 0, "vy": 0, "vz": 0, "th": 0, "sh": 1, "su": True, "hu": 1, "dead": False,
             "warp": 0, "dock": False, "surf": "", "cap": 0}
        m.update(self.ship)
        await self.ws.send(json.dumps(m))

    async def run(self):
        last_ping = 0.0
        while True:
            try:
                raw = await asyncio.wait_for(self.ws.recv(), timeout=0.2)
                m = json.loads(raw)
                if m.get("t") == "welcome":
                    self.my_id = m["id"]
                    self.clock0, self.local0 = m.get("now", 0.0), time.monotonic()
                elif m.get("t") == "players":
                    self.roster = {p.get("id"): p.get("name") for p in m.get("players") or []}
                elif m.get("t") == "ps":
                    self.bot_ship = m
                elif m.get("t") == "es":
                    for e in m.get("e") or []:
                        if e.get("dead") or e.get("gone"):
                            self.enemies.pop(e.get("id"), None)
                        else:
                            self.enemies[e.get("id")] = (e.get("rf", ""), e.get("x", 0), e.get("y", 0), e.get("z", 0))
                elif m.get("t") == "chat":
                    self.heard.append((m.get("from"), m.get("text", "")))
                    log(f"heard #{m.get('from')}: {m.get('text', '')}")
                elif m.get("t") == "gift":
                    self.gifts.append(m.get("a", 0))
                    log(f"gift of {m.get('a')} cr from #{m.get('from')}{'' if self.receipts else ' (no receipt sent)'}")
                    if self.receipts:
                        await self.ws.send(json.dumps({"t": "giftok", "to": m.get("from"), "a": m.get("a"), "gid": m.get("gid")}))
                elif m.get("t") == "cargo":
                    # (as a game does: what fitted, by the receipt; here, as much as cargo_load says)
                    items = m.get("items") or []
                    self.cargo_in.append(items)
                    loaded = items if self.cargo_load is None else self.cargo_load(items)
                    log(f"cargo from #{m.get('from')}: {items}; loaded {loaded}")
                    await self.ws.send(json.dumps({"t": "cargook", "to": m.get("from"), "gid": m.get("gid"), "items": loaded, "why": ""}))
                elif m.get("t") == "cargook":
                    self.cargo_receipts[m.get("gid")] = m
            except asyncio.TimeoutError:
                pass
            # (my ship ten times a second, as a game sends it about: a ship quiet 3 s is gone)
            if time.monotonic() - self.last_ship > 0.1:
                self.last_ship = time.monotonic()
                await self.send_ship()
            if time.monotonic() - last_ping > 2.0:
                last_ping = time.monotonic()
                await self.ws.send(json.dumps({"t": "ping", "c": last_ping}))

    async def say(self, text):
        log(f"say: {text}")
        await self.ws.send(json.dumps({"t": "chat", "text": text}))

    async def send_cargo(self, to, gid, items, age=0.0):
        """Cargo to the bot, as a player's game sends it (`age`: sent that many seconds ago)."""
        log(f"cargo #{gid} to #{to}: {items}{f', {age:.0f} s old' if age else ''}")
        await self.ws.send(json.dumps({"t": "cargo", "to": to, "gid": gid, "ts": self.now() - age, "items": items}))

    async def receipt(self, gid, seconds=10.0):
        """The bot's receipt for cargo number `gid`, or None."""
        end = time.monotonic() + seconds
        while time.monotonic() < end:
            if gid in self.cargo_receipts:
                return self.cargo_receipts[gid]
            await asyncio.sleep(0.2)
        return None

    async def hear(self, want, seconds=15.0, since=0):
        """The first message heard (after the first `since`) that has `want` in it, or None."""
        end = time.monotonic() + seconds
        while time.monotonic() < end:
            for who, text in self.heard[since:]:
                if want.lower() in text.lower():
                    return text
            await asyncio.sleep(0.2)
        return None


async def wait_status(cond, seconds):
    end = time.monotonic() + seconds
    s = {}
    while time.monotonic() < end:
        try:
            s = await api("/status")
            if cond(s):
                return s
        except (urllib.error.URLError, OSError, ValueError):
            pass
        await asyncio.sleep(1.0)
    return s


def mcp_session(lines):
    """Run the MCP server against the live bot with these requests; its replies by id."""
    p = subprocess.run([sys.executable, "-B", str(HERE / "coop_ai_mcp.py")], input="\n".join(json.dumps(l) for l in lines) + "\n",
                       capture_output=True, text=True, encoding="utf-8", timeout=60, env=dict(os.environ, COOP_BOT_API=API))
    return {r.get("id"): r for r in (json.loads(l) for l in p.stdout.splitlines() if l.strip())}


async def main():
    global renamed
    t0 = time.monotonic()
    s = await wait_status(lambda s: s.get("connected") and "ship" in s, 240)
    if not check(bool(s.get("connected")), "the bot game is up and connected", f"after {time.monotonic() - t0:.0f} s"):
        return
    check("ship" in s, "its pilot has a ship", json.dumps(s.get("ship", {}))[:200])
    level = s.get("level", "StarSystem")
    # (a name kept from a run that stopped part way: its first name back before anything else)
    if s.get("name") != BOT:
        log(f"the bot is called {s.get('name')}, kept from an earlier run: renaming it {BOT}")
        await api("/order", {"order": "rename", "target": BOT})
        s = await wait_status(lambda s: s.get("name") == BOT, 15)
        if not check(s.get("name") == BOT, f"the bot takes its first name, {BOT}, back", s.get("name", "")):
            return

    async with websockets.connect(URL, max_size=4 * 1024 * 1024) as ws:
        await ws.send(json.dumps(hello(name=ME, level=level, client="tester", captain=0, clock=0)))
        me = Player(ws)
        reader = asyncio.create_task(me.run())
        try:
            # the bot forms up on a player who joins
            check(await me.hear("welcome", 20) is not None, "the bot greets a player who joins")
            s = await wait_status(lambda s: s.get("order", {}).get("order") == "follow", 10)
            check(s.get("order", {}).get("leader") == ME, "and follows them", json.dumps(s.get("order", {})))

            # orders by name on the comms channel
            n = len(me.heard)
            await me.say(f"{BOT}, status")
            r = await me.hear(f"{BOT}:", 15, n)
            check(r is not None and "hull" in r, "'status' is answered with a report", r or "no answer")

            n = len(me.heard)
            await me.say(f"@{BOT} hold")
            r = await me.hear("holding position", 15, n)
            s = await wait_status(lambda s: s.get("order", {}).get("order") == "hold", 10)
            check(r is not None and s.get("order", {}).get("order") == "hold", "'hold' is carried out and answered", r or "no answer")

            n = len(me.heard)
            await me.say(f"{BOT}, follow me")
            r = await me.hear(f"following {ME}", 15, n)
            s = await wait_status(lambda s: s.get("order", {}).get("order") == "follow", 10)
            check(r is not None and s.get("order", {}).get("leader") == ME, "'follow me' is carried out and answered", r or "no answer")

            n = len(me.heard)
            await me.say("just talking, nothing for the bot")
            await asyncio.sleep(3)
            # (what it says by itself, starting or ending a fight, isn't an answer)
            own = ("engaging", "target destroyed", "arrived", "can't")
            answers = [t for _, t in me.heard[n:] if t.lower().startswith(BOT.lower() + ":")
                       and not t[len(BOT) + 1:].strip().lower().startswith(own)]
            check(not answers, "talk that isn't to it gets no answer", "; ".join(answers))

            # the API
            msgs = (await api("/messages?after=0")).get("messages", [])
            to_me = {m["text"]: m["to_me"] for m in msgs}
            check(to_me.get(f"{BOT}, status") is True and to_me.get("just talking, nothing for the bot") is False,
                  "/messages has the channel, marked by who it was to", f"{len(msgs)} messages")
            n = len(me.heard)
            r = await api("/say", {"text": "API check"})
            check(r.get("ok") is True and await me.hear("API check", 10, n) is not None, "/say reaches the players", json.dumps(r))
            mine = [m for m in (await api("/messages?after=0")).get("messages", []) if m["text"] == "API check"]
            check(len(mine) == 1 and mine[0]["mine"], "/messages has what it said, as its own")
            n = len(me.heard)
            r = await api("/order", {"order": "hold", "target": ""})
            heard = await me.hear("holding position", 10, n)
            s = await api("/status")
            check(r.get("ok") is True and heard is not None and s.get("order", {}).get("order") == "hold",
                  "/order is carried out and said on the channel", json.dumps(r))
            r = await api("/order", {"order": "dance", "target": ""})
            check(r.get("ok") is False, "/order refuses an order it doesn't know", json.dumps(r))
            r = await api("/nothing")
            check("error" in r, "an unknown call is an error", json.dumps(r))
            d = (await api("/destinations")).get("destinations", [])
            check(len(d) > 0 and all("name" in x and "km" in x for x in d), "/destinations lists the places to warp to",
                  ", ".join(x["name"] for x in d[:5]))

            # talk to it: with nobody listening its own personality answers; with a listener, it leaves talk alone
            await api("/chatmode", {"mode": "llm"})
            n = len(me.heard)
            await me.say(f"{BOT}, how's it going out there?")
            await asyncio.sleep(4)
            answers = [t for _, t in me.heard[n:] if t.lower().startswith(BOT.lower() + ":")
                       and not t[len(BOT) + 1:].strip().lower().startswith(own)]
            check(len(answers) > 0 and not any("orders only" in a for a in answers),
                  "talk to it, with nobody listening, is answered by its own personality", "; ".join(answers) or "no answer")
            last = (await api("/messages?listener=1&after=0")).get("last", 0)
            n = len(me.heard)
            await me.say(f"{BOT}, nice flying back there")
            await asyncio.sleep(4)
            answers = [t for _, t in me.heard[n:] if t.lower().startswith(BOT.lower() + ":")
                       and not t[len(BOT) + 1:].strip().lower().startswith(own)]
            check(not answers, "with a listener reading, talk to it is left for the listener", "; ".join(answers))
            last = (await api("/messages?listener=1&after=0")).get("last", 0)
            t0 = time.monotonic()
            waiter = asyncio.create_task(asyncio.to_thread(http, f"/messages?listener=1&after={last}&wait=20", None, 30))
            await asyncio.sleep(2)
            await me.say("anyone there?")
            got = await waiter
            took = time.monotonic() - t0
            # (the bot may have said something by itself first, which also answers the wait)
            if not any(m.get("text") == "anyone there?" for m in got.get("messages", [])):
                got = await api(f"/messages?after={last}")
            check(any(m.get("text") == "anyone there?" for m in got.get("messages", [])) and took < 10,
                  "a waiting /messages call answers when something is said", f"after {took:.1f} s")

            # the chat mode
            r = await api("/chatmode")
            check(r.get("mode") == "llm" and 0 <= r.get("listener_seen_s", -1) < 90, "/chatmode says llm, a listener just seen", json.dumps(r))
            n = len(me.heard)
            await me.say(f"{BOT}, chat off")
            r = await me.hear("chat mode personality", 10, n)
            s = await api("/status")
            check(r is not None and s.get("chat_mode") == "personality", "'chat off' switches to its own personality", r or "no answer")
            n = len(me.heard)
            await me.say(f"{BOT}, tell me about the vrask")
            q = await me.hear("(1) long", 10, n)
            n = len(me.heard)
            await me.say(f"{BOT}, 2")
            await asyncio.sleep(4)
            answers = [t for _, t in me.heard[n:] if t.lower().startswith(BOT.lower() + ":")
                       and not t[len(BOT) + 1:].strip().lower().startswith(own)]
            check(q is not None and len(answers) == 1 and "(1)" not in answers[0],
                  "with a listener still reading, the personality answers, and a number picks a branch", f"{q}; {'; '.join(answers)}")
            r = await api("/chatmode", {"mode": "pause"})
            check(r.get("ok") is True and r.get("mode") == "paused" and r.get("previous") == "personality", "POST /chatmode pauses it", json.dumps(r))
            n = len(me.heard)
            await me.say(f"{BOT}, nice flying back there")
            await asyncio.sleep(4)
            # (not its own "chat mode paused" announcement, which may arrive late)
            r = [t for _, t in me.heard[n:] if "standing by" in t.lower() and "chat mode" not in t.lower()]
            check(len(r) > 0, "paused, talk is answered with 'standing by'", "; ".join(r) or "no answer")
            n = len(me.heard)
            await me.say(f"{BOT}, status")
            r = await me.hear("hull", 10, n)
            check(r is not None, "and orders still work", r or "no answer")
            r = await api("/chatmode", {"mode": "resume"})
            m = (await api("/messages?after=0")).get("chat_mode")
            check(r.get("mode") == "personality" and m == "personality", "resume goes back to the mode before the pause", json.dumps(r))
            r = await api("/chatmode", {"mode": "loud"})
            check(r.get("ok") is False and "error" in r, "/chatmode refuses a mode it doesn't know", json.dumps(r))
            await api("/chatmode", {"mode": "llm"})

            # the MCP server, against the live bot
            n = len(me.heard)
            replies = await asyncio.to_thread(mcp_session, [
                {"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {"protocolVersion": "2025-06-18"}},
                {"jsonrpc": "2.0", "method": "notifications/initialized"},
                {"jsonrpc": "2.0", "id": 2, "method": "tools/call", "params": {"name": "status", "arguments": {}}},
                {"jsonrpc": "2.0", "id": 3, "method": "tools/call", "params": {"name": "say", "arguments": {"text": "via MCP"}}},
                {"jsonrpc": "2.0", "id": 4, "method": "tools/call", "params": {"name": "read_messages", "arguments": {}}}])
            status = replies.get(2, {}).get("result", {})
            check(not status.get("isError", True) and json.loads(status["content"][0]["text"]).get("connected") is True,
                  "MCP: the status tool reads the live bot")
            check(not replies.get(3, {}).get("result", {}).get("isError", True) and await me.hear("via MCP", 10, n) is not None,
                  "MCP: the say tool reaches the players")
            msgs = replies.get(4, {}).get("result", {})
            check(not msgs.get("isError", True) and "via MCP" in msgs["content"][0]["text"], "MCP: read_messages has the channel")

            # mining and selling: it mines the nearest field, and takes the ore to a market
            n = len(me.heard)
            # (two orders in one: mining sells when the hold is full, so this is mining, not a field
            # called "and then sell at the nearest station")
            await me.say(f"{BOT}, mine and then sell at the nearest station")
            r = await me.hear("mining", 15, n)
            s = await wait_status(lambda s: s.get("order", {}).get("order") == "mine", 10)
            check(r is not None and "don't know" not in r and s.get("order", {}).get("order") == "mine",
                  "'mine and then sell at the nearest station' is carried out and answered", r or "no answer")
            t0 = time.monotonic()
            s = await wait_status(lambda s: s.get("ship", {}).get("ore_t", 0) >= 40, 420)
            ore = s.get("ship", {}).get("ore_t", 0)
            check(ore >= 40, "it scans, flies to a rock and mines it",
                  f"{ore:.0f} t of ore aboard after {time.monotonic() - t0:.0f} s; {s.get('order', {}).get('doing')} (near {s.get('ship', {}).get('near')})")
            credits0 = s.get("ship", {}).get("credits", 0)
            n = len(me.heard)
            await me.say(f"{BOT}, sell")
            r = await me.hear("market", 15, n)
            t0 = time.monotonic()
            s = await wait_status(lambda s: s.get("order", {}).get("sales", 0) >= 1, 420)
            sold = await me.hear("sold", 20, n)
            o = s.get("order", {})
            check(o.get("sales", 0) >= 1 and s.get("ship", {}).get("credits", 0) > credits0 and sold is not None,
                  "'sell' takes the ore to a market, docks and sells it",
                  f"after {time.monotonic() - t0:.0f} s: {o.get('sold_t', 0):.0f} t sold for {o.get('earned', 0):.0f} credits; said '{sold}'; now {o.get('doing')}")
            # and back to mining
            s = await wait_status(lambda s: s.get("order", {}).get("step") in ("tofield", "scan", "approach", "cut") and not s.get("ship", {}).get("docked"), 120)
            check(s.get("order", {}).get("step") in ("tofield", "scan", "approach", "cut"), "then it goes back to mining", s.get("order", {}).get("doing", ""))

            # a mineral asked for: the rocks richest in platinum (or, with none in range, the most
            # valuable, and it says so)
            n = len(me.heard)
            await me.say(f"{BOT}, mine platinum")
            r = await me.hear("for platinum", 15, n)
            s = await wait_status(lambda s: s.get("order", {}).get("ore") == "platinum", 10)
            o = s.get("order", {})
            check(r is not None and o.get("order") == "mine" and o.get("ore") == "platinum" and "platinum" in o.get("line", ""),
                  "'mine platinum' is answered, and its status and line say what it's after", f"{r or 'no answer'}; {o.get('line')}")
            s = await wait_status(lambda s: s.get("order", {}).get("rock_mix") or s.get("order", {}).get("ore_missing"), 150)
            o = s.get("order", {})
            none_here = await me.hear("no platinum in the rocks here", 5, n)
            check("Pt " in o.get("rock_mix", "") or (o.get("ore_missing") is True and none_here is not None),
                  "it goes for a rock with platinum in it, or says there's none here and mines the most valuable",
                  f"{o.get('rock')}: {o.get('rock_mix')}; missing {o.get('ore_missing')}; {o.get('line')}")
            r = await api("/order", {"order": "mine", "target": "the most valuable ore"})
            s = await wait_status(lambda s: s.get("order", {}).get("ore") == "most valuable", 10)
            check(r.get("ok") is True and "most valuable" in r.get("answer", "") and s.get("order", {}).get("ore") == "most valuable",
                  "the API's mine order takes 'the most valuable ore'", json.dumps(r))
            await me.say(f"{BOT}, hold")

            # the hold and its credits
            n = len(me.heard)
            await me.say(f"{BOT}, what's in your hold?")
            r = await me.hear("cr aboard", 10, n)
            cargo = await api("/cargo")
            check(r is not None and "hold" in r and cargo.get("capacity_t", 0) > 0 and "items" in cargo,
                  "'what's in your hold?' is answered with the cargo, and /cargo has it", f"{r}; {json.dumps(cargo)[:160]}")
            raw = [i for i in cargo.get("items", []) if i.get("kind") != "part" and i.get("amount", 0) > 0.005]
            if raw and r is not None:
                biggest = max(raw, key=lambda i: i["amount"])["name"]
                listed = r.split(": ", 1)[1] if ": " in r else ""
                check(listed.split(", ")[0].endswith(biggest), "the cargo report lists the most aboard first", f"{biggest} first? {r}")
            # several orders in one message: a standing order, then a report
            n = len(me.heard)
            await me.say(f"{BOT}, hold, then cargo")
            r = await me.hear("cr aboard", 10, n)
            s = await api("/status")
            check(r is not None and s.get("order", {}).get("order") == "hold", "'hold, then cargo' holds and reports the hold", r or "no answer")
            s = await api("/status")
            have = s.get("ship", {}).get("credits", 0)
            n = len(me.heard)
            await me.say(f"{BOT}, give me 100 credits")
            r = await me.hear("transferred", 10, n)
            await asyncio.sleep(12)          # (past the receipt's wait: a gift received stays given)
            after = (await api("/status")).get("ship", {}).get("credits", 0)
            check(r is not None and me.gifts == [100] and abs(have - 100 - after) < 1,
                  "'give me 100 credits' sends them to the player, who keeps them", f"{r}; gifts {me.gifts}; {have:.0f} -> {after:.0f} cr")
            me.receipts = False
            n = len(me.heard)
            await me.say(f"{BOT}, transfer 50 credits")
            await me.hear("transferred", 10, n)
            mid = (await api("/status")).get("ship", {}).get("credits", 0)
            await asyncio.sleep(13)
            back = (await api("/status")).get("ship", {}).get("credits", 0)
            me.receipts = True
            check(abs(after - 50 - mid) < 1 and abs(back - after) < 1, "a gift that is never received comes back to the bot",
                  f"{after:.0f} -> {mid:.0f} -> {back:.0f} cr")
            n = len(me.heard)
            await me.say(f"{BOT}, give me 99999999 credits")
            r = await me.hear("only", 10, n)
            check(r is not None, "it won't give more credits than it has", r or "no answer")

            # ore both ways: my ship beside the bot's (cargo goes only within 50 km)
            me.ship = {"cv": 3, "tg": ""}
            await asyncio.sleep(2)
            bot_id = next((p.get("id") for p in (await api("/status")).get("players", []) if p.get("me")), None)

            def tonnes(items, mineral="Iron"):
                return sum(i.get("amount", i.get("a", 0)) for i in items or [] if i.get("id") == mineral)

            cargo = await api("/cargo")
            iron0 = tonnes(cargo.get("items"))
            n = len(me.heard)
            await me.send_cargo(bot_id, 1, [{"id": "Iron", "a": 50}])
            rc = await me.receipt(1)
            took = await me.hear("took 50 t of iron", 10, n)
            cargo = await api("/cargo")
            check(rc is not None and abs(tonnes(rc.get("items")) - 50) < 0.01 and abs(tonnes(cargo.get("items")) - iron0 - 50) < 0.5 and took is not None,
                  "cargo from a player's game is loaded, receipted and said", f"{took or 'not said'}; receipt {rc}; iron {iron0:.1f} -> {tonnes(cargo.get('items')):.1f} t")
            # (more than its hold takes: what fits, and the receipt says how much, so the rest stays with the player)
            free = cargo.get("free_t", 0)
            n = len(me.heard)
            await me.send_cargo(bot_id, 2, [{"id": "Iron", "a": round(free + 100, 1)}])
            rc = await me.receipt(2)
            full = await api("/cargo")
            no_room = await me.hear("no room", 10, n)
            check(rc is not None and abs(tonnes(rc.get("items")) - free) < 0.5 and full.get("free_t", 1) < 0.5 and no_room is not None,
                  "more than its hold takes: it loads what fits, and the receipt says how much", f"{free:.1f} t free; receipt {rc}; {no_room or 'not said'}")
            # (sent more than 6 s ago: refused, as the sender has had it back, or soon will)
            iron1 = tonnes(full.get("items"))
            await me.send_cargo(bot_id, 3, [{"id": "Iron", "a": 10}], age=8)
            rc = await me.receipt(3)
            check(rc is not None and not rc.get("items") and "late" in rc.get("why", "") and abs(tonnes((await api("/cargo")).get("items")) - iron1) < 0.5,
                  "cargo sent more than 6 s ago is refused, and the receipt says why", json.dumps(rc))
            # the bot's: a partial receipt (my hold took 60 of 100 t) puts the rest back aboard
            me.cargo_load = lambda items: [{"id": i.get("id"), "a": min(i.get("a", 0), 60)} for i in items]
            iron2 = tonnes((await api("/cargo")).get("items"))
            k = len(me.cargo_in)
            n = len(me.heard)
            await me.say(f"{BOT}, give me 100 t of iron")
            r = await me.hear("sending", 10, n)
            back = await me.hear("back aboard", 15, n)
            after = tonnes((await api("/cargo")).get("items"))
            sent = tonnes(me.cargo_in[k]) if len(me.cargo_in) > k else 0
            check(r is not None and abs(sent - 100) < 0.01 and back is not None and abs(after - (iron2 - 60)) < 0.5,
                  "'give me 100 t of iron' sends it, and a partial receipt puts the rest back aboard",
                  f"{r or 'no answer'}; sent {sent:.1f} t; {back or 'nothing back'}; iron {iron2:.1f} -> {after:.1f} t")
            me.cargo_load = None
            k = len(me.cargo_in)
            n = len(me.heard)
            await me.say(f"{BOT}, give me your ore")
            r = await me.hear("sending", 10, n)
            s = await wait_status(lambda s: s.get("ship", {}).get("ore_t", 1) < 0.5, 10)
            check(r is not None and len(me.cargo_in) > k and s.get("ship", {}).get("ore_t", 1) < 0.5, "'give me your ore' hands all of it over",
                  f"{r or 'no answer'}; {s.get('ship', {}).get('ore_t')} t of ore left")
            # (and mine to it: my game sends the ore with the order, the bot says what came)
            n = len(me.heard)
            await me.say(f"{BOT}, take my ore")
            await me.send_cargo(bot_id, 4, [{"id": "Nickel", "a": 20}])
            took = await me.hear("took 20 t of nickel", 10, n)
            msgs = (await api("/messages?after=0")).get("messages", [])
            asked = [m for m in msgs if m["text"] == f"{BOT}, take my ore"]
            check(took is not None and bool(asked) and asked[-1]["handled"], "'take my ore' is left to the player's game, and what it sends is said",
                  took or "not said")
            await me.say(f"{BOT}, hold")
            me.ship = None
            await asyncio.sleep(1)

            # the fights: my ship goes out beside the bot's, first as an older game's (no lock in it)
            me.ship = {"cv": 2}
            await asyncio.sleep(2)
            n = len(me.heard)
            await me.say(f"{BOT}, attack my target")
            r = await me.hear("doesn't say what you've locked", 10, n)
            check(r is not None, "'attack my target' from an older game: it says the game doesn't say",
                  r or "no answer")
            me.ship = {"cv": 3, "tg": ""}
            await asyncio.sleep(1.5)
            n = len(me.heard)
            await me.say(f"{BOT}, attack my target")
            r = await me.hear("nothing locked", 10, n)
            check(r is not None, "'attack my target' with nothing locked says so", r or "no answer")
            # (the bot hosts the level: an enemy from its own list, the nearest to it in its reach)
            b = me.bot_ship or {}
            near = sorted(((e, p) for e, p in me.enemies.items() if p[0] == b.get("rf", "")),
                          key=lambda ep: sum((ep[1][i + 1] - b.get(k, 0)) ** 2 for i, k in enumerate("xyz")))
            prey = (near or list(me.enemies.items()) or [(None, None)])[0][0]
            if check(prey is not None, "the bot's game sends its enemies", f"{len(me.enemies)} alive"):
                me.ship = {"cv": 3, "tg": prey}
                await asyncio.sleep(1.5)
                n = len(me.heard)
                await me.say(f"{BOT}, attack my target")
                r = await me.hear("engaging", 10, n)
                s = await wait_status(lambda s: s.get("order", {}).get("order") == "attack", 10)
                check(r is not None and s.get("order", {}).get("order") == "attack" and s.get("order", {}).get("target"),
                      "'attack my target' attacks what my game says I've locked", f"{prey}: {r}; {json.dumps(s.get('order', {}))}")
            n = len(me.heard)
            await me.say(f"{BOT}, defend me")
            # (its answer, whichever it is: with me already, on its way, or fighting where it is)
            r = None
            for _ in range(50):
                r = next((t for _, t in me.heard[n:] if any(w in t for w in ("with you", "on my way", "fighting here", "coming to you"))), None)
                if r:
                    break
                await asyncio.sleep(0.2)
            s = await wait_status(lambda s: s.get("order", {}).get("order") == "defend", 10)
            o = s.get("order", {})
            check(r is not None and o.get("order") == "defend"
                  and o.get("leader") == ME, "'defend me' sends it to me, to fight what's on me", f"{r}; {json.dumps(o)}")
            n = len(me.heard)
            await me.say(f"{BOT}, where are you?")
            r = await me.hear("from you", 10, n)
            msgs = (await api("/messages?after=0")).get("messages", [])
            asked = [m for m in msgs if m["text"] == f"{BOT}, where are you?"]
            check(r is not None and bool(asked) and asked[-1]["handled"], "'where are you' is answered at once, with how far, and marked handled",
                  r or "no answer")
            await me.say(f"{BOT}, hold")
            me.ship = None

            # its name: given on the comms, answered in it, and ordered by it
            bot_id = next((p.get("id") for p in (await api("/status")).get("players", []) if p.get("me")), None)
            n = len(me.heard)
            await me.say(f"{BOT}, your name is {NEW}")
            r = await me.hear(f"{NEW}: {NEW} it is", 15, n)
            s = await wait_status(lambda s: s.get("name") == NEW, 10)
            renamed = s.get("name") == NEW
            await asyncio.sleep(0.5)
            check(r is not None and renamed and me.roster.get(bot_id) == NEW,
                  f"'your name is {NEW}' renames it, and it answers in its new name",
                  f"{r or 'no answer'}; status {s.get('name')}; roster {me.roster.get(bot_id)}")
            n = len(me.heard)
            await me.say(f"{NEW}, status")
            r = await me.hear(f"{NEW}:", 15, n)
            check(r is not None and "hull" in r, "an order by its new name is carried out", r or "no answer")
            n = len(me.heard)
            await me.say(f"{BOT}, status")
            await asyncio.sleep(3)
            old = [t for _, t in me.heard[n:] if "hull" in t]
            check(not old, "and its old name no longer reaches it", "; ".join(old))
            n = len(me.heard)
            await me.say(f"{NEW}, call yourself {ME}")
            r = await me.hear("another player is called", 10, n)
            check(r is not None and (await api("/status")).get("name") == NEW, "a name another player has is refused", r or "no answer")
            # (its first name back through the API, so the name it keeps for its next start is the one it was launched with)
            n = len(me.heard)
            r = await api("/order", {"order": "rename", "target": BOT})
            back = await me.hear(f"{BOT}: {BOT} it is", 15, n)
            s = await wait_status(lambda s: s.get("name") == BOT, 10)
            renamed = s.get("name") != BOT
            check(r.get("ok") is True and back is not None and not renamed, "the API's rename gives it its first name back",
                  f"{json.dumps(r)}; {back or 'no answer'}")

            r = await api("/quit", {})
            check(r.get("ok") is True, "/quit is accepted")
        finally:
            # (stopped part way with the new name: its first name back, so its next start has it)
            if renamed:
                try:
                    await api("/order", {"order": "rename", "target": BOT})
                    await asyncio.sleep(2)
                except (urllib.error.URLError, OSError, ValueError):
                    pass
            reader.cancel()


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except Exception as e:  # a crash is a failure, said as one
        check(False, "the test ran to the end", f"{type(e).__name__}: {e}")
    log(f"done: {checks} checks, {failed} failed")
    sys.exit(1 if failed or checks == 0 else 0)
