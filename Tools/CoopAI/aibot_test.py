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
  - the MCP server's status and say tools work against the live bot
  - /quit closes it (the runner checks the game ends)

Prints a line a check ("[AIBot] PASS ..." or "[AIBot] FAIL ...") and exits 1 if any failed.
Needs the 'websockets' package.
"""
import asyncio, json, os, subprocess, sys, time, urllib.error, urllib.request
from pathlib import Path

import websockets

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "CoopTest"))
from protocol import PROTOCOL  # noqa: E402

API = sys.argv[1].rstrip("/") if len(sys.argv) > 1 else "http://127.0.0.1:7781"
URL = sys.argv[2] if len(sys.argv) > 2 else "ws://127.0.0.1:7792/coop"
BOT = sys.argv[3] if len(sys.argv) > 3 else "Wingman"
ME = "Tester"

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

    async def run(self):
        last_ping = 0.0
        while True:
            try:
                raw = await asyncio.wait_for(self.ws.recv(), timeout=0.5)
                m = json.loads(raw)
                if m.get("t") == "welcome":
                    self.my_id = m["id"]
                elif m.get("t") == "chat":
                    self.heard.append((m.get("from"), m.get("text", "")))
                    log(f"heard #{m.get('from')}: {m.get('text', '')}")
            except asyncio.TimeoutError:
                pass
            if time.monotonic() - last_ping > 2.0:
                last_ping = time.monotonic()
                await self.ws.send(json.dumps({"t": "ping", "c": last_ping}))

    async def say(self, text):
        log(f"say: {text}")
        await self.ws.send(json.dumps({"t": "chat", "text": text}))

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
    t0 = time.monotonic()
    s = await wait_status(lambda s: s.get("connected") and "ship" in s, 240)
    if not check(bool(s.get("connected")), "the bot game is up and connected", f"after {time.monotonic() - t0:.0f} s"):
        return
    check("ship" in s, "its pilot has a ship", json.dumps(s.get("ship", {}))[:200])
    level = s.get("level", "StarSystem")

    async with websockets.connect(URL, max_size=4 * 1024 * 1024) as ws:
        await ws.send(json.dumps({"t": "hello", "v": PROTOCOL, "name": ME, "level": level, "client": "tester", "captain": 0, "clock": 0}))
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

            r = await api("/quit", {})
            check(r.get("ok") is True, "/quit is accepted")
        finally:
            reader.cancel()


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except Exception as e:  # a crash is a failure, said as one
        check(False, "the test ran to the end", f"{type(e).__name__}: {e}")
    log(f"done: {checks} checks, {failed} failed")
    sys.exit(1 if failed or checks == 0 else 0)
