using System.Net;

namespace CoopServer.Tests;

/// <summary>Unit tests: reading the settings file and the command line, and reloading.</summary>
public sealed class ServerConfigTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("coopserver-tests-").FullName;

    public void Dispose() { try { Directory.Delete(dir, true); } catch { } }

    string Write(string json, string name = "coopserver.json")
    {
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, json);
        return path;
    }

    static ServerConfig Load(out string? error, params string[] args) => ServerConfig.Load(args, out error);

    [Fact]
    public void WithoutAFileTheDefaultsApply()
    {
        var cfg = Load(out var error, "--config", Path.Combine(dir, "missing.json"));
        Assert.Null(error);
        Assert.Null(cfg.Path);
        Assert.Equal("0.0.0.0", cfg.Bind);
        Assert.Equal(7777, cfg.Port);
        Assert.Equal(7, cfg.MaxPlayers);
        Assert.True(cfg.AllowList.Empty);
    }

    [Fact]
    public void TheShippedSettingsFileTakesSevenPlayers()
    {
        // the coopserver.json that goes out with the server
        string shipped = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "CoopServer", "coopserver.json"));
        Assert.True(File.Exists(shipped), shipped);
        var cfg = Load(out var error, "--config", shipped);
        Assert.Null(error);
        Assert.Equal(7, cfg.MaxPlayers);
    }

    [Fact]
    public void TheFileIsRead()
    {
        string path = Write("""
            {
              // comments and trailing commas are fine
              "port": 7800,
              "maxPlayers": 3,
              "allow": ["10.0.0.0/8"],
            }
            """);
        var cfg = Load(out var error, "--config", path);
        Assert.Null(error);
        Assert.Equal(Path.GetFullPath(path), cfg.Path);
        Assert.Equal(7800, cfg.Port);
        Assert.Equal(3, cfg.MaxPlayers);
        Assert.True(cfg.AllowList.Allows(IPAddress.Parse("10.1.2.3")));
        Assert.False(cfg.AllowList.Allows(IPAddress.Parse("192.168.1.1")));
    }

    [Fact]
    public void TheCommandLineWinsOverTheFile()
    {
        string path = Write("""{ "port": 7800, "maxPlayers": 3, "bind": "127.0.0.1", "allow": ["10.0.0.0/8"] }""");
        var cfg = Load(out var error, "--config", path, "--port", "7801", "--max-players=5", "--bind", "::",
                       "--allow", "192.168.1.0/24, 172.16.0.1", "--allow", "::1");
        Assert.Null(error);
        Assert.Equal(7801, cfg.Port);
        Assert.Equal(5, cfg.MaxPlayers);
        Assert.Equal("::", cfg.Bind);
        Assert.Equal(new[] { "192.168.1.0/24", "172.16.0.1", "::1" }, cfg.Allow);
        Assert.False(cfg.AllowList.Allows(IPAddress.Parse("10.1.2.3")));
        Assert.True(cfg.AllowList.Allows(IPAddress.Parse("::1")));
    }

    [Theory]
    [InlineData("--max-players", "0", "--max-players")]
    [InlineData("--port", "0", "--port")]
    [InlineData("--port", "70000", "--port")]
    [InlineData("--allow", "nonsense", "not an IP address")]
    public void BadValuesAreErrors(string flag, string value, string message)
    {
        Load(out var error, "--config", Path.Combine(dir, "missing.json"), flag, value);
        Assert.NotNull(error);
        Assert.Contains(message, error);
    }

    [Fact]
    public void AnUnreadableFileIsAnError()
    {
        string path = Write("{ this is not json");
        Load(out var error, "--config", path);
        Assert.NotNull(error);
        Assert.Contains("can't read", error);
    }

    [Fact]
    public void ReloadPicksUpTheLimitAndTheAllowList()
    {
        string path = Write("""{ "maxPlayers": 7 }""");
        var cfg = Load(out _, "--config", path);
        Assert.True(cfg.AllowList.Empty);
        Write("""{ "maxPlayers": 4, "allow": ["127.0.0.1"] }""");
        string said = cfg.Reload();
        Assert.Contains("reloaded", said);
        Assert.Equal(4, cfg.MaxPlayers);
        Assert.True(cfg.AllowList.Allows(IPAddress.Loopback));
        Assert.False(cfg.AllowList.Allows(IPAddress.Parse("10.0.0.1")));
    }

    [Fact]
    public void ReloadKeepsWhatTheCommandLineSet()
    {
        string path = Write("""{ "maxPlayers": 7 }""");
        var cfg = Load(out _, "--config", path, "--max-players", "2", "--allow", "10.0.0.1");
        Write("""{ "maxPlayers": 6, "allow": ["192.168.0.0/16"] }""");
        cfg.Reload();
        Assert.Equal(2, cfg.MaxPlayers);
        Assert.True(cfg.AllowList.Allows(IPAddress.Parse("10.0.0.1")));
        Assert.False(cfg.AllowList.Allows(IPAddress.Parse("192.168.1.1")));
    }

    [Fact]
    public void ABrokenFileKeepsTheOldSettings()
    {
        string path = Write("""{ "maxPlayers": 5, "allow": ["10.0.0.1"] }""");
        var cfg = Load(out _, "--config", path);
        Write("""{ "maxPlayers": 3, "allow": ["not an address"] }""");
        Assert.Contains("kept the old settings", cfg.Reload());
        Assert.Equal(5, cfg.MaxPlayers);
        Assert.True(cfg.AllowList.Allows(IPAddress.Parse("10.0.0.1")));
    }

    [Fact]
    public void ALimitBelowOneIsIgnoredOnReload()
    {
        string path = Write("""{ "maxPlayers": 5 }""");
        var cfg = Load(out _, "--config", path);
        Write("""{ "maxPlayers": 0 }""");
        cfg.Reload();
        Assert.Equal(5, cfg.MaxPlayers);
    }

    [Fact]
    public void ReloadWithoutAFileSaysSo()
    {
        var cfg = Load(out _, "--config", Path.Combine(dir, "missing.json"));
        Assert.Equal("no config file", cfg.Reload());
    }
}
