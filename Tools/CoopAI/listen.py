"""Listen on the co-op comms channel for the AI wingman: one line on stdout for each new message a
player sends (by default only those addressed to the wingman), as it arrives.

It is for a Claude Code session that answers players as the wingman: run it as a background
monitor (for example over SSH on the server that runs the bot game) and each printed line wakes
the session, which reads the message, checks status and cargo if the answer needs facts, and
answers with the coop-ai MCP server's say tool. While it runs, the bot knows a listener is there
and leaves talk that isn't an order unanswered for it.

That is so in the bot's chat mode "llm". In "personality" (the LLM off: the bot's built-in
personality answers talk) and "paused" (nobody answers talk), it keeps listening but prints only
when the mode changes, not the messages, so nothing wakes the session to answer them.

    python3 listen.py [--all] [--api http://127.0.0.1:7781]

--all prints every player's message (not the bot's own), not only those addressed to the wingman. Only the standard
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


def wanted(m, everything, mode="llm"):
    """Is a message printed? Players' messages to the wingman (any message with --all), and only
    while the chat mode is llm: in the others the bot answers talk itself, or nobody does."""
    return mode == "llm" and not m.get("mine") and (everything or m.get("to_me"))


def mode_note(was, now):
    """The line printed when the bot's chat mode changes (None if it hasn't, or isn't known)."""
    if not now or now == was:
        return None
    if now == "llm":
        return "chat mode llm: talk to the wingman is for you to answer again"
    what = "the bot's own personality answers talk" if now == "personality" else "nobody answers talk"
    return f"chat mode {now}: {what}; still listening, messages not printed (orders still work)"


def main(argv=None):
    a = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    a.add_argument("--api", default="http://127.0.0.1:7781")
    a.add_argument("--all", action="store_true", help="every player's message, not only those to the wingman")
    args = a.parse_args(argv)
    api = args.api.rstrip("/")
    cursor, down, mode = None, False, "llm"
    while True:
        try:
            if cursor is None:
                # (from now on: what was said before this started isn't news)
                first = get(api, "/messages?listener=1&after=0", 10)
                cursor = int(first.get("last", 0))
                print(f"listening on {api} from message {cursor}", flush=True)
                note = mode_note("llm", first.get("chat_mode"))
                if note:
                    print(note, flush=True)
                mode = first.get("chat_mode") or mode
            if down:
                # (it may have started again, numbering its messages from 0: from its latest)
                last = int(get(api, "/messages?listener=1&after=0", 10).get("last", 0))
                if last < cursor:
                    cursor = last
                print(f"the bot game is answering again (from message {cursor})", flush=True)
                down = False
            r = get(api, f"/messages?listener=1&after={cursor}&wait={WAIT}", WAIT + 10)
            if int(r.get("last", cursor)) < cursor:
                cursor = int(r.get("last", 0))
                print(f"the bot game started again: from message {cursor}", flush=True)
                continue
            note = mode_note(mode, r.get("chat_mode"))
            if note:
                print(note, flush=True)
            mode = r.get("chat_mode") or mode
            for m in r.get("messages", []):
                cursor = max(cursor, int(m.get("seq", cursor)))
                if wanted(m, args.all, mode):
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
