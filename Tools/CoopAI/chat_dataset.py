"""Turn the co-op server's comms log into a database of conversations with the AI wingman.

The server keeps the log when its settings have "chatLog" (or it is run with --chat-log): a file a
day, chat-yyyy-MM-dd.jsonl, one JSON object a line, for every chat message and AI wingman's
"doing" line in every room, and players coming into and leaving rooms. This reads a folder of
them and writes:

  conversations.jsonl  one line per conversation: a room from when someone came into it until
                       it emptied, or a quiet spell of --gap minutes. Every message in order, with
                       who said it, whether they are an AI player, how it was made ("via": order,
                       personality, llm, fixed, paused, event for a wingman; voice for a player's
                       spoken command; none for typed), and the wingman's "doing" lines between.
  pairs.jsonl          one line per wingman answer (via order, personality, llm, fixed or paused)
                       with the player's message it answers (the latest one from a person in the
                       room in the --reply seconds before it), and what the wingman was doing.

    python3 chat_dataset.py <log folder> [--out <folder>] [--gap 30] [--reply 60] [--anonymise]

--anonymise puts "Player 1", "Player 2" ... for the people's names, in who said it and in what
was said (the AI players keep theirs). Only the standard library is used.
"""
import argparse
import glob
import json
import os
import re
import sys
from datetime import datetime

ANSWERS = {"order", "personality", "llm", "fixed", "paused"}


def when(line):
    return datetime.strptime(line["time"], "%Y-%m-%dT%H:%M:%S.%fZ")


def read(folder):
    """Every line of every day's file, in order (a line that isn't JSON is passed over)."""
    lines = []
    for path in sorted(glob.glob(os.path.join(folder, "chat-*.jsonl"))):
        with open(path, encoding="utf-8") as f:
            for raw in f:
                raw = raw.strip()
                if not raw:
                    continue
                try:
                    lines.append(json.loads(raw))
                except ValueError:
                    print(f"{path}: a line that isn't JSON, passed over", file=sys.stderr)
    lines.sort(key=when)
    return lines


def is_ai(line):
    """An AI player: it came with an AI key, or its game marked how its message was made."""
    return bool(line.get("ai")) or (line.get("via") or "") not in ("", "voice")


def conversations(lines, gap_minutes=30):
    """The lines split into conversations, a room at a time."""
    open_ = {}           # room -> the conversation going on there
    done = []

    def close(room):
        c = open_.pop(room, None)
        if c and any(t["kind"] == "chat" for t in c["turns"]):
            done.append(c)

    for line in lines:
        room, kind, t = line.get("room", ""), line.get("kind", ""), when(line)
        c = open_.get(room)
        if c and (t - c["_last"]).total_seconds() > gap_minutes * 60:
            close(room)
            c = None
        if c is None:
            c = open_[room] = {"room": room, "start": line["time"], "end": line["time"], "people": [], "ai": [], "turns": [], "_last": t}
        c["_last"] = t
        c["end"] = line["time"]
        name = line.get("name", "?")
        who = c["ai"] if is_ai(line) else c["people"]
        if name not in who and kind in ("chat", "join"):
            who.append(name)
        if kind in ("chat", "doing"):
            turn = {"time": line["time"], "kind": kind, "name": name, "ai": is_ai(line), "text": line.get("text", "")}
            if line.get("via"):
                turn["via"] = line["via"]
            c["turns"].append(turn)
        elif kind == "leave" and not line.get("with"):
            close(room)          # (the room emptied)
    for room in list(open_):
        close(room)
    for c in done:
        c.pop("_last", None)
    done.sort(key=lambda c: c["start"])
    return done


def strip_name(text, name):
    """A wingman's message without its name in front ("Wingman: mining." is "mining.")."""
    for sep in (": ", ", "):
        if text.startswith(name + sep):
            return text[len(name) + len(sep):]
    return text


def pairs(convs, reply_seconds=60):
    """Each wingman answer with the message it answers."""
    out = []
    for c in convs:
        said = None                    # the latest message from a person
        doing = {}                     # wingman's name -> its latest doing line
        for turn in c["turns"]:
            t = datetime.strptime(turn["time"], "%Y-%m-%dT%H:%M:%S.%fZ")
            if turn["kind"] == "doing":
                doing[turn["name"]] = turn["text"]
            elif not turn["ai"]:
                said = (t, turn)
            elif turn.get("via") in ANSWERS and said and (t - said[0]).total_seconds() <= reply_seconds:
                q = said[1]
                out.append({
                    "time": q["time"], "room": c["room"],
                    "player": q["name"], "said": q["text"], "spoken": q.get("via") == "voice",
                    "wingman": turn["name"], "answer": strip_name(turn["text"], turn["name"]), "via": turn["via"],
                    "doing": doing.get(turn["name"], ""),
                    "seconds": round((t - said[0]).total_seconds(), 1),
                })
    return out


def anonymise(convs):
    """People's names as Player 1, 2 ... (in the same order everywhere), in who and in what was said."""
    names = {}
    for c in convs:
        for n in c["people"]:
            names.setdefault(n, f"Player {len(names) + 1}")
    # (the longest first, so "Ann Lee" goes before "Ann")
    pattern = re.compile("|".join(re.escape(n) for n in sorted(names, key=len, reverse=True))) if names else None

    def swap(s):
        return pattern.sub(lambda m: names[m.group(0)], s) if pattern else s

    for c in convs:
        c["people"] = [names[n] for n in c["people"]]
        for turn in c["turns"]:
            if not turn["ai"]:
                turn["name"] = names.get(turn["name"], turn["name"])
            turn["text"] = swap(turn["text"])
    return convs


def write(path, rows):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        for r in rows:
            f.write(json.dumps(r, ensure_ascii=False) + "\n")


def main(argv=None):
    a = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    a.add_argument("folder", help="the server's chat log folder")
    a.add_argument("--out", default=".", help="where to write conversations.jsonl and pairs.jsonl")
    a.add_argument("--gap", type=float, default=30, help="minutes of quiet that end a conversation")
    a.add_argument("--reply", type=float, default=60, help="seconds within which an answer follows what it answers")
    a.add_argument("--anonymise", action="store_true", help="people's names as Player 1, 2 ...")
    args = a.parse_args(argv)
    convs = conversations(read(args.folder), args.gap)
    if args.anonymise:
        convs = anonymise(convs)
    ps = pairs(convs, args.reply)
    os.makedirs(args.out, exist_ok=True)
    write(os.path.join(args.out, "conversations.jsonl"), convs)
    write(os.path.join(args.out, "pairs.jsonl"), ps)
    print(f"{len(convs)} conversations, {sum(len(c['turns']) for c in convs)} lines, {len(ps)} answers paired, in {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
