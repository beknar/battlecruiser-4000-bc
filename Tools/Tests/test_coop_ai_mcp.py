"""Tests of the co-op AI's MCP server (Tools/CoopAI/coop_ai_mcp.py) without the game: its JSON-RPC
over stdio (the handshake, the tool list, calls, notifications, batches, bad input), and its tools
against a stand-in for the bot game's HTTP API."""
import json
import os
import subprocess
import sys
import threading
import unittest
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

HERE = Path(__file__).resolve().parent
SCRIPT = HERE.parent / "CoopAI" / "coop_ai_mcp.py"
sys.path.insert(0, str(SCRIPT.parent))
import coop_ai_mcp as M  # noqa: E402


class FakeBot(BaseHTTPRequestHandler):
    """The bot game's API, as far as the tests need it: answers, and remembers what it was sent."""
    said, orders = [], []

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
            after = int(self.path.split("after=")[1])
            msgs = [{"seq": i, "from": "Ann", "text": f"m{i}"} for i in range(1, 4) if i > after]
            self.reply(200, {"last": 3, "messages": msgs})
        elif self.path == "/destinations":
            self.reply(200, {"destinations": [{"name": "Ochre", "kind": "Planet", "km": 12.5}]})
        else:
            self.reply(404, {"error": "no such call"})

    def do_POST(self):
        body = json.loads(self.rfile.read(int(self.headers["Content-Length"])).decode() or "{}")
        if self.path == "/say":
            FakeBot.said.append(body["text"])
            self.reply(200, {"ok": True})
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
        self.assertEqual(names, ["status", "read_messages", "say", "order", "destinations", "launch_bot", "stop_bot"])
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


if __name__ == "__main__":
    unittest.main()
