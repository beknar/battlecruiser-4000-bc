using System;

namespace CarrierGame.Demo
{
    // The co-op messages, as JSON (JsonUtility). Everything coming in is read into one Msg, whose
    // fields are the union of every kind (each kind only fills its own); everything going out is
    // its own small class, so it carries only its own fields. Positions are relative to the star
    // system (where there is one), so they mean the same in every game sharing the orbit clock.

    [Serializable]
    public class CoopPlayer
    {
        public int id;
        public string name;
        public string level;
        public string client;
        public int captain;
        /// <summary>Joined with one of the server's AI keys (the server says so; a game can't).</summary>
        public bool ai;
    }

    /// <summary>One enemy, as the host has it.</summary>
    [Serializable]
    public class CoopEntity
    {
        public string id;
        public double x, y, z;    // from the reach `rf` (or, rf "", in the system: double, millions of kilometres across)
        public string rf = "";
        public float qx, qy, qz, qw, vx, vy, vz;
        public float hu;          // hull, 0 to 1
        public bool dead, gone, lurk;
        public int sy;            // its components (ShipSystems.ToWire; 0, from an older game: whole)
        public float sf;          // its shield: 1 + how charged (0: none, or not sent)
        public bool sd;           // its shield down
        public int tr;            // the player its tractor beam holds: 1 + their id (0: none, or from an older game)
        public float ar;          // its armour: 1 + how whole (0: none, or from an older game)
        public string k;          // made during play: its key in CoopSpawnables ("": in the level; older games leave it out)
        public int ck;            // its cloak: 0 none (or an older game), 1 down, 2 up
        public int pt;            // the player its targeting laser paints: 1 + their id (0: none, or an older game)
    }

    /// <summary>Anything that arrives.</summary>
    [Serializable]
    public class CoopMsg
    {
        public string t;
        public int id, from, max, host;
        public double now, epoch, c, s, ts;
        public string level, why, name;
        public CoopPlayer p;
        public CoopPlayer[] players;
        // a player's ship (and everything with a position: from the reach `rf`, or the system's origin if rf is "")
        public double x, y, z;
        public string rf = "";
        public float qx, qy, qz, qw, vx, vy, vz, th, sh, hu;
        public bool su, dead, dock;
        public int warp, cap, sy;   // (sy: the ship's components, ShipSystems.ToWire)
        public string surf, fr;
        // enemies
        public CoopEntity[] e;
        // shots and beams
        public string src, k, pf, tg;
        public float dx, dy, dz;
        // damage (kd: the kind as games before the new kinds know it; kx: the kind + 1, 0 if not
        // sent; cp: the component the shooter aimed at, a ShipSystem, 0 the hull)
        public float a;
        public int kd, kx, cp;
        // a gift of credits (its number, for the receipt)
        public int gid;
        // a tractor beam's hold on my ship, from the host: on or let go, how many beams, held fast
        public bool on, held;
        public int n;
        // a player's ship: boarders aboard it now, and taken by them (with dead; the "board"
        // message's count is n)
        public int bd;
        public bool cpt;
        // ... and by which faction (with cpt; "" or left out: the Vrask); a "board" message's
        // boarders' faction (the same)
        public string cby, fa;
        // a player's ship: what its game has in it (CoopWire.Content; older games leave it out: 0)
        public int cv;
        // a player's ship: its paint (PaintScheme.ToCode; "" or left out: unpainted)
        public string pt;
        // an artillery shell's aim (a fire message of kind "shell": from the reach arf)
        public double ax, ay, az;
        public string arf = "";
        // a strike from orbit (CoopWire.Strike)
        public string body, place, weapon;
        public double u, v;
        public float radius, cd, rb, pop;
        public int cpb;
        // a message on the comms channel
        public string text;
        // the self-test's bot
        public string note;
        public string[] ids;
    }

    // (key: the server's API key the player joins with, CoopKey; none for an open server)
    [Serializable] class HelloMsg { public string t = CoopWire.Hello; public int v; public string name, level, client, key; public int captain; public double clock; }
    [Serializable] class LevelMsg { public string t = CoopWire.Level; public string level; public double clock; }
    [Serializable] class CaptainMsg { public string t = CoopWire.Captain; public int captain; }
    [Serializable] class PingMsg { public string t = CoopWire.Ping; public double c; }

