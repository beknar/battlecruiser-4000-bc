using System.Net;
using CoopServer;

// Battlecruiser 4000 BC co-op server. Players connect with a WebSocket to ws://<address>:<port>/coop
// (wss:// with a certificate); the same protocol serves the Windows game and the web (WebGPU) game.

if (args.Contains("--help") || args.Contains("-h") || args.Contains("/?"))
{
    Console.WriteLine("""
        Battlecruiser 4000 BC co-op server

          CoopServer [--port 7777] [--bind 0.0.0.0] [--max-players 7]
                     [--allow 192.168.1.0/24,10.0.0.5] [--config coopserver.json]
                     [--cert server.pfx --cert-password secret] [--keys coopkeys.json] [--open]
          CoopServer --new-key <name> [--ai]      make an API key (shown once), then exit
          CoopServer --list-keys                  list the keys made, then exit
          CoopServer --revoke-key <name|key>      revoke a key (by its name, or its first characters), then exit

          --port          the port players connect to (default 7777)
          --bind          the address to listen on (0.0.0.0: every network; 127.0.0.1: this computer only)
          --max-players   how many players at once (default 7)
          --allow         only these addresses may connect: IPs or CIDR ranges, comma-separated or
                          repeated (default: anyone). Overrides the config file's list.
          --config        the settings file (default: coopserver.json next to the program). Its
                          allow-list and player limit are re-read whenever it is saved.
          --cert          a .pfx certificate, to serve wss:// (a web page served over https can
                          only connect to wss://)
          --keys          the API keys' file (default: coopkeys.json next to the settings)
          --open          let players in without an API key (by default each player, and each
                          AI player, needs one of this server's keys)

        Players connect to ws://<this computer's address>:<port>/coop, giving their API key in the
        game's co-op panel (or -apikey <key> on the command line). While it runs, type 'status' for
        who is on, 'newkey <name>' or 'newaikey <name>' for a key, 'keys' to list them, 'revoke
        <name|key>', 'reload' to re-read the config file, or 'quit' to stop.
        """);
    return 0;
}

var cfg = ServerConfig.Load(args, out string? error);
if (error != null) { Console.Error.WriteLine($"CoopServer: {error}"); return 1; }

// the keys, from the command line (the server isn't started)
static string KeyList(ApiKeys keys)
{
    var list = keys.List();
    if (list.Count == 0) return "no API keys yet: make one with --new-key <name> (or 'newkey <name>' while the server runs)\n";
    var sb = new System.Text.StringBuilder();
    foreach (var e in list) sb.AppendLine($"  {e.Starts}...  {e.Name}{(e.Ai ? "  (AI player)" : "")}  made {e.Created:yyyy-MM-dd HH:mm} UTC");
    return sb.ToString();
}
static string NewKey(ApiKeys keys, string name, bool ai)
{
    string key = keys.Issue(name, ai);
    return $"API key for {name}{(ai ? " (AI player)" : "")}:\n\n  {key}\n\n" +
           "Give it to them: it isn't shown again (the server keeps only its hash). It goes in the game's\n" +
           "co-op panel, or after -apikey on the command line.\n";
}
if (Array.IndexOf(args, "--new-key") is int nk && nk >= 0)
{
    string? name = nk + 1 < args.Length && !args[nk + 1].StartsWith("--") ? args[nk + 1] : null;
    if (name == null) { Console.Error.WriteLine("CoopServer: --new-key needs a name: who or what the key is for"); return 1; }
    try { Console.Write(NewKey(cfg.Keys, name, args.Contains("--ai"))); }
    catch (Exception e) { Console.Error.WriteLine($"CoopServer: can't save the key to {cfg.Keys.Path}: {e.Message}"); return 1; }
    return 0;
}
if (args.Contains("--list-keys")) { Console.Write($"keys in {cfg.Keys.Path}:\n{KeyList(cfg.Keys)}"); return 0; }
if (Array.IndexOf(args, "--revoke-key") is int rk && rk >= 0)
{
    string? which = rk + 1 < args.Length ? args[rk + 1] : null;
    if (which == null) { Console.Error.WriteLine("CoopServer: --revoke-key needs a key's name or its first characters"); return 1; }
    try
    {
        var gone = cfg.Keys.Revoke(which, out string why);
        Console.WriteLine(gone.Count > 0 ? $"revoked {gone.Count} key{(gone.Count == 1 ? "" : "s")} (a running server drops the players using it)" : why);
        return gone.Count > 0 ? 0 : 1;
    }
    catch (Exception e) { Console.Error.WriteLine($"CoopServer: {e.Message}"); return 1; }
}

var builder = WebApplication.CreateSlimBuilder();
builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(k =>
{
    k.Limits.MaxRequestBodySize = 64 * 1024;
    var ip = cfg.Bind is "*" or "0.0.0.0" ? IPAddress.Any : cfg.Bind is "::" ? IPAddress.IPv6Any : IPAddress.Parse(cfg.Bind);
    k.Listen(ip, cfg.Port, lo =>
    {
        if (!string.IsNullOrEmpty(cfg.Certificate)) lo.UseHttps(cfg.Certificate, cfg.CertificatePassword);
    });
});
var app = builder.Build();
var hub = new Hub(cfg);
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });

static void Log(string s) => Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {s}");

