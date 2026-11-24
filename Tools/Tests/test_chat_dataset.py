"""Tests of Tools/CoopAI/chat_dataset.py, which turns the co-op server's comms log into
conversations and wingman answers paired with what they answer."""
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "CoopAI"))
import chat_dataset as cd  # noqa: E402


def line(sec, kind, name, text="", room="NineHells", ai=False, via=None, with_=None, day="2026-11-24"):
    m, s = divmod(sec, 60)
    h, m = divmod(m, 60)
    d = {"time": f"{day}T{h:02d}:{m:02d}:{s:02d}.000Z", "room": room, "kind": kind, "from": 1, "name": name, "ai": ai}
    if text:
        d["text"] = text
    if via:
        d["via"] = via
    if with_ is not None:
        d["with"] = with_
    return d


class ChatDatasetTests(unittest.TestCase):
    def test_an_answer_is_paired_with_what_it_answers(self):
        lines = [
            line(0, "join", "Wingman", ai=True, with_=[]),
            line(5, "join", "Ann", with_=[{"name": "Wingman"}]),
            line(10, "doing", "Wingman", "holding position", ai=True),
            line(20, "chat", "Ann", "Wingman, mine at the Inner Belt please", via="voice"),
            line(21, "chat", "Wingman", "Wingman: mining at the Inner Belt.", ai=True, via="order"),
            line(30, "chat", "Wingman", "Wingman: sold 40 t of ore.", ai=True, via="event"),
            line(40, "chat", "Ann", "how are you holding up?"),
            line(45, "chat", "Wingman", "Wingman: hull's fine, hold a third full.", ai=True, via="llm"),
        ]
        convs = cd.conversations(lines)
        self.assertEqual(1, len(convs))
        self.assertEqual(["Ann"], convs[0]["people"])
        self.assertEqual(["Wingman"], convs[0]["ai"])
        ps = cd.pairs(convs)
        self.assertEqual(2, len(ps))                       # (the event isn't an answer)
        self.assertEqual("Wingman, mine at the Inner Belt please", ps[0]["said"])
        self.assertTrue(ps[0]["spoken"])
        self.assertEqual("mining at the Inner Belt.", ps[0]["answer"])
        self.assertEqual("order", ps[0]["via"])
        self.assertEqual("holding position", ps[0]["doing"])
        self.assertEqual("llm", ps[1]["via"])
        self.assertFalse(ps[1]["spoken"])
        self.assertEqual(5.0, ps[1]["seconds"])

    def test_a_room_emptying_or_a_long_quiet_ends_a_conversation(self):
        lines = [
            line(0, "chat", "Ann", "hello"),
            line(10, "leave", "Ann", with_=[]),
            line(20, "chat", "Bob", "anyone?"),
            line(20 + 31 * 60, "chat", "Bob", "still here"),
        ]
        self.assertEqual(3, len(cd.conversations(lines)))

    def test_rooms_are_kept_apart(self):
        lines = [
            line(0, "chat", "Ann", "Wingman, status", room="NineHells"),
            line(1, "chat", "Bob", "hi", room="StarSystem"),
            line(2, "chat", "Wingman", "Wingman: hull 100 %.", room="NineHells", ai=True, via="order"),
        ]
        ps = cd.pairs(cd.conversations(lines))
        self.assertEqual(1, len(ps))
        self.assertEqual("Ann", ps[0]["player"])

    def test_an_answer_long_after_isnt_paired(self):
        lines = [
            line(0, "chat", "Ann", "Wingman, how's it going?"),
            line(200, "chat", "Wingman", "Wingman: quiet out here.", ai=True, via="personality"),
        ]
        self.assertEqual([], cd.pairs(cd.conversations(lines)))

    def test_anonymise_swaps_peoples_names_everywhere(self):
        lines = [
            line(0, "chat", "Ann Lee", "Wingman, follow Bob"),
            line(1, "chat", "Bob", "thanks Ann Lee"),
            line(2, "chat", "Wingman", "Wingman: forming up on Bob.", ai=True, via="order"),
        ]
        convs = cd.anonymise(cd.conversations(lines))
        turns = convs[0]["turns"]
        self.assertEqual(["Player 1", "Player 2"], convs[0]["people"])
        self.assertEqual("Wingman, follow Player 2", turns[0]["text"])
        self.assertEqual("thanks Player 1", turns[1]["text"])
        self.assertEqual("Wingman", turns[2]["name"])
        self.assertEqual("Wingman: forming up on Player 2.", turns[2]["text"])

    def test_the_files_are_read_and_written(self):
        with tempfile.TemporaryDirectory() as d:
            logs, out = os.path.join(d, "logs"), os.path.join(d, "out")
            os.makedirs(logs)
            with open(os.path.join(logs, "chat-2026-11-24.jsonl"), "w", encoding="utf-8") as f:
                f.write(json.dumps(line(0, "chat", "Ann", "Wingman, cargo")) + "\n")
                f.write("not json\n")
                f.write(json.dumps(line(1, "chat", "Wingman", "Wingman: hold empty.", ai=True, via="order")) + "\n")
            self.assertEqual(0, cd.main([logs, "--out", out]))
            with open(os.path.join(out, "pairs.jsonl"), encoding="utf-8") as f:
                rows = [json.loads(x) for x in f]
            self.assertEqual("hold empty.", rows[0]["answer"])


if __name__ == "__main__":
    unittest.main()
