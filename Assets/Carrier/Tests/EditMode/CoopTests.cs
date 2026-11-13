using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CarrierGame.Demo;
using NUnit.Framework;
using UnityEngine;

namespace CarrierGame.Tests
{
    /// <summary>Co-op: how the enemies scale with players, the messages and the server's address.</summary>
    public class CoopScalingTests
    {
        [TestCase(1, 1f, 1f)]
        [TestCase(2, 1.6f, 1.12f)]
        [TestCase(4, 2.8f, 1.36f)]
        [TestCase(7, 4.6f, 1.72f)]
        public void EnemiesScaleWithThePlayers(int players, float toughness, float firepower)
        {
            Assert.That(CoopScaling.Toughness(players), Is.EqualTo(toughness).Within(1e-4f));
            Assert.That(CoopScaling.Firepower(players), Is.EqualTo(firepower).Within(1e-4f));
        }

        [TestCase(0)]
        [TestCase(-3)]
        public void NoPlayersCountsAsOne(int players)
        {
            Assert.That(CoopScaling.Toughness(players), Is.EqualTo(1f));
            Assert.That(CoopScaling.Firepower(players), Is.EqualTo(1f));
        }

        [Test]
        public void EachPlayerMakesThemHarderButLessThanAWholeShipMore()
        {
            for (int n = 2; n <= 7; n++)
            {
                Assert.That(CoopScaling.Toughness(n), Is.GreaterThan(CoopScaling.Toughness(n - 1)));
                Assert.That(CoopScaling.Firepower(n), Is.GreaterThan(CoopScaling.Firepower(n - 1)));
                Assert.That(CoopScaling.Toughness(n), Is.LessThan(n), "toughness grows slower than the players' firepower");
                Assert.That(CoopScaling.Firepower(n), Is.LessThan(CoopScaling.Toughness(n)));
            }
        }
    }

    public class CoopMessageTests
    {
        static readonly string[] ServerOnly = CoopWire.ServerOnly;
        static string Server(params string[] path) => Path.GetFullPath(Path.Combine(new[] { Application.dataPath, "..", "Server" }.Concat(path).ToArray()));

        [Test]
        public void TheGameSpeaksTheServersProtocol()
        {
            // the server builds the game's own CoopWire.cs, and takes its version from it
            string project = Server("CoopServer.Core", "CoopServer.Core.csproj"), hub = Server("CoopServer.Core", "Hub.cs");
            if (!File.Exists(project)) Assert.Ignore($"no server source at {project}");
            StringAssert.Contains(@"Assets\Carrier\Scripts\Net\CoopWire.cs", File.ReadAllText(project));
            StringAssert.Contains("public const int Protocol = CoopWire.Protocol;", File.ReadAllText(hub));
            Assert.That(CoopClient.Protocol, Is.EqualTo(CoopWire.Protocol));
        }

        [Test]
        public void TheGameReadsEveryFieldOfTheServersMessages()
        {
            // Server/protocol: one of each message the server sends, with all its fields (the
            // server's tests check it sends nothing else)
            string dir = Server("protocol");
            if (!Directory.Exists(dir)) Assert.Ignore($"no samples at {dir}");
            var fields = new System.Collections.Generic.HashSet<string>(
                typeof(CoopMsg).GetFields().Concat(typeof(CoopPlayer).GetFields()).Select(f => f.Name));
            foreach (var type in ServerOnly)
            {
                string json = File.ReadAllText(Path.Combine(dir, type + ".json"));
                Assert.That(JsonUtility.FromJson<CoopMsg>(json).t, Is.EqualTo(type));
                foreach (Match key in Regex.Matches(json, "\"(\\w+)\"\\s*:"))
                    Assert.That(fields, Has.Member(key.Groups[1].Value), $"'{type}' has a field '{key.Groups[1].Value}' the game doesn't read");
            }
            var players = JsonUtility.FromJson<CoopMsg>(File.ReadAllText(Path.Combine(dir, "players.json")));
            Assert.That(players.players.Length, Is.EqualTo(2));
            Assert.That(players.players[1].client, Is.EqualTo("WebGLPlayer"));
            var join = JsonUtility.FromJson<CoopMsg>(File.ReadAllText(Path.Combine(dir, "join.json")));
            Assert.That(join.p.name, Is.EqualTo("Bravo"));
        }

