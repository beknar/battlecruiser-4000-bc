using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace CoopServer;

/// <summary>
/// The comms channel's log (chatLog in the settings, or --chat-log): every chat message and AI
/// wingman's "doing" line in every room, players coming into and leaving rooms, and their changes
/// of name (a "rename" line, with the name it was as "was"), one JSON object
/// a line, in a file a day (chat-yyyy-MM-dd.jsonl, by UTC). It is for building a set of
/// conversations and answers for a later wingman. The lines are written by a thread of its own, so
/// the hub never waits on the disk; a line the disk won't take is dropped, said once in the
/// server's log, and the next one tried again.
/// </summary>
public sealed class ChatLog : IDisposable
{
    /// <summary>The longest text kept in a line (a game sends at most 400 characters).</summary>
    public const int MaxText = 2000;

    readonly Channel<(DateTime at, JsonObject line)> queue = Channel.CreateUnbounded<(DateTime, JsonObject)>(new UnboundedChannelOptions { SingleReader = true });
    readonly Task writer;
    readonly Action<string> log;
    readonly Func<DateTime> now;
    bool failing;

    public string Folder { get; }

    public ChatLog(string folder, Action<string>? log = null, Func<DateTime>? clock = null)
    {
        Folder = Path.GetFullPath(folder);
        Directory.CreateDirectory(Folder);
        this.log = log ?? (_ => { });
        now = clock ?? (() => DateTime.UtcNow);
        writer = Task.Run(Write);
    }

    /// <summary>The file for a day.</summary>
    public string FileFor(DateTime utc) => Path.Combine(Folder, $"chat-{utc:yyyy-MM-dd}.jsonl");

    /// <summary>Add a line (time first, then the fields given, with no empty ones). Never blocks.</summary>
    public void Add(JsonObject fields)
    {
        var at = now();
        var line = new JsonObject { ["time"] = at.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") };
        foreach (var (k, v) in fields)
        {
            if (v == null) continue;
            if (v is JsonValue s && s.TryGetValue<string>(out var text))
            {
                if (text.Length == 0) continue;
                line[k] = text.Length > MaxText ? text[..MaxText] : text;
            }
            else line[k] = v.DeepClone();
        }
        queue.Writer.TryWrite((at, line));
    }

    async Task Write()
    {
        await foreach (var (at, line) in queue.Reader.ReadAllAsync())
        {
            try
            {
                File.AppendAllText(FileFor(at), line.ToJsonString() + "\n");
                if (failing) { failing = false; log($"chat log: writing to {Folder} again"); }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (!failing) log($"chat log: can't write to {Folder} ({e.Message}); lines are dropped until it can");
                failing = true;
            }
        }
    }

    /// <summary>Write what is waiting, then stop.</summary>
    public void Dispose()
    {
        queue.Writer.TryComplete();
        try { writer.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
    }
}
