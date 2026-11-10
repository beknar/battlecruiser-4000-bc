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
    }

    /// <summary>One enemy, as the host has it.</summary>
    [Serializable]
    public class CoopEntity
    {
        public string id;
        public float x, y, z, qx, qy, qz, qw, vx, vy, vz;
        public float hu;          // hull, 0 to 1
        public bool dead, gone, lurk;
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
        // a player's ship
        public float x, y, z, qx, qy, qz, qw, vx, vy, vz, th, sh, hu;
        public bool su, dead, dock;
        public int warp, cap;
        public string surf;
        // enemies
        public CoopEntity[] e;
        // shots and beams
        public string src, k, pf, tg;
        public float dx, dy, dz;
        // damage
        public float a;
        public int kd;
        // the self-test's bot
        public string note;
        public string[] ids;
    }

    [Serializable] class HelloMsg { public string t = CoopWire.Hello; public int v; public string name, level, client; public int captain; public double clock; }
    [Serializable] class LevelMsg { public string t = CoopWire.Level; public string level; public double clock; }
    [Serializable] class CaptainMsg { public string t = CoopWire.Captain; public int captain; }
    [Serializable] class PingMsg { public string t = CoopWire.Ping; public double c; }

    /// <summary>My ship, to everyone in my room.</summary>
    [Serializable]
    class ShipMsg
    {
        public string t = CoopWire.Ship;
        public double ts;
        public float x, y, z, qx, qy, qz, qw, vx, vy, vz, th, sh, hu;
        public bool su, dead, dock;
        public int warp, cap;
        public string surf = "";
    }

    /// <summary>The enemies, from the host to everyone else in the room.</summary>
    [Serializable]
    class EntitiesMsg { public string t = CoopWire.Enemies; public double ts; public CoopEntity[] e; }

    /// <summary>A shot, missile or beam, for show in the other games (k: CoopWire.Shot, Missile or Beam).</summary>
    [Serializable]
    class FireMsg { public string t = CoopWire.Fire; public string src, k, pf, tg = ""; public float x, y, z, dx, dy, dz; }

    /// <summary>A hit on an enemy, to the host (whose game has the real one).</summary>
    [Serializable]
    class HitEnemyMsg { public string t = CoopWire.HitEnemy; public bool toHost = true; public string tg; public float a, x, y, z; public int kd; }   // tg: the enemy

    /// <summary>A hit on a player's ship, from the host to that player.</summary>
    [Serializable]
    class HitPlayerMsg { public string t = CoopWire.HitPlayer; public int to; public float a, x, y, z; public int kd; }

    /// <summary>For the self-test's bot: what this game can see (enemy ids and such).</summary>
    [Serializable]
    class TestMsg { public string t = CoopWire.Test; public string note; public string[] ids; }
}