        [Test]
        public void AHelloCarriesWhatTheServerReads()
        {
            string json = JsonUtility.ToJson(new HelloMsg { v = CoopClient.Protocol, name = "Tester", level = "StarSystem", client = "WindowsPlayer", captain = 2, clock = 12.5 });
            foreach (var field in new[] { "\"t\":\"hello\"", $"\"v\":{CoopClient.Protocol}", "\"name\":\"Tester\"", "\"level\":\"StarSystem\"", "\"client\":\"WindowsPlayer\"", "\"captain\":2", "\"clock\":12.5" })
                StringAssert.Contains(field, json);
        }

        [Test]
        public void AWelcomeIsRead()
        {
            var m = JsonUtility.FromJson<CoopMsg>("{\"t\":\"welcome\",\"id\":3,\"max\":7,\"now\":12.5,\"level\":\"StarSystem\",\"epoch\":2.5,\"host\":3}");
            Assert.That(m.t, Is.EqualTo("welcome"));
            Assert.That(m.id, Is.EqualTo(3));
            Assert.That(m.max, Is.EqualTo(7));
            Assert.That(m.host, Is.EqualTo(3));
            Assert.That(m.now - m.epoch, Is.EqualTo(10.0).Within(1e-9));
            Assert.That(m.level, Is.EqualTo("StarSystem"));
        }

        [Test]
        public void TheRosterIsRead()
        {
            var m = JsonUtility.FromJson<CoopMsg>("{\"t\":\"players\",\"max\":7,\"players\":[{\"id\":1,\"name\":\"A\",\"level\":\"L\",\"client\":\"WebGLPlayer\",\"captain\":4},{\"id\":2,\"name\":\"B\",\"level\":\"M\",\"client\":\"WindowsPlayer\",\"captain\":0}]}");
            Assert.That(m.players.Length, Is.EqualTo(2));
            Assert.That(m.players[0].name, Is.EqualTo("A"));
            Assert.That(m.players[0].captain, Is.EqualTo(4));
            Assert.That(m.players[1].level, Is.EqualTo("M"));
        }

        [Test]
        public void TheEnemiesGoOutAndComeBackTheSame()
        {
            var sent = new EntitiesMsg { ts = 4.25, e = new[] { new CoopEntity { id = "System/Base/Gun A", x = 1, y = 2, z = 3, qw = 1, hu = 0.5f, dead = false, lurk = true } } };
            var got = JsonUtility.FromJson<CoopMsg>(JsonUtility.ToJson(sent));
            Assert.That(got.t, Is.EqualTo("es"));
            Assert.That(got.ts, Is.EqualTo(4.25));
            Assert.That(got.e.Length, Is.EqualTo(1));
            Assert.That(got.e[0].id, Is.EqualTo("System/Base/Gun A"));
            Assert.That(new Double3(got.e[0].x, got.e[0].y, got.e[0].z), Is.EqualTo(new Double3(1, 2, 3)));
            Assert.That(got.e[0].hu, Is.EqualTo(0.5f));
            Assert.That(got.e[0].lurk, Is.True);
        }

        [Test]
        public void HitsGoWhereTheServerSendsThem()
        {
            StringAssert.Contains("\"toHost\":true", JsonUtility.ToJson(new HitEnemyMsg { tg = "x", a = 10 }));
            StringAssert.Contains("\"to\":5", JsonUtility.ToJson(new HitPlayerMsg { to = 5, a = 10 }));
        }

