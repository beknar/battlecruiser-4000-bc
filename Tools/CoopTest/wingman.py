"""A scripted wingman for the co-op scene of the demo reel (StarSystem.exe -reel <dir> -all
-reelcoop <host:port>).

    python wingman.py ws://127.0.0.1:7790/coop <name> <right,up,forward> [captain]

It waits in a room of its own ("Lobby") until a game joins the server, then goes to that game's
level as a guest (so the game hosts and runs the enemies as usual) and flies in formation with
the game's ship: at the slot given (metres to the right, up and forward of it, in the ship's own
frame), turning as it turns. Its updates carry the game ship's own timestamps, so the two stay
in step however the game's clock runs. The game can move it ("slot|<name>=x,y,z") and have it
fire a volley ahead ("fire|pf=<shot>|s=<seconds>"). It leaves when the game says "bye" or goes.
Needs the 'websockets' package.
"""
import asyncio, json, sys, time

import websockets

URL = sys.argv[1] if len(sys.argv) > 1 else "ws://127.0.0.1:7790/coop"
NAME = sys.argv[2] if len(sys.argv) > 2 else "Wing"
SLOT = tuple(float(c) for c in (sys.argv[3] if len(sys.argv) > 3 else "250,20,-200").split(","))
CAPTAIN = int(sys.argv[4]) if len(sys.argv) > 4 else 1


def log(s):
    print(f"{time.strftime('%H:%M:%S')}  {NAME}: {s}", flush=True)


def rotate(q, v):
    """v turned by the quaternion q = (x, y, z, w)."""
    x, y, z, w = q
    vx, vy, vz = v
    # t = 2 * cross(q.xyz, v); v' = v + w * t + cross(q.xyz, t)
    tx, ty, tz = 2 * (y * vz - z * vy), 2 * (z * vx - x * vz), 2 * (x * vy - y * vx)
    return (vx + w * tx + (y * tz - z * ty), vy + w * ty + (z * tx - x * tz), vz + w * tz + (x * ty - y * tx))


class Wingman:
    def __init__(self):
        self.my_id = 0
        self.game_id = 0
        self.level = ""
        self.slot = SLOT
        self.firing_until = 0.0
        self.pf = ""
        self.next_shot = 0.0
        self.next_ping = 0.0
        self.last = None          # the game ship's latest update
        self.done = False

    async def send(self, ws, msg):
        await ws.send(json.dumps(msg))

    def where(self, m):
        """My place: the slot, in the game ship's frame (shared coordinates)."""
        q = (m["qx"], m["qy"], m["qz"], m["qw"])
        r, u, f = self.slot
        ox, oy, oz = rotate(q, (r, u, f))      # Unity: x right, y up, z forward
        return (m["x"] + ox, m["y"] + oy, m["z"] + oz), q

    async def follow(self, ws, m):
        (x, y, z), q = self.where(m)
        await self.send(ws, {"t": "ps", "ts": m["ts"], "x": x, "y": y, "z": z, "qx": q[0], "qy": q[1], "qz": q[2], "qw": q[3],
                             "vx": m.get("vx", 0), "vy": m.get("vy", 0), "vz": m.get("vz", 0), "th": m.get("th", 0),
                             "sh": 1, "su": True, "hu": 1, "dead": False, "warp": 0, "dock": False, "surf": "", "cap": CAPTAIN})

    async def shoot(self, ws):
        m = self.last
        if m is None or not self.pf:
            return
        (x, y, z), q = self.where(m)
        fx, fy, fz = rotate(q, (0.0, 0.0, 1.0))
        # from the bow, a touch apart each time (two guns)
        side = 6.0 if int(time.monotonic() * 7) % 2 else -6.0
        sx, sy, sz = rotate(q, (side, 4.0, 40.0))
        await self.send(ws, {"t": "fx", "src": f"p{self.my_id}", "k": "shot", "pf": self.pf,
                             "x": x + sx, "y": y + sy, "z": z + sz, "dx": fx, "dy": fy, "dz": fz, "tg": ""})

    async def handle(self, ws, m):
        t = m.get("t")
        if t == "welcome":
            self.my_id = m["id"]
            log(f"on the server as #{self.my_id}, waiting for a game")
        elif t == "players":
            game = next((p for p in m.get("players", []) if p["id"] != self.my_id and p.get("client") not in ("wingman", "bot")
                         and p.get("level") not in ("", "Lobby")), None)
            if game is not None and not self.game_id:
                self.game_id, self.level = game["id"], game["level"]
                log(f"{game['name']} (#{self.game_id}) is in '{self.level}': joining")
                await self.send(ws, {"t": "level", "level": self.level, "clock": 0.0})
            elif self.game_id and not any(p["id"] == self.game_id for p in m.get("players", [])):
                log("the game has gone")
                self.done = True
        elif t == "ps" and m.get("from") == self.game_id:
            self.last = m
            await self.follow(ws, m)
        elif t == "test":
            note = m.get("note", "")
            if note.startswith("slot|"):
                for part in note[5:].split("|"):
                    who, _, v = part.partition("=")
                    if who == NAME:
                        self.slot = tuple(float(c) for c in v.split(","))
                        log(f"slot now {self.slot}")
            elif note.startswith("fire|"):
                kv = dict(p.partition("=")[::2] for p in note.split("|")[1:])
                self.pf = kv.get("pf", self.pf)
                self.firing_until = time.monotonic() + float(kv.get("s", "2.5"))
                log(f"firing '{self.pf}'")
            elif note == "bye":
                self.done = True

    async def run(self):
        async with websockets.connect(URL) as ws:
            await self.send(ws, {"t": "hello", "v": 1, "name": NAME, "level": "Lobby", "client": "wingman", "captain": CAPTAIN, "clock": 0.0})
            while not self.done:
                try:
                    raw = await asyncio.wait_for(ws.recv(), timeout=0.03)
                    await self.handle(ws, json.loads(raw))
                except asyncio.TimeoutError:
                    pass
                now = time.monotonic()
                # the server drops a player it hasn't heard from in 45 s: a ping now and then
                if now >= self.next_ping:
                    self.next_ping = now + 10.0
                    await self.send(ws, {"t": "ping", "c": now})
                if now < self.firing_until and now >= self.next_shot:
                    self.next_shot = now + 0.14
                    await self.shoot(ws)
            log("leaving")


async def main():
    # keep trying until the server is up
    for _ in range(60):
        try:
            await Wingman().run()
            return
        except (OSError, websockets.InvalidHandshake):
            await asyncio.sleep(1.0)


asyncio.run(main())
