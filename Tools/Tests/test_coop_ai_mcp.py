"""Tests of the co-op AI's MCP server (Tools/CoopAI/coop_ai_mcp.py) without the game: its JSON-RPC
over stdio (the handshake, the tool list, calls, notifications, batches, bad input), and its tools
against a stand-in for the bot game's HTTP API."""
import json
import os
import subprocess
import sys
import threading
import unittest
import urllib.parse
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

HERE = Path(__file__).resolve().parent
SCRIPT = HERE.parent / "CoopAI" / "coop_ai_mcp.py"
sys.path.insert(0, str(SCRIPT.parent))
import coop_ai_mcp as M  # noqa: E402


class FakeBot(BaseHTTPRequestHandler):
    """The bot game's API, as far as the tests need it: answers, and remembers what it was sent."""
    said, orders, queries, modes = [], [], [], []
    mode = "llm"

    def log_message(self, *a):
        pass

    def reply(self, code, obj):
        body = json.dumps(obj).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path == "/status":
            self.reply(200, {"name": "Wingman", "connected": True, "order": {"order": "hold"}})
        elif self.path.startswith("/messages"):
            q = urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query)
            FakeBot.queries.append(q)
            after = int(q["after"][0])
            msgs = [{"seq": i, "from": "Ann", "text": f"m{i}"} for i in range(1, 4) if i > after]
            self.reply(200, {"last": 3, "chat_mode": FakeBot.mode, "messages": msgs})
        elif self.path == "/cargo":
            self.reply(200, {"capacity_t": 600, "used_t": 46, "credits": 918, "items": [{"id": "Iron", "amount": 46}]})
        elif self.path == "/chatmode":
            self.reply(200, {"mode": FakeBot.mode, "listener_seen_s": 4.5, "listening": True, "resumes_to": FakeBot.mode})
        elif self.path == "/destinations":
            self.reply(200, {"destinations": [{"name": "Ochre", "kind": "Planet", "km": 12.5}]})
        else:
            self.reply(404, {"error": "no such call"})

    def do_POST(self):
        body = json.loads(self.rfile.read(int(self.headers["Content-Length"])).decode() or "{}")
        if self.path == "/say":
            FakeBot.said.append(body["text"])
            self.reply(200, {"ok": True})
        elif self.path == "/chatmode":
            FakeBot.modes.append(body["mode"])
            FakeBot.mode = {"on": "llm", "off": "personality", "pause": "paused", "resume": "llm", "unpause": "llm"}.get(body["mode"], body["mode"])
            self.reply(200, {"ok": True, "previous": "llm", "mode": FakeBot.mode, "listener_seen_s": 4.5})
        elif self.path == "/order":
            FakeBot.orders.append(body)
            ok = body["order"] != "dock"
            self.reply(200, {"ok": ok, "answer": "holding position." if ok else "can't dock here"})
        else:
            self.reply(404, {"error": "no such call"})


def call(name, args=None, id_=1):
    return M.handle({"jsonrpc": "2.0", "id": id_, "method": "tools/call", "params": {"name": name, "arguments": args or {}}})


def body(reply):
    return reply["result"]["content"][0]["text"]


