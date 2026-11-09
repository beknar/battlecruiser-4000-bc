using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace CoopServer.Tests;

/// <summary>
/// Integration tests for the hub: real WebSocket connections (over loopback TCP, without the web
/// server round it) talking the co-op protocol: joining, the player limit, rooms and hosts,
/// passing messages on, and what the server refuses.
/// </summary>
public sealed class HubTests : IAsyncDisposable
{
    readonly ServerConfig cfg = new() { MaxPlayers = 7 };
    readonly Hub hub;
    readonly CancellationTokenSource stop = new();
    readonly TcpListener listener = new(IPAddress.Loopback, 0);
    readonly List<TestClient> clients = new();
    readonly List<Task> sessions = new();

    public HubTests()
    {
        hub = new Hub(cfg);
        listener.Start();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var c in clients) await c.DisposeAsync();
        stop.Cancel();
        try { await Task.WhenAll(sessions).WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        listener.Stop();
    }

    /// <summary>A new connection to the hub (nothing sent yet).</summary>
    async Task<TestClient> Open()
    {
        var tcp = new TcpClient();
        var connecting = tcp.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        var serverSide = await listener.AcceptTcpClientAsync();
        await connecting;
        var serverWs = WebSocket.CreateFromStream(serverSide.GetStream(), new WebSocketCreationOptions { IsServer = true });
        var clientWs = WebSocket.CreateFromStream(tcp.GetStream(), new WebSocketCreationOptions { IsServer = false });
        sessions.Add(Task.Run(async () =>
        {
            try { await hub.Run(serverWs, IPAddress.Loopback, stop.Token); }
            finally { serverSide.Dispose(); }
        }));
        var c = new TestClient(clientWs, tcp);
        clients.Add(c);
        return c;
    }

    /// <summary>A player in a level, past its welcome.</summary>
    async Task<(TestClient c, JsonObject welcome)> Join(string name, string level, double clock = 0.0, int captain = 0)
    {
        var c = await Open();
        await c.Send(new JsonObject { ["t"] = "hello", ["v"] = Hub.Protocol, ["name"] = name, ["level"] = level, ["client"] = "Test", ["captain"] = captain, ["clock"] = clock });
        var welcome = await c.Next("welcome");
        c.Id = (int)welcome["id"]!;
        return (c, welcome);
    }

    // ------------------------------------------------------------------ joining

    [Fact]
    public async Task AHelloIsWelcomed()
    {
        var (a, welcome) = await Join("Alpha", "StarSystem", clock: 100.0);
        Assert.True(a.Id >= 1);
        Assert.Equal(7, (int)welcome["max"]!);
        Assert.Equal("StarSystem", (string?)welcome["level"]);
        Assert.Equal(a.Id, (int)welcome["host"]!);                       // first in: the host
        double now = (double)welcome["now"]!, epoch = (double)welcome["epoch"]!;
        Assert.InRange(now - epoch, 99.0, 101.0);                         // the room's clock starts at the player's
        var roster = await a.Next("players");
        Assert.Equal(7, (int)roster["max"]!);
        var me = Assert.Single(roster["players"]!.AsArray());
        Assert.Equal("Alpha", (string?)me!["name"]);
        Assert.Equal("Test", (string?)me["client"]);
        Assert.Equal(1, hub.Count);
    }

    [Fact]
    public async Task SomethingOtherThanAHelloFirstIsRefused()
    {
        var c = await Open();
        await c.Send(new JsonObject { ["t"] = "ping", ["c"] = 1 });
        var reject = await c.Next("reject");
        Assert.Contains("hello", (string?)reject["why"]);
        Assert.Equal(0, hub.Count);
    }

    [Fact]
    public async Task AnotherProtocolIsRefused()
    {
        var c = await Open();
        await c.Send(new JsonObject { ["t"] = "hello", ["v"] = Hub.Protocol + 1, ["name"] = "Future", ["level"] = "L" });
        var reject = await c.Next("reject");
        Assert.Contains("protocol", (string?)reject["why"]);
        Assert.Equal(0, hub.Count);
    }

