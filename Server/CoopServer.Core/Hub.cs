using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using CarrierGame;

namespace CoopServer;

/// <summary>
/// The players and their rooms. Each level is a room; the first player in it is its host (that
/// player's game runs the enemies for everyone in the room), and when the host leaves the next
/// player to have joined takes over. Messages are JSON, one per WebSocket text frame:
/// - the server's own: hello, level, ping (from clients); welcome, reject, players, room, join,
///   leave, pong (to clients);
/// - anything else is passed on with "from" added: to one player ("to": id), to the room's host
///   ("toHost": true), or to everyone else in the sender's room.
/// The server keeps a clock (seconds since it started) and each room's epoch, so every game in
/// a room can run its orbits on the same time. Unless the server is open, a hello must carry one
/// of its API keys ("key"), or the player is turned away.
/// </summary>
public sealed class Hub
{
    public const int Protocol = CoopWire.Protocol;
    /// <summary>Messages that can wait to go out to one player.</summary>
    public const int QueueLength = 2000;
    /// <summary>The most of one player's lines the chat log takes in a second.</summary>
    public const int LogPerSecond = 5;
    readonly ServerConfig cfg;
    readonly object gate = new();
    readonly Dictionary<int, Player> players = new();
    readonly Dictionary<string, Room> rooms = new();
    readonly Stopwatch clock = Stopwatch.StartNew();
    int nextId = 1;
    /// <summary>The comms channel's log (null: none kept).</summary>
    readonly ChatLog? chatLog;

    public Hub(ServerConfig cfg, ChatLog? chatLog = null) { this.cfg = cfg; this.chatLog = chatLog; }

    public double Now => clock.Elapsed.TotalSeconds;
    public int Count { get { lock (gate) return players.Count; } }

    sealed class Player
    {
        public int Id;
        public string Name = "";
        public string Level = "";
        public string Client = "";
        public int Captain;
        public string Address = "";
        /// <summary>Who the API key it came with was made for ("" on an open server), and the
        /// key's hash (to drop the player if it is revoked).</summary>
        public string KeyName = "", KeyHash = "";
        public bool KeyAi;
        public double JoinedAt;
        public WebSocket Ws = null!;
        /// <summary>Ends the connection (from anywhere: it is run on the thread pool, never
        /// inside the hub's lock).</summary>
        public CancellationTokenSource? Session;
        public bool Overflowed;
        // (the chat log takes at most LogPerSecond of a player's lines a second, so a game that
        // floods the channel can't fill the disk; what it sends is still passed on)
        double logSecond = double.MinValue;
        int logged;
        public bool MayLog(double now)
        {
            if (now - logSecond >= 1.0) { logSecond = now; logged = 0; }
            return ++logged <= LogPerSecond;
        }
        // what is waiting to go out; a player this far behind is let go rather than have
        // messages dropped (a lost 'room' would leave them wrong about who hosts)
        public readonly Channel<string> Out = Channel.CreateBounded<string>(new BoundedChannelOptions(QueueLength) { FullMode = BoundedChannelFullMode.Wait });
        // (ai: joined with an AI key, which only the server knows, so a game can trust it)
        public JsonObject Public() => new() { ["id"] = Id, ["name"] = Name, ["level"] = Level, ["client"] = Client, ["captain"] = Captain, ["ai"] = KeyAi };
    }

    sealed class Room
    {
        public string Level = "";
        public double Epoch;
        public int Host;
        public readonly List<int> Members = new();
        public JsonObject Info() => new() { ["t"] = CoopWire.Room, ["level"] = Level, ["epoch"] = Epoch, ["host"] = Host };
    }

    static void Log(string s) => Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {s}");

