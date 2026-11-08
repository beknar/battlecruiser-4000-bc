"""Tests for the co-op server, against a running one.

    python server_test.py <host> <port> [--allowtest <config file the server is using>]

- 4 players get in, a fifth is turned away (server full);
- a client speaking another protocol is turned away;
- messages are passed on: to everyone else in the room, to one player ("to"), to the host only
  ("toHost"), each with "from" added; the server's own message types can't be forged;
- the first in a room hosts it; when the host leaves, the next player hosts, and everyone is told;
- a ping is answered with the server's clock; every room has an epoch (its orbit clock);
- the status page answers.
With --allowtest (the server started with that config file, which this rewrites): an address not
on the allow-list is refused (HTTP 403), and a saved change to the file lets it in, live.
Needs the 'websockets' package.
"""
import asyncio, json, sys, time, urllib.request

import websockets

HOST, PORT = sys.argv[1], int(sys.argv[2])
URL = f"ws://{HOST}:{PORT}/coop"
results = []


def check(name, ok, detail=""):
    results.append((name, ok))
    print(f"  {'ok  ' if ok else 'FAIL'} {name}{('  (' + detail + ')') if detail else ''}", flush=True)


async def recv_until(ws, t, timeout=3.0):
    """The next message of type t (others skipped), or None."""
    end = time.monotonic() + timeout
    while time.monotonic() < end:
        try:
            m = json.loads(await asyncio.wait_for(ws.recv(), timeout=end - time.monotonic()))
        except (asyncio.TimeoutError, websockets.ConnectionClosed):
            return None
        if m.get("t") == t:
            return m
    return None


async def join(name, level="TestLevel", v=1):
    ws = await websockets.connect(URL)
    await ws.send(json.dumps({"t": "hello", "v": v, "name": name, "level": level, "client": "test", "captain": 0, "clock": 12.5}))
    return ws


async def main():
    print(f"co-op server test against {URL}")
    # ---- four players, then one too many
    # one at a time, so they join in this order (P0 first, so P0 hosts)
    clients, welcomes = [], []
    for i in range(4):
        clients.append(await join(f"P{i}"))
        welcomes.append(await recv_until(clients[-1], "welcome"))
    ids = [w["id"] if w else 0 for w in welcomes]
    check("four players are welcomed", all(ids) and len(set(ids)) == 4, f"ids {ids}")
    check("the first in a room hosts it", all(w and w["host"] == ids[0] for w in welcomes), f"host {welcomes[0]['host'] if welcomes[0] else '?'}")
    check("each room has an orbit clock that starts where its first player's was",
          welcomes[0] is not None and abs((welcomes[0]["now"] - welcomes[0]["epoch"]) - 12.5) < 1.0)
    fifth = await join("P5")
    rej = await recv_until(fifth, "reject")
    check("a fifth player is turned away (the server is full)", rej is not None and "full" in rej.get("why", ""), rej.get("why", "") if rej else "no reject")
    await fifth.close()

    # ---- passing messages on
    a, b, c, d = clients
    await a.send(json.dumps({"t": "chat", "k": "hello all"}))
    got = [await recv_until(x, "chat", 2.0) for x in (b, c, d)]
    check("a message goes to everyone else in the room, with who sent it",
          all(g and g["from"] == ids[0] and g["k"] == "hello all" for g in got))
    await b.send(json.dumps({"t": "chat", "k": "just c", "to": ids[2]}))
    got_c = await recv_until(c, "chat", 2.0)
    got_d = await recv_until(d, "chat", 0.6)
    got_a = await recv_until(a, "chat", 0.3)
    check("a message to one player goes to that player only", got_c is not None and got_c["k"] == "just c" and got_d is None and got_a is None)
    await c.send(json.dumps({"t": "chat", "k": "host", "toHost": True}))
    got_a = await recv_until(a, "chat", 2.0)
    got_b = await recv_until(b, "chat", 0.6)
    check("a message for the host goes to the host only", got_a is not None and got_a["k"] == "host" and got_b is None)
    await d.send(json.dumps({"t": "welcome", "id": 99, "host": 99}))
    forged = await recv_until(b, "welcome", 0.6)
    check("the server's own messages can't be forged", forged is None)

    # ---- the clock
    t0 = time.monotonic()
    await b.send(json.dumps({"t": "ping", "c": 42.0}))
    pong = await recv_until(b, "pong", 2.0)
    check("a ping is answered with the server's clock", pong is not None and pong["c"] == 42.0 and pong["s"] > 0, f"{(time.monotonic() - t0) * 1000:.0f} ms")

    # ---- the host leaves
    await a.close()
    rooms = [await recv_until(x, "room", 3.0) for x in (b, c, d)]
    check("when the host leaves, the next player hosts and everyone is told", all(r and r["host"] == ids[1] for r in rooms),
          f"new host {rooms[0]['host'] if rooms[0] else '?'}")
    left = await recv_until(c, "players", 2.0)
    check("everyone gets the new list of players", left is not None and len(left["players"]) == 3)
    # a player moving to another level gets its own room
    await d.send(json.dumps({"t": "level", "level": "Other", "clock": 3.0}))
    r = await recv_until(d, "room", 2.0)
    check("a player in another level is in that level's room, hosting it", r is not None and r["level"] == "Other" and r["host"] == ids[3])
    for x in (b, c, d):
        await x.close()

    # ---- the wrong protocol
    old = await join("Old", v=999)
    rej = await recv_until(old, "reject")
    check("a game speaking another protocol is turned away", rej is not None and "protocol" in rej.get("why", ""))
    await old.close()

    # ---- the status page
    try:
        page = urllib.request.urlopen(f"http://{HOST}:{PORT}/", timeout=3).read().decode()
        check("the status page answers", "co-op server" in page, page.splitlines()[0] if page else "")
    except Exception as e:
        check("the status page answers", False, str(e))

    # ---- the allow-list (the server started with this config file)
    if "--allowtest" in sys.argv:
        cfg = sys.argv[sys.argv.index("--allowtest") + 1]
        def write(allow):
            with open(cfg, "w") as f:
                json.dump({"port": PORT, "maxPlayers": 4, "allow": allow}, f)
        write(["10.123.0.0/16"])
        await asyncio.sleep(3.0)     # a change notice, or the server's 2 s check
        try:
            ws = await join("Outsider")
            refused = False
            await ws.close()
        except websockets.InvalidStatus as e:
            refused = e.response.status_code == 403
        except Exception as e:
            refused = "403" in str(e)
        check("an address not on the allow-list is refused (HTTP 403)", refused)
        write(["10.123.0.0/16", "0.0.0.0/0", "::/0"])
        await asyncio.sleep(3.0)
        try:
            ws = await join("Insider")
            w = await recv_until(ws, "welcome")
            await ws.close()
            check("a saved change to the allow-list lets it in, without a restart", w is not None)
        except Exception as e:
            check("a saved change to the allow-list lets it in, without a restart", False, str(e))

    passed = sum(1 for _, ok in results if ok)
    print(f"{'PASS' if passed == len(results) else 'FAIL'}: co-op server ({passed} of {len(results)})")
    sys.exit(0 if passed == len(results) else 1)


asyncio.run(main())