    [Fact]
    public async Task AFullServerTurnsTheNextOneAwayAndTakesThemWhenThereIsRoom()
    {
        cfg.MaxPlayers = 2;
        var (a, _) = await Join("A", "L");
        var (b, _) = await Join("B", "L");
        var c = await Open();
        await c.Send(new JsonObject { ["t"] = "hello", ["v"] = Hub.Protocol, ["name"] = "C", ["level"] = "L" });
        var reject = await c.Next("reject");
        Assert.Contains("full (2 of 2", (string?)reject["why"]);
        Assert.Equal(2, hub.Count);

        await a.DisposeAsync();
        await b.Next("leave");
        var (d, welcome) = await Join("D", "L");
        Assert.Equal(2, (int)welcome["max"]!);
        Assert.Equal(2, hub.Count);
    }

    [Fact]
    public async Task TheLimitCanBeRaisedWhileRunning()
    {
        cfg.MaxPlayers = 1;
        await Join("A", "L");
        cfg.MaxPlayers = 2;                                               // as a reload does
        var (_, welcome) = await Join("B", "L");
        Assert.Equal(2, (int)welcome["max"]!);
    }

    [Fact]
    public async Task SevenPlayersFitAndTheEighthIsTurnedAway()
    {
        for (int i = 1; i <= 7; i++) await Join($"P{i}", "StarSystem");
        Assert.Equal(7, hub.Count);
        var extra = await Open();
        await extra.Send(new JsonObject { ["t"] = "hello", ["v"] = Hub.Protocol, ["name"] = "P8", ["level"] = "StarSystem" });
        Assert.Contains("full (7 of 7", (string?)(await extra.Next("reject"))["why"]);
    }

    [Theory]
    [InlineData("\u0001Bob\u0002", "Bob")]
    [InlineData("  Spaced  ", "Spaced")]
    [InlineData("A name far too long for the roster to show", "A name far too long for ")]
    public async Task NamesAreCleanedUp(string sent, string kept)
    {
        var (a, _) = await Join(sent, "L");
        var roster = await a.Next("players");
        Assert.Equal(kept, (string?)roster["players"]![0]!["name"]);
    }

    [Fact]
    public async Task ANamelessPlayerGetsANumber()
    {
        var (a, _) = await Join("", "L");
        var roster = await a.Next("players");
        Assert.Equal($"Player {a.Id}", (string?)roster["players"]![0]!["name"]);
    }

    // ------------------------------------------------------------------ rooms and hosts

    [Fact]
    public async Task TheSecondPlayerInALevelJoinsTheFirstOnesRoom()
    {
        var (a, wa) = await Join("Alpha", "StarSystem", clock: 50.0);
        var (b, wb) = await Join("Bravo", "StarSystem", clock: 3.0);
        Assert.Equal(a.Id, (int)wb["host"]!);                             // the first one hosts
        Assert.Equal((double)wa["epoch"]!, (double)wb["epoch"]!, 6);       // one clock for the room
        var join = await a.Next("join");
        Assert.Equal("Bravo", (string?)join["p"]!["name"]);
        var roster = await b.Next("players", m => m["players"]!.AsArray().Count == 2);
        Assert.Equal(new[] { "Alpha", "Bravo" }, roster["players"]!.AsArray().Select(p => (string?)p!["name"]));
    }

    [Fact]
    public async Task WhenTheHostLeavesTheNextPlayerHosts()
    {
        var (a, _) = await Join("Alpha", "L");
        var (b, _) = await Join("Bravo", "L");
        var (c, _) = await Join("Charlie", "L");
        await a.DisposeAsync();
        foreach (var x in new[] { b, c })
        {
            Assert.Equal(a.Id, (int)(await x.Next("leave"))["id"]!);
            var room = await x.Next("room");
            Assert.Equal(b.Id, (int)room["host"]!);
        }
        var roster = await c.Next("players", m => m["players"]!.AsArray().Count == 2);
        Assert.DoesNotContain(roster["players"]!.AsArray(), p => (int)p!["id"]! == a.Id);
    }

