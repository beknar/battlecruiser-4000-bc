using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json.Nodes;

namespace CoopServer.Tests;

/// <summary>
/// The comms channel's log: what goes in it (chat and wingman "doing" lines from every room,
/// players coming and going), what doesn't (anything else), a file a day, the welcome's notice to
/// games, and no log at all unless one is set.
/// </summary>
public sealed class ChatLogTests : IAsyncDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "coopchatlog-" + Guid.NewGuid().ToString("N"));
    readonly CancellationTokenSource stop = new();
    readonly TcpListener listener = new(IPAddress.Loopback, 0);
    readonly List<TestClient> clients = new();
    readonly List<Task> sessions = new();
    DateTime today = new(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
    ChatLog? log;
    Hub hub = null!;

    public ChatLogTests() => listener.Start();

    public async ValueTask DisposeAsync()
    {
        foreach (var c in clients) await c.DisposeAsync();
        stop.Cancel();
        try { await Task.WhenAll(sessions).WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        listener.Stop();
        log?.Dispose();
        try { Directory.Delete(folder, true); } catch { }
    }

    void Start(bool logging)
    {
        log = logging ? new ChatLog(folder, clock: () => today) : null;
        hub = new Hub(new ServerConfig { MaxPlayers = 7, RequireKey = false }, log);
    }

    async Task<(TestClient c, JsonObject welcome)> Join(string name, string level)
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
        await c.Send(new JsonObject { ["t"] = "hello", ["v"] = Hub.Protocol, ["name"] = name, ["level"] = level, ["client"] = "Test", ["clock"] = 0.0 });
        var welcome = await c.Next("welcome");
        c.Id = (int)welcome["id"]!;
        return (c, welcome);
    }

    /// <summary>The log's lines so far (once what was sent has been passed on and written).</summary>
    List<JsonObject> Lines(DateTime? day = null)
    {
        string file = log!.FileFor(day ?? today);
        // (written by the log's own thread: given a moment)
        for (int i = 0; i < 50 && !File.Exists(file); i++) Thread.Sleep(20);
        Thread.Sleep(100);
        return File.ReadAllLines(file).Select(l => (JsonObject)JsonNode.Parse(l)!).ToList();
    }

    [Fact]
    public async Task ChatFromEveryRoomIsLoggedWithWhoAndWhere()
    {
        Start(logging: true);
        var (a, _) = await Join("Alpha", "NineHells");
        var (b, _) = await Join("Bravo", "NineHells");
        var (c, _) = await Join("Charlie", "StarSystem");
        await a.Send(new JsonObject { ["t"] = "chat", ["text"] = "Wingman, mine at the Inner Belt please" });
        await b.Next("chat");
        await b.Send(new JsonObject { ["t"] = "chat", ["text"] = "Wingman: mining at the Inner Belt.", ["via"] = "order" });
        await a.Next("chat");
        await c.Send(new JsonObject { ["t"] = "chat", ["text"] = "anyone about?", ["via"] = "voice" });
        await Task.Delay(100);
        var said = Lines().Where(l => (string?)l["kind"] == "chat").ToList();
        Assert.Equal(3, said.Count);
        Assert.Equal("NineHells", (string?)said[0]["room"]);
        Assert.Equal("Alpha", (string?)said[0]["name"]);
        Assert.Equal(a.Id, (int)said[0]["from"]!);
        Assert.False((bool)said[0]["ai"]!);
        Assert.Equal("Wingman, mine at the Inner Belt please", (string?)said[0]["text"]);
        Assert.Null(said[0]["via"]);                                   // (a typed message has none)
        Assert.Equal("order", (string?)said[1]["via"]);
        Assert.Equal("StarSystem", (string?)said[2]["room"]);
        Assert.Equal("voice", (string?)said[2]["via"]);
        Assert.StartsWith("2026-11-24T12:00:00", (string?)said[0]["time"]);
    }

    [Fact]
    public async Task AWingmansDoingLineIsLoggedAndOtherMessagesAreNot()
    {
        Start(logging: true);
        var (a, _) = await Join("Wingman", "NineHells");
        var (b, _) = await Join("Bravo", "NineHells");
        await a.Send(new JsonObject { ["t"] = "ps", ["x"] = 1.0 });
        await a.Send(new JsonObject { ["t"] = "doing", ["text"] = "mining in Outer Drift" });
        await b.Next("doing");
        var lines = Lines();
        Assert.DoesNotContain(lines, l => (string?)l["kind"] == "ps");
        var doing = Assert.Single(lines, l => (string?)l["kind"] == "doing");
        Assert.Equal("mining in Outer Drift", (string?)doing["text"]);
        Assert.Equal("Wingman", (string?)doing["name"]);
    }

    [Fact]
    public async Task ComingAndGoingIsLoggedWithWhoElseIsThere()
    {
        Start(logging: true);
        var (a, _) = await Join("Alpha", "NineHells");
        var (b, _) = await Join("Bravo", "NineHells");
        await b.Send(new JsonObject { ["t"] = "level", ["level"] = "StarSystem" });
        await b.Next("room");
        var lines = Lines();
        var joins = lines.Where(l => (string?)l["kind"] == "join").ToList();
        Assert.Equal(3, joins.Count);                                  // Alpha, Bravo, then Bravo elsewhere
        Assert.Empty(joins[0]["with"]!.AsArray());
        Assert.Equal("Alpha", (string?)joins[1]["with"]![0]!["name"]);
        Assert.Equal("Test", (string?)joins[1]["client"]);
        var left = Assert.Single(lines, l => (string?)l["kind"] == "leave");
        Assert.Equal("Bravo", (string?)left["name"]);
        Assert.Equal("NineHells", (string?)left["room"]);
        Assert.Equal("StarSystem", (string?)joins[2]["room"]);
        _ = a;
    }

    [Fact]
    public async Task AFloodIsPassedOnButOnlyPartLogged()
    {
        Start(logging: true);
        var (a, _) = await Join("Alpha", "NineHells");
        var (b, _) = await Join("Bravo", "NineHells");
        for (int i = 0; i < 40; i++) await a.Send(new JsonObject { ["t"] = "chat", ["text"] = $"spam {i}" });
        for (int i = 0; i < 40; i++) await b.Next("chat");           // (every one passed on)
        int kept = Lines().Count(l => (string?)l["kind"] == "chat");
        // (five a second: ten if the flood crossed into a second second)
        Assert.InRange(kept, Hub.LogPerSecond, 2 * Hub.LogPerSecond);
    }

    [Fact]
    public async Task TheWelcomeSaysTheLogIsKept()
    {
        Start(logging: true);
        var (_, welcome) = await Join("Alpha", "NineHells");
        Assert.True((bool)welcome["chatLog"]!);
    }

    [Fact]
    public async Task WithoutALogNothingIsWrittenAndTheWelcomeSaysNothing()
    {
        Start(logging: false);
        var (a, welcome) = await Join("Alpha", "NineHells");
        var (b, _) = await Join("Bravo", "NineHells");
        await a.Send(new JsonObject { ["t"] = "chat", ["text"] = "hello" });
        await b.Next("chat");
        Assert.Null(welcome["chatLog"]);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task EachDayHasItsOwnFile()
    {
        Start(logging: true);
        var (a, _) = await Join("Alpha", "NineHells");
        var (b, _) = await Join("Bravo", "NineHells");
        await a.Send(new JsonObject { ["t"] = "chat", ["text"] = "before midnight" });
        await b.Next("chat");
        var first = today;
        today = today.AddDays(1);
        await a.Send(new JsonObject { ["t"] = "chat", ["text"] = "after midnight" });
        await b.Next("chat");
        Assert.Contains(Lines(first), l => (string?)l["text"] == "before midnight");
        Assert.Equal("after midnight", (string?)Assert.Single(Lines(today), l => (string?)l["kind"] == "chat")["text"]);
    }

    [Fact]
    public void ATextTooLongIsCut()
    {
        using var cut = new ChatLog(folder, clock: () => today);
        cut.Add(new JsonObject { ["kind"] = "chat", ["text"] = new string('x', ChatLog.MaxText + 50), ["to"] = null, ["via"] = "" });
        cut.Dispose();
        var line = (JsonObject)JsonNode.Parse(File.ReadAllLines(cut.FileFor(today)).Single())!;
        Assert.Equal(ChatLog.MaxText, ((string?)line["text"])!.Length);
        Assert.False(line.ContainsKey("to"));                          // (empty fields are left out)
        Assert.False(line.ContainsKey("via"));
    }

    [Fact]
    public void TheSettingIsFoundNextToTheSettingsFileOrOnTheCommandLine()
    {
        Directory.CreateDirectory(folder);
        string settings = Path.Combine(folder, "coopserver.json");
        File.WriteAllText(settings, "{ \"chatLog\": \"logs\" }");
        var cfg = ServerConfig.Load(new[] { "--config", settings, "--keys", Path.Combine(folder, "k.json") }, out var error);
        Assert.Null(error);
        Assert.Equal(Path.Combine(folder, "logs"), cfg.ChatLogFolder);
        var other = Path.Combine(folder, "elsewhere");
        cfg = ServerConfig.Load(new[] { "--config", settings, "--keys", Path.Combine(folder, "k.json"), "--chat-log", other }, out error);
        Assert.Equal(other, cfg.ChatLogFolder);
        File.WriteAllText(settings, "{ }");
        cfg = ServerConfig.Load(new[] { "--config", settings, "--keys", Path.Combine(folder, "k.json") }, out error);
        Assert.Null(cfg.ChatLogFolder);
    }
}