        [Test]
        public void TheGameNeverSendsTheServersOwnMessages()
        {
            var sent = new object[] { new LevelMsg(), new CaptainMsg(), new PingMsg(), new ShipMsg(), new EntitiesMsg(), new FireMsg(), new HitEnemyMsg(), new HitPlayerMsg(), new TestMsg() };
            foreach (var msg in sent)
            {
                string t = JsonUtility.FromJson<CoopMsg>(JsonUtility.ToJson(msg)).t;
                Assert.That(t, Is.Not.Empty, msg.GetType().Name);
                Assert.That(ServerOnly, Has.No.Member(t), msg.GetType().Name);
            }
        }
    }

    public class CoopAddressTests
    {
        static (string address, string port) Split(string s)
        {
            string address = "old", port = "7777";
            CoopMenu.SplitAddress(s, ref address, ref port);
            return (address, port);
        }

        [TestCase("192.168.1.20:7000", "192.168.1.20", "7000")]
        [TestCase("  example.com:8080  ", "example.com", "8080")]
        [TestCase("example.com", "example.com", "7777")]
        [TestCase("[::1]:7001", "::1", "7001")]
        [TestCase("::1", "::1", "7777")]
        [TestCase("fe80::2", "fe80::2", "7777")]
        [TestCase("host:notaport", "host:notaport", "7777")]
        [TestCase("ws://example.com:9000/coop", "ws://example.com:9000/coop", "7777")]
        [TestCase("wss://example.com", "wss://example.com", "7777")]
        public void AnAddressIsSplitIntoHostAndPort(string typed, string address, string port)
        {
            var got = Split(typed);
            Assert.That(got.address, Is.EqualTo(address));
            Assert.That(got.port, Is.EqualTo(port));
        }

        [TestCase("192.168.1.20", "7000")]
        [TestCase("example.com", "7777")]
        [TestCase("::1", "7001")]
        [TestCase("fe80::2", "7777")]
        public void AnAddressAndPortJoinedSplitBackTheSame(string address, string port)
        {
            string joined = CoopMenu.JoinAddress(address, port);
            Assert.That(Split(joined), Is.EqualTo((address, port)), joined);
        }

        [Test]
        public void AWebSocketAddressIsKeptWhole()
        {
            Assert.That(CoopMenu.JoinAddress("ws://example.com:9000/coop", "7777"), Is.EqualTo("ws://example.com:9000/coop"));
            Assert.That(Split(CoopMenu.JoinAddress("ws://example.com:9000/coop", "7777")).address, Is.EqualTo("ws://example.com:9000/coop"));
        }

        [TestCase("192.168.1.20", 7777, false, "ws://192.168.1.20:7777/coop")]
        [TestCase(" example.com ", 7001, true, "wss://example.com:7001/coop")]
        [TestCase("::1", 7777, false, "ws://[::1]:7777/coop")]
        [TestCase("[::1]", 7777, false, "ws://[::1]:7777/coop")]
        [TestCase("ws://example.com:9000", 7777, false, "ws://example.com:9000/coop")]
        [TestCase("wss://example.com/game/coop", 7777, false, "wss://example.com/game/coop")]
        public void TheServersWebSocketAddress(string address, int port, bool secure, string url) =>
            Assert.That(CoopClient.UrlFor(address, port, secure), Is.EqualTo(url));

        [TestCase("http://host/index.html?systemtest&cooptest", "cooptest", true)]
        [TestCase("http://host/index.html?server=1.2.3.4:7777&connect", "connect", true)]
        [TestCase("http://host/index.html?server=1.2.3.4:7777&connect", "server", true)]
        [TestCase("http://host/index.html?servers=1", "server", false)]
        [TestCase("http://host/index.html?a=1#connect", "connect", false)]
        [TestCase("http://host/index.html#?connect", "connect", false)]
        [TestCase("http://host/index.html", "connect", false)]
        [TestCase(null, "connect", false)]
        public void APageAddressIsSearchedForItsSwitches(string url, string key, bool found) =>
            Assert.That(LaunchArgs.HasQueryKey(url, key), Is.EqualTo(found));

