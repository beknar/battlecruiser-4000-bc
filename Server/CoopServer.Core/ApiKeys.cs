using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoopServer;

/// <summary>
/// The API keys a server issues: a player (or an AI player) needs one to connect, unless the
/// server is open (requireKey false). A key is shown once, when it is made; the file
/// (coopkeys.json) keeps only its SHA-256 hash, its first characters (to tell keys apart in a
/// list), who it was made for and when. The file is read again whenever it changes, so a key
/// revoked by editing it, or by another copy of the program (--revoke-key), stops working at once.
/// </summary>
public sealed class ApiKeys
{
    public const string Prefix = "bc4k_";

    public sealed class Entry
    {
        /// <summary>Who or what it was made for ("Alice", "wingman bot").</summary>
        public string Name { get; set; } = "";
        /// <summary>The first characters of the key, to recognise it ("bc4k_7f3a").</summary>
        public string Starts { get; set; } = "";
        /// <summary>The key's SHA-256, in hex.</summary>
        public string Hash { get; set; } = "";
        /// <summary>Made for an AI player (a bot), not a person.</summary>
        public bool Ai { get; set; }
        public DateTime Created { get; set; }
    }

    sealed class FileShape
    {
        [JsonPropertyName("keys")] public List<Entry> Keys { get; set; } = new();
    }

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
    };

    readonly object gate = new();
    List<Entry> entries = new();
    (DateTime, long) stamp = (DateTime.MinValue, -1);

    /// <summary>The keys file (null: kept in memory only, as in the tests).</summary>
    public string? Path { get; }
    /// <summary>Why the file couldn't be read, if it couldn't (its keys are then none).</summary>
    public string? Error { get; private set; }

    public ApiKeys(string? path)
    {
        Path = path;
        lock (gate) Refresh();
    }

    public int Count { get { lock (gate) { Refresh(); return entries.Count; } } }

    public IReadOnlyList<Entry> List() { lock (gate) { Refresh(); return entries.ToList(); } }

    /// <summary>Make a key for `name`, keep its hash, and return the key itself (shown once).</summary>
    public string Issue(string name, bool ai)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException("a key needs a name: who or what it is for");
        if (name.Length > 40) name = name[..40];
        string key = Prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant();
        lock (gate)
        {
            Refresh();
            entries.Add(new Entry { Name = name, Starts = key[..(Prefix.Length + 4)], Hash = HashOf(key), Ai = ai, Created = DateTime.UtcNow });
            Save();
        }
        return key;
    }

    /// <summary>Revoke the keys made for a name, or a key given by its first characters (as the
    /// list shows them) or in full. Returns how many were revoked.</summary>
    public int Revoke(string which)
    {
        which = (which ?? "").Trim();
        if (which.Length == 0) return 0;
        string hash = HashOf(which);
        lock (gate)
        {
            Refresh();
            int n = entries.RemoveAll(e => string.Equals(e.Name, which, StringComparison.OrdinalIgnoreCase)
                                          || string.Equals(e.Starts, which, StringComparison.OrdinalIgnoreCase)
                                          || e.Hash == hash);
            if (n > 0) Save();
            return n;
        }
    }

    /// <summary>The entry a key belongs to, or null if it isn't one of these keys.</summary>
    public Entry? Check(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        byte[] hash = Convert.FromHexString(HashOf(key.Trim()));
        lock (gate)
        {
            Refresh();
            Entry? found = null;
            foreach (var e in entries)
            {
                byte[] other;
                try { other = Convert.FromHexString(e.Hash); } catch (FormatException) { continue; }
                // (the same time whether it matches or not)
                if (CryptographicOperations.FixedTimeEquals(hash, other)) found = e;
            }
            return found;
        }
    }

    public static string HashOf(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

    /// <summary>Read the file again if it has changed (under the lock).</summary>
    void Refresh()
    {
        if (Path == null) return;
        (DateTime, long) now;
        try { var f = new FileInfo(Path); now = f.Exists ? (f.LastWriteTimeUtc, f.Length) : (DateTime.MinValue, -1); }
        catch { return; }
        if (now == stamp) return;
        stamp = now;
        if (now.Item2 < 0) { entries = new List<Entry>(); Error = null; return; }
        try
        {
            entries = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(Path), Json)?.Keys ?? new List<Entry>();
            Error = null;
        }
        catch (Exception e)
        {
            // a broken file lets no one in, rather than everyone
            entries = new List<Entry>();
            Error = $"can't read {Path}: {e.Message}";
        }
    }

    /// <summary>Write the file (under the lock): to a temporary file first, then over the old one.</summary>
    void Save()
    {
        if (Path == null) return;
        string text = "// Battlecruiser 4000 BC co-op server: its API keys. Only each key's hash is kept here; the key\n" +
                      "// itself was shown once, when it was made. Delete an entry (or use --revoke-key) to revoke it.\n" +
                      JsonSerializer.Serialize(new FileShape { Keys = entries }, Json) + "\n";
        string tmp = Path + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, Path, true);
        try { var f = new FileInfo(Path); stamp = (f.LastWriteTimeUtc, f.Length); } catch { }
    }
}
