using System;

namespace MeesaMultisMaker.MapGeneration
{
    /// <summary>
    /// Classic Perlin noise with octave (fractal) support.
    /// Produces values in approximately -1..1.
    /// </summary>
    public class PerlinNoise
    {
        private readonly int[] _perm;
        private readonly Random _rng;

        public PerlinNoise(int seed)
        {
            _rng = new Random(seed);
            _perm = new int[512];
            var p = new int[256];
            for (int i = 0; i < 256; i++) p[i] = i;
            // Fisher-Yates shuffle
            for (int i = 255; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                int tmp = p[i]; p[i] = p[j]; p[j] = tmp;
            }
            for (int i = 0; i < 512; i++) _perm[i] = p[i & 255];
        }

        /// <summary>
        /// Single-octave 2D Perlin noise.
        /// </summary>
        public double Noise(double x, double y)
        {
            int xi = (int)Math.Floor(x) & 255;
            int yi = (int)Math.Floor(y) & 255;
            double xf = x - Math.Floor(x);
            double yf = y - Math.Floor(y);

            double u = Fade(xf);
            double v = Fade(yf);

            int aa = _perm[_perm[xi] + yi];
            int ab = _perm[_perm[xi] + yi + 1];
            int ba = _perm[_perm[xi + 1] + yi];
            int bb = _perm[_perm[xi + 1] + yi + 1];

            double x1 = Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1, yf), u);
            double x2 = Lerp(Grad(ab, xf, yf - 1), Grad(bb, xf - 1, yf - 1), u);

            return Lerp(x1, x2, v);
        }

        /// <summary>
        /// Fractal Brownian Motion — multiple octaves of noise layered together.
        /// Returns values in approximately -1..1.
        /// </summary>
        public double FBM(double x, double y, int octaves, double lacunarity = 2.0, double persistence = 0.5)
        {
            double total = 0;
            double amplitude = 1;
            double frequency = 1;
            double maxValue = 0;

            for (int i = 0; i < octaves; i++)
            {
                total += Noise(x * frequency, y * frequency) * amplitude;
                maxValue += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            return total / maxValue;
        }

        private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
        private static double Lerp(double a, double b, double t) => a + t * (b - a);

        private static double Grad(int hash, double x, double y)
        {
            int h = hash & 3;
            double u = h < 2 ? x : y;
            double v = h < 2 ? y : x;
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }
    }
}