    /// <summary>My ship, to everyone in my room.</summary>
    [Serializable]
    class ShipMsg
    {
        public string t = CoopWire.Ship;
        public double ts;
        public double x, y, z;
        public string rf = "";
        public float qx, qy, qz, qw, vx, vy, vz, th, sh, hu;
        public bool su, dead, dock;
        public int warp, cap;
        public string surf = "";
        /// <summary>Which frigate (FrigateRoster id; older games leave it out).</summary>
        public string fr = "";
        /// <summary>Its components (ShipSystems.ToWire; older games leave it out: whole).</summary>
        public int sy;
        /// <summary>Boarders aboard (older games leave it out: none), and taken by them (with
        /// dead: no explosion in the other games).</summary>
        public int bd;
        public bool cpt;
        /// <summary>The faction that took it (with cpt; older games leave it out: the Vrask).</summary>
        public string cby;
        /// <summary>What this game has in it (CoopWire.Content; older games leave it out: 0).</summary>
        public int cv = CoopWire.Content;
        /// <summary>Its paint (ShipPaint: PaintScheme.ToCode, "" unpainted; older games leave
        /// it out, and ignore it).</summary>
        public string pt = "";
    }

    /// <summary>The enemies, from the host to everyone else in the room.</summary>
    [Serializable]
    class EntitiesMsg { public string t = CoopWire.Enemies; public double ts; public CoopEntity[] e; }

    /// <summary>A shot, missile or beam, for show in the other games (k: CoopWire.Shot, Missile or Beam).</summary>
    [Serializable]
    class FireMsg { public string t = CoopWire.Fire; public string src, k, pf, tg = "", rf = ""; public double x, y, z; public float dx, dy, dz; }

    /// <summary>An artillery shell, for show in the other games: a Fire with where it is going and its kind.</summary>
    [Serializable]
    class ShellMsg { public string t = CoopWire.Fire; public string src, k, pf, tg = "", rf = ""; public double x, y, z; public float dx, dy, dz; public double ax, ay, az; public string arf = ""; public int kx; }

    /// <summary>A strike from orbit by the host's Vrask siege, for every game's world.</summary>
    [Serializable]
    class StrikeMsg { public string t = CoopWire.Strike; public string body, place, weapon; public double u, v; public float radius, cd, rb, pop; public int cpb; }

    /// <summary>A hit on an enemy, to the host (whose game has the real one).</summary>
    [Serializable]
    class HitEnemyMsg { public string t = CoopWire.HitEnemy; public bool toHost = true; public string tg, rf = ""; public float a; public double x, y, z; public int kd, kx, cp; }   // tg: the enemy; cp: the component aimed at

    /// <summary>A hit on a player's ship, from the host to that player.</summary>
    [Serializable]
    class HitPlayerMsg { public string t = CoopWire.HitPlayer; public int to; public float a; public string rf = ""; public double x, y, z; public int kd, kx, cp; }

    /// <summary>The host's tractor beams on one player's ship: on (or let go), pulling toward
    /// x, y, z (from the reach rf), n beams, held fast (it can't fly free).</summary>
    [Serializable]
    class TractorMsg { public string t = CoopWire.Tractor; public int to; public bool on, held; public int n; public string rf = ""; public double x, y, z; }

    /// <summary>Boarders the host's pods have put aboard one player's ship (n of them).</summary>
    [Serializable]
    class BoardMsg { public string t = CoopWire.Board; public int to, n; public string fa; }

    /// <summary>A missile or pod shot down: whose it was (src), and where.</summary>
    [Serializable]
    class ShotDownMsg { public string t = CoopWire.ShotDown; public string src, rf = ""; public double x, y, z; }

    /// <summary>Credits for one player (from the AI wingman's hold to theirs).</summary>
    [Serializable]
    class GiftMsg { public string t = CoopWire.Gift; public int to, gid; public float a; public double ts; }

    /// <summary>For the self-test's bot: what this game can see (enemy ids and such).</summary>
    [Serializable]
    public class TestMsg { public string t = CoopWire.Test; public string note; public string[] ids; }
}