class Protocol(unittest.TestCase):
    def test_initialize_answers_the_version_asked_for_if_known(self):
        r = M.handle({"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {"protocolVersion": "2025-03-26"}})
        self.assertEqual(r["result"]["protocolVersion"], "2025-03-26")
        self.assertIn("tools", r["result"]["capabilities"])
        r = M.handle({"jsonrpc": "2.0", "id": 2, "method": "initialize", "params": {"protocolVersion": "1999-01-01"}})
        self.assertEqual(r["result"]["protocolVersion"], M.PROTOCOLS[0])

    def test_tools_are_listed_with_schemas(self):
        tools = M.handle({"jsonrpc": "2.0", "id": 1, "method": "tools/list"})["result"]["tools"]
        names = [t["name"] for t in tools]
        self.assertEqual(names, ["status", "read_messages", "wait_messages", "cargo", "give_credits", "say", "order", "destinations",
                                 "get_chat_mode", "set_chat_mode", "launch_bot", "stop_bot"])
        order = next(t for t in tools if t["name"] == "order")
        self.assertEqual(order["inputSchema"]["required"], ["order"])
        self.assertEqual(order["inputSchema"]["properties"]["order"]["enum"], M.ORDERS)

    def test_notifications_get_no_answer(self):
        self.assertIsNone(M.handle({"jsonrpc": "2.0", "method": "notifications/initialized"}))

    def test_unknown_method_and_tool_are_errors(self):
        self.assertEqual(M.handle({"jsonrpc": "2.0", "id": 5, "method": "nope"})["error"]["code"], -32601)
        self.assertEqual(call("nope")["error"]["code"], -32602)

    def test_serve_handles_bad_json_batches_and_blank_lines(self):
        out = []
        M.serve(["", "not json", json.dumps([{"jsonrpc": "2.0", "id": 1, "method": "ping"},
                                             {"jsonrpc": "2.0", "method": "notifications/initialized"}])], out.append)
        self.assertEqual(json.loads(out[0])["error"]["code"], -32700)
        batch = json.loads(out[1])
        self.assertEqual(len(batch), 1)
        self.assertEqual(batch[0], {"jsonrpc": "2.0", "id": 1, "result": {}})

    def test_bad_arguments_are_tool_errors_not_crashes(self):
        r = call("say", {"text": "   "})
        self.assertTrue(r["result"]["isError"])
        r = call("order", {"order": "dance"})
        self.assertTrue(r["result"]["isError"])
        self.assertIn("unknown order", body(r))

    def test_over_stdio_a_message_a_line(self):
        lines = [{"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {"protocolVersion": "2025-06-18"}},
                 {"jsonrpc": "2.0", "method": "notifications/initialized"},
                 {"jsonrpc": "2.0", "id": 2, "method": "tools/list"}]
        p = subprocess.run([sys.executable, "-B", str(SCRIPT)], input="\n".join(json.dumps(l) for l in lines) + "\n",
                           capture_output=True, text=True, encoding="utf-8", timeout=30,
                           env=dict(os.environ, COOP_BOT_API="http://127.0.0.1:9"))
        replies = [json.loads(l) for l in p.stdout.splitlines() if l.strip()]
        self.assertEqual([r["id"] for r in replies], [1, 2])
        self.assertEqual(replies[0]["result"]["serverInfo"]["name"], "coop-ai")


class Tools(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.http = HTTPServer(("127.0.0.1", 0), FakeBot)
        threading.Thread(target=cls.http.serve_forever, daemon=True).start()
        cls.api_was = M.API
        M.API = f"http://127.0.0.1:{cls.http.server_address[1]}"

    @classmethod
    def tearDownClass(cls):
        cls.http.shutdown()
        cls.http.server_close()
        M.API = cls.api_was

    def setUp(self):
        M.cursor = 0
        FakeBot.said.clear()
        FakeBot.orders.clear()
        FakeBot.queries.clear()
        FakeBot.modes.clear()
        FakeBot.mode = "llm"

    def test_status(self):
        r = call("status")
        self.assertFalse(r["result"]["isError"])
        self.assertEqual(json.loads(body(r))["name"], "Wingman")

    def test_read_messages_moves_on_from_the_last_read(self):
        first = json.loads(body(call("read_messages")))
        self.assertEqual([m["seq"] for m in first["messages"]], [1, 2, 3])
        again = json.loads(body(call("read_messages")))
        self.assertEqual(again["messages"], [])
        older = json.loads(body(call("read_messages", {"after": 1})))
        self.assertEqual([m["seq"] for m in older["messages"]], [2, 3])

    def test_reading_marks_a_listener(self):
        call("read_messages")
        self.assertEqual(FakeBot.queries[-1]["listener"], ["1"])

    def test_wait_messages_waits_from_the_last_read_and_caps_the_wait(self):
        first = json.loads(body(call("wait_messages", {"timeout": 5})))
        self.assertEqual([m["seq"] for m in first["messages"]], [1, 2, 3])
        self.assertEqual(FakeBot.queries[-1]["wait"], ["5"])
        self.assertEqual(FakeBot.queries[-1]["listener"], ["1"])
        call("wait_messages", {"timeout": 9999})
        self.assertEqual(FakeBot.queries[-1]["after"], ["3"])
        self.assertEqual(FakeBot.queries[-1]["wait"], [str(M.WAIT_MAX)])

    def test_cargo(self):
        r = call("cargo")
        self.assertFalse(r["result"]["isError"])
        self.assertEqual(json.loads(body(r))["credits"], 918)

    def test_give_credits_is_the_give_order(self):
        self.assertFalse(call("give_credits", {"amount": "500", "to": "Ann"})["result"]["isError"])
        self.assertFalse(call("give_credits", {})["result"]["isError"])
        self.assertFalse(call("give_credits", {"amount": 250.7})["result"]["isError"])
        self.assertEqual(FakeBot.orders, [{"order": "give", "target": "500 to Ann"}, {"order": "give", "target": "all"},
                                          {"order": "give", "target": "250"}])
        self.assertTrue(call("give_credits", {"amount": "lots"})["result"]["isError"])
        self.assertTrue(call("give_credits", {"amount": "0"})["result"]["isError"])
        self.assertTrue(call("give_credits", {"amount": "inf"})["result"]["isError"])
        self.assertFalse(call("give_credits", {"amount": "1,500"})["result"]["isError"])
        self.assertEqual(FakeBot.orders[-1], {"order": "give", "target": "1500"})

    def test_a_restarted_bot_game_is_read_from_its_first_message(self):
        M.cursor = 50          # (the old game had got that far; the fake one is at 3)
        r = json.loads(body(call("read_messages")))
        self.assertEqual([m["seq"] for m in r["messages"]], [1, 2, 3])
        M.cursor = 50
        r = json.loads(body(call("wait_messages", {"timeout": 1})))
        self.assertEqual([m["seq"] for m in r["messages"]], [1, 2, 3])

    def test_say_and_order_reach_the_game(self):
        self.assertFalse(call("say", {"text": " on my way "})["result"]["isError"])
        self.assertEqual(FakeBot.said, ["on my way"])
        r = call("order", {"order": "HOLD"})
        self.assertFalse(r["result"]["isError"])
        self.assertEqual(FakeBot.orders, [{"order": "hold", "target": ""}])

    def test_an_order_the_game_refuses_is_an_error(self):
        r = call("order", {"order": "dock"})
        self.assertTrue(r["result"]["isError"])
        self.assertIn("can't dock", body(r))

    def test_chat_mode_is_read_and_switched(self):
        r = json.loads(body(call("get_chat_mode")))
        self.assertEqual(r["mode"], "llm")
        self.assertEqual(r["listener_seen_s"], 4.5)
        self.assertFalse(call("set_chat_mode", {"mode": "OFF"})["result"]["isError"])
        self.assertEqual(FakeBot.modes, ["off"])
        self.assertEqual(json.loads(body(call("get_chat_mode")))["mode"], "personality")
        self.assertEqual(json.loads(body(call("read_messages")))["chat_mode"], "personality")
        bad = call("set_chat_mode", {"mode": "loud"})
        self.assertTrue(bad["result"]["isError"])
        self.assertIn("unknown chat mode", body(bad))
        self.assertEqual(FakeBot.modes, ["off"], "a mode it doesn't know never reaches the game")

    def test_launch_bot_refuses_an_unknown_chat_mode(self):
        r = call("launch_bot", {"game": str(SCRIPT), "chatmode": "loud"})
        self.assertTrue(r["result"]["isError"])
        self.assertIn("chatmode", body(r))

    def test_destinations(self):
        self.assertEqual(json.loads(body(call("destinations")))["destinations"][0]["name"], "Ochre")

    def test_no_game_is_a_tool_error_saying_how_to_start_it(self):
        M.API = "http://127.0.0.1:9"
        try:
            r = call("status")
        finally:
            M.API = f"http://127.0.0.1:{self.http.server_address[1]}"
        self.assertTrue(r["result"]["isError"])
        self.assertIn("launch_bot", body(r))

    def test_launch_bot_without_a_game_says_so(self):
        r = call("launch_bot", {"game": str(HERE / "no_such_game.exe")})
        self.assertTrue(r["result"]["isError"])
        self.assertIn("no game at", body(r))

    def test_stop_bot_with_nothing_running(self):
        self.assertTrue(call("stop_bot")["result"]["isError"])


class Listener(unittest.TestCase):
    def test_lines_and_what_is_printed(self):
        import listen
        m = {"seq": 7, "from": "Ann", "to_me": True, "mine": False, "text": "how's the hold?"}
        self.assertEqual(listen.line_for(m), "[7] Ann (to wingman): how's the hold?")
        self.assertTrue(listen.wanted(m, False))
        self.assertFalse(listen.wanted(dict(m, to_me=False), False))
        self.assertTrue(listen.wanted(dict(m, to_me=False), True))
        self.assertFalse(listen.wanted(dict(m, mine=True), True), "the bot's own lines are never news")
        self.assertFalse(listen.wanted(m, False, "personality"), "the bot answers talk itself")
        self.assertFalse(listen.wanted(m, True, "paused"), "nobody answers talk")
        self.assertTrue(listen.wanted(m, False, "llm"))
        self.assertFalse(listen.wanted(dict(m, handled=True), True), "an order the bot carried out itself isn't given again")

    def test_a_change_of_chat_mode_is_printed(self):
        import listen
        self.assertIsNone(listen.mode_note("llm", "llm"))
        self.assertIsNone(listen.mode_note("llm", None), "an older bot that doesn't say")
        self.assertIn("personality", listen.mode_note("llm", "personality"))
        self.assertIn("still listening", listen.mode_note("personality", "paused"))
        self.assertIn("chat mode llm", listen.mode_note("paused", "llm"))


if __name__ == "__main__":
    unittest.main()
