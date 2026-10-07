using System;
using System.Collections.Generic;
using System.Drawing;

namespace MeesaMultisMaker.ThreeD
{
    /// <summary>
    /// Point-cloud-native selection generators. Selections are bool[] masks
    /// over the model's points (cheap even at 1M). All generators take an
    /// existing mask to add to (or subtract from when subtract=true).
    /// </summary>
    public static class SplatSelect
    {
        public static int Count(bool[] sel)
        {
            if (sel == null) return 0;
            int n = 0;
            for (int i = 0; i < sel.Length; i++) if (sel[i]) n++;
            return n;
        }

        public static void All(bool[] sel, bool value)
        {
            if (sel == null) return;
            for (int i = 0; i < sel.Length; i++) sel[i] = value;
        }

        public static void Invert(bool[] sel)
        {
            if (sel == null) return;
            for (int i = 0; i < sel.Length; i++) sel[i] = !sel[i];
        }

        /// <summary>Axis slab: 0=X, 1=Y, 2=Z, inclusive range in model units.</summary>
        public static int Slab(PlySplatModel model, bool[] sel, int axis, float lo, float hi, bool subtract)
        {
            if (model == null || sel == null) return 0;
            int n = 0;
            var pts = model.Points;
            int m = Math.Min(sel.Length, pts.Count);
            for (int i = 0; i < m; i++)
            {
                var p = pts[i];
                float v = axis == 0 ? p.X : (axis == 1 ? p.Y : p.Z);
                if (v >= lo && v <= hi)
                {
                    if (subtract) { if (sel[i]) { sel[i] = false; n--; } }
                    else if (!sel[i]) { sel[i] = true; n++; }
                }
            }
            return n;
        }

        /// <summary>
        /// Uniform-grid spatial hash for neighbor queries. Cell size should
        /// be a few times the median splat spacing.
        /// </summary>
        public class SpatialHash
        {
            private readonly Dictionary<string, List<int>> _cells = new Dictionary<string, List<int>>();
            private readonly float _cell;
            private readonly List<SplatPoint> _pts;

            public SpatialHash(List<SplatPoint> pts, float cell)
            {
                _pts = pts;
                _cell = Math.Max(1e-6f, cell);
                for (int i = 0; i < pts.Count; i++)
                {
                    var p = pts[i];
                    string k = Key(p.X, p.Y, p.Z);
                    List<int> list;
                    if (!_cells.TryGetValue(k, out list))
                    {
                        list = new List<int>();
                        _cells[k] = list;
                    }
                    list.Add(i);
                }
            }

            private string Key(float x, float y, float z)
            {
                return ((int)Math.Floor(x / _cell)) + "," +
                       ((int)Math.Floor(y / _cell)) + "," +
                       ((int)Math.Floor(z / _cell));
            }

            public int CountNeighbors(int idx, float radius, int maxCount)
            {
                var c = _pts[idx];
                int r = (int)Math.Ceiling(radius / _cell);
                int cx = (int)Math.Floor(c.X / _cell);
                int cy = (int)Math.Floor(c.Y / _cell);
                int cz = (int)Math.Floor(c.Z / _cell);
                float r2 = radius * radius;
                int n = 0;
                for (int ax = cx - r; ax <= cx + r; ax++)
                    for (int ay = cy - r; ay <= cy + r; ay++)
                        for (int az = cz - r; az <= cz + r; az++)
                        {
                            List<int> list;
                            if (!_cells.TryGetValue(ax + "," + ay + "," + az, out list)) continue;
                            foreach (int j in list)
                            {
                                if (j == idx) continue;
                                var q = _pts[j];
                                float dx = q.X - c.X, dy = q.Y - c.Y, dz = q.Z - c.Z;
                                if (dx * dx + dy * dy + dz * dz <= r2)
                                {
                                    n++;
                                    if (n >= maxCount) return n;
                                }
                            }
                        }
                return n;
            }

            /// <summary>All point indices within radius (including idx).</summary>
            public List<int> Radius(int idx, float radius)
            {
                var out_ = new List<int>();
                var c = _pts[idx];
                int r = (int)Math.Ceiling(radius / _cell);
                int cx = (int)Math.Floor(c.X / _cell);
                int cy = (int)Math.Floor(c.Y / _cell);
                int cz = (int)Math.Floor(c.Z / _cell);
                float r2 = radius * radius;
                for (int ax = cx - r; ax <= cx + r; ax++)
                    for (int ay = cy - r; ay <= cy + r; ay++)
                        for (int az = cz - r; az <= cz + r; az++)
                        {
                            List<int> list;
                            if (!_cells.TryGetValue(ax + "," + ay + "," + az, out list)) continue;
                            foreach (int j in list)
                            {
                                var q = _pts[j];
                                float dx = q.X - c.X, dy = q.Y - c.Y, dz = q.Z - c.Z;
                                if (dx * dx + dy * dy + dz * dz <= r2) out_.Add(j);
                            }
                        }
                return out_;
            }
        }