    [Fact]
    public async Task ChangingLevelMovesAPlayerToThatRoom()
    {
        var (a, _) = await Join("Alpha", "Space");
        var (b, _) = await Join("Bravo", "Space");
        var (c, _) = await Join("Charlie", "Asteroids");
        await b.Send(new JsonObject { ["t"] = "level", ["level"] = "Asteroids", ["clock"] = 9.0 });
        var room = await b.Next("room");
        Assert.Equal("Asteroids", (string?)room["level"]);
        Assert.Equal(c.Id, (int)room["host"]!);                           // Charlie was there first
        Assert.Equal(b.Id, (int)(await a.Next("leave"))["id"]!);
        Assert.Equal("Bravo", (string?)(await c.Next("join"))["p"]!["name"]);
        var roster = await a.Next("players", m => m["players"]!.AsArray().Any(p => (string?)p!["name"] == "Bravo" && (string?)p["level"] == "Asteroids"));
        Assert.NotNull(roster);
    }

    [Fact]
    public async Task ARoomEmptiesAndStartsAfreshWithANewClock()
    {
        var (a, wa) = await Join("Alpha", "L", clock: 500.0);
        await a.DisposeAsync();
        var (b, wb) = await Join("Bravo", "L", clock: 2.0);
        Assert.Equal(b.Id, (int)wb["host"]!);
        Assert.InRange((double)wb["now"]! - (double)wb["epoch"]!, 1.0, 3.0);
    }

    // ------------------------------------------------------------------ passing messages on

    [Fact]
    public async Task AMessageGoesToTheOthersInTheRoomOnly()
    {
        var (a, _) = await Join("Alpha", "L1");
        var (b, _) = await Join("Bravo", "L1");
        var (c, _) = await Join("Charlie", "L2");
        await a.Send(new JsonObject { ["t"] = "ps", ["x"] = 12.5 });
        var got = await b.Next("ps");
        Assert.Equal(a.Id, (int)got["from"]!);
        Assert.Equal(12.5, (double)got["x"]!);
        Assert.False(await c.Gets("ps"));
        Assert.False(await a.Gets("ps"));
    }

    [Fact]
    public async Task AMessageToOnePlayerReachesOnlyThem()
    {
        var (a, _) = await Join("Alpha", "L1");
        var (b, _) = await Join("Bravo", "L1");
        var (c, _) = await Join("Charlie", "L2");
        await a.Send(new JsonObject { ["t"] = "dp", ["to"] = c.Id, ["a"] = 100 });
        var got = await c.Next("dp");
        Assert.Equal(a.Id, (int)got["from"]!);
        Assert.False(await b.Gets("dp"));
    }

    [Fact]
    public async Task AMessageForTheHostReachesOnlyTheHost()
    {
        var (a, _) = await Join("Host", "L");
        var (b, _) = await Join("Guest", "L");
        var (c, _) = await Join("Guest2", "L");
        await b.Send(new JsonObject { ["t"] = "de", ["toHost"] = true, ["tg"] = "enemy-1", ["a"] = 40 });
        var got = await a.Next("de");
        Assert.Equal(b.Id, (int)got["from"]!);
        Assert.Equal("enemy-1", (string?)got["tg"]);
        Assert.False(await c.Gets("de"));
        // the host sending to the host: nobody
        await a.Send(new JsonObject { ["t"] = "de", ["toHost"] = true });
        Assert.False(await b.Gets("de"));
    }

    [Theory]
    [InlineData("welcome")]
    [InlineData("players")]
    [InlineData("room")]
    [InlineData("join")]
    [InlineData("leave")]
    [InlineData("reject")]
    [InlineData("pong")]
    public async Task OnlyTheServerSendsItsOwnMessages(string forged)
    {
        var (a, _) = await Join("Alpha", "L");
        var (b, _) = await Join("Bravo", "L");
        await a.Next("players", m => m["players"]!.AsArray().Count == 2);
        await b.Next("players", m => m["players"]!.AsArray().Count == 2);
        await a.Send(new JsonObject { ["t"] = forged, ["host"] = a.Id, ["id"] = 99 });
        Assert.False(await b.Gets(forged));
    }

    [Fact]
    public async Task APingIsAnswered()
    {
        var (a, _) = await Join("Alpha", "L");
        await a.Send(new JsonObject { ["t"] = "ping", ["c"] = 1234.5 });
        var pong = await a.Next("pong");
        Assert.Equal(1234.5, (double)pong["c"]!);
        Assert.True((double)pong["s"]! >= 0.0);
    }

