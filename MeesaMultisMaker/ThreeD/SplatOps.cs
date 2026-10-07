using System;
using System.Collections.Generic;
using System.Drawing;

namespace MeesaMultisMaker.ThreeD
{
    /// <summary>
    /// Point-cloud-native operators. Every op takes an optional selection
    /// mask (null = whole model) and returns the affected splat count.
    /// Callers snapshot affected structs before mutating for undo.
    /// </summary>
    public static class SplatOps
    {
        public static List<int> Affected(PlySplatModel model, bool[] sel)
        {
            var out_ = new List<int>();
            if (model == null) return out_;
            int n = model.Count;
            if (sel == null)
            {
                for (int i = 0; i < n; i++) out_.Add(i);
                return out_;
            }
            for (int i = 0; i < n && i < sel.Length; i++)
                if (sel[i]) out_.Add(i);
            return out_;
        }

        public static float EstimateSpacing(PlySplatModel model)
        {
            if (model == null || model.Count == 0) return 0.05f;
            float span = Math.Max(model.MaxX - model.MinX,
                Math.Max(model.MaxY - model.MinY, model.MaxZ - model.MinZ));
            if (span < 1e-6f) span = 1f;
            float per = (float)Math.Pow(Math.Max(1, model.Count), 1.0 / 3.0);
            return span / Math.Max(1f, per);
        }

        #region Color

        private static void RgbToHsl(byte r, byte g, byte b, out double h, out double s, out double l)
        {
            double rr = r / 255.0, gg = g / 255.0, bb = b / 255.0;
            double mx = Math.Max(rr, Math.Max(gg, bb)), mn = Math.Min(rr, Math.Min(gg, bb));
            l = (mx + mn) / 2.0;
            if (mx == mn) { h = 0; s = 0; return; }
            double d = mx - mn;
            s = l > 0.5 ? d / (2.0 - mx - mn) : d / (mx + mn);
            if (mx == rr) h = (gg - bb) / d + (gg < bb ? 6 : 0);
            else if (mx == gg) h = (bb - rr) / d + 2;
            else h = (rr - gg) / d + 4;
            h *= 60.0;
        }

        private static void HslToRgb(double h, double s, double l, out byte r, out byte g, out byte b)
        {
            h = ((h % 360.0) + 360.0) % 360.0;
            s = Math.Max(0, Math.Min(1, s));
            l = Math.Max(0, Math.Min(1, l));
            double rr, gg, bb;
            if (s == 0) { rr = gg = bb = l; }
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;
                double hk = h / 360.0;
                rr = Hue(p, q, hk + 1.0 / 3.0);
                gg = Hue(p, q, hk);
                bb = Hue(p, q, hk - 1.0 / 3.0);
            }
            r = (byte)Math.Max(0, Math.Min(255, (int)(rr * 255 + 0.5)));
            g = (byte)Math.Max(0, Math.Min(255, (int)(gg * 255 + 0.5)));
            b = (byte)Math.Max(0, Math.Min(255, (int)(bb * 255 + 0.5)));
        }

