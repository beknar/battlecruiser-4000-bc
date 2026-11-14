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
/// made or revoked by another copy of the program (--new-key, --revoke-key) counts at once; the
/// running server drops the players whose key has been revoked (Hub.DropRevoked).
///
/// Changing the file (making or revoking a key) is done under a lock (coopkeys.json.lock), the
/// file read fresh inside it, so two copies changing it at once can't undo each other's change;
/// and never while the file can't be read (a broken file is left for its owner to mend, not
/// written over). A file that can't be read for a moment (another program has it open) is tried
/// again, the keys read last kept meanwhile; one that is broken lets no one in.
/// </summary>
public sealed class ApiKeys
{
    public const string Prefix = "bc4k_";
    /// <summary>How many characters of a key (after the prefix) the file keeps, to tell it by.</summary>
    public const int StartsLength = 8;

    public sealed class Entry
    {
        /// <summary>Who or what it was made for ("Alice", "wingman bot").</summary>
        public string Name { get; set; } = "";
        /// <summary>The first characters of the key, to recognise it ("bc4k_7f3a09c1").</summary>
        public string Starts { get; set; } = "";
        /// <summary>The key's SHA-256, in hex.</summary>
        public string Hash { get; set; } = "";
        /// <summary>Made for an AI player (a bot), not a person.</summary>
        public bool Ai { get; set; }
        public DateTime Created { get; set; }
    }

    sealed class FileShape
    {
        [JsonPropertyName("keys")] public List<Entry?>? Keys { get; set; } = new();
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
    /// <summary>Why the file couldn't be read, if it couldn't (no one gets in meanwhile).</summary>
    public string? Error { get; private set; }

    public ApiKeys(string? path)
    {
        Path = path;
        lock (gate) Refresh(false);
    }

    public int Count { get { lock (gate) { Refresh(false); return entries.Count; } } }

    public IReadOnlyList<Entry> List() { lock (gate) { Refresh(false); return entries.ToList(); } }

    /// <summary>Make a key for `name`, keep its hash, and return the key itself (shown once).
    /// Throws if the file can't be read or written (nothing is changed then).</summary>
    public string Issue(string name, bool ai)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException("a key needs a name: who or what it is for");
        if (name.Length > 40) name = name[..40];
        string key = Prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant();
        Change(list => list.Add(new Entry { Name = name, Starts = key[..(Prefix.Length + StartsLength)], Hash = HashOf(key), Ai = ai, Created = DateTime.UtcNow }));
        return key;
    }

    /// <summary>Revoke the keys made for a name, or the one key given in full or by its first
    /// characters (at least as many as the list shows; a start that more than one key has
    /// revokes none). Returns the hashes of those revoked (none if none matched, and why).
    /// Throws if the file can't be read or written.</summary>
    public List<string> Revoke(string which, out string why)
    {
        which = (which ?? "").Trim();
        var gone = new List<string>();
        string reason = "";
        if (which.Length == 0) { why = "say which key: its name, or its first characters"; return gone; }
        string hash = HashOf(which);
        Change(list =>
        {
            var match = list.Where(e => string.Equals(e.Name, which, StringComparison.OrdinalIgnoreCase) || e.Hash == hash).ToList();
            if (match.Count == 0 && which.StartsWith(Prefix) && which.Length >= Prefix.Length + StartsLength)
            {
                var starts = list.Where(e => which.StartsWith(e.Starts, StringComparison.OrdinalIgnoreCase)).ToList();
                if (starts.Count == 1) match = starts;
                else if (starts.Count > 1) reason = $"{starts.Count} keys start with that: give more of it, or the name";
            }
            foreach (var e in match) { list.Remove(e); gone.Add(e.Hash); }
            if (match.Count == 0 && reason == "") reason = $"no key for '{which}'";
            return match.Count > 0;
        });
        why = reason;
        return gone;
    }

    /// <summary>The entry a key belongs to, or null if it isn't one of these keys.</summary>
    public Entry? Check(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        byte[] hash = Convert.FromHexString(HashOf(key.Trim()));
        lock (gate)
        {
            Refresh(false);
            Entry? found = null;
            foreach (var e in entries)
            {
                byte[] other;
                try { other = Convert.FromHexString(e.Hash ?? ""); } catch (Exception) { continue; }
                // (the same time whether it matches or not)
                if (other.Length == hash.Length && CryptographicOperations.FixedTimeEquals(hash, other)) found = e;
            }
            return found;
        }
    }

    /// <summary>Is a key with this hash still one of these (a player who joined with it stays on)?</summary>
    public bool Has(string hash)
    {
        lock (gate)
        {
            Refresh(false);
            return entries.Any(e => e.Hash == hash);
        }
    }

    public static string HashOf(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

    /// <summary>Read the file again if it has changed (or `force`), under the lock. A read that
    /// fails for a moment (the file in use) keeps the keys read last and is tried again next
    /// time; a file that is broken lets no one in until it is mended.</summary>
    void Refresh(bool force)
    {
        if (Path == null) return;
        (DateTime, long) now;
        try { var f = new FileInfo(Path); now = f.Exists ? (f.LastWriteTimeUtc, f.Length) : (DateTime.MinValue, -1); }
        catch { return; }
        if (now == stamp && !force) return;
        if (now.Item2 < 0) { entries = new List<Entry>(); Error = null; stamp = now; return; }
        string text;
        try { text = File.ReadAllText(Path); }
        catch (IOException) { return; }                    // (in use: tried again next time)
        catch (UnauthorizedAccessException e) { entries = new List<Entry>(); Error = $"can't read {Path}: {e.Message}"; return; }
        try
        {
            var read = JsonSerializer.Deserialize<FileShape>(text, Json)?.Keys ?? new List<Entry?>();
            entries = read.Where(e => e != null && !string.IsNullOrEmpty(e.Hash)).Select(e => e!).ToList();
            Error = null;
        }
        catch (Exception e)
        {
            // a broken file lets no one in, rather than everyone
            entries = new List<Entry>();
            Error = $"can't read {Path}: {e.Message}";
        }
        stamp = now;
    }

    /// <summary>Change the keys: under the file's lock, the file read fresh, `change` applied
    /// (it says whether it changed anything), and written back. Throws if the file can't be
    /// read (so it isn't written over) or written.</summary>
    void Change(Action<List<Entry>> change) => Change(l => { change(l); return true; });

    void Change(Func<List<Entry>, bool> change)
    {
        lock (gate)
        {
            using var held = Path != null ? Lock(Path + ".lock") : null;
            Refresh(true);
            if (Error != null) throw new IOException($"{Error}: mend it (or delete it) first; nothing was changed");
            var list = entries.ToList();
            if (!change(list)) return;
            entries = list;
            Save();
        }
    }

    /// <summary>The keys file's lock, waited for (a few seconds at most).</summary>
    static FileStream Lock(string lockPath)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            try { return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose); }
            catch (IOException) when (DateTime.UtcNow < until) { Thread.Sleep(50); }
        }
    }

    /// <summary>Write the file (under the locks): to a temporary file first, then over the old one.</summary>
    void Save()
    {
        if (Path == null) return;
        string text = "// Battlecruiser 4000 BC co-op server: its API keys. Only each key's hash is kept here; the key\n" +
                      "// itself was shown once, when it was made. Delete an entry (or use --revoke-key) to revoke it.\n" +
                      JsonSerializer.Serialize(new FileShape { Keys = entries.Cast<Entry?>().ToList() }, Json) + "\n";
        string tmp = Path + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, Path, true);
        try { var f = new FileInfo(Path); stamp = (f.LastWriteTimeUtc, f.Length); } catch { }
    }
}
