"""Listen on the co-op comms channel for the AI wingman: one line on stdout for each new message a
player sends (by default only those addressed to the wingman), as it arrives.

It is for a Claude Code session that answers players as the wingman: run it as a background
monitor (for example over SSH on the server that runs the bot game) and each printed line wakes
the session, which reads the message, checks status and cargo if the answer needs facts, and
answers with the coop-ai MCP server's say tool. While it runs, the bot knows a listener is there
and leaves talk that isn't an order unanswered for it.

    python3 listen.py [--all] [--api http://127.0.0.1:7781]

--all prints every player's message, not only those addressed to the wingman. Only the standard
library is used. Ctrl+C stops it.
"""
import argparse
import json
import sys
import time
import urllib.error
import urllib.request

WAIT = 50


def get(api, path, timeout):
    with urllib.request.urlopen(api + path, timeout=timeout) as r:
        return json.loads(r.read().decode() or "{}")


def line_for(m):
    """The printed line for a message: its number, who sent it, whether it's to the wingman, the text."""
    return f"[{m.get('seq')}] {m.get('from', '?')}{' (to wingman)' if m.get('to_me') else ''}: {m.get('text', '')}"


def wanted(m, everything):
    return not m.get("mine") and (everything or m.get("to_me"))


def main(argv=None):
    a = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    a.add_argument("--api", default="http://127.0.0.1:7781")
    a.add_argument("--all", action="store_true", help="every player's message, not only those to the wingman")
    args = a.parse_args(argv)
    api = args.api.rstrip("/")
    cursor, down = None, False
    while True:
        try:
            if cursor is None:
                # (from now on: what was said before this started isn't news)
                cursor = int(get(api, "/messages?listener=1&after=0", 10).get("last", 0))
                print(f"listening on {api} from message {cursor}", flush=True)
            r = get(api, f"/messages?listener=1&after={cursor}&wait={WAIT}", WAIT + 10)
            if down:
                print("the bot game is answering again", flush=True)
                down = False
            for m in r.get("messages", []):
                cursor = max(cursor, int(m.get("seq", cursor)))
                if wanted(m, args.all):
                    print(line_for(m), flush=True)
            cursor = max(cursor, int(r.get("last", cursor)))
        except KeyboardInterrupt:
            return 0
        except (urllib.error.URLError, OSError, ValueError) as e:
            if not down:
                print(f"the bot game isn't answering at {api} ({getattr(e, 'reason', e)}); trying again every 5 s", flush=True)
                down = True
            time.sleep(5)


if __name__ == "__main__":
    sys.exit(main())
