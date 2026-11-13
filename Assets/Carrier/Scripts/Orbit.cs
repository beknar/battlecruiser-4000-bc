using UnityEngine;

namespace CarrierGame
{
    /// <summary>
    /// Where something is on its orbit, as a function of time. The thing (its transform) is a
    /// frame that moves but doesn't turn: what is inside it (a planet and what it carries, a
    /// moon, a station, an asteroid field) goes along.
    ///
    /// - Kepler: an ellipse round its parent (or round the star, for no parent), with the
    ///   period Kepler's third law gives for the parent's mass: T = 2 pi sqrt(a^3 / GM). The
    ///   planets' and moons' orbits are circles; the comets' are long ellipses, fast near the
    ///   star and slow far out.
    /// - Lagrange: one of the five points where a small body keeps its place relative to a
    ///   planet and the star, going round with the planet. L1 and L2 lie on the line through
    ///   the star and the planet, inside and outside its orbit, at the edge of its Hill sphere
    ///   (r (mu/3)^(1/3) from it); L3 is opposite the planet; L4 and L5 are 60 degrees ahead of
    ///   and behind it on its orbit, where the star, the planet and the point make an
    ///   equilateral triangle. L4 and L5 are stable (for a planet under 1/26 of the star's mass):
    ///   something there swings slowly round the point (librates) and stays. L1, L2 and L3 are
    ///   not: something there drifts off unless it fires its thrusters (StationKeeping).
    ///
    /// OrbitalSystem moves every orbit each frame.
    /// </summary>
    public class Orbit : MonoBehaviour
    {
        public enum Kind { Kepler, Lagrange }

        public Kind kind = Kind.Kepler;
        [Tooltip("What it goes round (none: the star).")]
        public Orbit parent;
        [Tooltip("Its mass as a fraction of what it goes round (0 for a station or a field of rocks).")]
        public float massRatio;

        [Header("Kepler orbit (relative to what it goes round)")]
        public double semiMajor = 100000.0;
        public double eccentricity;
        [Tooltip("Toward the nearest point of the orbit (unit).")]
        public Vector3 periapsis = Vector3.right;
        [Tooltip("90 degrees on round the orbit, the way it goes (unit, at right angles to periapsis).")]
        public Vector3 ahead = Vector3.forward;
        [Tooltip("Mean anomaly at the start (radians: 0 at the nearest point).")]
        public double meanAnomaly0;

        [Header("Lagrange point")]
        [Tooltip("The planet whose Lagrange point this is (its orbit is round the star).")]
        public Orbit secondary;
        [Range(1, 5)] public int point = 4;
        [Tooltip("Libration round L4 or L5: how far it swings to and fro along the orbit (degrees).")]
        public float librationDegrees;
        public float librationPhase;
        [Tooltip("Added to the point (station-keeping drift), in the system's frame (m).")]
        [System.NonSerialized] public Vector3 drift;

        [Header("Carrying ships")]
        [Tooltip("Ships within this distance of it are carried along with it (0: none). The smallest such reach a ship is in wins.")]
        public float reach;
        [Tooltip("Turn with the orbit to keep the same side to what it goes round.")]
        public bool faceParent;
        [Tooltip("Mark this planet's five Lagrange points on the system map.")]
        public bool lagrangeOnMap;

        Quaternion rotation0;
        Vector3 dir0;
        bool started;

        // ------------------------------------------------------------------ masses and periods

        /// <summary>The gravitational parameter (G times the mass) of this body, m^3/s^2.</summary>
        public double BodyGM => massRatio * PrimaryGM;

        /// <summary>The gravitational parameter of what it goes round.</summary>
        public double PrimaryGM => parent != null ? parent.BodyGM : OrbitalSystem.StarGM;

        /// <summary>Seconds for one orbit.</summary>
        public double Period => kind == Kind.Lagrange && secondary != null ? secondary.Period
            : 2.0 * System.Math.PI * System.Math.Sqrt(semiMajor * semiMajor * semiMajor / System.Math.Max(1.0, PrimaryGM));

        /// <summary>Radians per second, on average.</summary>
        public double MeanMotion => 2.0 * System.Math.PI / Period;

        /// <summary>The Hill-sphere fraction (mu/3)^(1/3): how far L1 and L2 are from the planet, as
        /// a fraction of its distance from the star.</summary>
        public static double HillFraction(double mu) => System.Math.Pow(System.Math.Max(0.0, mu) / 3.0, 1.0 / 3.0);

        /// <summary>The libration rate round L4/L5 for a planet of mass fraction mu, as a fraction
        /// of its orbital rate: sqrt(27 mu / 4).</summary>
        public static double LibrationFraction(double mu) => System.Math.Sqrt(27.0 * mu / 4.0);

        // ------------------------------------------------------------------ where

        /// <summary>Its offset from what it goes round at time t (s), in the system's frame.</summary>
        public Vector3 Offset(double t) => OffsetD(t).ToVector3();