// the game connects here
app.Map("/coop", async (HttpContext ctx) =>
{
    var address = ctx.Connection.RemoteIpAddress;
    if (!cfg.AllowList.Allows(address))
    {
        Log($"{(address?.IsIPv4MappedToIPv6 == true ? address.MapToIPv4() : address)}: not on the allow-list, refused");
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        await ctx.Response.WriteAsync("This address may not connect to this server.");
        return;
    }
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsync("This is the co-op game's WebSocket address: connect from the game.");
        return;
    }
    using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    await hub.Run(ws, address, app.Lifetime.ApplicationStopping);
});

// a status page for a browser (also subject to the allow-list)
app.MapGet("/", (HttpContext ctx) =>
{
    if (!cfg.AllowList.Allows(ctx.Connection.RemoteIpAddress)) return Results.StatusCode(403);
    return Results.Text(hub.Status(owner: false));
});

// the config file: re-read the allow-list and player limit whenever it is saved
// Reload the settings when their file is saved. A change notice is quickest, but none arrives on
// some file systems (a Windows drive seen from WSL, network shares, some container mounts), so the
// file's time and size are also checked every 2 seconds. Either way it reloads once per change.
FileSystemWatcher? watcher = null;
Timer? poller = null;
if (cfg.Path != null)
{
    var reloadLock = new object();
    (DateTime, long) Stamp()
    {
        try { var f = new FileInfo(cfg.Path); return f.Exists ? (f.LastWriteTimeUtc, f.Length) : (DateTime.MinValue, -1); }
        catch { return (DateTime.MinValue, -1); }
    }
    var seen = Stamp();
    void ReloadIfChanged()
    {
        lock (reloadLock)
        {
            var now = Stamp();
            if (now == seen || now.Item2 < 0) return;
            Thread.Sleep(200);           // editors save in bursts: let it settle
            seen = Stamp();
            Log($"config: {cfg.Reload()}");
        }
    }
    try
    {
        watcher = new FileSystemWatcher(Path.GetDirectoryName(Path.GetFullPath(cfg.Path))!, Path.GetFileName(cfg.Path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
        };
        watcher.Changed += (_, _) => ReloadIfChanged();
        watcher.Created += (_, _) => ReloadIfChanged();
        watcher.Renamed += (_, _) => ReloadIfChanged();
        watcher.EnableRaisingEvents = true;
    }
    catch (Exception e) { Log($"config: can't watch the settings file ({e.Message}); checking it every 2 s instead"); }
    poller = new Timer(_ => ReloadIfChanged(), null, 2000, 2000);
}
// a key revoked from beside the server (--revoke-key) or in the file: its players are dropped
var revoker = new Timer(_ => { try { hub.DropRevoked(); } catch (Exception e) { Log($"keys: {e.Message}"); } }, null, 3000, 3000);

// console commands (when there is a console to read)
_ = Task.Run(() =>
{
    try
    {
        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            string cmd = line.Trim();
            int sp = cmd.IndexOf(' ');
            string verb = (sp < 0 ? cmd : cmd[..sp]).ToLowerInvariant(), rest = sp < 0 ? "" : cmd[(sp + 1)..].Trim();
            switch (verb)
            {
                case "status": Console.Write(hub.Status(owner: true)); break;
                case "reload": Log($"config: {cfg.Reload()}"); break;
                case "keys": Console.Write(KeyList(cfg.Keys)); break;
                case "newkey":
                case "newaikey":
                    if (rest.Length == 0) { Console.WriteLine($"{verb} <name>: who or what the key is for"); break; }
                    try { Console.Write(NewKey(cfg.Keys, rest, verb == "newaikey")); }
                    catch (Exception e) { Console.WriteLine($"can't save the key: {e.Message}"); }
                    break;
                case "revoke":
                    try
                    {
                        var gone = cfg.Keys.Revoke(rest, out string why);
                        Console.WriteLine(gone.Count > 0 ? $"revoked {gone.Count} key{(gone.Count == 1 ? "" : "s")}" : why);
                        if (gone.Count > 0) hub.DropRevoked();
                    }
                    catch (Exception e) { Console.WriteLine(e.Message); }
                    break;
                case "quit": case "exit": app.Lifetime.StopApplication(); return;
                case "": break;
                default: Console.WriteLine("commands: status, newkey <name>, newaikey <name>, keys, revoke <name|key>, reload, quit"); break;
            }
        }
    }
    catch { }
});

string scheme = string.IsNullOrEmpty(cfg.Certificate) ? "ws" : "wss";
Log($"Battlecruiser 4000 BC co-op server, protocol {Hub.Protocol}");
Log($"listening on {scheme}://{cfg.Bind}:{cfg.Port}/coop, up to {cfg.MaxPlayers} players");
Log($"allowed to connect: {cfg.AllowList}{(cfg.Path != null ? $" (settings: {cfg.Path})" : "")}");
Log($"{cfg.KeyRule}{(cfg.RequireKey ? $" (keys: {cfg.Keys.Path})" : "")}");
if (cfg.RequireKey && cfg.Keys.Count == 0) Log("no API keys yet, so no one can join: type 'newkey <name>' to make one");
Log("type 'status', 'newkey <name>', 'newaikey <name>', 'keys', 'revoke <name|key>', 'reload' or 'quit'");
try { await app.RunAsync(); }
catch (IOException e) { Console.Error.WriteLine($"CoopServer: can't listen on port {cfg.Port}: {e.Message}"); return 2; }
watcher?.Dispose();
poller?.Dispose();
revoker.Dispose();
return 0;