    [Fact]
    public async Task AChangeOfCaptainGoesOutInTheRoster()
    {
        var (a, _) = await Join("Alpha", "L", captain: 1);
        var (b, _) = await Join("Bravo", "L");
        await a.Send(new JsonObject { ["t"] = "captain", ["captain"] = 4 });
        var roster = await b.Next("players", m => m["players"]!.AsArray().Any(p => (int)p!["id"]! == a.Id && (int)p["captain"]! == 4));
        Assert.NotNull(roster);
    }

    [Fact]
    public async Task SomethingThatIsNotJsonIsIgnored()
    {
        var (a, _) = await Join("Alpha", "L");
        await a.SendRaw("this is not json");
        await a.Send(new JsonObject { ["t"] = "ping", ["c"] = 7 });
        Assert.Equal(7, (int)(await a.Next("pong"))["c"]!);
    }

    [Fact]
    public async Task AFloodOfMessagesGetsAPlayerDisconnected()
    {
        var (a, _) = await Join("Alpha", "L");
        var (b, _) = await Join("Bravo", "L");
        try { for (int i = 0; i < 450; i++) await a.Send(new JsonObject { ["t"] = "ps", ["i"] = i }); }
        catch (WebSocketException) { }                                    // cut off part way
        var leave = await b.Next("leave", timeout: 10);
        Assert.Equal(a.Id, (int)leave["id"]!);
        Assert.Equal(1, hub.Count);
    }

    [Fact]
    public async Task TheStatusListsThePlayersAndRooms()
    {
        var (a, _) = await Join("Alpha", "StarSystem");
        await Join("Bravo", "StarSystem");
        string status = hub.Status();
        Assert.Contains("2 of 7 players", status);
        Assert.Contains("StarSystem: 2 player(s)", status);
        Assert.Contains($"#{a.Id} Alpha (Test)  host", status);
    }
}

/// <summary>One test connection: sends JSON, and reads what comes back in the background.</summary>
sealed class TestClient : IAsyncDisposable
{
    readonly WebSocket ws;
    readonly TcpClient tcp;
    readonly Channel<JsonObject> inbox = Channel.CreateUnbounded<JsonObject>();
    readonly Task reader;
    readonly CancellationTokenSource cts = new();
    bool disposed;
    public int Id;

    public TestClient(WebSocket ws, TcpClient tcp)
    {
        this.ws = ws;
        this.tcp = tcp;
        reader = Task.Run(Read);
    }

    async Task Read()
    {
        var buffer = new byte[64 * 1024];
        var text = new MemoryStream();
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var r = await ws.ReceiveAsync(buffer, cts.Token);
                if (r.MessageType == WebSocketMessageType.Close) break;
                text.Write(buffer, 0, r.Count);
                if (!r.EndOfMessage) continue;
                if (JsonNode.Parse(text.ToArray()) is JsonObject o) inbox.Writer.TryWrite(o);
                text.SetLength(0);
            }
        }
        catch { }
        finally { inbox.Writer.TryComplete(); }
    }

    public Task Send(JsonObject msg) => SendRaw(msg.ToJsonString());

    public async Task SendRaw(string s) =>
        await ws.SendAsync(Encoding.UTF8.GetBytes(s), WebSocketMessageType.Text, true, CancellationToken.None);

    /// <summary>The next message of a kind (and matching, if given), passing over the others.</summary>
    public async Task<JsonObject> Next(string type, Func<JsonObject, bool>? match = null, double timeout = 5)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
        try
        {
            while (true)
            {
                var m = await inbox.Reader.ReadAsync(limit.Token);
                if ((string?)m["t"] == type && (match == null || match(m))) return m;
            }
        }
        catch (OperationCanceledException) { throw new TimeoutException($"no '{type}' within {timeout} s"); }
        catch (ChannelClosedException) { throw new InvalidOperationException($"connection closed while waiting for '{type}'"); }
    }

    /// <summary>Whether a message of a kind arrives within a short wait. A connection that has
    /// closed is an error (else "nothing arrived" would pass for a hub that dropped it).</summary>
    public async Task<bool> Gets(string type, double seconds = 0.4)
    {
        try { await Next(type, timeout: seconds); return true; }
        catch (TimeoutException) { return false; }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            if (ws.State == WebSocketState.Open)
                await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch { }
        cts.Cancel();
        try { await reader.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        ws.Dispose();
        tcp.Dispose();
    }
}