        /// <summary>
        /// Flood fill from a seed through neighbors within radius.
        /// Used by connected-components (generous radius) and color wand
        /// (tight radius + color gate).
        /// </summary>
        public static int Flood(PlySplatModel model, bool[] sel, int seed,
            SpatialHash hash, float radius, Func<SplatPoint, SplatPoint, bool> accept, bool subtract)
        {
            if (model == null || sel == null || seed < 0 || seed >= model.Count) return 0;
            var seen = new bool[model.Count];
            var queue = new Queue<int>();
            queue.Enqueue(seed);
            seen[seed] = true;
            var seedPt = model.Points[seed];
            int n = 0;
            int guard = 0;
            while (queue.Count > 0)
            {
                if (++guard > model.Count * 4) break; // safety
                int i = queue.Dequeue();
                var p = model.Points[i];
                if (accept(p, seedPt))
                {
                    if (subtract) { if (sel[i]) { sel[i] = false; n--; } }
                    else if (!sel[i]) { sel[i] = true; n++; }
                    foreach (int j in hash.Radius(i, radius))
                    {
                        if (j < 0 || j >= seen.Length || seen[j]) continue;
                        seen[j] = true;
                        queue.Enqueue(j);
                    }
                }
            }
            return n;
        }

        public static double ColorDist(SplatPoint a, SplatPoint b)
        {
            double dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
            return Math.Sqrt(dr * dr + dg * dg + db * db);
        }

        /// <summary>Normal-facing select: normal within cosTol of direction.
        /// Optional object rotation matrix (dial-rotated normals).</summary>
        public static int ByNormal(PlySplatModel model, bool[] sel,
            float dx, float dy, float dz, float cosTol, bool subtract, double[] robj = null)
        {
            if (model == null || sel == null) return 0;
            double nl = Math.Sqrt((double)dx * dx + (double)dy * dy + (double)dz * dz);
            if (nl < 1e-9) return 0;
            dx = (float)(dx / nl); dy = (float)(dy / nl); dz = (float)(dz / nl);
            int n = 0;
            var pts = model.Points;
            int m = Math.Min(sel.Length, pts.Count);
            for (int i = 0; i < m; i++)
            {
                var p = pts[i];
                double nx = p.Nx, ny = p.Ny, nz = p.Nz;
                if (robj != null)
                {
                    double rx = robj[0] * nx + robj[1] * ny + robj[2] * nz;
                    double ry = robj[3] * nx + robj[4] * ny + robj[5] * nz;
                    double rz = robj[6] * nx + robj[7] * ny + robj[8] * nz;
                    nx = rx; ny = ry; nz = rz;
                }
                if (nx * dx + ny * dy + nz * dz >= cosTol)
                {
                    if (subtract) { if (sel[i]) { sel[i] = false; n--; } }
                    else if (!sel[i]) { sel[i] = true; n++; }
                }
            }
            return n;
        }

        /// <summary>
        /// Coarse front-surface depth map for brush/SAM selection: minimum
        /// projected depth per cell, so backfaces can be rejected.
        /// </summary>
        public class DepthMap
        {
            public readonly float[] MinDepth;
            public readonly int W, H, Cell;

            public DepthMap(PlySplatModel model, float yawDeg, float pitchDeg, bool uo,
                double[] robj, float zoom, int width, int height, int cell)
            {
                W = Math.Max(1, (width + cell - 1) / cell);
                H = Math.Max(1, (height + cell - 1) / cell);
                Cell = cell;
                MinDepth = new float[W * H];
                for (int i = 0; i < MinDepth.Length; i++) MinDepth[i] = float.MaxValue;

                double yaw = yawDeg * Math.PI / 180.0;
                double cosY = Math.Cos(yaw), sinY = Math.Sin(yaw);
                double pitch = pitchDeg * Math.PI / 180.0;
                double cosP = Math.Cos(pitch), sinP = Math.Sin(pitch);
                double fit = Math.Min(width, height) * 0.42 * zoom;
                double cx = width * 0.5, cy = height * 0.52;

                var pts = model.Points;
                for (int i = 0; i < pts.Count; i++)
                {
                    var p = pts[i];
                    double qx = p.X, qy = p.Y, qz = p.Z;
                    if (robj != null)
                    {
                        qx = robj[0] * p.X + robj[1] * p.Y + robj[2] * p.Z;
                        qy = robj[3] * p.X + robj[4] * p.Y + robj[5] * p.Z;
                        qz = robj[6] * p.X + robj[7] * p.Y + robj[8] * p.Z;
                    }
                    double x1 = qx * cosY + qz * sinY;
                    double z1 = -qx * sinY + qz * cosY;
                    double sx, sy, depth;
                    if (uo)
                    {
                        sx = cx + (x1 - z1) * fit;
                        sy = cy + (x1 + z1) * fit - qy * SplatRenderer.UoVerticalScale * fit;
                        depth = x1 + z1 + qy;
                    }
                    else
                    {
                        double y2 = qy * cosP - z1 * sinP;
                        sx = cx + x1 * fit;
                        sy = cy - y2 * fit;
                        depth = qy * sinP + z1 * cosP;
                    }
                    int px = (int)sx / cell, py = (int)sy / cell;
                    if (px < 0 || py < 0 || px >= W || py >= H) continue;
                    float d = (float)depth;
                    int k = py * W + px;
                    if (d < MinDepth[k]) MinDepth[k] = d;
                }
            }

