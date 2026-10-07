using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MeesaMultisMaker.ThreeD
{
    /// <summary>
    /// One gaussian splat / point loaded from a PLY file.
    /// Positions are normalized to roughly fit a unit cube centered on origin.
    /// </summary>
    public struct SplatPoint
    {
        public float X, Y, Z;
        public byte R, G, B;
        // Linear per-axis sigma (normalized to scene scale). The renderer
        // projects the full 3D covariance, so elongated gaussians keep
        // their orientation instead of collapsing to discs.
        public float S0, S1, S2;
        // Unit quaternion, 3DGS convention rot_0 = w.
        public float Qw, Qx, Qy, Qz;
        public float Opacity; // 0..1
        // Surface normal (model frame, Y-mirrored like positions).
        // Missing in file -> (0,1,0). Used by relight + normal select.
        public float Nx, Ny, Nz;
    }

    /// <summary>
    /// Minimal PLY reader for gaussian-splat clouds (as written by ComfyUI
    /// SplatToFile3D) and plain colored point clouds. Supports ascii and
    /// binary_little_endian with per-vertex float/uchar properties.
    /// </summary>
    public class PlySplatModel
    {
        public List<SplatPoint> Points = new List<SplatPoint>();
        public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
        // Post-normalization bounds (Y-up, unit-cube-ish). The viewport
        // ground grid sits at NMinY.
        public float NMinX, NMinY, NMinZ, NMaxX, NMaxY, NMaxZ;
        public string SourcePath = string.Empty;

        public int Count { get { return Points != null ? Points.Count : 0; } }

        private class PropInfo
        {
            public string Name;
            public string Type;
        }

        public static PlySplatModel Load(string path)
        {
            var model = new PlySplatModel { SourcePath = path };
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                // Read header line by line (ascii), remembering where data starts.
                var headerLines = new List<string>();
                var sb = new StringBuilder();
                int b;
                while ((b = fs.ReadByte()) != -1)
                {
                    if (b == '\n')
                    {
                        string line = sb.ToString().TrimEnd('\r');
                        sb.Length = 0;
                        headerLines.Add(line);
                        if (line == "end_header") break;
                    }
                    else sb.Append((char)b);
                }
                long dataStart = fs.Position;

                string format = "ascii";
                int vertexCount = 0;
                var props = new List<PropInfo>();
                bool inVertex = false;
                foreach (var line in headerLines)
                {
                    if (line.StartsWith("format ", StringComparison.Ordinal))
                    {
                        if (line.Contains("binary_little_endian")) format = "binary_little_endian";
                        else if (line.Contains("binary_big_endian")) format = "binary_big_endian";
                        else format = "ascii";
                    }
                    else if (line.StartsWith("element ", StringComparison.Ordinal))
                    {
                        var parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        inVertex = parts.Length >= 3 && parts[1] == "vertex";
                        if (inVertex && parts.Length >= 3) int.TryParse(parts[2], out vertexCount);
                    }
                    else if (inVertex && line.StartsWith("property ", StringComparison.Ordinal))
                    {
                        var parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3 && parts[1] != "list")
                            props.Add(new PropInfo { Type = parts[1], Name = parts[2] });
                    }
                }

                if (vertexCount <= 0) throw new Exception("PLY has no vertices.");
                if (format == "binary_big_endian")
                    throw new Exception("PLY binary_big_endian is not supported.");

                // Column indices we care about.
                int ix = IndexOf(props, "x"), iy = IndexOf(props, "y"), iz = IndexOf(props, "z");
                int ir = IndexOf(props, "red"), ig = IndexOf(props, "green"), ib = IndexOf(props, "blue");
                int ifx = IndexOf(props, "f_dc_0"), ify = IndexOf(props, "f_dc_1"), ifz = IndexOf(props, "f_dc_2");
                int is0 = IndexOf(props, "scale_0"), is1 = IndexOf(props, "scale_1"), is2 = IndexOf(props, "scale_2");
                int isc = IndexOf(props, "scale");
                int iop = IndexOf(props, "opacity");
                int ir0 = IndexOf(props, "rot_0"), ir1 = IndexOf(props, "rot_1"),
                    ir2 = IndexOf(props, "rot_2"), ir3 = IndexOf(props, "rot_3");
                int inx = IndexOf(props, "nx"), iny = IndexOf(props, "ny"), inz = IndexOf(props, "nz");
                if (ix < 0 || iy < 0 || iz < 0) throw new Exception("PLY vertices lack x/y/z.");

                bool hasRgb = ir >= 0 && ig >= 0 && ib >= 0;
                bool hasDc = ifx >= 0 && ify >= 0 && ifz >= 0;

                if (format == "ascii")
                {
                    using (var sr = new StreamReader(fs, Encoding.ASCII))
                    {
                        for (int v = 0; v < vertexCount; v++)
                        {
                            string l = sr.ReadLine();
                            if (l == null) break;
                            if (l.Length == 0) { v--; continue; }
                            var tok = l.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            float x = ParseFloat(tok, ix), y = ParseFloat(tok, iy), z = ParseFloat(tok, iz);
                            byte r = 200, g = 200, bl = 200;
                            if (hasRgb)
                            {
                                r = ParseByte(tok, props[ir].Type, ir);
                                g = ParseByte(tok, props[ig].Type, ig);
                                bl = ParseByte(tok, props[ib].Type, ib);
                            }
                            else if (hasDc)
                            {
                                r = DcToByte(ParseFloat(tok, ifx));
                                g = DcToByte(ParseFloat(tok, ify));
                                bl = DcToByte(ParseFloat(tok, ifz));
                            }
                            float s0, s1, s2;
                            ReadSigmas(tok, props, is0, is1, is2, isc, out s0, out s1, out s2);
                            float qw, qx, qy, qz;
                            ReadQuat(tok, ir0, ir1, ir2, ir3, out qw, out qx, out qy, out qz);
                            float op = ReadOpacity(tok, props, iop);
                            float nx, ny, nz;
                            ReadNormal(tok, inx, iny, inz, out nx, out ny, out nz);
                            model.Points.Add(new SplatPoint { X = x, Y = y, Z = z, R = r, G = g, B = bl, S0 = s0, S1 = s1, S2 = s2, Qw = qw, Qx = qx, Qy = qy, Qz = qz, Opacity = op, Nx = nx, Ny = ny, Nz = nz });
                        }
                    }
                }
                else
                {
                    fs.Position = dataStart;
                    using (var br = new BinaryReader(fs))
                    {
                        int n = props.Count;
                        for (int v = 0; v < vertexCount; v++)
                        {
                            double[] vals = new double[n];
                            for (int p = 0; p < n; p++)
                                vals[p] = ReadBinary(br, props[p].Type);
                            float x = (float)vals[ix], y = (float)vals[iy], z = (float)vals[iz];
                            byte r = 200, g = 200, bl = 200;
                            if (hasRgb)
                            {
                                r = ToByte(vals[ir], props[ir].Type);
                                g = ToByte(vals[ig], props[ig].Type);
                                bl = ToByte(vals[ib], props[ib].Type);
                            }
                            else if (hasDc)
                            {
                                r = DcToByte((float)vals[ifx]);
                                g = DcToByte((float)vals[ify]);
                                bl = DcToByte((float)vals[ifz]);
                            }
                            float s0, s1, s2;
                            ReadSigmas(vals, props, is0, is1, is2, isc, out s0, out s1, out s2);
                            float qw, qx, qy, qz;
                            ReadQuat(vals, ir0, ir1, ir2, ir3, out qw, out qx, out qy, out qz);
                            float op = iop >= 0 ? Sigmoid((float)vals[iop]) : 1f;
                            float nx, ny, nz;
                            ReadNormal(vals, inx, iny, inz, out nx, out ny, out nz);
                            model.Points.Add(new SplatPoint { X = x, Y = y, Z = z, R = r, G = g, B = bl, S0 = s0, S1 = s1, S2 = s2, Qw = qw, Qx = qx, Qy = qy, Qz = qz, Opacity = op, Nx = nx, Ny = ny, Nz = nz });
                        }
                    }
                }
            }

            model.Normalize();
            return model;
        }

        private static float ClampSigma(float v)
        {
            if (v < 0.0005f) return 0.0005f;
            if (v > 0.35f) return 0.35f;
            return v;
        }

        private static int IndexOf(List<PropInfo> props, string name)
        {
            for (int i = 0; i < props.Count; i++)
                if (string.Equals(props[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        private static float ParseFloat(string[] tok, int idx)
        {
            if (idx < 0 || idx >= tok.Length) return 0;
            float v;
            if (float.TryParse(tok[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return 0;
        }

        private static byte ParseByte(string[] tok, string type, int idx)
        {
            if (idx < 0 || idx >= tok.Length) return 200;
            if (IsFloatType(type))
            {
                float f;
                if (!float.TryParse(tok[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out f)) return 200;
                if (f <= 1.0f && f >= 0f) return (byte)(f * 255f);
                if (f < 0) return 0;
                if (f > 255) return 255;
                return (byte)f;
            }
            int iv;
            if (!int.TryParse(tok[idx], NumberStyles.Integer, CultureInfo.InvariantCulture, out iv)) return 200;
            if (iv < 0) return 0;
            if (iv > 255) return 255;
            return (byte)iv;
        }

        private static bool IsFloatType(string t)
        {
            return t == "float" || t == "float32" || t == "double" || t == "float64";
        }

        private static byte ToByte(double v, string type)
        {
            if (IsFloatType(type))
            {
                if (v <= 1.0 && v >= 0.0) return (byte)(v * 255.0);
                if (v < 0) return 0;
                if (v > 255) return 255;
                return (byte)v;
            }
            if (v < 0) return 0;
            if (v > 255) return 255;
            return (byte)v;
        }

        private const float SH_C0 = 0.28209479177387814f;

        private static byte DcToByte(float dc)
        {
            float c = 0.5f + SH_C0 * dc;
            if (c < 0) return 0;
            if (c > 1) return 255;
            return (byte)(c * 255f);
        }

        private static float Sigmoid(float x)
        {
            return 1f / (1f + (float)Math.Exp(-x));
        }

        private static float ClampLog(double v)
        {
            if (v > 3) v = 3;
            if (v < -8) v = -8;
            return (float)Math.Exp(v);
        }

        private static void ReadSigmas(string[] tok, List<PropInfo> props,
            int is0, int is1, int is2, int isc, out float s0, out float s1, out float s2)
        {
            s0 = s1 = s2 = 0.02f;
            try
            {
                if (is0 >= 0 || is1 >= 0 || is2 >= 0)
                {
                    if (is0 >= 0) s0 = ClampLog(ParseFloat(tok, is0));
                    if (is1 >= 0) s1 = ClampLog(ParseFloat(tok, is1));
                    if (is2 >= 0) s2 = ClampLog(ParseFloat(tok, is2));
                }
                else if (isc >= 0)
                {
                    float s = ParseFloat(tok, isc);
                    if (s < 0) s = 0.02f;
                    s0 = s1 = s2 = s;
                }
            }
            catch { s0 = s1 = s2 = 0.02f; }
        }

        private static void ReadSigmas(double[] vals, List<PropInfo> props,
            int is0, int is1, int is2, int isc, out float s0, out float s1, out float s2)
        {
            s0 = s1 = s2 = 0.02f;
            try
            {
                if (is0 >= 0 || is1 >= 0 || is2 >= 0)
                {
                    if (is0 >= 0) s0 = ClampLog(vals[is0]);
                    if (is1 >= 0) s1 = ClampLog(vals[is1]);
                    if (is2 >= 0) s2 = ClampLog(vals[is2]);
                }
                else if (isc >= 0)
                {
                    float s = (float)vals[isc];
                    if (s < 0) s = 0.02f;
                    s0 = s1 = s2 = s;
                }
            }
            catch { s0 = s1 = s2 = 0.02f; }
        }

        private static void ReadQuat(string[] tok,
            int ir0, int ir1, int ir2, int ir3,
            out float qw, out float qx, out float qy, out float qz)
        {
            qw = 1; qx = qy = qz = 0;
            if (ir0 < 0 || ir1 < 0 || ir2 < 0 || ir3 < 0) return;
            try
            {
                qw = ParseFloat(tok, ir0); qx = ParseFloat(tok, ir1);
                qy = ParseFloat(tok, ir2); qz = ParseFloat(tok, ir3);
                NormQuat(ref qw, ref qx, ref qy, ref qz);
            }
            catch { qw = 1; qx = qy = qz = 0; }
        }

        private static void ReadQuat(double[] vals,
            int ir0, int ir1, int ir2, int ir3,
            out float qw, out float qx, out float qy, out float qz)
        {
            qw = 1; qx = qy = qz = 0;
            if (ir0 < 0 || ir1 < 0 || ir2 < 0 || ir3 < 0) return;
            try
            {
                qw = (float)vals[ir0]; qx = (float)vals[ir1];
                qy = (float)vals[ir2]; qz = (float)vals[ir3];
                NormQuat(ref qw, ref qx, ref qy, ref qz);
            }
            catch { qw = 1; qx = qy = qz = 0; }
        }

        private static void NormQuat(ref float w, ref float x, ref float y, ref float z)
        {
            double n = Math.Sqrt((double)w * w + (double)x * x + (double)y * y + (double)z * z);
            if (n < 1e-9) { w = 1; x = y = z = 0; return; }
            w = (float)(w / n); x = (float)(x / n);
            y = (float)(y / n); z = (float)(z / n);
        }

        private static void ReadNormal(string[] tok, int inx, int iny, int inz,
            out float nx, out float ny, out float nz)
        {
            nx = 0; ny = -1; nz = 0;
            if (inx < 0 || iny < 0 || inz < 0) return;
            try
            {
                nx = ParseFloat(tok, inx); ny = ParseFloat(tok, iny); nz = ParseFloat(tok, inz);
                NormVec(ref nx, ref ny, ref nz);
            }
            catch { nx = 0; ny = -1; nz = 0; }
        }

        private static void ReadNormal(double[] vals, int inx, int iny, int inz,
            out float nx, out float ny, out float nz)
        {
            nx = 0; ny = -1; nz = 0;
            if (inx < 0 || iny < 0 || inz < 0) return;
            try
            {
                nx = (float)vals[inx]; ny = (float)vals[iny]; nz = (float)vals[inz];
                NormVec(ref nx, ref ny, ref nz);
            }
            catch { nx = 0; ny = -1; nz = 0; }
        }

        // File frame is Y-down: file-down (0,-1,0) becomes world-up after
        // the Y-mirror in Normalize, so defaults use (0,-1,0) here.
        private static void NormVec(ref float x, ref float y, ref float z)
        {
            double n = Math.Sqrt((double)x * x + (double)y * y + (double)z * z);
            if (n < 1e-9) { x = 0; y = -1; z = 0; return; }
            x = (float)(x / n); y = (float)(y / n); z = (float)(z / n);
        }

        private static float ReadOpacity(string[] tok, List<PropInfo> props, int iop)
        {
            if (iop < 0) return 1f;
            try
            {
                if (IsFloatType(props[iop].Type))
                {
                    float v = ParseFloat(tok, iop);
                    // Heuristic: logit values live outside 0..1 or look like logits;
                    // plain 0..1 alpha passes through.
                    if (v < 0f || v > 1f) return Sigmoid(v);
                    return v;
                }
                int iv;
                if (int.TryParse(tok[iop], out iv))
                {
                    if (iv < 0) return 0;
                    if (iv > 255) return 255;
                    return iv / 255f;
                }
            }
            catch { }
            return 1f;
        }

        private static double ReadBinary(BinaryReader br, string type)
        {
            switch (type)
            {
                case "char":
                case "int8": return br.ReadSByte();
                case "uchar":
                case "uint8": return br.ReadByte();
                case "short":
                case "int16": return br.ReadInt16();
                case "ushort":
                case "uint16": return br.ReadUInt16();
                case "int":
                case "int32": return br.ReadInt32();
                case "uint":
                case "uint32": return br.ReadUInt32();
                case "float":
                case "float32": return br.ReadSingle();
                case "double":
                case "float64": return br.ReadDouble();
                default: return br.ReadSingle();
            }
        }

        /// <summary>
        /// Center on origin and scale so the largest dimension spans ~2 units.
        /// Splat sizes are scaled by the same factor (clamped to a sane range).
        /// Internal so mesh sampling can reuse the exact same framing.
        /// </summary>
        internal void Normalize()
        {
            if (Points.Count == 0) return;
            float minX = Points[0].X, minY = Points[0].Y, minZ = Points[0].Z;
            float maxX = minX, maxY = minY, maxZ = minZ;
            foreach (var p in Points)
            {
                if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
                if (p.Z < minZ) minZ = p.Z; if (p.Z > maxZ) maxZ = p.Z;
            }
            MinX = minX; MinY = minY; MinZ = minZ;
            MaxX = maxX; MaxY = maxY; MaxZ = maxZ;
            float cx = (minX + maxX) * 0.5f, cy = (minY + maxY) * 0.5f, cz = (minZ + maxZ) * 0.5f;
            float span = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
            if (span < 1e-6f) span = 1f;
            float s = 2f / span;
            float nMinX = float.MaxValue, nMinY = float.MaxValue, nMinZ = float.MaxValue;
            float nMaxX = float.MinValue, nMaxY = float.MinValue, nMaxZ = float.MinValue;
            for (int i = 0; i < Points.Count; i++)
            {
                var p = Points[i];
                p.X = (p.X - cx) * s;
                // Splat files from the image-to-3D pipeline arrive Y-down
                // (image space); the viewport is Y-up, so negate here.
                // Without this the model renders upside-down.
                p.Y = -(p.Y - cy) * s;
                p.Z = (p.Z - cz) * s;
                p.S0 = ClampSigma(p.S0 * s);
                p.S1 = ClampSigma(p.S1 * s);
                p.S2 = ClampSigma(p.S2 * s);
                // Mirror with the Y-flipped positions.
                p.Ny = -p.Ny;
                Points[i] = p;
                if (p.X < nMinX) nMinX = p.X; if (p.X > nMaxX) nMaxX = p.X;
                if (p.Y < nMinY) nMinY = p.Y; if (p.Y > nMaxY) nMaxY = p.Y;
                if (p.Z < nMinZ) nMinZ = p.Z; if (p.Z > nMaxZ) nMaxZ = p.Z;
            }
            NMinX = nMinX; NMinY = nMinY; NMinZ = nMinZ;
            NMaxX = nMaxX; NMaxY = nMaxY; NMaxZ = nMaxZ;
        }
    }
}