        /// <summary>Its offset from what it goes round at time t (s), in double precision (the
        /// system is millions of kilometres across).</summary>
        public Double3 OffsetD(double t)
        {
            if (kind == Kind.Lagrange && secondary != null) return LagrangeOffsetD(t);
            double e = System.Math.Min(0.99, System.Math.Max(0.0, eccentricity));
            double M = meanAnomaly0 + MeanMotion * t;
            double E = M;
            for (int i = 0; i < 12; i++)     // Kepler's equation, E - e sin E = M (Newton's method)
            {
                double f = E - e * System.Math.Sin(E) - M;
                E -= f / (1.0 - e * System.Math.Cos(E));
            }
            double x = semiMajor * (System.Math.Cos(E) - e);
            double y = semiMajor * System.Math.Sqrt(1.0 - e * e) * System.Math.Sin(E);
            Plane(out Double3 P, out Double3 Q);
            return P * x + Q * y;
        }

        /// <summary>The orbit's plane as exact unit vectors (in doubles: a float's "unit" vector is
        /// out by a few parts in a hundred million, a hundred metres at a planet's distance).</summary>
        void Plane(out Double3 P, out Double3 Q)
        {
            P = ((Double3)periapsis).normalized;
            Q = (Double3)ahead;
            Q = (Q - P * Double3.Dot(P, Q)).normalized;
        }

        /// <summary>A Lagrange point of the secondary at time t (its offset from the star).</summary>
        Double3 LagrangeOffsetD(double t)
        {
            double mu = secondary.massRatio;
            Double3 s = secondary.OffsetD(t);
            Double3 p = LagrangePointD(s, secondary.periapsis, secondary.ahead, mu, point,
                                       librationDegrees * System.Math.Sin(secondary.MeanMotion * LibrationFraction(mu) * t + librationPhase));
            return p + drift;
        }

        /// <summary>LagrangePoint in double precision.</summary>
        public static Double3 LagrangePointD(Double3 s, Vector3 p, Vector3 q, double mu, int n, double swing = 0.0)
        {
            double k = HillFraction(mu);
            Double3 P = p, Q = q;
            double sp = Double3.Dot(s, P), sq = Double3.Dot(s, Q);
            Double3 along = -P * sq + Q * sp;
            switch (n)
            {
                case 1: return s * (1.0 - k);
                case 2: return s * (1.0 + k);
                case 3: return -s * (1.0 + 5.0 * mu / 12.0);
                default:
                {
                    double a = ((n == 4 ? 60.0 : -60.0) + swing) * System.Math.PI / 180.0;
                    return s * System.Math.Cos(a) + along * System.Math.Sin(a);
                }
            }
        }

        /// <summary>Lagrange point `n` (1..5) for a planet at offset `s` from the star on an orbit
        /// in the plane of (p, q), with mass fraction mu; `swing` degrees of libration added to L4/L5.</summary>
        public static Vector3 LagrangePoint(Vector3 s, Vector3 p, Vector3 q, double mu, int n, double swing = 0.0)
        {
            float k = (float)HillFraction(mu);
            // the way the planet is going round, at this point of its orbit
            float sp = Vector3.Dot(s, p), sq = Vector3.Dot(s, q);
            Vector3 along = -p * sq + q * sp;
            switch (n)
            {
                case 1: return s * (1f - k);
                case 2: return s * (1f + k);
                case 3: return -s * (1f + 5f * (float)mu / 12f);
                default:
                {
                    float a = (float)((n == 4 ? 60.0 : -60.0) + swing) * Mathf.Deg2Rad;
                    return s * Mathf.Cos(a) + along * Mathf.Sin(a);
                }
            }
        }

        /// <summary>A point of the ellipse at eccentric anomaly E (its offset from what it goes round).</summary>
        public Vector3 EllipsePoint(double E)
        {
            double e = System.Math.Min(0.99, System.Math.Max(0.0, eccentricity));
            return periapsis * (float)(semiMajor * (System.Math.Cos(E) - e)) + ahead * (float)(semiMajor * System.Math.Sqrt(1.0 - e * e) * System.Math.Sin(E));
        }

        /// <summary>Velocity at time t relative to what it goes round (m/s, finite difference).</summary>
        public Vector3 Velocity(double t) => (OffsetD(t + 0.5) - OffsetD(t - 0.5)).ToVector3();

        /// <summary>Put its frame at a world position (OrbitalSystem works it out in double, round
        /// the world's origin), `offset` from what it goes round (for turning to face it).</summary>
        public void Put(Vector3 world, Double3 offset)
        {
            Vector3 o = offset.normalized.ToVector3();
            if (!started)
            {
                started = true;
                rotation0 = transform.rotation;
                dir0 = o;
            }
            transform.position = world;
            if (faceParent && o.sqrMagnitude > 0.5f && dir0.sqrMagnitude > 0.5f)
                transform.rotation = Quaternion.FromToRotation(dir0, o) * rotation0;
        }
    }
}
