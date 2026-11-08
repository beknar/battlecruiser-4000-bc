using System.Net;
using CoopServer;

// Battlecruiser 4000 BC co-op server. Players connect with a WebSocket to ws://<address>:<port>/coop
// (wss:// with a certificate); the same protocol serves the Windows game and the web (WebGPU) game.

if (args.Contains("--help") || args.Contains("-h") || args.Contains("/?"))
{
    Console.WriteLine("""
        Battlecruiser 4000 BC co-op server

          CoopServer [--port 7777] [--bind 0.0.0.0] [--max-players 4]
                     [--allow 192.168.1.0/24,10.0.0.5] [--config coopserver.json]
                     [--cert server.pfx --cert-password secret]

          --port          the port players connect to (default 7777)
          --bind          the address to listen on (0.0.0.0: every network; 127.0.0.1: this computer only)
          --max-players   how many players at once (default 4)
          --allow         only these addresses may connect: IPs or CIDR ranges, comma-separated or
                          repeated (default: anyone). Overrides the config file's list.
          --config        the settings file (default: coopserver.json next to the program). Its
                          allow-list and player limit are re-read whenever it is saved.
          --cert          a .pfx certificate, to serve wss:// (a web page served over https can
                          only connect to wss://)

        Players connect to ws://<this computer's address>:<port>/coop. Type 'status' and Enter for
        who is on, 'reload' to re-read the config file, 'quit' to stop.
        """);
    return 0;
}

var cfg = ServerConfig.Load(args, out string? error);
if (error != null) { Console.Error.WriteLine($"CoopServer: {error}"); return 1; }

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
    return Results.Text(hub.Status());
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

// console commands (when there is a console to read)
_ = Task.Run(() =>
{
    try
    {
        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            switch (line.Trim().ToLowerInvariant())
            {
                case "status": Console.Write(hub.Status()); break;
                case "reload": Log($"config: {cfg.Reload()}"); break;
                case "quit": case "exit": app.Lifetime.StopApplication(); return;
                case "": break;
                default: Console.WriteLine("commands: status, reload, quit"); break;
            }
        }
    }
    catch { }
});

string scheme = string.IsNullOrEmpty(cfg.Certificate) ? "ws" : "wss";
Log($"Battlecruiser 4000 BC co-op server, protocol {Hub.Protocol}");
Log($"listening on {scheme}://{cfg.Bind}:{cfg.Port}/coop, up to {cfg.MaxPlayers} players");
Log($"allowed to connect: {cfg.AllowList}{(cfg.Path != null ? $" (settings: {cfg.Path})" : "")}");
Log("type 'status', 'reload' or 'quit'");
try { await app.RunAsync(); }
catch (IOException e) { Console.Error.WriteLine($"CoopServer: can't listen on port {cfg.Port}: {e.Message}"); return 2; }
watcher?.Dispose();
poller?.Dispose();
return 0;
