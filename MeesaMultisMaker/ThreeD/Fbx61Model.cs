using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace MeesaMultisMaker.ThreeD
{
    /// <summary>
    /// Minimal FBX 6.1 ASCII reader (Blender 2.7x-era files, e.g. "FBX 6.1.0
    /// project file"). Assimp 5 only imports FBX 2011+, so these files need
    /// this fallback. Scope: static bind-pose meshes with normals, UVs,
    /// per-polygon materials and external diffuse textures. Skinning,
    /// animation, cameras, lights and limbs are ignored.
    /// </summary>
    public static class Fbx61Model
    {
        private class Node
        {
            public string Name = "";
            public List<string> Strs = new List<string>();
            public List<float> Nums = new List<float>();
            public List<Node> Children = new List<Node>();

            public Node Find(string name)
            {
                foreach (var c in Children)
                    if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;
                return null;
            }

            public List<Node> FindAll(string name)
            {
                var r = new List<Node>();
                foreach (var c in Children)
                    if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) r.Add(c);
                return r;
            }
        }

        public static PlySplatModel Load(string path)
        {
            Node root = ParseFile(path);
            string modelDir = "";
            try { modelDir = Path.GetDirectoryName(Path.GetFullPath(path)); }
            catch { modelDir = ""; }

            Node objects = null;
            foreach (var c in root.Children)
                if (c.Name == "Objects") { objects = c; break; }
            if (objects == null) throw new Exception("FBX 6.1: no Objects section.");

            // Connections in file order (child -> parent links).
            Node conns = null;
            foreach (var c in root.Children)
                if (c.Name == "Connections") { conns = c; break; }

            // Index objects by full name.
            var models = new Dictionary<string, Node>(StringComparer.Ordinal);
            var materials = new Dictionary<string, Node>(StringComparer.Ordinal);
            var textures = new Dictionary<string, Node>(StringComparer.Ordinal);
            foreach (var c in objects.Children)
            {
                if (c.Strs.Count == 0) continue;
                if (c.Name == "Model") models[c.Strs[0]] = c;
                else if (c.Name == "Material") materials[c.Strs[0]] = c;
                else if (c.Name == "Texture") textures[c.Strs[0]] = c;
            }

            var tris = new List<MeshSplatModel.Tri>();
            var ownedTex = new List<Bitmap>();
            int meshCount = 0;
            int texturedMeshes = 0;

            foreach (var kvp in models)
            {
                Node m = kvp.Value;
                if (m.Strs.Count < 2 || !string.Equals(m.Strs[1], "Mesh", StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    Node vertsNode = m.Find("Vertices");
                    Node pviNode = m.Find("PolygonVertexIndex");
                    if (vertsNode == null || pviNode == null) continue;
                    float[] verts = vertsNode.Nums.ToArray();
                    int[] pvi = ToIntArray(pviNode.Nums);
                    if (verts.Length < 9 || pvi.Length < 3) continue;

                    // Local transform (Blender: degrees, XYZ euler).
                    float[] localT = { 0, 0, 0 }, localR = { 0, 0, 0 }, localS = { 1, 1, 1 };
                    Node props = m.Find("Properties60");
                    if (props == null) props = m.Find("Properties70");
                    if (props != null)
                    {
                        foreach (var p in props.FindAll("Property"))
                        {
                            if (p.Strs.Count < 1 || p.Nums.Count < 3) continue;
                            float[] v = p.Nums.ToArray();
                            if (p.Strs[0] == "Lcl Translation") { localT[0] = v[0]; localT[1] = v[1]; localT[2] = v[2]; }
                            else if (p.Strs[0] == "Lcl Rotation") { localR[0] = v[0]; localR[1] = v[1]; localR[2] = v[2]; }
                            else if (p.Strs[0] == "Lcl Scaling") { localS[0] = v[0]; localS[1] = v[1]; localS[2] = v[2]; }
                        }
                    }
                    float[,] mat = ComposeTRS(localT, localR, localS);

                    // Layers.
                    Node normLayer = m.Find("LayerElementNormal");
                    Node uvLayer = m.Find("LayerElementUV");
                    Node matLayer = m.Find("LayerElementMaterial");

                    // Linked materials/textures (connection order).
                    var linkedMats = new List<string>();
                    var linkedTex = new List<string>();
                    if (conns != null)
                    {
                        foreach (var cc in conns.Children)
                        {
                            if (cc.Name != "Connect" || cc.Strs.Count < 3) continue;
                            if (cc.Strs[2] != kvp.Key) continue;
                            if (cc.Strs[1].StartsWith("Material::", StringComparison.Ordinal)) linkedMats.Add(cc.Strs[1]);
                            else if (cc.Strs[1].StartsWith("Texture::", StringComparison.Ordinal)) linkedTex.Add(cc.Strs[1]);
                        }
                    }

                    // Material diffuse colors + one texture for the model.
                    var matColors = new List<float[]>();
                    foreach (var mn in linkedMats)
                    {
                        Node mn2;
                        float[] col = { 1, 1, 1 };
                        if (materials.TryGetValue(mn, out mn2))
                        {
                            Node pr = mn2.Find("Properties60");
                            if (pr == null) pr = mn2.Find("Properties70");
                            if (pr != null)
                            {
                                foreach (var pp in pr.FindAll("Property"))
                                {
                                    if (pp.Strs.Count < 1 || pp.Nums.Count < 3) continue;
                                    if (pp.Strs[0] == "Diffuse" || pp.Strs[0] == "DiffuseColor")
                                    {
                                        float[] v = pp.Nums.ToArray();
                                        col[0] = v[0]; col[1] = v[1]; col[2] = v[2];
                                    }
                                }
                            }
                        }
                        matColors.Add(col);
                    }
                    if (matColors.Count == 0) matColors.Add(new float[] { 0.78f, 0.78f, 0.78f });

                    Bitmap modelTex = null;
                    foreach (var tn in linkedTex)
                    {
                        Node tx;
                        if (!textures.TryGetValue(tn, out tx)) continue;
                        string fn = ChildString(tx, "RelativeFilename");
                        if (string.IsNullOrEmpty(fn)) fn = ChildString(tx, "FileName");
                        if (string.IsNullOrEmpty(fn)) continue;
                        try
                        {
                            modelTex = MeshSplatModel.FindExternalTexture(modelDir, fn);
                            if (modelTex != null) { ownedTex.Add(modelTex); break; }
                        }
                        catch { }
                    }

                    string matMapping = ChildString(matLayer, "MappingInformationType");
                    List<int> matIdx = matLayer != null ? ToIntList(matLayer.Find("Materials")) : null;

                    string normMapping = ChildString(normLayer, "MappingInformationType");
                    string normRef = ChildString(normLayer, "ReferenceInformationType");
                    List<float> normArr = normLayer != null ? ToFloatList(normLayer.Find("Normals")) : null;
                    List<int> normIdx = normLayer != null ? ToIntList(normLayer.Find("NormalIndex")) : null;

                    string uvMapping = ChildString(uvLayer, "MappingInformationType");
                    string uvRef = ChildString(uvLayer, "ReferenceInformationType");
                    List<float> uvArr = uvLayer != null ? ToFloatList(uvLayer.Find("UV")) : null;
                    List<int> uvIdx = uvLayer != null ? ToIntList(uvLayer.Find("UVIndex")) : null;
                    bool hasUv = modelTex != null && uvArr != null && uvArr.Count >= 2;

                    // Walk polygons.
                    var face = new List<int>();
                    int poly = -1;
                    int corner = 0;
                    bool meshTextured = false;
                    Action flush = () =>
                    {
                        if (face.Count < 3) return;
                        poly++;
                        int mi = 0;
                        if (!string.IsNullOrEmpty(matMapping) && matMapping == "ByPolygon" && matIdx != null && poly < matIdx.Count)
                            mi = matIdx[poly];
                        if (mi < 0 || mi >= matColors.Count) mi = 0;
                        float[] mc = matColors[mi];
                        // Fan-triangulate.
                        for (int fi = 1; fi + 1 < face.Count; fi++)
                        {
                            int[] triIdx = { face[0], face[fi], face[fi + 1] };
                            int[] triCorner = { corner - face.Count, corner - face.Count + fi, corner - face.Count + fi + 1 };
                            // Blender 2.7x-era 6.1 files store Blender-space
                            // (Z-up) coordinates: convert to Y-up here so the
                            // model matches Assimp-loaded files.
                            float[] P = new float[9];
                            float[] N = new float[9];
                            float[] U = new float[6];
                            bool nOk = true, uOk = hasUv;
                            for (int k = 0; k < 3; k++)
                            {
                                int vi = triIdx[k];
                                int ci = triCorner[k];
                                // Z-up -> Y-up: (x, y, z) becomes (x, z, -y).
                                P[k * 3] = verts[vi * 3]; P[k * 3 + 1] = verts[vi * 3 + 2]; P[k * 3 + 2] = -verts[vi * 3 + 1];
                                if (!ResolveNormal(normMapping, normRef, normArr, normIdx, ci, vi, N, k)) nOk = false;
                                if (uOk && !ResolveUv(uvMapping, uvRef, uvArr, uvIdx, ci, vi, U, k)) uOk = false;
                            }
                            // File normals are Z-up too: same (x, y, z) -> (x, z, -y).
                            if (nOk)
                            {
                                for (int k = 0; k < 3; k++)
                                {
                                    float ty = N[k * 3 + 1];
                                    N[k * 3 + 1] = N[k * 3 + 2];
                                    N[k * 3 + 2] = -ty;
                                }
                            }
                            // Transform (positions full TRS, normals rotation-only).
                            float[] Pw = new float[9];
                            float[] Nw = new float[9];
                            for (int k = 0; k < 3; k++)
                            {
                                TransformPoint(mat, P[k * 3], P[k * 3 + 1], P[k * 3 + 2], Pw, k * 3);
                                if (nOk) TransformVector(mat, N[k * 3], N[k * 3 + 1], N[k * 3 + 2], Nw, k * 3);
                            }
                            if (!nOk) { Nw[0] = Nw[3] = Nw[6] = 0; Nw[1] = Nw[4] = Nw[7] = 0; Nw[2] = Nw[5] = Nw[8] = 0; }
                            float ux = Pw[3] - Pw[0], uy = Pw[4] - Pw[1], uz = Pw[5] - Pw[2];
                            float vx = Pw[6] - Pw[0], vy = Pw[7] - Pw[1], vz = Pw[8] - Pw[2];
                            float fnx = uy * vz - uz * vy, fny = uz * vx - ux * vz, fnz = ux * vy - uy * vx;
                            double area = 0.5 * Math.Sqrt((double)fnx * fnx + (double)fny * fny + (double)fnz * fnz);
                            if (area < 1e-12) continue;
                            fnx = (float)(fnx / (2.0 * area)); fny = (float)(fny / (2.0 * area)); fnz = (float)(fnz / (2.0 * area));
                            var t = new MeshSplatModel.Tri();
                            t.Ax = Pw[0]; t.Ay = Pw[1]; t.Az = Pw[2];
                            t.Bx = Pw[3]; t.By = Pw[4]; t.Bz = Pw[5];
                            t.Cx = Pw[6]; t.Cy = Pw[7]; t.Cz = Pw[8];
                            if (nOk) { t.Nax = Nw[0]; t.Nay = Nw[1]; t.Naz = Nw[2]; t.Nbx = Nw[3]; t.Nby = Nw[4]; t.Nbz = Nw[5]; t.Ncx = Nw[6]; t.Ncy = Nw[7]; t.Ncz = Nw[8]; }
                            t.Nx = fnx; t.Ny = fny; t.Nz = fnz;
                            t.R = mc[0]; t.G = mc[1]; t.B = mc[2];
                            t.HasVc = false;
                            t.Area = (float)area;
                            if (uOk && modelTex != null)
                            {
                                t.Ua = U[0]; t.Va = U[1]; t.Ub = U[2]; t.Vb = U[3]; t.Uc = U[4]; t.Vc = U[5];
                                t.HasUv = true; t.Tex = modelTex;
                                meshTextured = true;
                            }
                            tris.Add(t);
                        }
                    };

                    foreach (int raw in pvi)
                    {
                        if (raw < 0) { face.Add(~raw); corner++; flush(); face.Clear(); }
                        else { face.Add(raw); corner++; }
                    }
                    flush();

                    meshCount++;
                    if (meshTextured) texturedMeshes++;
                }
                catch
                {
                    // Skip a bad mesh, keep the rest.
                }
            }

            if (tris.Count == 0)
                throw new Exception("FBX 6.1: no mesh triangles parsed.");
            try
            {
                return MeshSplatModel.SampleTris(tris, path, texturedMeshes, meshCount);
            }
            finally
            {
                foreach (var b in ownedTex)
                {
                    try { if (b != null) b.Dispose(); } catch { }
                }
            }
        }

        private static bool ResolveNormal(string mapping, string refType, List<float> arr, List<int> idx, int corner, int vert, float[] N, int k)
        {
            try
            {
                if (arr == null) return false;
                int ai = -1;
                bool byVert = !string.IsNullOrEmpty(mapping) && mapping == "ByVertice";
                if (refType == "IndexToDirect" && idx != null)
                {
                    int ii = byVert ? vert : corner;
                    if (ii < 0 || ii >= idx.Count) return false;
                    ai = idx[ii];
                }
                else ai = byVert ? vert : corner;
                if (ai < 0 || (ai + 1) * 3 > arr.Count) return false;
                float x = arr[ai * 3], y = arr[ai * 3 + 1], z = arr[ai * 3 + 2];
                double n = Math.Sqrt((double)x * x + (double)y * y + (double)z * z);
                if (n < 1e-9) return false;
                N[k * 3] = (float)(x / n); N[k * 3 + 1] = (float)(y / n); N[k * 3 + 2] = (float)(z / n);
                return true;
            }
            catch { return false; }
        }

        private static bool ResolveUv(string mapping, string refType, List<float> arr, List<int> idx, int corner, int vert, float[] U, int k)
        {
            try
            {
                if (arr == null) return false;
                int ai = -1;
                bool byVert = !string.IsNullOrEmpty(mapping) && mapping == "ByVertice";
                if (refType == "IndexToDirect" && idx != null)
                {
                    int ii = byVert ? vert : corner;
                    if (ii < 0 || ii >= idx.Count) return false;
                    ai = idx[ii];
                }
                else ai = byVert ? vert : corner;
                if (ai < 0 || (ai + 1) * 2 > arr.Count) return false;
                U[k * 2] = arr[ai * 2]; U[k * 2 + 1] = arr[ai * 2 + 1];
                return true;
            }
            catch { return false; }
        }

        private static float[,] ComposeTRS(float[] t, float[] rDeg, float[] s)
        {
            double rx = rDeg[0] * Math.PI / 180.0, ry = rDeg[1] * Math.PI / 180.0, rz = rDeg[2] * Math.PI / 180.0;
            double cx = Math.Cos(rx), sx = Math.Sin(rx), cy = Math.Cos(ry), sy = Math.Sin(ry), cz = Math.Cos(rz), sz = Math.Sin(rz);
            // R = Rz * Ry * Rx.
            var m = new float[4, 4];
            m[0, 0] = (float)(cy * cz * s[0]); m[0, 1] = (float)(-cy * sz * s[1]); m[0, 2] = (float)(sy * s[2]); m[0, 3] = t[0];
            m[1, 0] = (float)((sx * sy * cz + cx * sz) * s[0]); m[1, 1] = (float)((-sx * sy * sz + cx * cz) * s[1]); m[1, 2] = (float)(-sx * cy * s[2]); m[1, 3] = t[1];
            m[2, 0] = (float)((-cx * sy * cz + sx * sz) * s[0]); m[2, 1] = (float)((cx * sy * sz + sx * cz) * s[1]); m[2, 2] = (float)(cx * cy * s[2]); m[2, 3] = t[2];
            m[3, 0] = 0; m[3, 1] = 0; m[3, 2] = 0; m[3, 3] = 1;
            return m;
        }

        private static void TransformPoint(float[,] m, float x, float y, float z, float[] o, int oi)
        {
            o[oi] = m[0, 0] * x + m[0, 1] * y + m[0, 2] * z + m[0, 3];
            o[oi + 1] = m[1, 0] * x + m[1, 1] * y + m[1, 2] * z + m[1, 3];
            o[oi + 2] = m[2, 0] * x + m[2, 1] * y + m[2, 2] * z + m[2, 3];
        }

        private static void TransformVector(float[,] m, float x, float y, float z, float[] o, int oi)
        {
            o[oi] = m[0, 0] * x + m[0, 1] * y + m[0, 2] * z;
            o[oi + 1] = m[1, 0] * x + m[1, 1] * y + m[1, 2] * z;
            o[oi + 2] = m[2, 0] * x + m[2, 1] * y + m[2, 2] * z;
            double n = Math.Sqrt((double)o[oi] * o[oi] + (double)o[oi + 1] * o[oi + 1] + (double)o[oi + 2] * o[oi + 2]);
            if (n > 1e-9) { o[oi] = (float)(o[oi] / n); o[oi + 1] = (float)(o[oi + 1] / n); o[oi + 2] = (float)(o[oi + 2] / n); }
        }

        private static string ChildString(Node n, string child)
        {
            try
            {
                Node c = n != null ? n.Find(child) : null;
                if (c != null && c.Strs.Count > 0) return c.Strs[0];
            }
            catch { }
            return "";
        }

        private static List<float> ToFloatList(Node n)
        {
            if (n == null) return null;
            return n.Nums;
        }

        private static List<int> ToIntList(Node n)
        {
            if (n == null) return null;
            var r = new List<int>(n.Nums.Count);
            foreach (var f in n.Nums) r.Add((int)f);
            return r;
        }

        private static int[] ToIntArray(List<float> f)
        {
            var r = new int[f.Count];
            for (int i = 0; i < f.Count; i++) r[i] = (int)f[i];
            return r;
        }

        // ---- file parser: tolerant line-based FBX 6.1 ASCII reader ----

        private static Node ParseFile(string path)
        {
            var root = new Node { Name = "" };
            var stack = new Stack<Node>();
            stack.Push(root);
            Node pendingNode = null;
            StringBuilder pendingSb = null;

            foreach (var raw in File.ReadLines(path))
            {
                string line = raw.TrimEnd();
                if (line.Length == 0) continue;
                string trimmed = line.TrimStart();
                if (trimmed.Length == 0) continue;
                if (trimmed[0] == ';') continue;

                // Closing braces.
                int closes = 0;
                while (closes < trimmed.Length && trimmed[closes] == '}') closes++;
                if (closes > 0)
                {
                    FinalizePending(ref pendingNode, ref pendingSb);
                    for (int i = 0; i < closes && stack.Count > 1; i++) stack.Pop();
                    string rest2 = trimmed.Substring(closes).Trim();
                    if (rest2.Length == 0) continue;
                    line = rest2;
                    trimmed = rest2;
                }

                int indent = 0;
                while (indent < line.Length && line[indent] == '\t') indent++;
                string content = line.Substring(indent);

                int colon = content.IndexOf(':');
                if (colon < 0)
                {
                    // Continuation of a multi-line list.
                    if (pendingSb != null) { pendingSb.Append(' '); pendingSb.Append(content); }
                    continue;
                }

                FinalizePending(ref pendingNode, ref pendingSb);

                string name = content.Substring(0, colon).Trim();
                string rest = content.Substring(colon + 1);
                bool opens = false;
                string rt = rest.TrimEnd();
                if (rt.EndsWith("{")) { opens = true; rest = rt.Substring(0, rt.Length - 1); }

                var node = new Node { Name = name };
                ParseProps(rest, node);
                while (stack.Count - 1 > indent) stack.Pop();
                stack.Peek().Children.Add(node);

                // A brace-less header ending in ',' starts a multi-line list.
                // Header numbers are already in node.Nums; only continuations
                // accumulate into pendingSb.
                if (!opens && raw.TrimEnd().EndsWith(","))
                {
                    pendingNode = node;
                    pendingSb = new StringBuilder();
                }
                if (opens) stack.Push(node);
            }
            FinalizePending(ref pendingNode, ref pendingSb);
            return root;
        }

        private static void FinalizePending(ref Node pendingNode, ref StringBuilder pendingSb)
        {
            if (pendingNode == null || pendingSb == null) return;
            string extra = pendingSb.ToString();
            if (extra.Length > 0)
            {
                pendingNode.Nums.AddRange(SplitFloats(extra));
            }
            pendingNode = null;
            pendingSb = null;
        }

        private static void ParseProps(string rest, Node node)
        {
            // Tokenize respecting quotes: "a" , "b" , 1.0 , ...
            var tokens = new List<string>();
            var cur = new StringBuilder();
            bool inQ = false;
            for (int i = 0; i < rest.Length; i++)
            {
                char ch = rest[i];
                if (ch == '"') { inQ = !inQ; continue; }
                if (ch == ',' && !inQ)
                {
                    tokens.Add(cur.ToString().Trim());
                    cur.Length = 0;
                    continue;
                }
                cur.Append(ch);
            }
            tokens.Add(cur.ToString().Trim());
            // Determine quoted-ness per token by re-scanning is complex;
            // instead: tokens that parse as numbers go to Nums, others to Strs.
            foreach (var t in tokens)
            {
                if (t.Length == 0) continue;
                float f;
                if (float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out f))
                    node.Nums.Add(f);
                else
                    node.Strs.Add(t);
            }
        }

        private static List<float> SplitFloats(string s)
        {
            var r = new List<float>();
            var parts = s.Split(new char[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                float f;
                if (float.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) r.Add(f);
            }
            return r;
        }
    }
}
