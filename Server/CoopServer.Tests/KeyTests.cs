using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json.Nodes;

namespace CoopServer.Tests;

/// <summary>
/// The API keys: made, checked, kept only as hashes, revoked, read again when their file
/// changes; and the hub turning away a player without a valid key, over real connections.
/// </summary>
public sealed class KeyTests : IAsyncDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("coopkeys-tests-").FullName;
    readonly CancellationTokenSource stop = new();
    readonly TcpListener listener = new(IPAddress.Loopback, 0);
    readonly List<TestClient> clients = new();
    readonly List<Task> sessions = new();

    public KeyTests() => listener.Start();

    public async ValueTask DisposeAsync()
    {
        foreach (var c in clients) await c.DisposeAsync();
        stop.Cancel();
        try { await Task.WhenAll(sessions).WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        listener.Stop();
        try { Directory.Delete(dir, true); } catch { }
    }

    string KeysPath => Path.Combine(dir, "coopkeys.json");

    // ------------------------------------------------------------------ the keys themselves

    [Fact]
    public void AKeyIsCheckedAndOnlyItsHashIsKept()
    {
        var keys = new ApiKeys(KeysPath);
        string key = keys.Issue("Alice", ai: false);
        Assert.StartsWith(ApiKeys.Prefix, key);
        Assert.True(key.Length >= ApiKeys.Prefix.Length + 40);
        Assert.Equal("Alice", keys.Check(key)?.Name);
        Assert.Equal("Alice", keys.Check("  " + key + " ")?.Name);        // pasted with spaces round it
        Assert.Null(keys.Check(key + "x"));
        Assert.Null(keys.Check(""));
        Assert.Null(keys.Check(null));
        string file = File.ReadAllText(KeysPath);
        Assert.DoesNotContain(key, file);                                 // never the key itself
        Assert.Contains(ApiKeys.HashOf(key), file);
        Assert.Contains(key[..(ApiKeys.Prefix.Length + ApiKeys.StartsLength)], file);   // its first characters, to tell it apart
    }

    [Fact]
    public void KeysAreDifferentAndMarkedForAIPlayers()
    {
        var keys = new ApiKeys(KeysPath);
        string a = keys.Issue("wingman", ai: true), b = keys.Issue("wingman", ai: true);
        Assert.NotEqual(a, b);
        Assert.True(keys.Check(a)!.Ai);
        Assert.Equal(2, keys.Count);
        Assert.Throws<ArgumentException>(() => keys.Issue("  ", false));
    }

    [Fact]
    public void AKeyIsRevokedByNameByItsFirstCharactersOrInFull()
    {
        var keys = new ApiKeys(KeysPath);
        string a = keys.Issue("Alice", false), b = keys.Issue("Bob", false), c = keys.Issue("Cleo", false);
        Assert.Single(keys.Revoke("alice", out _));
        Assert.Null(keys.Check(a));
        Assert.Single(keys.Revoke(b[..(ApiKeys.Prefix.Length + ApiKeys.StartsLength + 3)], out _));   // more of it than the list shows
        Assert.Null(keys.Check(b));
        Assert.Single(keys.Revoke(c, out _));
        Assert.Null(keys.Check(c));
        Assert.Empty(keys.Revoke("nobody", out string why));
        Assert.Contains("no key", why);
        Assert.Equal(0, keys.Count);
    }

    [Fact]
    public void AnotherCopysChangesToTheFileCountAtOnce()
    {
        // the running server's copy, and --new-key / --revoke-key run beside it
        var server = new ApiKeys(KeysPath);
        var cli = new ApiKeys(KeysPath);
        string key = cli.Issue("Dana", false);
        Assert.Equal("Dana", server.Check(key)?.Name);
        cli.Revoke("Dana", out _);
        Assert.Null(server.Check(key));
    }

    [Fact]
    public void ABrokenFileLetsNoOneInAndIsntWrittenOver()
    {
        var keys = new ApiKeys(KeysPath);
        string key = keys.Issue("Eve", false);
        string broken = "{ this isn't json, and Eve's key is in it";
        File.WriteAllText(KeysPath, broken);
        Assert.Null(keys.Check(key));
        Assert.NotNull(keys.Error);
        // making or revoking a key now would lose the others: refused, the file left as it is
        Assert.Throws<IOException>(() => keys.Issue("Frank", false));
        Assert.Throws<IOException>(() => keys.Revoke("Eve", out _));
        Assert.Equal(broken, File.ReadAllText(KeysPath));
    }

    [Fact]
    public void AFileInUseForAMomentKeepsTheKeysReadLast()
    {
        var keys = new ApiKeys(KeysPath);
        string key = keys.Issue("Gina", false);
        var other = new ApiKeys(KeysPath);
        other.Issue("Hal", false);                                         // (the file changed)
        using (new FileStream(KeysPath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Equal("Gina", keys.Check(key)?.Name);                   // can't be read: the keys read last
        Assert.Equal(2, keys.Count);                                      // and read once it can be
    }

    [Fact]
    public void AnEntryWithNoHashIsPassedOver()
    {
        var keys = new ApiKeys(KeysPath);
        string key = keys.Issue("Ivy", false);
        string text = File.ReadAllText(KeysPath).Replace("\"keys\": [", "\"keys\": [ null, { \"name\": \"broken\", \"hash\": null },");
        File.WriteAllText(KeysPath, text);
        Assert.Equal("Ivy", keys.Check(key)?.Name);
        Assert.Null(keys.Check("bc4k_" + new string('0', 40)));
    }

    [Fact]
    public void AStartNoKeyHasRevokesNone()
    {
        var keys = new ApiKeys(KeysPath);
        keys.Issue("Jo", false);
        keys.Issue("Kit", false);
        Assert.Empty(keys.Revoke(ApiKeys.Prefix + "zzzzzzzz", out string why));
        Assert.Contains("no key", why);
        Assert.Equal(2, keys.Count);
    }

    [Fact]
    public void KeysGivenOnTheCommandLineAreFromTheFolderItRunsIn()
    {
        string cwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(dir);
            var cfg = ServerConfig.Load(new[] { "--config", Path.Combine(dir, "missing.json"), "--keys", "here.json" }, out var error);
            Assert.Null(error);
            Assert.Equal(Path.Combine(dir, "here.json"), cfg.Keys.Path);
        }
        finally { Directory.SetCurrentDirectory(cwd); }
    }

    [Fact]
    public void TheSettingsSayWhereTheKeysAreAndWhetherOneIsNeeded()
    {
        string cfgPath = Path.Combine(dir, "coopserver.json");
        File.WriteAllText(cfgPath, "{ \"keysFile\": \"mykeys.json\" }");
        var cfg = ServerConfig.Load(new[] { "--config", cfgPath }, out var error);
        Assert.Null(error);
        Assert.True(cfg.RequireKey);                                       // needed unless told otherwise
        Assert.Equal(Path.Combine(dir, "mykeys.json"), cfg.Keys.Path);    // next to the settings
        var open = ServerConfig.Load(new[] { "--config", cfgPath, "--open" }, out error);
        Assert.Null(error);
        Assert.False(open.RequireKey);
        var other = ServerConfig.Load(new[] { "--config", cfgPath, "--keys", Path.Combine(dir, "x", "..", "other.json") }, out error);
        Assert.Equal(Path.Combine(dir, "other.json"), other.Keys.Path);
    }

    [Fact]
    public void TheShippedSettingsNeedAKey()
    {
        string shipped = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "CoopServer", "coopserver.json"));
        string copy = Path.Combine(dir, "coopserver.json");
        File.Copy(shipped, copy);
        var cfg = ServerConfig.Load(new[] { "--config", copy }, out var error);
        Assert.Null(error);
        Assert.True(cfg.RequireKey);
        Assert.Equal(Path.Combine(dir, "coopkeys.json"), cfg.Keys.Path);
    }

    // ------------------------------------------------------------------ the hub

    Hub NewHub(bool requireKey, out ServerConfig cfg)
    {
        File.WriteAllText(Path.Combine(dir, "s.json"), $"{{ \"requireKey\": {(requireKey ? "true" : "false")} }}");
        cfg = ServerConfig.Load(new[] { "--config", Path.Combine(dir, "s.json") }, out var error);
        Assert.Null(error);
        return new Hub(cfg);
    }

    async Task<TestClient> Hello(Hub hub, string name, string? key)
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
        var hello = new JsonObject { ["t"] = "hello", ["v"] = Hub.Protocol, ["name"] = name, ["level"] = "StarSystem", ["client"] = "Test", ["captain"] = 0, ["clock"] = 0.0 };
        if (key != null) hello["key"] = key;
        await c.Send(hello);
        return c;
    }

    [Fact]
    public async Task WithoutAKeyAPlayerIsTurnedAway()
    {
        var hub = NewHub(true, out var cfg);
        cfg.Keys.Issue("Alice", false);
        var c = await Hello(hub, "Alice", null);
        var reject = await c.Next("reject");
        Assert.Contains("API key", (string?)reject["why"]);
        Assert.Equal(0, hub.Count);
    }

    [Fact]
    public async Task AWrongKeyIsTurnedAway()
    {
        var hub = NewHub(true, out var cfg);
        string key = cfg.Keys.Issue("Alice", false);
        var c = await Hello(hub, "Alice", key[..^1] + (key[^1] == '0' ? '1' : '0'));
        var reject = await c.Next("reject");
        Assert.Contains("isn't one of this server's", (string?)reject["why"]);
    }

    [Fact]
    public async Task WithItsKeyAPlayerIsWelcomedAndTheStatusSaysWhose()
    {
        var hub = NewHub(true, out var cfg);
        string key = cfg.Keys.Issue("wingman bot", ai: true);
        var c = await Hello(hub, "Wingman", key);
        await c.Next("welcome");
        Assert.Equal(1, hub.Count);
        string status = hub.Status(owner: true);
        Assert.Contains("AI key: wingman bot", status);
        Assert.DoesNotContain(key, status);
        // the status page (anyone the allow-list lets in) doesn't say whose keys
        string page = hub.Status(owner: false);
        Assert.DoesNotContain("wingman bot", page);
        Assert.DoesNotContain("issued", page);
    }

    [Fact]
    public async Task ARevokedKeyLetsNoOneInAndDropsThoseOn()
    {
        var hub = NewHub(true, out var cfg);
        string key = cfg.Keys.Issue("Alice", false);
        var a = await Hello(hub, "Alice", key);
        await a.Next("welcome");
        cfg.Keys.Revoke("Alice", out _);
        var b = await Hello(hub, "Alice again", key);
        await b.Next("reject");
        // the one already on is dropped, told why
        Assert.Equal(1, hub.DropRevoked());
        var told = await a.Next("reject");
        Assert.Contains("revoked", (string?)told["why"]);
        for (int i = 0; i < 40 && hub.Count > 0; i++) await Task.Delay(50);
        Assert.Equal(0, hub.Count);
    }

    [Fact]
    public async Task AnOpenServerNeedsNoKey()
    {
        var hub = NewHub(false, out _);
        var c = await Hello(hub, "Anyone", null);
        await c.Next("welcome");
    }
}
