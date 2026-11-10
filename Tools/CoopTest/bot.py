"""A scripted second player for the game's co-op self-test (-cooptest).

    python bot.py ws://127.0.0.1:7790/coop [level]

It joins first, so it hosts the level's room, and does what the game under test asks (the game
sends "test" notes): flies its ship beside the game's, moves an enemy, hits the game's ship and
shoots, counts the game's hits on the enemy, leaves (so the game becomes host), comes back as a
guest, counts the enemies the game sends, hits one, flies to where the game's enemies will go for
it and counts their hits. It needs the 'websockets' package (pip install websockets).
"""
import asyncio, json, sys, time

import websockets

from protocol import PROTOCOL

URL = sys.argv[1] if len(sys.argv) > 1 else "ws://127.0.0.1:7790/coop"
LEVEL = sys.argv[2] if len(sys.argv) > 2 else "StarSystem"


def log(s):
    print(f"{time.strftime('%H:%M:%S')}  bot: {s}", flush=True)


class Bot:
    def __init__(self):
        self.state = {}            # what the game asked for
        self.de = 0                # the game's hits on the enemy (while I host)
        self.dp = 0                # the game's enemies' hits on my ship (while I'm a guest)
        self.es = 0                # enemies in the last update from the game (while I'm a guest)
        self.leaving = False
        self.done = False
        self.my_id = 0
        self.game_id = 0
        self.host = 0
        self.clock0 = 0.0          # server clock at welcome, and my own then
        self.local0 = 0.0
        self.pos = None            # where my ship is (shared frame)
        self.es_told = False

    def now(self):
        return self.clock0 + (time.monotonic() - self.local0)

    async def send(self, ws, msg):
        await ws.send(json.dumps(msg))

    async def note(self, ws, text):
        await self.send(ws, {"t": "test", "note": text})
        log(f"said '{text}'")

    async def ship(self, ws):
        if self.pos is None:
            return
        x, y, z = self.pos
        await self.send(ws, {"t": "ps", "ts": self.now(), "x": x, "y": y, "z": z, "qx": 0, "qy": 0, "qz": 0, "qw": 1,
                             "vx": 0, "vy": 0, "vz": 0, "th": 0, "sh": 1, "su": True, "hu": 1, "dead": False,
                             "warp": 0, "dock": False, "surf": "", "cap": 1})

    async def session(self, name, clock):
        async with websockets.connect(URL, max_size=4 * 1024 * 1024) as ws:
            await self.send(ws, {"t": "hello", "v": PROTOCOL, "name": name, "level": LEVEL, "client": "bot", "captain": 1, "clock": clock})
            tick = 0
            while not self.done and not self.leaving:
                try:
                    raw = await asyncio.wait_for(ws.recv(), timeout=0.05)
                    await self.handle(ws, json.loads(raw))
                except asyncio.TimeoutError:
                    pass
                tick += 1
                # 20 times a second: my ship, and (as host) the enemy the game asked me to move
                await self.ship(ws)
                if self.host == self.my_id and "target" in self.state and "front" in self.state:
                    fx, fy, fz = self.state["front"]
                    await self.send(ws, {"t": "es", "ts": self.now(), "e": [{"id": self.state["target"], "x": fx, "y": fy, "z": fz,
                                                                          "qx": 0, "qy": 0, "qz": 0, "qw": 1, "hu": 0.6}]})
                    if tick % 10 == 0 and self.state.get("pf") and self.game_id:
                        # a shot from my ship toward the game's
                        bx, by, bz = self.pos
                        gx, gy, gz = self.state["game"]
                        d = [gx - bx, gy - by, gz - bz]
                        n = max(1e-3, sum(c * c for c in d) ** 0.5)
                        await self.send(ws, {"t": "fx", "src": f"p{self.my_id}", "k": "shot", "pf": self.state["pf"],
                                             "x": bx, "y": by, "z": bz, "dx": d[0] / n, "dy": d[1] / n, "dz": d[2] / n, "tg": ""})
            if self.leaving:
                await ws.close()

    async def handle(self, ws, m):
        t = m.get("t")
        if t == "welcome":
            self.my_id, self.host = m["id"], m["host"]
            self.clock0, self.local0 = m["now"], time.monotonic()
            log(f"welcome: I'm #{self.my_id}, host #{self.host}, room clock {self.now() - m['epoch']:.1f} s")
        elif t == "room":
            self.host = m["host"]
            log(f"room: host #{self.host}")
        elif t == "players":
            others = [p["id"] for p in m.get("players", []) if p["id"] != self.my_id]
            if others:
                self.game_id = others[0]
        elif t == "ps" and m.get("from") == self.game_id:
            self.state["game"] = (m["x"], m["y"], m["z"])
        elif t == "de":
            self.de += 1
        elif t == "dp":
            self.dp += 1
        elif t == "es":
            self.es = len(m.get("e") or [])
            # back as a guest: say how many enemies the game sent, the first time
            if not self.es_told and self.host != self.my_id:
                self.es_told = True
                await self.note(ws, f"es={self.es}")
        elif t == "test":
            await self.on_note(ws, m.get("note", ""))

    async def on_note(self, ws, note):
        log(f"the game says '{note}'")
        if note.startswith("ready|"):
            for part in note.split("|")[1:]:
                k, _, v = part.partition("=")
                self.state[k] = tuple(float(c) for c in v.split(",")) if k in ("front", "beside") else v
            self.pos = self.state["beside"]
            # three hits on the game's ship
            for _ in range(3):
                await self.send(ws, {"t": "dp", "to": self.game_id, "a": 300, "x": self.pos[0], "y": self.pos[1], "z": self.pos[2], "kd": 0})
        elif note == "de?":
            await self.note(ws, f"de={self.de}")
        elif note == "leave":
            self.leaving = True
        elif note.startswith("hit|"):
            target = note.split("target=")[1]
            for _ in range(3):
                await self.send(ws, {"t": "de", "toHost": True, "tg": target, "a": 200, "x": 0, "y": 0, "z": 0, "kd": 0})
        elif note.startswith("goto="):
            self.pos = tuple(float(c) for c in note[5:].split(","))
        elif note == "dp?":
            await self.note(ws, f"dp={self.dp}")
        elif note == "bye":
            self.done = True


async def main():
    bot = Bot()
    # first: the host (in first, with an orbit clock well under way)
    await bot.session("Bot", 300.0)
    if bot.done:
        return
    await asyncio.sleep(1.0)
    # then back as a guest; the es count goes to the game when it asks for a hit
    bot.leaving = False
    bot.state.pop("target", None)
    await bot.session("Bot", 0.0)
    log(f"done: hits from the game {bot.de}, hits taken as a guest {bot.dp}")


asyncio.run(main())
