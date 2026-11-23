#nullable enable
using System;

namespace BlakesTesting.Flow
{
    /// <summary>
    ///     Revit warns "Line is slightly off axis and may cause inaccuracies" about a model line that is a hair off the X, Y
    ///     or Z axis. A flow arrow along a vertical pipe in a 3D view is exactly that: the view's up direction is never
    ///     exactly vertical, so the arrow's shaft lands a fraction of a degree off. This puts such a line exactly on the
    ///     axis it nearly lies on, keeping its length. No Revit types, so it is tested without Revit.
    /// </summary>
    public static class AxisSnap
    {
        /// <summary>A line within this many degrees of an axis is put on it. Far inside what a person could see.</summary>
        public const double ToleranceDegrees = 2.0;

        /// <summary>
        ///     The end of the line from <paramref name="a"/> to <paramref name="b"/>, moved onto the axis the line nearly
        ///     lies on (same length, same start); <paramref name="b"/> itself when the line is on an axis already or not
        ///     near one.
        /// </summary>
        public static (double X, double Y, double Z) SnapEnd((double X, double Y, double Z) a, (double X, double Y, double Z) b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
            double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (len < 1e-12) return b;

            double cosTol = Math.Cos(ToleranceDegrees * Math.PI / 180);
            if (Math.Abs(dx) / len >= cosTol)
                return (dy == 0 && dz == 0) ? b : (a.X + Math.Sign(dx) * len, a.Y, a.Z);
            if (Math.Abs(dy) / len >= cosTol)
                return (dx == 0 && dz == 0) ? b : (a.X, a.Y + Math.Sign(dy) * len, a.Z);
            if (Math.Abs(dz) / len >= cosTol)
                return (dx == 0 && dy == 0) ? b : (a.X, a.Y, a.Z + Math.Sign(dz) * len);
            return b;
        }
    }
}
