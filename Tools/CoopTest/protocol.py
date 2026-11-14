"""The co-op protocol version, read from the game's CoopWire.cs: the one place it is written, so
the bots can't drift from the game and the server (both build that file). And the API key the
bots join with: COOP_KEY in the environment (a server needs one unless it is open)."""
import os
import re

WIRE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "Carrier", "Scripts", "Net", "CoopWire.cs")


def _version():
    with open(WIRE, encoding="utf-8") as f:
        m = re.search(r"public const int Protocol = (\d+);", f.read())
    if not m:
        raise RuntimeError(f"no protocol version in {WIRE}")
    return int(m.group(1))


PROTOCOL = _version()
KEY = os.environ.get("COOP_KEY", "").strip()


def hello(**fields):
    """A hello message: the protocol version, the fields given, and the API key if there is one."""
    m = {"t": "hello", "v": PROTOCOL, **fields}
    if KEY and "key" not in m:
        m["key"] = KEY
    return m
