using System;

namespace TimeWave.Shared
{
    /// <summary>Integer helpers; the simulation never uses float so every replay produces identical results.</summary>
    public static class IntMath
    {
        public static long DistSq(int ax, int ay, int bx, int by)
        {
            long dx = bx - ax, dy = by - ay;
            return dx * dx + dy * dy;
        }

        /// <summary>Floor of the square root. Math.Sqrt is correctly rounded (IEEE 754), and the result is fixed up to the exact integer.</summary>
        public static int Sqrt(long value)
        {
            if (value <= 0) return 0;
            long r = (long)Math.Sqrt(value);
            while (r * r > value) r--;
            while ((r + 1) * (r + 1) <= value) r++;
            return (int)r;
        }

        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
    }
}