            /// <summary>True when depth is at/near the front surface at (sx,sy).</summary>
            public bool IsFront(float sx, float sy, float depth, float tol)
            {
                int px = (int)sx / Cell, py = (int)sy / Cell;
                if (px < 0 || py < 0 || px >= W || py >= H) return true;
                float m = MinDepth[py * W + px];
                if (m == float.MaxValue) return true;
                return depth <= m + tol;
            }
        }

        /// <summary>
        /// Project every splat center (for brush/SAM): returns parallel arrays
        /// of screen x/y/depth under the given camera + object matrix.
        /// </summary>
        public static void ProjectAll(PlySplatModel model, float yawDeg, float pitchDeg, bool uo,
            double[] robj, float zoom, int width, int height,
            float[] sxOut, float[] syOut, float[] depthOut)
        {
            double yaw = yawDeg * Math.PI / 180.0;
            double cosY = Math.Cos(yaw), sinY = Math.Sin(yaw);
            double pitch = pitchDeg * Math.PI / 180.0;
            double cosP = Math.Cos(pitch), sinP = Math.Sin(pitch);
            double fit = Math.Min(width, height) * 0.42 * zoom;
            double cx = width * 0.5, cy = height * 0.52;
            var pts = model.Points;
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                double qx = p.X, qy = p.Y, qz = p.Z;
                if (robj != null)
                {
                    qx = robj[0] * p.X + robj[1] * p.Y + robj[2] * p.Z;
                    qy = robj[3] * p.X + robj[4] * p.Y + robj[5] * p.Z;
                    qz = robj[6] * p.X + robj[7] * p.Y + robj[8] * p.Z;
                }
                double x1 = qx * cosY + qz * sinY;
                double z1 = -qx * sinY + qz * cosY;
                if (uo)
                {
                    sxOut[i] = (float)(cx + (x1 - z1) * fit);
                    syOut[i] = (float)(cy + (x1 + z1) * fit - qy * SplatRenderer.UoVerticalScale * fit);
                    depthOut[i] = (float)(x1 + z1 + qy);
                }
                else
                {
                    double y2 = qy * cosP - z1 * sinP;
                    sxOut[i] = (float)(cx + x1 * fit);
                    syOut[i] = (float)(cy - y2 * fit);
                    depthOut[i] = (float)(qy * sinP + z1 * cosP);
                }
            }
        }

        /// <summary>
        /// Back-project a same-size mask bitmap into a selection: splats whose
        /// center lands on a bright mask pixel, front surface only (depth map).
        /// Mask white (>=128) = select.
        /// </summary>
        public static int FromMask(PlySplatModel model, bool[] sel, Bitmap mask,
            float yawDeg, float pitchDeg, bool uo, double[] robj, float zoom,
            int width, int height, float depthTol, bool subtract)
        {
            if (model == null || sel == null || mask == null) return 0;
            int n = 0;
            var rect = new Rectangle(0, 0, mask.Width, mask.Height);
            var data = mask.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            byte[] buf;
            try
            {
                buf = new byte[Math.Abs(data.Stride) * mask.Height];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, buf.Length);
            }
            finally { mask.UnlockBits(data); }

            var depth = new DepthMap(model, yawDeg, pitchDeg, uo, robj, zoom, width, height, 4);
            int count = model.Count;
            var sx = new float[count];
            var sy = new float[count];
            var dp = new float[count];
            ProjectAll(model, yawDeg, pitchDeg, uo, robj, zoom, width, height, sx, sy, dp);

            float scalex = (float)mask.Width / Math.Max(1, width);
            float scaley = (float)mask.Height / Math.Max(1, height);
            for (int i = 0; i < count; i++)
            {
                int mx = (int)(sx[i] * scalex), my = (int)(sy[i] * scaley);
                if (mx < 0 || my < 0 || mx >= mask.Width || my >= mask.Height) continue;
                int a = buf[my * data.Stride + mx * 4 + 3];
                int lum = (buf[my * data.Stride + mx * 4] + buf[my * data.Stride + mx * 4 + 1] + buf[my * data.Stride + mx * 4 + 2]) / 3;
                if (a < 128 && lum < 128) continue;
                if (lum < 128) continue;
                if (!depth.IsFront(sx[i], sy[i], dp[i], depthTol)) continue;
                if (subtract) { if (sel[i]) { sel[i] = false; n--; } }
                else if (!sel[i]) { sel[i] = true; n++; }
            }
            return n;
        }
    }
}
