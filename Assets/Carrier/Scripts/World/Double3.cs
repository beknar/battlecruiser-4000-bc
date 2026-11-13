using System;
using UnityEngine;

namespace CarrierGame
{
    /// <summary>A position or offset in double precision (metres): where things are in the star
    /// system, millions of kilometres across. A float is good to a few centimetres only within a
    /// few hundred kilometres of the world's origin, so the world is drawn round the frigate
    /// (OrbitalSystem.Origin) and only the system's own positions are kept in these.</summary>
    [Serializable]
    public struct Double3 : IEquatable<Double3>
    {
        public double x, y, z;

        public Double3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public Double3(Vector3 v) { x = v.x; y = v.y; z = v.z; }

        public static readonly Double3 zero = new Double3(0, 0, 0);

        public double sqrMagnitude => x * x + y * y + z * z;
        public double magnitude => Math.Sqrt(sqrMagnitude);
        public Double3 normalized { get { double m = magnitude; return m > 1e-12 ? this / m : zero; } }

        public static Double3 operator +(Double3 a, Double3 b) => new Double3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Double3 operator -(Double3 a, Double3 b) => new Double3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Double3 operator -(Double3 a) => new Double3(-a.x, -a.y, -a.z);
        public static Double3 operator *(Double3 a, double k) => new Double3(a.x * k, a.y * k, a.z * k);
        public static Double3 operator *(double k, Double3 a) => new Double3(a.x * k, a.y * k, a.z * k);
        public static Double3 operator /(Double3 a, double k) => new Double3(a.x / k, a.y / k, a.z / k);
        public static Double3 operator +(Double3 a, Vector3 b) => new Double3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Double3 operator -(Double3 a, Vector3 b) => new Double3(a.x - b.x, a.y - b.y, a.z - b.z);

        public static double Dot(Double3 a, Double3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Double3 Cross(Double3 a, Double3 b) => new Double3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static double Distance(Double3 a, Double3 b) => (a - b).magnitude;
        public static Double3 Lerp(Double3 a, Double3 b, double t) => a + (b - a) * t;

        /// <summary>As a float vector (for an offset, or a position near the origin).</summary>
        public Vector3 ToVector3() => new Vector3((float)x, (float)y, (float)z);
        public static explicit operator Vector3(Double3 d) => d.ToVector3();
        public static implicit operator Double3(Vector3 v) => new Double3(v);

        public bool Equals(Double3 o) => x == o.x && y == o.y && z == o.z;
        public override bool Equals(object o) => o is Double3 d && Equals(d);
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2);
        public override string ToString() => $"({x:F1}, {y:F1}, {z:F1})";
    }
}
