using System;

namespace MeesaMultisMaker.ThreeD
{
    /// <summary>
    /// Trilinear cage deformer for the quad view. The box starts as the
    /// model's live bounds (NMin/NMax frame — the normalized coordinates
    /// the renderer consumes); each of the 8 corners can then be dragged
    /// in an ortho quadrant, skewing/scaling the cloud.
    /// Corner k packs (ix, iy, iz) as bits 0, 1, 2
    /// (0 = min face, 1 = max face). All cage coordinates are model frame
    /// (pre Rot/Scale UI); the form maps screen drags through the object
    /// matrix. Deform() rebuilds positions from Before, so repeated drags
    /// never accumulate error. Covariance/sigmas are intentionally left
    /// alone (same policy as the Move/Clone shape ops).
    /// </summary>
    public class SplatCage
    {
        public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
        public readonly double[] CX = new double[8];
        public readonly double[] CY = new double[8];
        public readonly double[] CZ = new double[8];
        /// Full snapshot at init: undo record, cancel restore, deform source.
        public SplatPoint[] Before;

        public static readonly int[,] Edges = new int[,]
        {
            { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 },
            { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 },
            { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 }
        };

        public static SplatCage Init(PlySplatModel model)
        {
            var c = new SplatCage();
            c.MinX = model.NMinX; c.MinY = model.NMinY; c.MinZ = model.NMinZ;
            c.MaxX = model.NMaxX; c.MaxY = model.NMaxY; c.MaxZ = model.NMaxZ;
            c.Before = model.Points.ToArray();
            c.ResetCorners();
            return c;
        }

        public void ResetCorners()
        {
            for (int k = 0; k < 8; k++)
            {
                CX[k] = ((k & 1) == 0) ? MinX : MaxX;
                CY[k] = (((k >> 1) & 1) == 0) ? MinY : MaxY;
                CZ[k] = (((k >> 2) & 1) == 0) ? MinZ : MaxZ;
            }
        }

        public void Deform(PlySplatModel model)
        {
            if (Before == null) return;
            double sx = MaxX - MinX, sy = MaxY - MinY, sz = MaxZ - MinZ;
            var pts = model.Points;
            int n = Math.Min(pts.Count, Before.Length);
            for (int i = 0; i < n; i++)
            {
                var b = Before[i];
                double u = sx > 1e-9 ? (b.X - MinX) / sx : 0.5;
                double v = sy > 1e-9 ? (b.Y - MinY) / sy : 0.5;
                double w = sz > 1e-9 ? (b.Z - MinZ) / sz : 0.5;
                if (u < 0) u = 0; else if (u > 1) u = 1;
                if (v < 0) v = 0; else if (v > 1) v = 1;
                if (w < 0) w = 0; else if (w > 1) w = 1;
                double x = 0, y = 0, z = 0;
                for (int k = 0; k < 8; k++)
                {
                    double m = (((k & 1) == 0) ? (1 - u) : u)
                             * ((((k >> 1) & 1) == 0) ? (1 - v) : v)
                             * ((((k >> 2) & 1) == 0) ? (1 - w) : w);
                    x += CX[k] * m;
                    y += CY[k] * m;
                    z += CZ[k] * m;
                }
                var p = pts[i];
                p.X = (float)x;
                p.Y = (float)y;
                p.Z = (float)z;
                pts[i] = p;
            }
        }

        /// <summary>
        /// Refresh the live (N*) bounds after positions changed. Min/Max
        /// keep their file-bounds meaning and are left alone.
        /// </summary>
        public static void RecalcBounds(PlySplatModel model)
        {
            if (model.Points.Count == 0) return;
            float nMinX = float.MaxValue, nMinY = float.MaxValue, nMinZ = float.MaxValue;
            float nMaxX = float.MinValue, nMaxY = float.MinValue, nMaxZ = float.MinValue;
            foreach (var p in model.Points)
            {
                if (p.X < nMinX) nMinX = p.X; if (p.X > nMaxX) nMaxX = p.X;
                if (p.Y < nMinY) nMinY = p.Y; if (p.Y > nMaxY) nMaxY = p.Y;
                if (p.Z < nMinZ) nMinZ = p.Z; if (p.Z > nMaxZ) nMaxZ = p.Z;
            }
            model.NMinX = nMinX; model.NMinY = nMinY; model.NMinZ = nMinZ;
            model.NMaxX = nMaxX; model.NMaxY = nMaxY; model.NMaxZ = nMaxZ;
        }

        /// <summary>
        /// Row-major 3x3 inverse (maps screen drags back through the
        /// Rot/Scale object matrix). Identity fallback when singular.
        /// </summary>
        public static void Invert3x3(double[] m, double[] inv)
        {
            double a = m[0], b = m[1], c = m[2];
            double d = m[3], e = m[4], f = m[5];
            double g = m[6], h = m[7], i = m[8];
            double A = e * i - f * h, B = f * g - d * i, C = d * h - e * g;
            double det = a * A + b * B + c * C;
            if (Math.Abs(det) < 1e-12)
            {
                inv[0] = 1; inv[1] = 0; inv[2] = 0;
                inv[3] = 0; inv[4] = 1; inv[5] = 0;
                inv[6] = 0; inv[7] = 0; inv[8] = 1;
                return;
            }
            double s = 1.0 / det;
            inv[0] = A * s; inv[1] = (c * h - b * i) * s; inv[2] = (b * f - c * e) * s;
            inv[3] = B * s; inv[4] = (a * i - c * g) * s; inv[5] = (c * d - a * f) * s;
            inv[6] = C * s; inv[7] = (b * g - a * h) * s; inv[8] = (a * e - b * d) * s;
        }
    }
}
