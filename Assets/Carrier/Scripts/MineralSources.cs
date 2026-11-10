using System.Collections.Generic;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>What the mining gear (the laser and the sensor) has in common: where the minerals
    /// are, and when the ship can't use the gear.</summary>
    public static class MineralSources
    {
        static readonly List<SurfaceVein> veins = new List<SurfaceVein>();

        /// <summary>Every source not mined out whose edge is within `range` of a point: down on a
        /// planet, its veins; in space, the deposits (asteroids, moons, comets and their debris).</summary>
        public static void Near(Vector3 at, float range, List<IMineralSource> into)
        {
            into.Clear();
            if (Demo.PlanetLanding.OnSurface)
            {
                SurfaceVeins.Near(Demo.PlanetLanding.Instance.Surface, at, range, veins);
                foreach (var v in veins) if (!v.Depleted) into.Add(v);
                return;
            }
            foreach (var d in MineralDeposit.All)
                if (d != null && !d.Depleted && Vector3.Distance(at, d.Position) - d.Radius <= range) into.Add(d);
        }

        /// <summary>Why the ship can't use its mining gear now (null if it can): it is destroyed,
        /// in warp, or landing or taking off.</summary>
        public static string ShipBusy(CarrierController ship, WarpDrive warp)
        {
            if (ship.Destroyed) return "the ship is destroyed";
            if (warp != null && warp.Current != WarpDrive.State.Ready) return "the ship is in warp";
            if (Demo.PlanetLanding.Transitioning) return "not while landing or taking off";
            return null;
        }
    }
}