    // A client's fields, read without trusting their types: one of the wrong type (a name sent
    // as a number, a captain as a string, a fraction where a whole number goes) reads as missing
    // instead of throwing and ending the connection.
    internal static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    internal static int? Int(JsonNode? n) => n is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;
    internal static double? Num(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;

    /// <summary>Serve one connection until it closes.</summary>
    public async Task Run(WebSocket ws, IPAddress? address, CancellationToken stop)
    {
        string from = address?.IsIPv4MappedToIPv6 == true ? address.MapToIPv4().ToString() : address?.ToString() ?? "?";
        var buffer = new byte[64 * 1024];
        var text = new MemoryStream();         // a message's bytes (it can come in pieces)
        Player? me = null;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stop);
        Task? writer = null;
        int perSecond = 0;
        double secondStart = Now;
        try
        {
            // the first thing a client sends is its hello
            var hello = await Receive(ws, buffer, text, TimeSpan.FromSeconds(10), cts.Token);
            if (hello == null || Str(hello["t"]) != CoopWire.Hello)
            {
                await Reject(ws, "expected a hello first");
                Log($"{from}: no hello, closed");
                return;
            }
            int v = Int(hello["v"]) ?? 0;
            if (v != Protocol)
            {
                await Reject(ws, $"this server speaks co-op protocol {Protocol}, the game speaks {v}: update one or the other");
                Log($"{from}: protocol {v} refused");
                return;
            }
            ApiKeys.Entry? key = null;
            if (cfg.RequireKey)
            {
                string? given = Str(hello["key"]);
                key = cfg.Keys.Check(given);
                if (key == null)
                {
                    bool none = string.IsNullOrWhiteSpace(given);
                    await Reject(ws, none
                        ? "this server needs an API key: ask its owner for one, and enter it in the co-op panel"
                        : "that API key isn't one of this server's (it may have been revoked): ask its owner for a new one");
                    Log($"{from}: {(none ? "no API key" : "an API key it doesn't know")}, refused");
                    return;
                }
            }
            string? full = null;
            lock (gate)
            {
                if (players.Count >= cfg.MaxPlayers) full = $"the server is full ({players.Count} of {cfg.MaxPlayers} players)";
                else me = new Player
                {
                    Id = nextId++,
                    Name = Clean(Str(hello["name"]), $"Player {nextId - 1}"),
                    Client = Clean(Str(hello["client"]), "?"),
                    Captain = Int(hello["captain"]) ?? 0,
                    Address = from,
                    KeyName = key?.Name ?? "",
                    KeyHash = key?.Hash ?? "",
                    KeyAi = key?.Ai ?? false,
                    JoinedAt = Now,
                    Ws = ws,
                    Session = cts,
                };
                if (me != null) players[me.Id] = me;
            }
            if (me == null)
            {
                await Reject(ws, full ?? "refused");
                Log($"{from}: refused, {full}");
                return;
            }
            writer = Task.Run(() => Writer(me, cts.Token));
            Log($"{me.Name} (#{me.Id}, {me.Client}) joined from {from}{(key != null ? $" with {(key.Ai ? "the AI key" : "the key")} for {key.Name}" : "")}: {Count}/{cfg.MaxPlayers} players");
            Join(me, Clean(Str(hello["level"]), "?"), Num(hello["clock"]) ?? 0.0, true);

            while (!cts.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                var msg = await Receive(ws, buffer, text, TimeSpan.FromSeconds(45), cts.Token);
                if (msg == null) break;
                // a flood is a bug or worse: drop the connection
                if (Now - secondStart >= 1.0) { secondStart = Now; perSecond = 0; }
                if (++perSecond > 400) { Log($"{me.Name}: over 400 messages a second, disconnected"); break; }
                // one bad message is the client's bug: say so and carry on
                try { Handle(me, msg); }
                catch (Exception e) { Log($"{me.Name}: a message was ignored ({e.GetType().Name}: {e.Message})"); }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (Exception e) { Log($"{from}: {e.GetType().Name}: {e.Message}"); }
        finally
        {
            if (me != null) Leave(me);
            cts.Cancel();
            if (writer != null) { try { await writer; } catch { } }
            try { if (ws.State == WebSocketState.Open) await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { }
        }
    }

    static string Clean(string? s, string fallback)
    {
        s = (s ?? "").Trim();
        if (s.Length == 0) return fallback;
        var sb = new StringBuilder();
        foreach (char c in s) if (!char.IsControl(c)) sb.Append(c);
        s = sb.ToString();
        return s.Length > 24 ? s[..24] : s;
    }

    static async Task Reject(WebSocket ws, string why)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(new JsonObject { ["t"] = CoopWire.Reject, ["why"] = why }.ToJsonString());
            await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, why.Length > 100 ? why[..100] : why, CancellationToken.None);
        }
        catch { }
    }

    /// <summary>One whole message (frames put together), or null when the socket closes or goes quiet.</summary>
    static async Task<JsonObject?> Receive(WebSocket ws, byte[] buffer, MemoryStream text, TimeSpan quiet, CancellationToken token)
    {
        text.SetLength(0);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(quiet);
        while (true)
        {
            WebSocketReceiveResult r;
            try { r = await ws.ReceiveAsync(buffer, timeout.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
            if (r.MessageType == WebSocketMessageType.Close) return null;
            text.Write(buffer, 0, r.Count);
            if (text.Length > 1024 * 1024) return null;            // nothing we send is this big
            if (!r.EndOfMessage) continue;
            if (r.MessageType != WebSocketMessageType.Text) { text.SetLength(0); continue; }
            // decoded whole, as a letter can be split between pieces
            try { return JsonNode.Parse(text.GetBuffer().AsSpan(0, (int)text.Length)) as JsonObject; }
            catch { text.SetLength(0); continue; }                  // not JSON: ignore it
        }
    }

    static async Task Writer(Player p, CancellationToken token)
    {
        await foreach (var s in p.Out.Reader.ReadAllAsync(token))
        {
            if (p.Ws.State != WebSocketState.Open) break;
            await p.Ws.SendAsync(Encoding.UTF8.GetBytes(s), WebSocketMessageType.Text, true, token);
        }
    }

    static void Send(Player p, JsonNode msg)
    {
        if (p.Out.Writer.TryWrite(msg.ToJsonString()) || p.Overflowed) return;
        // the queue is full: this player has fallen too far behind to catch up
        p.Overflowed = true;
        Log($"{p.Name} (#{p.Id}): {QueueLength} messages waiting to go out, disconnected");
        var session = p.Session;
        if (session != null) ThreadPool.QueueUserWorkItem(_ => { try { session.Cancel(); } catch (ObjectDisposedException) { } });
    }

    // ------------------------------------------------------------------ messages

    void Handle(Player me, JsonObject msg)
    {
        string t = Str(msg["t"]) ?? "";
        switch (t)
        {
            case CoopWire.Ping:
                Send(me, new JsonObject { ["t"] = CoopWire.Pong, ["c"] = msg["c"]?.DeepClone(), ["s"] = Now });
                return;
            case CoopWire.Level:
                Join(me, Clean(Str(msg["level"]), "?"), Num(msg["clock"]) ?? 0.0, false);
                return;
            case CoopWire.Captain:
                lock (gate) me.Captain = Int(msg["captain"]) ?? me.Captain;
                Roster();
                return;
            case CoopWire.Hello:
                return;                                             // only the first one counts
        }
        if (Array.IndexOf(CoopWire.ServerOnly, t) >= 0) return;    // only the server says these
        msg[CoopWire.From] = me.Id;
        lock (gate)
        {
            if (chatLog != null && (t == CoopWire.Chat || t == CoopWire.Doing) && me.MayLog(Now))
                chatLog.Add(Said(me, t, msg));
            if (Int(msg[CoopWire.To]) is int to && to > 0)
            {
                if (players.TryGetValue(to, out var target)) Send(target, msg);
                return;
            }
            if (!rooms.TryGetValue(me.Level, out var room)) return;
            if (msg[CoopWire.ToHost] is JsonNode th && th.GetValueKind() == System.Text.Json.JsonValueKind.True)
            {
                if (room.Host != me.Id && players.TryGetValue(room.Host, out var host)) Send(host, msg);
                return;
            }
            foreach (int id in room.Members)
                if (id != me.Id && players.TryGetValue(id, out var other)) Send(other, msg);
        }
    }

    /// <summary>Put a player in a level's room (making it if it's new: its clock starts where the
    /// player's own orbits are, so their world doesn't jump), and tell everyone.</summary>
    void Join(Player me, string level, double clientClock, bool first)
    {
        lock (gate)
        {
            if (!first && me.Level == level) return;
            if (me.Level.Length > 0) LeaveRoom(me);
            me.Level = level;
            if (!rooms.TryGetValue(level, out var room))
            {
                rooms[level] = room = new Room { Level = level, Epoch = Now - Math.Max(0.0, clientClock), Host = me.Id };
                Log($"  room '{level}' opened, host {me.Name}");
            }
            room.Members.Add(me.Id);
            var welcome = new JsonObject
            {
                ["t"] = first ? CoopWire.Welcome : CoopWire.Room,
                ["id"] = me.Id,
                ["max"] = cfg.MaxPlayers,
                ["now"] = Now,
                ["level"] = room.Level,
                ["epoch"] = room.Epoch,
                ["host"] = room.Host,
            };
            // (so the game can tell the player that what they say on comms is kept)
            if (chatLog != null) welcome["chatLog"] = true;
            Send(me, welcome);
            chatLog?.Add(Event(me, "join", room));
            foreach (int id in room.Members)
                if (id != me.Id && players.TryGetValue(id, out var other))
                    Send(other, new JsonObject { ["t"] = CoopWire.Join, ["p"] = me.Public() });
            Log($"  {me.Name} is in '{level}' ({room.Members.Count} there, host #{room.Host})");
        }
        Roster();
    }

    /// <summary>The chat log's line for a message on comms (chat) or a wingman's "doing" line: the
    /// room, who (their id, name, and whether they came with an AI key), who it was to if only
    /// one player, how it was made ("via", which an AI player's game adds: order, personality,
    /// llm ...; "voice" for a player's spoken command), and the text.</summary>
    static JsonObject Said(Player me, string t, JsonObject msg) => new()
    {
        ["room"] = me.Level,
        ["kind"] = t,
        ["from"] = me.Id,
        ["name"] = me.Name,
        ["ai"] = me.KeyAi,
        ["to"] = Int(msg[CoopWire.To]) is int to && to > 0 ? to : null,
        ["via"] = Str(msg["via"]),
        ["text"] = Str(msg["text"]) ?? "",
    };

    /// <summary>The chat log's line for a player coming into a room or leaving it (with who else
    /// is there), so a conversation can be told from the next.</summary>
    JsonObject Event(Player me, string kind, Room room)
    {
        var others = new JsonArray();
        foreach (int id in room.Members)
            if (id != me.Id && players.TryGetValue(id, out var p)) others.Add(new JsonObject { ["id"] = p.Id, ["name"] = p.Name, ["ai"] = p.KeyAi });
        return new JsonObject
        {
            ["room"] = room.Level, ["kind"] = kind, ["from"] = me.Id, ["name"] = me.Name, ["ai"] = me.KeyAi,
            ["client"] = me.Client, ["with"] = others,
        };
    }

    void LeaveRoom(Player me)
    {
        if (!rooms.TryGetValue(me.Level, out var room)) return;
        room.Members.Remove(me.Id);
        chatLog?.Add(Event(me, "leave", room));
        foreach (int id in room.Members)
            if (players.TryGetValue(id, out var other)) Send(other, new JsonObject { ["t"] = CoopWire.Leave, ["id"] = me.Id });
        if (room.Members.Count == 0)
        {
            rooms.Remove(me.Level);
            Log($"  room '{room.Level}' closed");
            return;
        }
        if (room.Host == me.Id)
        {
            room.Host = room.Members[0];
            Log($"  '{room.Level}': host is now #{room.Host}");
            foreach (int id in room.Members)
                if (players.TryGetValue(id, out var other)) Send(other, room.Info());
        }
    }

    void Leave(Player me)
    {
        lock (gate)
        {
            if (!players.Remove(me.Id)) return;
            LeaveRoom(me);
            me.Out.Writer.TryComplete();
        }
        Log($"{me.Name} (#{me.Id}) left: {Count}/{cfg.MaxPlayers} players");
        Roster();
    }

    /// <summary>Everyone gets the full list of players (who, where, which captain).</summary>
    void Roster()
    {
        lock (gate)
        {
            var list = new JsonArray();
            foreach (var p in players.Values.OrderBy(p => p.Id)) list.Add(p.Public());
            var msg = new JsonObject { ["t"] = CoopWire.Players, ["max"] = cfg.MaxPlayers, ["players"] = list };
            foreach (var p in players.Values) Send(p, msg.DeepClone());
        }
    }

    /// <summary>Drop the players whose API key has been revoked (made with --revoke-key, or
    /// in the console, or deleted from the file): told why, then disconnected. Returns how many.</summary>
    public int DropRevoked()
    {
        if (!cfg.RequireKey) return 0;
        var gone = new List<Player>();
        lock (gate)
            foreach (var p in players.Values)
                if (p.KeyHash.Length > 0 && !cfg.Keys.Has(p.KeyHash)) gone.Add(p);
        foreach (var p in gone)
        {
            Log($"{p.Name} (#{p.Id}): the API key for {p.KeyName} was revoked, disconnected");
            Send(p, new JsonObject { ["t"] = CoopWire.Reject, ["why"] = "your API key was revoked: ask the server's owner for a new one" });
            var session = p.Session;
            if (session != null) ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(300); try { session.Cancel(); } catch (ObjectDisposedException) { } });
        }
        return gone.Count;
    }

    /// <summary>A line for the status page and the console. `owner`: the console's, which says
    /// whose key each player came with and how many keys there are (the status page, open to
    /// anyone the allow-list lets in, doesn't).</summary>
    public string Status(bool owner = false)
    {
        lock (gate)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Battlecruiser 4000 BC co-op server: {players.Count} of {cfg.MaxPlayers} players, up {TimeSpan.FromSeconds(Now):hh\\:mm\\:ss}");
            sb.AppendLine(owner ? $"allowed: {cfg.AllowList}; {cfg.KeyRule}" : $"allowed: {cfg.AllowList}; {(cfg.RequireKey ? "an API key needed" : "open")}");
            foreach (var r in rooms.Values)
            {
                sb.AppendLine($"  {r.Level}: {r.Members.Count} player(s), orbit clock {Now - r.Epoch:F0} s");
                foreach (int id in r.Members)
                    if (players.TryGetValue(id, out var p))
                        sb.AppendLine($"    #{p.Id} {p.Name} ({p.Client}){(owner && p.KeyName.Length > 0 ? $", {(p.KeyAi ? "AI key" : "key")}: {p.KeyName}" : "")}{(r.Host == p.Id ? "  host" : "")}");
            }
            return sb.ToString();
        }
    }
}
