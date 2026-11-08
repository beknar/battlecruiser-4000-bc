using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace CoopServer;

/// <summary>
/// The server's settings: coopserver.json next to the program (or --config &lt;file&gt;), then
/// anything on the command line on top. The allow-list and the player limit are read again
/// whenever the file changes, without restarting.
/// </summary>
public sealed class ServerConfig
{
    public string Bind { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 7777;
    public int MaxPlayers { get; set; } = 4;
    /// <summary>Addresses allowed to connect: single IPs ("192.168.1.20", "::1") or ranges in CIDR
    /// form ("192.168.1.0/24", "10.0.0.0/8"). Empty: anyone may connect.</summary>
    public List<string> Allow { get; set; } = new();
    /// <summary>A .pfx certificate for wss:// (needed when the web client's page is served over https).</summary>
    public string? Certificate { get; set; }
    public string? CertificatePassword { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] public string? Path { get; private set; }
    [System.Text.Json.Serialization.JsonIgnore] public IpAllowList AllowList { get; private set; } = new(new List<string>());

    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static ServerConfig Load(string[] args, out string? error)
    {
        error = null;
        string? path = Arg(args, "--config") ?? System.IO.Path.Combine(AppContext.BaseDirectory, "coopserver.json");
        var cfg = new ServerConfig();
        if (File.Exists(path))
        {
            try { cfg = JsonSerializer.Deserialize<ServerConfig>(File.ReadAllText(path), Json) ?? new ServerConfig(); }
            catch (Exception e) { error = $"can't read {path}: {e.Message}"; return cfg; }
            cfg.Path = System.IO.Path.GetFullPath(path);
        }
        // the command line wins over the file
        if (Arg(args, "--bind") is string b) cfg.Bind = b;
        if (Arg(args, "--port") is string p && int.TryParse(p, out int port)) cfg.Port = port;
        if (Arg(args, "--max-players") is string m && int.TryParse(m, out int max)) { cfg.MaxPlayers = max; cfg.maxFromArgs = true; }
        var allow = Args(args, "--allow").SelectMany(a => a.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToList();
        if (allow.Count > 0) { cfg.Allow = allow; cfg.allowFromArgs = true; }
        if (Arg(args, "--cert") is string c) cfg.Certificate = c;
        if (Arg(args, "--cert-password") is string cp) cfg.CertificatePassword = cp;
        if (cfg.MaxPlayers < 1) { error = "--max-players must be at least 1"; return cfg; }
        if (cfg.Port < 1 || cfg.Port > 65535) { error = "--port must be 1 to 65535"; return cfg; }
        try { cfg.AllowList = new IpAllowList(cfg.Allow); }
        catch (FormatException e) { error = e.Message; }
        return cfg;
    }

    bool allowFromArgs, maxFromArgs;

    /// <summary>Read the allow-list and player limit from the file again (whichever of them the
    /// command line gave, it still wins).</summary>
    public string Reload()
    {
        if (Path == null || !File.Exists(Path)) return "no config file";
        try
        {
            var fresh = JsonSerializer.Deserialize<ServerConfig>(File.ReadAllText(Path), Json) ?? new ServerConfig();
            if (!allowFromArgs)
            {
                AllowList = new IpAllowList(fresh.Allow);
                Allow = fresh.Allow;
            }
            if (!maxFromArgs && fresh.MaxPlayers >= 1) MaxPlayers = fresh.MaxPlayers;
            return $"reloaded: max players {MaxPlayers}, allow {AllowList}";
        }
        catch (Exception e) { return $"kept the old settings: {e.Message}"; }
    }

    static string? Arg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
        foreach (var a in args) if (a.StartsWith(name + "=")) return a[(name.Length + 1)..];
        return null;
    }

    static IEnumerable<string> Args(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) yield return args[i + 1];
        foreach (var a in args) if (a.StartsWith(name + "=")) yield return a[(name.Length + 1)..];
    }
}

/// <summary>Which addresses may connect: single IPs and CIDR ranges, IPv4 and IPv6 (an IPv4
/// address arriving as IPv6, ::ffff:a.b.c.d, counts as the IPv4 one). Empty allows anyone.</summary>
public sealed class IpAllowList
{
    readonly List<(byte[] net, int bits, string text)> ranges = new();

    public IpAllowList(IEnumerable<string> entries)
    {
        foreach (var raw in entries)
        {
            string e = raw.Trim();
            if (e.Length == 0) continue;
            int slash = e.IndexOf('/');
            string ip = slash >= 0 ? e[..slash] : e;
            if (!IPAddress.TryParse(ip, out var addr)) throw new FormatException($"not an IP address or range: '{e}'");
            byte[] bytes = addr.GetAddressBytes();
            int bits = bytes.Length * 8;
            if (slash >= 0 && (!int.TryParse(e[(slash + 1)..], out bits) || bits < 0 || bits > bytes.Length * 8))
                throw new FormatException($"bad prefix length in '{e}'");
            ranges.Add((bytes, bits, e));
        }
    }

    public bool Empty => ranges.Count == 0;

    public bool Allows(IPAddress? address)
    {
        if (ranges.Count == 0) return true;
        if (address == null) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        byte[] a = address.GetAddressBytes();
        foreach (var (net, bits, _) in ranges)
        {
            if (net.Length != a.Length) continue;
            int full = bits / 8, rest = bits % 8;
            bool ok = true;
            for (int i = 0; i < full && ok; i++) ok = a[i] == net[i];
            if (ok && rest > 0)
            {
                int mask = 0xFF << (8 - rest) & 0xFF;
                ok = (a[full] & mask) == (net[full] & mask);
            }
            if (ok) return true;
        }
        return false;
    }

    public override string ToString() => ranges.Count == 0 ? "everyone" : string.Join(", ", ranges.Select(r => r.text));
}
