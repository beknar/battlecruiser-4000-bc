using System.Text.Json.Nodes;
using CarrierGame;

namespace CoopServer.Tests;

/// <summary>
/// The server's messages against the samples in Server/protocol, which the game's tests check
/// too: every field the hub sends must be in its sample (so the game knows to read it).
/// </summary>
public sealed class ProtocolTests : IAsyncDisposable
{
    static readonly string Samples = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "protocol"));

    readonly HubTests hub = new();

    public ValueTask DisposeAsync() => hub.DisposeAsync();

    static JsonObject Sample(string type)
    {
        string path = Path.Combine(Samples, type + ".json");
        Assert.True(File.Exists(path), $"no sample for '{type}' at {path}");
        return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }

    /// <summary>Every field of `got` is in `sample` (objects and arrays of objects looked into).</summary>
    static void Within(JsonObject got, JsonObject sample, string where)
    {
        foreach (var (key, value) in got)
        {
            Assert.True(sample.ContainsKey(key), $"{where}: the server sends '{key}', which isn't in the sample");
            if (value is JsonObject o)
                Within(o, sample[key]!.AsObject(), $"{where}.{key}");
            else if (value is JsonArray a)
                foreach (var item in a)
                    if (item is JsonObject io) Within(io, sample[key]!.AsArray()[0]!.AsObject(), $"{where}.{key}[]");
        }
    }

    static void Matches(JsonObject msg)
    {
        string type = (string)msg["t"]!;
        Within(msg, Sample(type), type);
    }

    [Fact]
    public void ThereIsASampleOfEveryMessageOnlyTheServerSends()
    {
        foreach (var type in CoopWire.ServerOnly)
            Assert.Equal(type, (string?)Sample(type)["t"]);
    }

    [Fact]
    public async Task EveryMessageTheServerSendsMatchesItsSample()
    {
        var (a, welcome) = await hub.Join("Alpha", "StarSystem");
        Matches(welcome);
        Matches(await a.Next(CoopWire.Players));
        var (b, _) = await hub.Join("Bravo", "StarSystem");
        Matches(await a.Next(CoopWire.Join));
        await a.Send(new JsonObject { ["t"] = CoopWire.Ping, ["c"] = 3.5 });
        Matches(await a.Next(CoopWire.Pong));
        // a change of level: a room with the welcome's fields
        await b.Send(new JsonObject { ["t"] = CoopWire.Level, ["level"] = "AsteroidAmbush", ["clock"] = 1.0 });
        Matches(await b.Next(CoopWire.Room));
        Matches(await a.Next(CoopWire.Leave));
        // a change of host: a room with only the room's fields
        await b.Send(new JsonObject { ["t"] = CoopWire.Level, ["level"] = "StarSystem", ["clock"] = 1.0 });
        await a.Next(CoopWire.Join);
        await a.DisposeAsync();
        Matches(await b.Next(CoopWire.Room, m => !m.ContainsKey("id")));
        // turned away
        var c = await hub.Open();
        await c.Send(new JsonObject { ["t"] = CoopWire.Hello, ["v"] = CoopWire.Protocol + 1 });
        Matches(await c.Next(CoopWire.Reject));
    }
}