        private static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
            return p;
        }

        private delegate void RgbEdit(ref byte r, ref byte g, ref byte b);

        private static int MapColor(PlySplatModel model, bool[] sel, RgbEdit fn)
        {
            var idx = Affected(model, sel);
            var pts = model.Points;
            foreach (int i in idx)
            {
                var p = pts[i];
                byte r = p.R, g = p.G, b = p.B;
                fn(ref r, ref g, ref b);
                p.R = r; p.G = g; p.B = b;
                pts[i] = p;
            }
            return idx.Count;
        }

        public static int HueShift(PlySplatModel model, bool[] sel, float degrees)
        {
            return MapColor(model, sel, delegate (ref byte r, ref byte g, ref byte b)
            {
                double h, s, l;
                RgbToHsl(r, g, b, out h, out s, out l);
                HslToRgb(h + degrees, s, l, out r, out g, out b);
            });
        }

        public static int SatMul(PlySplatModel model, bool[] sel, float f)
        {
            return MapColor(model, sel, delegate (ref byte r, ref byte g, ref byte b)
            {
                double h, s, l;
                RgbToHsl(r, g, b, out h, out s, out l);
                HslToRgb(h, s * f, l, out r, out g, out b);
            });
        }

        public static int LightMul(PlySplatModel model, bool[] sel, float f)
        {
            return MapColor(model, sel, delegate (ref byte r, ref byte g, ref byte b)
            {
                double h, s, l;
                RgbToHsl(r, g, b, out h, out s, out l);
                HslToRgb(h, s, l * f, out r, out g, out b);
            });
        }

        public static int Tint(PlySplatModel model, bool[] sel, Color tint, float strength)
        {
            strength = Math.Max(0f, Math.Min(1f, strength));
            return MapColor(model, sel, delegate (ref byte r, ref byte g, ref byte b)
            {
                r = (byte)(r + (tint.R - r) * strength);
                g = (byte)(g + (tint.G - g) * strength);
                b = (byte)(b + (tint.B - b) * strength);
            });
        }

        public static int Invert(PlySplatModel model, bool[] sel)
        {
            return MapColor(model, sel, delegate (ref byte r, ref byte g, ref byte b)
            {
                r = (byte)(255 - r); g = (byte)(255 - g); b = (byte)(255 - b);
            });
        }

        public static int Grayscale(PlySplatModel model, bool[] sel)
        {
            return MapColor(model, sel, delegate (ref byte r, ref byte g, ref byte b)
            {
                byte v = (byte)(0.11 * r + 0.59 * g + 0.3 * b);
                r = g = b = v;
            });
        }

        public static int Posterize(PlySplatModel model, bool[] sel, int levels)
        {
            levels = Math.Max(2, Math.Min(16, levels));
            return MapColor(model, sel, delegate (ref byte r, ref byte g, ref byte b)
            {
                r = (byte)(Math.Round(r / 255.0 * (levels - 1)) / (levels - 1) * 255);
                g = (byte)(Math.Round(g / 255.0 * (levels - 1)) / (levels - 1) * 255);
                b = (byte)(Math.Round(b / 255.0 * (levels - 1)) / (levels - 1) * 255);
            });
        }

        /// <summary>
        /// True UO hue via hues.mul table (mirrors HuesReader.ApplyHue:
        /// max-channel luminance selects one of 32 entries; partial mode
        /// only touches exact-gray splats).
        /// </summary>
        public static int ApplyUoHue(PlySplatModel model, bool[] sel, Color[] table, bool partial)
        {
            if (table == null || table.Length < 32) return 0;
            return MapColor(model, sel, delegate (ref byte r, ref byte g, ref byte b)
            {
                if (partial && !(r == g && g == b)) return;
                int lum = Math.Max(r, Math.Max(g, b));
                int idx = (lum * 31) / 255;
                if (idx < 0) idx = 0;
                if (idx > 31) idx = 31;
                Color hc = table[idx];
                r = hc.R; g = hc.G; b = hc.B;
            });
        }

        /// <summary>
        /// Pull dark edge splats toward their brightest nearby neighbor.
        /// Kills black-background fringes from renders.
        /// </summary>
        public static int Defringe(PlySplatModel model, bool[] sel, int darkThr, float strength)
        {
            var idx = Affected(model, sel);
            if (idx.Count == 0) return 0;
            strength = Math.Max(0f, Math.Min(1f, strength));
            float spacing = EstimateSpacing(model);
            var hash = new SplatSelect.SpatialHash(model.Points, Math.Max(spacing * 2f, 1e-4f));
            var inSet = new HashSet<int>(idx);
            var pts = model.Points;
            int n = 0;
            float radius = spacing * 6f;
            foreach (int i in idx)
            {
                var p = pts[i];
                int mx = Math.Max(p.R, Math.Max(p.G, p.B));
                if (mx >= darkThr) continue;
                // Brightest neighbor (any splat, not just selection).
                int best = -1, bestLum = mx;
                foreach (int j in hash.Radius(i, radius))
                {
                    if (j == i || !inSet.Contains(j))
                    {
                        var q = pts[j];
                        int lum = Math.Max(q.R, Math.Max(q.G, q.B));
                        if (lum > bestLum) { bestLum = lum; best = j; }
                    }
                }
                if (best < 0) continue;
                var t = pts[best];
                p.R = (byte)(p.R + (t.R - p.R) * strength);
                p.G = (byte)(p.G + (t.G - p.G) * strength);
                p.B = (byte)(p.B + (t.B - p.B) * strength);
                pts[i] = p;
                n++;
            }
            return n;
        }

        /// <summary>
        /// Cheap cavity AO: dense neighborhoods darken. Strength 0..1.
        /// </summary>
        public static int AoBake(PlySplatModel model, bool[] sel, float strength, float radiusFactor)
        {
            var idx = Affected(model, sel);
            if (idx.Count == 0) return 0;
            strength = Math.Max(0f, Math.Min(1f, strength));
            float spacing = EstimateSpacing(model);
            var hash = new SplatSelect.SpatialHash(model.Points, Math.Max(spacing * 2f, 1e-4f));
            var pts = model.Points;
            float radius = spacing * Math.Max(2f, radiusFactor);
            foreach (int i in idx)
            {
                int c = hash.CountNeighbors(i, radius, 24);
                float occ = Math.Min(1f, c / 12f);
                float f = 1f - strength * occ * 0.75f;
                var p = pts[i];
                p.R = (byte)(p.R * f); p.G = (byte)(p.G * f); p.B = (byte)(p.B * f);
                pts[i] = p;
            }
            return idx.Count;
        }

        /// <summary>
        /// Lambert relight with loaded normals. factor = ambient +
        /// strength * max(0, n.dot(l)). Optional object rotation matrix
        /// (dial-rotated normals); null = model frame.
        /// </summary>
        public static int Relight(PlySplatModel model, bool[] sel,
            float lx, float ly, float lz, float ambient, float strength, double[] robj)
        {
            var idx = Affected(model, sel);
            if (idx.Count == 0) return 0;
            double nl = Math.Sqrt((double)lx * lx + (double)ly * ly + (double)lz * lz);
            if (nl < 1e-9) return 0;
            lx = (float)(lx / nl); ly = (float)(ly / nl); lz = (float)(lz / nl);
            ambient = Math.Max(0f, Math.Min(1f, ambient));
            strength = Math.Max(0f, strength);
            var pts = model.Points;
            foreach (int i in idx)
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
                double d = Math.Max(0.0, nx * lx + ny * ly + nz * lz);
                float f = (float)(ambient + strength * d);
                p.R = (byte)Math.Min(255, p.R * f);
                p.G = (byte)Math.Min(255, p.G * f);
                p.B = (byte)Math.Min(255, p.B * f);
                pts[i] = p;
            }
            return idx.Count;
        }

        #endregion

        #region Geometry

        public static int Move(PlySplatModel model, bool[] sel, float dx, float dy, float dz)
        {
            var idx = Affected(model, sel);
            var pts = model.Points;
            foreach (int i in idx)
            {
                var p = pts[i];
                p.X += dx; p.Y += dy; p.Z += dz;
                pts[i] = p;
            }
            return idx.Count;
        }

        public static int Mirror(PlySplatModel model, bool[] sel, int axis)
        {
            var idx = Affected(model, sel);
            var pts = model.Points;
            foreach (int i in idx)
            {
                var p = pts[i];
                if (axis == 0) p.X = -p.X;
                else if (axis == 1) p.Y = -p.Y;
                else p.Z = -p.Z;
                if (axis == 0) p.Nx = -p.Nx;
                else if (axis == 1) p.Ny = -p.Ny;
                else p.Nz = -p.Nz;
                // Conjugate the rotation: R' = F*R*F (F = axis mirror).
                float w, x, y, z;
                SplatRenderer.MirrorQuat(p.Qw, p.Qx, p.Qy, p.Qz, axis, out w, out x, out y, out z);
                p.Qw = w; p.Qx = x; p.Qy = y; p.Qz = z;
                pts[i] = p;
            }
            return idx.Count;
        }

        public static List<int> Clone(PlySplatModel model, bool[] sel, float dx, float dy, float dz)
        {
            var idx = Affected(model, sel);
            var added = new List<int>(idx.Count);
            var pts = model.Points;
            foreach (int i in idx)
            {
                var p = pts[i];
                p.X += dx; p.Y += dy; p.Z += dz;
                added.Add(pts.Count);
                pts.Add(p);
            }
            return added;
        }

        public static SplatPoint[] DeleteIndices(PlySplatModel model, List<int> ascending)
        {
            var pts = model.Points;
            var removed = new SplatPoint[ascending.Count];
            for (int k = 0; k < ascending.Count; k++) removed[k] = pts[ascending[k]];
            for (int k = ascending.Count - 1; k >= 0; k--) pts.RemoveAt(ascending[k]);
            return removed;
        }

        /// <summary>Delete splats with fewer than k neighbors in radius.</summary>
        public static int OutlierRemove(PlySplatModel model, bool[] sel, int k, float radiusFactor, out List<int> removedIdx, out SplatPoint[] removed)
        {
            removedIdx = new List<int>();
            removed = new SplatPoint[0];
            var idx = Affected(model, sel);
            if (idx.Count == 0) return 0;
            float spacing = EstimateSpacing(model);
            var hash = new SplatSelect.SpatialHash(model.Points, Math.Max(spacing * 2f, 1e-4f));
            float radius = spacing * Math.Max(1.5f, radiusFactor);
            var pts = model.Points;
            var kill = new List<int>();
            foreach (int i in idx)
            {
                if (hash.CountNeighbors(i, radius, k) < k) kill.Add(i);
            }
            kill.Sort();
            removedIdx = kill;
            removed = DeleteIndices(model, kill);
            return kill.Count;
        }

        /// <summary>Voxel-grid thin: keep first splat per cell.</summary>
        public static int VoxelDownsample(PlySplatModel model, bool[] sel, float cell, out List<int> removedIdx, out SplatPoint[] removed)
        {
            removedIdx = new List<int>();
            removed = new SplatPoint[0];
            var idx = Affected(model, sel);
            if (idx.Count == 0 || cell <= 0) return 0;
            var seen = new HashSet<string>();
            var kill = new List<int>();
            var pts = model.Points;
            foreach (int i in idx)
            {
                var p = pts[i];
                string key = ((int)Math.Floor(p.X / cell)) + "," +
                             ((int)Math.Floor(p.Y / cell)) + "," +
                             ((int)Math.Floor(p.Z / cell));
                if (!seen.Add(key)) kill.Add(i);
            }
            kill.Sort();
            removedIdx = kill;
            removed = DeleteIndices(model, kill);
            return kill.Count;
        }

        /// <summary>One Laplacian relax pass toward neighbor centroid.</summary>
        public static int LaplacianSmooth(PlySplatModel model, bool[] sel, float lambda)
        {
            var idx = Affected(model, sel);
            if (idx.Count == 0) return 0;
            lambda = Math.Max(0f, Math.Min(1f, lambda));
            float spacing = EstimateSpacing(model);
            var hash = new SplatSelect.SpatialHash(model.Points, Math.Max(spacing * 2f, 1e-4f));
            var pts = model.Points;
            var inSet = new HashSet<int>(idx);
            var newPos = new Dictionary<int, float[]>();
            foreach (int i in idx)
            {
                var near = hash.Radius(i, spacing * 3f);
                double sx = 0, sy = 0, sz = 0;
                int c = 0;
                foreach (int j in near)
                {
                    if (j == i || !inSet.Contains(j)) continue;
                    var q = pts[j];
                    sx += q.X; sy += q.Y; sz += q.Z;
                    c++;
                    if (c >= 12) break;
                }
                if (c == 0) continue;
                var p = pts[i];
                newPos[i] = new float[]
                {
                    (float)(p.X + lambda * (sx / c - p.X)),
                    (float)(p.Y + lambda * (sy / c - p.Y)),
                    (float)(p.Z + lambda * (sz / c - p.Z))
                };
            }
            foreach (var kv in newPos)
            {
                var p = pts[kv.Key];
                p.X = kv.Value[0]; p.Y = kv.Value[1]; p.Z = kv.Value[2];
                pts[kv.Key] = p;
            }
            return newPos.Count;
        }

        /// <summary>Push splats along their normals (destructive explode).</summary>
        public static int Explode(PlySplatModel model, bool[] sel, float distance)
        {
            var idx = Affected(model, sel);
            var pts = model.Points;
            foreach (int i in idx)
            {
                var p = pts[i];
                p.X += p.Nx * distance;
                p.Y += p.Ny * distance;
                p.Z += p.Nz * distance;
                pts[i] = p;
            }
            return idx.Count;
        }

        #endregion
    }
}
