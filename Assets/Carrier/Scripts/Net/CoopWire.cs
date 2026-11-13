namespace CarrierGame
{
    /// <summary>
    /// The co-op protocol's version and message names, in one place for everyone who speaks it:
    /// the game, the server (Server/CoopServer.Core compiles this very file) and the test bots
    /// (Tools/CoopTest read the version from it). Plain C# with no Unity in it, so the server can
    /// build it too. Sample messages from the server are in Server/protocol; both sides' tests
    /// check them.
    ///
    /// Released games and servers must keep understanding each other, so:
    /// - adding a field, or a new kind of game message, keeps the version (the game's JSON reader
    ///   skips fields it doesn't know, and the server passes on anything it doesn't handle);
    /// - a field or a message name is never renamed or given another meaning;
    /// - only a change old builds can't live with bumps the version, and a server refuses a game
    ///   with another version, so every released build would need replacing.
    /// </summary>
    public static class CoopWire
    {
        // 2: positions are from the reach they are in (rf), the system at its real size
        public const int Protocol = 2;

        // the game to the server
        public const string Hello = "hello", Level = "level", Ping = "ping", Captain = "captain";
        // the server to the game
        public const string Welcome = "welcome", Reject = "reject", Players = "players", Room = "room",
            Join = "join", Leave = "leave", Pong = "pong";
        /// <summary>Messages only the server sends: one from a game is never passed on.</summary>
        public static readonly string[] ServerOnly = { Welcome, Reject, Players, Room, Join, Leave, Pong };

        // between games (the server passes them on with "from" added): a player's ship, the
        // host's enemies, a shot or missile or beam for show, a hit on an enemy (to the host), a
        // hit on a player's ship (from the host), and the self-test's notes
        public const string Ship = "ps", Enemies = "es", Fire = "fx", HitEnemy = "de", HitPlayer = "dp", Test = "test";
        // the kinds of Fire
        public const string Shot = "shot", Missile = "msl", Beam = "beam";
        // a message on the comms channel, to everyone in the room (CoopChat): {text}
        public const string Chat = "chat";

        // how a game message is routed: to one player, to the room's host, or (neither) to
        // everyone else in the room; the server adds who it came from
        public const string To = "to", ToHost = "toHost", From = "from";
    }
}