        [TestCase("http://host/index.html?server=1.2.3.4%3A7777&connect", "server", "1.2.3.4:7777")]
        [TestCase("http://host/index.html?landworld=Thalassa_Prime#top", "landworld", "Thalassa_Prime")]
        [TestCase("http://host/index.html?connect", "connect", null)]
        [TestCase("http://host/index.html#?server=x", "server", null)]
        [TestCase("http://host/index.html", "server", null)]
        public void APageAddressGivesItsValues(string url, string key, string value) =>
            Assert.That(LaunchArgs.QueryValue(url, key), Is.EqualTo(value));
    }

    /// <summary>Keeping the game's clock in step with the server's.</summary>
    public class ClockSyncTests
    {
        // a server clock 100 s ahead of ours; a ping sent at local time t with round trip rtt
        // comes back at t + rtt carrying the server's time at its half-way point
        const double Ahead = 100.0;

        static void Ping(ClockSync c, double t, double rtt) => c.Pong(t, t + rtt * 0.5 + Ahead, t + rtt);

        [Test]
        public void TheWelcomeSetsTheClock()
        {
            var c = new ClockSync();
            c.Welcome(1000.0, 400.0);
            Assert.That(c.Offset, Is.EqualTo(600.0));
            Assert.That(c.Synced, Is.False);
        }

        [Test]
        public void PingsBringTheClockInStep()
        {
            var c = new ClockSync();
            c.Welcome(Ahead + 0.3, 0.0);        // the welcome is 0.3 s out (it came over a slow link)
            for (int i = 0; i < 20; i++) Ping(c, 1.0 + i * 2.0, 0.02);
            Assert.That(c.Synced, Is.True);
            Assert.That(c.Offset, Is.EqualTo(Ahead).Within(1e-3));
            Assert.That(c.Rtt, Is.EqualTo(0.02).Within(1e-9));
        }

        [Test]
        public void AClockFarOutIsSetOutright()
        {
            var c = new ClockSync();
            c.Welcome(0.0, 0.0);                 // 100 s out
            Ping(c, 1.0, 0.05);
            Assert.That(c.Offset, Is.EqualTo(Ahead).Within(1e-9));
        }

        [Test]
        public void ASlowPingDoesntMoveTheClock()
        {
            var c = new ClockSync();
            c.Welcome(Ahead, 0.0);
            Ping(c, 1.0, 0.01);
            // a ping held up on the way back: half its extra delay would show as a clock error
            c.Pong(3.0, 3.005 + Ahead, 3.4);
            Assert.That(c.Offset, Is.EqualTo(Ahead).Within(1e-9));
        }

        [Test]
        public void ALastingRiseInLatencyIsTakenUp()
        {
            // 2 ms at first, then the link settles at 10 ms and the server's clock steps 50 ms
            // ahead (it was restarted, say): the clock must follow within a minute or two
            var c = new ClockSync();
            c.Welcome(Ahead, 0.0);
            double t = 1.0;
            for (int i = 0; i < 5; i++, t += 2.0) Ping(c, t, 0.002);
            for (int i = 0; i < 60; i++, t += 2.0) c.Pong(t, t + 0.005 + Ahead + 0.05, t + 0.010);
            Assert.That(c.Offset, Is.EqualTo(Ahead + 0.05).Within(1e-3));
        }

        [Test]
        public void AResetStartsOver()
        {
            var c = new ClockSync();
            Ping(c, 1.0, 0.01);
            c.Reset();
            Assert.That(c.Synced, Is.False);
            Assert.That(c.Rtt, Is.EqualTo(0.0));
        }
    }
}
