using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace MeesaMultisMaker.ThreeD
{
    /// <summary>
    /// True gaussian-splat rasterizer for the UO isometric viewport.
    ///
    /// Each splat's full 3D covariance (per-axis sigma + quaternion) is
    /// projected through one of two cameras, eigen-decomposed into an
    /// oriented 2D ellipse, and drawn back-to-front with a two-shell falloff
    /// approximating the gaussian core. Near-circular splats take a fast
    /// plain-ellipse path.
    ///
    /// Cameras (measured against real OSI art: land tiles are 44x44 px,
    /// i.e. 1:1 ground diamonds):
    ///  - UO oblique (uoOblique=true): the actual UO projection. Ground maps
    ///    exactly like the main canvas (sx=(x-z), sy=(x+z), 1:1 diamonds with
    ///    45-degree edges) and heights draw straight up undistorted. This is
    ///    what the 8 facing exports always use.
    ///  - Orbit (uoOblique=false): free yaw/pitch tilted orthographic camera
    ///    for inspecting tops and bottoms. Cannot reproduce 1:1 diamonds at
    ///    any tilt (a tilted camera always flattens them); that is expected.
    ///
    /// highQuality=false draws single flat discs (used while orbit-dragging).
    /// </summary>
    public static class SplatRenderer
    {
        public static readonly string[] DirectionNames =
            new string[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        /// <summary>Vertical draw scale relative to ground (1 = undistorted).</summary>
        public const double UoVerticalScale = 1.0;

        /// <summary>
        /// Floor-grid density: the ground grid is this many cells across.
        /// The tile slicer cuts on these same cells, so slices always fall
        /// on floor lines. Change here changes both together.
        /// </summary>
        public const int FloorGridCells = 10;

        /// <summary>
        /// Optional selection overlay, set by the editor before rendering
        /// (same indexing as the model points). Selected splats are tinted
        /// toward HighlightColor. Null = no highlight. UI thread only.
        /// </summary>
        public static bool[] HighlightMask;
        public static Color HighlightColor = Color.Cyan;

        private static void ApplyHighlight(bool selected, ref byte r, ref byte g, ref byte b)
        {
            if (!selected) return;
            Color h = HighlightColor;
            r = (byte)((r + h.R) / 2);
            g = (byte)((g + h.G) / 2);
            b = (byte)((b + h.B) / 2);
        }

        /// <summary>
        /// Object rotation matrix (row-major 3x3) for the manual Rot X/Y/Z
        /// dials. Extrinsic fixed-axis order: X applied first, then Y, then
        /// Z (R = Rz * Ry * Rx). The facing yaw is composed on top of this
        /// by the caller (positions) and by folding it into the camera rows.
        /// </summary>
        public static double[] ObjectMatrix(float rxDeg, float ryDeg, float rzDeg)
        {
            double ax = rxDeg * Math.PI / 180.0;
            double ay = ryDeg * Math.PI / 180.0;
            double az = rzDeg * Math.PI / 180.0;
            double cx = Math.Cos(ax), sx = Math.Sin(ax);
            double cy = Math.Cos(ay), sy = Math.Sin(ay);
            double cz = Math.Cos(az), sz = Math.Sin(az);
            // Ry * Rx
            double yx00 = cy, yx01 = sy * sx, yx02 = sy * cx;
            double yx10 = 0, yx11 = cx, yx12 = -sx;
            double yx20 = -sy, yx21 = cy * sx, yx22 = cy * cx;
            // Rz * (Ry * Rx)
            return new double[]
            {
                cz * yx00 - sz * yx10, cz * yx01 - sz * yx11, cz * yx02 - sz * yx12,
                sz * yx00 + cz * yx10, sz * yx01 + cz * yx11, sz * yx02 + cz * yx12,
                yx20, yx21, yx22
            };
        }

        /// <summary>
        /// Position matrix for manual object scale+rotation: M = R * S
        /// (scale columns of the rotation). Covariances instead take the
        /// scaled sigmas with rows folded by the pure rotation.
        /// </summary>
        public static double[] ScaledMatrix(double[] rot, float sx, float sy, float sz)
        {
            return new double[]
            {
                rot[0] * sx, rot[1] * sy, rot[2] * sz,
                rot[3] * sx, rot[4] * sy, rot[5] * sz,
                rot[6] * sx, rot[7] * sy, rot[8] * sz
            };
        }

        /// <summary>Quaternion (w,x,y,z) to row-major 3x3.</summary>
        public static double[] QuatToMatrix(float w, float x, float y, float z)
        {
            return new double[]
            {
                1 - 2 * (y * y + z * z), 2 * (x * y - w * z), 2 * (x * z + w * y),
                2 * (x * y + w * z), 1 - 2 * (x * x + z * z), 2 * (y * z - w * x),
                2 * (x * z - w * y), 2 * (y * z + w * x), 1 - 2 * (x * x + y * y)
            };
        }

        /// <summary>Row-major 3x3 to unit quaternion (Shepperd).</summary>
        public static void MatrixToQuat(double[] m, out float w, out float x, out float y, out float z)
        {
            double t = m[0] + m[4] + m[8];
            double qw, qx, qy, qz;
            if (t > 0)
            {
                double s = Math.Sqrt(t + 1.0) * 2;
                qw = 0.25 * s;
                qx = (m[7] - m[5]) / s;
                qy = (m[2] - m[6]) / s;
                qz = (m[3] - m[1]) / s;
            }
            else if (m[0] > m[4] && m[0] > m[8])
            {
                double s = Math.Sqrt(1.0 + m[0] - m[4] - m[8]) * 2;
                qw = (m[7] - m[5]) / s;
                qx = 0.25 * s;
                qy = (m[1] + m[3]) / s;
                qz = (m[2] + m[6]) / s;
            }
            else if (m[4] > m[8])
            {
                double s = Math.Sqrt(1.0 + m[4] - m[0] - m[8]) * 2;
                qw = (m[2] - m[6]) / s;
                qx = (m[1] + m[3]) / s;
                qy = 0.25 * s;
                qz = (m[5] + m[7]) / s;
            }
            else
            {
                double s = Math.Sqrt(1.0 + m[8] - m[0] - m[4]) * 2;
                qw = (m[3] - m[1]) / s;
                qx = (m[2] + m[6]) / s;
                qy = (m[5] + m[7]) / s;
                qz = 0.25 * s;
            }
            double n = Math.Sqrt(qw * qw + qx * qx + qy * qy + qz * qz);
            if (n < 1e-9) { w = 1; x = y = z = 0; return; }
            w = (float)(qw / n); x = (float)(qx / n);
            y = (float)(qy / n); z = (float)(qz / n);
        }

        /// <summary>Mirror a rotation across a coordinate plane: R' = F*R*F.</summary>
        public static void MirrorQuat(float qw, float qx, float qy, float qz, int axis,
            out float w, out float x, out float y, out float z)
        {
            double[] r = QuatToMatrix(qw, qx, qy, qz);
            double sx = axis == 0 ? -1 : 1;
            double sy = axis == 1 ? -1 : 1;
            double sz = axis == 2 ? -1 : 1;
            // R' = F*R*F scales rows by s then columns by s.
            double[] m = new double[9];
            double[] s = new double[] { sx, sy, sz };
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    m[i * 3 + j] = s[i] * r[i * 3 + j] * s[j];
            MatrixToQuat(m, out w, out x, out y, out z);
        }

        /// <summary>Fold a row-major 3x3 into camera row vectors: r' = r * M.</summary>
        public static void FoldRows(double[] m,
            ref double xr0, ref double xr1, ref double xr2,
            ref double yr0, ref double yr1, ref double yr2)
        {
            double nx0 = xr0 * m[0] + xr1 * m[3] + xr2 * m[6];
            double nx1 = xr0 * m[1] + xr1 * m[4] + xr2 * m[7];
            double nx2 = xr0 * m[2] + xr1 * m[5] + xr2 * m[8];
            double ny0 = yr0 * m[0] + yr1 * m[3] + yr2 * m[6];
            double ny1 = yr0 * m[1] + yr1 * m[4] + yr2 * m[7];
            double ny2 = yr0 * m[2] + yr1 * m[5] + yr2 * m[8];
            xr0 = nx0; xr1 = nx1; xr2 = nx2;
            yr0 = ny0; yr1 = ny1; yr2 = ny2;
        }

        /// <summary>Apply a row-major 3x3 to a point.</summary>
        public static void TransformPoint(double[] m, double x, double y, double z,
            out double ox, out double oy, out double oz)
        {
            ox = m[0] * x + m[1] * y + m[2] * z;
            oy = m[3] * x + m[4] * y + m[5] * z;
            oz = m[6] * x + m[7] * y + m[8] * z;
        }

        /// <summary>Footprint radius in sigmas (3 covers ~99% of the mass).</summary>
        private const double RadiusSigmas = 3.0;

        private struct DrawItem
        {
            public float SX, SY, RX, RY, AngDeg, Depth;
            public int ColorKey;
            public byte R8, G8, B8, A8;
            public bool Circular;
        }

        /// <summary>
        /// Render the model to a bitmap. When transparent is true the
        /// background stays alpha-0; otherwise bgColor is filled first.
        /// </summary>
        /// <param name="gridColor">Ground-grid pen color; pass Color.Empty for no grid.</param>
        /// <param name="uoOblique">True UO projection (exports + UO view); false = free orbit camera.</param>
        /// <param name="gridYawDeg">Yaw the ground grid is drawn with. Pass the model yaw for an
        /// attached grid, or 0 for a canvas-aligned fixed grid under a rotated model.</param>
        /// <param name="skipSplats">Draw background + grid only (the GPU layer is composited over).</param>
        /// <param name="rotXDeg">Manual object rotation, composed under the facing yaw.</param>
        /// <param name="scX">Manual object scale (1 = unchanged).</param>
        public static Bitmap Render(PlySplatModel model, float yawDeg, float pitchDeg,
            int width, int height, float zoom, float pointScale,
            bool transparent, Color bgColor, int maxPoints, bool highQuality,
            Color gridColor, bool uoOblique, float gridYawDeg, bool skipSplats = false,
            float rotXDeg = 0f, float rotYDeg = 0f, float rotZDeg = 0f,
            float scX = 1f, float scY = 1f, float scZ = 1f)
        {
            var bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            if (model == null || model.Count == 0)
            {
                using (var g0 = Graphics.FromImage(bmp))
                {
                    if (!transparent) { g0.Clear(bgColor); }
                    using (var f = new Font(FontFamily.GenericSansSerif, 10))
                    using (var br = new SolidBrush(transparent ? Color.White : Color.Black))
                    {
                        g0.DrawString("No 3D model loaded.", f, br, new PointF(10, 10));
                    }
                }
                return bmp;
            }

            double yaw = yawDeg * Math.PI / 180.0;
            double pitch = pitchDeg * Math.PI / 180.0;
            double cosY = Math.Cos(yaw), sinY = Math.Sin(yaw);
            double cosP = Math.Cos(pitch), sinP = Math.Sin(pitch);

            // Camera rows for the covariance projection, in MODEL frame.
            // Orbit: tilted orthographic rows (yaw folded in). Oblique: UO
            // rows, where screen x = x1-z1 and screen y = x1+z1 (ground)
            // minus vertical, with (x1,z1) the yaw-rotated model coords.
            // The oblique rows MUST carry the yaw too: at yaw 0 they reduce
            // to (1,0,-1)/(1,-V,1), but at any other facing fixed rows
            // compute every ellipse for the wrong camera, which renders as
            // streaky fur. (This was the SuperSplat quality gap.)
            double xr0, xr1, xr2, yr0, yr1, yr2;
            if (uoOblique)
            {
                xr0 = cosY + sinY; xr1 = 0; xr2 = sinY - cosY;
                yr0 = cosY - sinY; yr1 = -UoVerticalScale; yr2 = sinY + cosY;
            }
            else
            {
                xr0 = cosY; xr1 = 0; xr2 = sinY;
                yr0 = sinP * sinY; yr1 = cosP; yr2 = -sinP * cosY;
            }
            // Manual object rotation composes under the facing yaw: points
            // are pre-rotated in the loop below, covariances via these rows.
            // Scale stretches the sigmas; positions use R*S.
            if (scX <= 0) scX = 1f;
            if (scY <= 0) scY = 1f;
            if (scZ <= 0) scZ = 1f;
            double[] robj = ObjectMatrix(rotXDeg, rotYDeg, rotZDeg);
            double[] mpos = ScaledMatrix(robj, scX, scY, scZ);
            FoldRows(robj, ref xr0, ref xr1, ref xr2, ref yr0, ref yr1, ref yr2);

            // Fit unit-cube model (~2 units) into the smaller dimension.
            double fit = Math.Min(width, height) * 0.42 * zoom;

            int count = model.Count;
            int stride = 1;
            if (maxPoints > 0 && count > maxPoints)
                stride = (count + maxPoints - 1) / maxPoints;
            // Subsampling spreads the kept splats apart by ~sqrt(stride), so
            // grow each footprint by the same factor. Otherwise a 1M-splat
            // model previewed at 45k looks holey while the full export is
            // solid. This keeps preview coverage faithful to the export.
            double radiusBoost = Math.Sqrt((double)stride);

            var items = new List<DrawItem>((count + stride - 1) / stride);
            double cx = width * 0.5, cy = height * 0.52;
            var pts = model.Points;
            int renderCount = skipSplats ? 0 : count;
            for (int i = 0; i < renderCount; i += stride)
            {
                var p = pts[i];
                if (p.Opacity < 0.03f) continue;

                // Manual object scale+rotation first, then the facing yaw below.
                double qx, qy, qz;
                TransformPoint(mpos, p.X, p.Y, p.Z, out qx, out qy, out qz);

                // Rigid part.
                double x1 = qx * cosY + qz * sinY;
                double z1 = -qx * sinY + qz * cosY;
                float sx, sy;
                double depth;
                if (uoOblique)
                {
                    // True UO mapping: 1:1 ground diamonds, verticals up.
                    sx = (float)(cx + (x1 - z1) * fit);
                    sy = (float)(cy + (x1 + z1) * fit - qy * UoVerticalScale * fit);
                    depth = x1 + z1 + qy;
                }
                else
                {
                    double y2 = qy * cosP - z1 * sinP;
                    double z2 = qy * sinP + z1 * cosP;
                    sx = (float)(cx + x1 * fit);
                    sy = (float)(cy - y2 * fit);
                    depth = z2;
                }
                if (sx < -96 || sy < -96 || sx > width + 96 || sy > height + 96) continue;

                // 3x3 covariance from quaternion + per-axis sigma.
                double w = p.Qw, x = p.Qx, y = p.Qy, z = p.Qz;
                double r00 = 1 - 2 * (y * y + z * z);
                double r01 = 2 * (x * y - w * z);
                double r02 = 2 * (x * z + w * y);
                double r10 = 2 * (x * y + w * z);
                double r11 = 1 - 2 * (x * x + z * z);
                double r12 = 2 * (y * z - w * x);
                double r20 = 2 * (x * z - w * y);
                double r21 = 2 * (y * z + w * x);
                double r22 = 1 - 2 * (x * x + y * y);
                double s0 = p.S0 * scX, s1 = p.S1 * scY, s2 = p.S2 * scZ;
                // M = R * diag(s); Cov = M * M'.
                double m00 = r00 * s0, m01 = r01 * s1, m02 = r02 * s2;
                double m10 = r10 * s0, m11 = r11 * s1, m12 = r12 * s2;
                double m20 = r20 * s0, m21 = r21 * s1, m22 = r22 * s2;
                double c00 = m00 * m00 + m01 * m01 + m02 * m02;
                double c01 = m00 * m10 + m01 * m11 + m02 * m12;
                double c02 = m00 * m20 + m01 * m21 + m02 * m22;
                double c11 = m10 * m10 + m11 * m11 + m12 * m12;
                double c12 = m10 * m20 + m11 * m21 + m12 * m22;
                double c22 = m20 * m20 + m21 * m21 + m22 * m22;
                // Mirror with the Y-flipped positions (see GlSplatRenderer):
                // orientations must follow the same M=diag(1,-1,1) mirror.
                c01 = -c01;
                c12 = -c12;

                // Project: a/b/c of the 2x2 screen covariance.
                // v' = Cov * v for v in {Xr, Yr}, then dot with Xr/Yr.
                double t0 = c00 * xr0 + c01 * xr1 + c02 * xr2;
                double t1 = c01 * xr0 + c11 * xr1 + c12 * xr2;
                double t2 = c02 * xr0 + c12 * xr1 + c22 * xr2;
                double a = t0 * xr0 + t1 * xr1 + t2 * xr2;
                double b = t0 * yr0 + t1 * yr1 + t2 * yr2;
                double u0 = c00 * yr0 + c01 * yr1 + c02 * yr2;
                double u1 = c01 * yr0 + c11 * yr1 + c12 * yr2;
                double u2 = c02 * yr0 + c12 * yr1 + c22 * yr2;
                double c = u0 * yr0 + u1 * yr1 + u2 * yr2;

                double trace = (a + c) * 0.5;
                double diff = (a - c) * 0.5;
                double disc = Math.Sqrt(Math.Max(0.0, diff * diff + b * b));
                double l1 = Math.Max(1e-12, trace + disc);
                double l2 = Math.Max(1e-12, trace - disc);
                double ang = 0.5 * Math.Atan2(2 * b, a - c) * 180.0 / Math.PI;

                float rx = (float)(RadiusSigmas * Math.Sqrt(l1) * fit * pointScale * radiusBoost);
                float ry = (float)(RadiusSigmas * Math.Sqrt(l2) * fit * pointScale * radiusBoost);
                if (rx < 1f) rx = 1f;
                if (ry < 1f) ry = 1f;
                if (rx > 96f) rx = 96f;
                if (ry > 96f) ry = 96f;

                byte alpha = (byte)(p.Opacity * 255f);
                if (alpha < 20) alpha = 20;
                alpha = (byte)((alpha >> 4) << 4); // quantize: small brush cache
                byte hr = p.R, hg = p.G, hb = p.B;
                if (HighlightMask != null && i < HighlightMask.Length && HighlightMask[i])
                    ApplyHighlight(true, ref hr, ref hg, ref hb);
                int key = (alpha << 24) | (hr << 16) | (hg << 8) | hb;
                items.Add(new DrawItem
                {
                    SX = sx, SY = sy, RX = rx, RY = ry,
                    AngDeg = (float)ang, Depth = (float)depth,
                    ColorKey = key, R8 = hr, G8 = hg, B8 = hb, A8 = alpha,
                    Circular = (ry / rx) > 0.87f
                });
            }

            // Painter's order: far first.
            items.Sort((aa, bb) => aa.Depth.CompareTo(bb.Depth));

            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = highQuality ? SmoothingMode.AntiAlias : SmoothingMode.HighSpeed;
                g.CompositingQuality = highQuality ? CompositingQuality.HighQuality : CompositingQuality.HighSpeed;
                if (!transparent) g.Clear(bgColor);

                // Ground grid behind the model, hugging the footprint.
                if (gridColor != Color.Empty)
                    DrawGroundGrid(g, model, gridYawDeg, pitchDeg, uoOblique, fit, cx, cy, gridColor);

                var brushes = new Dictionary<int, SolidBrush>(1024);
                try
                {
                    foreach (var it in items)
                    {
                        if (!highQuality || it.Circular)
                        {
                            // Fast path: single flat disc.
                            float rr = it.RX > it.RY ? it.RX : it.RY;
                            SolidBrush br = GetBrush(brushes, it.ColorKey, it.A8, it.R8, it.G8, it.B8);
                            float d = rr * 2f;
                            g.FillEllipse(br, it.SX - rr, it.SY - rr, d, d);
                        }
                        else
                        {
                            // True splat: soft two-shell oriented falloff
                            // (faint skirt + denser core), approximating the
                            // gaussian exp(-d^2/2s^2) profile.
                            var m = g.Transform;
                            g.TranslateTransform(it.SX, it.SY);
                            g.RotateTransform(it.AngDeg);
                            byte outerA = (byte)((it.A8 * 30 / 100) & ~15);
                            if (outerA >= 16)
                            {
                                int outerKey = (outerA << 24) | (it.R8 << 16) | (it.G8 << 8) | it.B8;
                                SolidBrush outer = GetBrush(brushes, outerKey, outerA, it.R8, it.G8, it.B8);
                                g.FillEllipse(outer, -it.RX, -it.RY, it.RX * 2f, it.RY * 2f);
                            }
                            byte innerA = (byte)((it.A8 * 65 / 100) & ~15);
                            if (innerA >= 16)
                            {
                                int innerKey = (innerA << 24) | (it.R8 << 16) | (it.G8 << 8) | it.B8;
                                SolidBrush inner = GetBrush(brushes, innerKey, innerA, it.R8, it.G8, it.B8);
                                float ix = it.RX * 0.55f, iy = it.RY * 0.55f;
                                g.FillEllipse(inner, -ix, -iy, ix * 2f, iy * 2f);
                            }
                            g.Transform = m;
                        }
                    }
                }
                finally
                {
                    foreach (var kv in brushes) kv.Value.Dispose();
                }
            }
            return bmp;
        }

        /// <summary>
        /// Single source of truth for point projection (viewport, grid and
        /// lasso hit-testing all go through here).
        /// </summary>
        public static void ProjectPoint(float x, float y, float z,
            float yawDeg, float pitchDeg, bool uoOblique,
            double fit, double cx, double cy, out float sx, out float sy)
        {
            double yaw = yawDeg * Math.PI / 180.0;
            double cosY = Math.Cos(yaw), sinY = Math.Sin(yaw);
            double x1 = x * cosY + z * sinY;
            double z1 = -x * sinY + z * cosY;
            if (uoOblique)
            {
                sx = (float)(cx + (x1 - z1) * fit);
                sy = (float)(cy + (x1 + z1) * fit - y * UoVerticalScale * fit);
            }
            else
            {
                double pitch = pitchDeg * Math.PI / 180.0;
                double cosP = Math.Cos(pitch), sinP = Math.Sin(pitch);
                double y2 = y * cosP - z1 * sinP;
                sx = (float)(cx + x1 * fit);
                sy = (float)(cy - y2 * fit);
            }
        }

        /// <summary>
        /// UO-style diamond grid on the model's ground plane. In UO mode the
        /// grid uses gridYawDeg (0 = canvas-aligned, fixed under the rotating
        /// model); in orbit mode it follows the model view.
        /// </summary>
        private static void DrawGroundGrid(Graphics g, PlySplatModel model,
            float gridYawDeg, float pitchDeg, bool uoOblique,
            double fit, double cx, double cy, Color color)
        {
            double foot = Math.Max(
                Math.Max(Math.Abs(model.NMinX), Math.Abs(model.NMaxX)),
                Math.Max(Math.Abs(model.NMinZ), Math.Abs(model.NMaxZ)));
            if (foot < 0.8) foot = 0.8;
            double e = foot + 0.4;
            const int cells = FloorGridCells;
            double step = 2 * e / cells;
            double gy = model.NMinY;
            using (var pen = new Pen(color, 1f))
            {
                for (int k = 0; k <= cells; k++)
                {
                    double t = -e + k * step;
                    float ax, ay, bx, by2;
                    ProjectPoint((float)t, (float)gy, (float)-e, gridYawDeg, pitchDeg, uoOblique, fit, cx, cy, out ax, out ay);
                    ProjectPoint((float)t, (float)gy, (float)e, gridYawDeg, pitchDeg, uoOblique, fit, cx, cy, out bx, out by2);
                    g.DrawLine(pen, ax, ay, bx, by2);
                    ProjectPoint((float)-e, (float)gy, (float)t, gridYawDeg, pitchDeg, uoOblique, fit, cx, cy, out ax, out ay);
                    ProjectPoint((float)e, (float)gy, (float)t, gridYawDeg, pitchDeg, uoOblique, fit, cx, cy, out bx, out by2);
                    g.DrawLine(pen, ax, ay, bx, by2);
                }
            }
        }

        private static SolidBrush GetBrush(Dictionary<int, SolidBrush> cache, int key,
            byte a, byte r, byte g, byte b)
        {
            SolidBrush br;
            if (!cache.TryGetValue(key, out br))
            {
                br = new SolidBrush(Color.FromArgb(a, r, g, b));
                cache[key] = br;
            }
            return br;
        }

        /// <summary>Crop fully-transparent margins (for tight UO sprites).</summary>
        public static Bitmap CropTransparent(Bitmap src, int pad)
        {
            if (src == null) return null;
            var rect = new Rectangle(0, 0, src.Width, src.Height);
            var data = src.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            int minX = src.Width, minY = src.Height, maxX = -1, maxY = -1;
            try
            {
                int bytes = Math.Abs(data.Stride) * src.Height;
                var buf = new byte[bytes];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, bytes);
                for (int y = 0; y < src.Height; y++)
                {
                    int row = y * data.Stride;
                    for (int x = 0; x < src.Width; x++)
                    {
                        if (buf[row + x * 4 + 3] > 8)
                        {
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                    }
                }
            }
            finally { src.UnlockBits(data); }
            if (maxX < minX) return new Bitmap(src);
            minX = Math.Max(0, minX - pad);
            minY = Math.Max(0, minY - pad);
            maxX = Math.Min(src.Width - 1, maxX + pad);
            maxY = Math.Min(src.Height - 1, maxY + pad);
            var out_ = new Bitmap(maxX - minX + 1, maxY - minY + 1,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(out_))
                g.DrawImage(src, new Rectangle(0, 0, out_.Width, out_.Height),
                    new Rectangle(minX, minY, out_.Width, out_.Height), GraphicsUnit.Pixel);
            return out_;
        }
    }
}
