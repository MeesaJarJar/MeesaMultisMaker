using Assimp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace MeesaMultisMaker.ThreeD
{
    /// <summary>
    /// Loads a mesh file (OBJ/FBX/glTF/GLB/STL/DAE/...) via Assimp and
    /// converts it to a <see cref="PlySplatModel"/> by sampling points on the
    /// triangle surfaces, so it renders in the existing splat viewport.
    /// Color per sample = diffuse texture (embedded or external file,
    /// sampled at interpolated UVs) multiplied by vertex colors when present,
    /// falling back to vertex colors, material diffuse, then neutral gray.
    /// </summary>
    public static class MeshSplatModel
    {
        private const int TargetPoints = 100000;
        private const int MaxPoints = 250000;

        /// <summary>Human-readable texture report from the last Load call.</summary>
        public static string LastTextureReport = string.Empty;

        private class TexData
        {
            public Bitmap Bmp;
            public BitmapData Lock;
            public byte[] Px;
            public int Stride, W, H;
        }

        internal struct Tri
        {
            public float Ax, Ay, Az, Bx, By, Bz, Cx, Cy, Cz;
            public float Nx, Ny, Nz;
            public float R, G, B;
            public float Area;
            public float Nax, Nay, Naz, Nbx, Nby, Nbz, Ncx, Ncy, Ncz;
            public float Ar, Ag, Ab, Br, Bg, Bb, Cr, Cg, Cb;
            public bool HasVc;
            public float Ua, Va, Ub, Vb, Uc, Vc;
            public bool HasUv;
            public Bitmap Tex; // shared ref, owned by the texture cache
            public float Fr, Fg, Fb; // base-color factor multiply (default 1)
            public bool AlphaTest; // cutout discard
            public float Cutoff;
        }

        public static PlySplatModel Load(string path)
        {
            Scene scene;
            try
            {
                using (var ctx = new AssimpContext())
                {
                    scene = ctx.ImportFile(path,
                        PostProcessSteps.Triangulate |
                        PostProcessSteps.JoinIdenticalVertices |
                        PostProcessSteps.PreTransformVertices |
                        PostProcessSteps.GenerateSmoothNormals);
                }
            }
            catch (Exception ex)
            {
                // Pre-2011 FBX (e.g. FBX 6.1 ASCII) is beyond Assimp 5 - try
                // the built-in legacy reader before giving up.
                if (IsLegacyFbx61(path))
                {
                    try { return Fbx61Model.Load(path); }
                    catch (Exception ex2) { throw new Exception("Legacy FBX 6.1 read failed: " + ex2.Message); }
                }
                throw new Exception("Assimp could not read this 3D file: " + ex.Message);
            }

            if (scene == null || !scene.HasMeshes)
                throw new Exception("Assimp found no meshes in this file.");

            string ext = "";
            try { ext = Path.GetExtension(path).ToLowerInvariant(); }
            catch { }

            // glTF direct texture path (Assimp 5.0 drops embedded glTF images
            // and leaves empty texture paths). Owned bitmaps, disposed below.
            GlbModel glb = null;
            var glbOwned = new List<Bitmap>();
            if (ext == ".glb" || ext == ".gltf")
            {
                try
                {
                    glb = GlbTextureReader.Load(path);
                    foreach (var m in glb.Ordered)
                    {
                        if (m != null && m.Tex != null && !glbOwned.Contains(m.Tex))
                            glbOwned.Add(m.Tex);
                    }
                }
                catch { glb = null; }
            }

            string modelDir = "";
            try { modelDir = Path.GetDirectoryName(Path.GetFullPath(path)); }
            catch { modelDir = ""; }

            // Resolve one diffuse texture per material (embedded first, else
            // external file next to the model). Owned here, disposed at the end.
            var texByMaterial = new Dictionary<int, Bitmap>();
            int texturedMeshes = 0;
            int meshCount = 0;
            try
            {
                var mats = scene.Materials;
                if (mats != null)
                {
                    for (int mi = 0; mi < mats.Count; mi++)
                    {
                        try { texByMaterial[mi] = ResolveDiffuseTexture(scene, mats[mi], modelDir); }
                        catch { texByMaterial[mi] = null; }
                    }
                }
            }
            catch { }

            var tris = new List<Tri>();
            double totalArea = 0;
            foreach (var mesh in scene.Meshes)
            {
                if (mesh == null || !mesh.HasVertices || !mesh.HasFaces) continue;
                meshCount++;
                var verts = mesh.Vertices;
                var norms = mesh.HasNormals ? mesh.Normals : null;

                // Color source: vertex colors > material diffuse > gray.
                bool useVc = false;
                try { useVc = mesh.HasVertexColors(0) && mesh.VertexColorChannels != null && mesh.VertexColorChannels.Length > 0 && mesh.VertexColorChannels[0] != null && mesh.VertexColorChannels[0].Count == mesh.VertexCount; }
                catch { useVc = false; }
                float mr = 0.78f, mg = 0.78f, mb = 0.78f;
                if (!useVc)
                {
                    try
                    {
                        var mats = scene.Materials;
                        if (mats != null && mesh.MaterialIndex >= 0 && mesh.MaterialIndex < mats.Count)
                        {
                            var mat = mats[mesh.MaterialIndex];
                            if (mat != null && mat.HasColorDiffuse)
                            {
                                var d = mat.ColorDiffuse;
                                mr = Clamp01(d.R); mg = Clamp01(d.G); mb = Clamp01(d.B);
                            }
                        }
                    }
                    catch { }
                }

                // Diffuse texture + UV channel for this mesh (if any).
                // glTF direct match first (name, then index); else Assimp slot.
                Bitmap meshTex = null;
                List<Vector3D> uvChan = null;
                int uvChannel = 0;
                float fr = 1f, fg = 1f, fb = 1f;
                bool aTest = false;
                float cut = 0.5f;
                GlbMaterial gmat = null;
                if (glb != null)
                {
                    try
                    {
                        var mats0 = scene.Materials;
                        string mname = null;
                        if (mats0 != null && mesh.MaterialIndex >= 0 && mesh.MaterialIndex < mats0.Count)
                            mname = mats0[mesh.MaterialIndex].Name;
                        if (!string.IsNullOrEmpty(mname) && glb.ByName.ContainsKey(mname))
                            gmat = glb.ByName[mname];
                        else if (mesh.MaterialIndex >= 0 && mesh.MaterialIndex < glb.Ordered.Count)
                            gmat = glb.Ordered[mesh.MaterialIndex];
                    }
                    catch { gmat = null; }
                }
                if (gmat != null && gmat.Tex != null)
                {
                    meshTex = gmat.Tex;
                    uvChannel = Math.Max(0, gmat.TexCoord);
                    fr = gmat.Fr; fg = gmat.Fg; fb = gmat.Fb;
                    aTest = gmat.AlphaTest; cut = gmat.Cutoff;
                }
                else
                {
                    try
                    {
                        var mats0 = scene.Materials;
                        if (mats0 != null && mesh.MaterialIndex >= 0 && mesh.MaterialIndex < mats0.Count)
                        {
                            var sl = mats0[mesh.MaterialIndex].GetMaterialTextures(TextureType.Diffuse);
                            if (sl != null && sl.Length > 0) uvChannel = Math.Max(0, sl[0].UVIndex);
                        }
                    }
                    catch { }
                    try { texByMaterial.TryGetValue(mesh.MaterialIndex, out meshTex); }
                    catch { meshTex = null; }
                }
                try
                {
                    var chs = mesh.TextureCoordinateChannels;
                    int ch = uvChannel;
                    if (chs == null || ch < 0 || ch >= chs.Length || chs[ch] == null || chs[ch].Count != mesh.VertexCount) ch = 0;
                    if (meshTex != null && chs != null && ch >= 0 && ch < chs.Length && chs[ch] != null && chs[ch].Count == mesh.VertexCount
                        && mesh.UVComponentCount != null && mesh.UVComponentCount.Length > ch && mesh.UVComponentCount[ch] >= 2)
                    {
                        uvChan = chs[ch];
                        texturedMeshes++;
                    }
                    else meshTex = null;
                }
                catch { meshTex = null; uvChan = null; }

                foreach (var face in mesh.Faces)
                {
                    try
                    {
                        if (face == null || face.IndexCount != 3) continue;
                        int i0 = face.Indices[0], i1 = face.Indices[1], i2 = face.Indices[2];
                        if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= mesh.VertexCount || i1 >= mesh.VertexCount || i2 >= mesh.VertexCount) continue;

                        var a = verts[i0]; var b = verts[i1]; var c = verts[i2];
                        float ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z;
                        float vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
                        float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                        double area = 0.5 * Math.Sqrt((double)nx * nx + (double)ny * ny + (double)nz * nz);
                        if (area < 1e-12) continue;

                        float fnx = (float)(nx / (2.0 * area)), fny = (float)(ny / (2.0 * area)), fnz = (float)(nz / (2.0 * area));
                        float r = mr, g = mg, bl = mb;
                        bool perVert = useVc;
                        float nax = 0, nay = 0, naz = 0, nbx = 0, nby = 0, nbz = 0, ncx = 0, ncy = 0, ncz = 0;
                        float ar = 0, ag = 0, ab = 0, br = 0, bg = 0, bb = 0, cr = 0, cg = 0, cb = 0;
                        if (norms != null && norms.Count == mesh.VertexCount)
                        {
                            nax = norms[i0].X; nay = norms[i0].Y; naz = norms[i0].Z;
                            nbx = norms[i1].X; nby = norms[i1].Y; nbz = norms[i1].Z;
                            ncx = norms[i2].X; ncy = norms[i2].Y; ncz = norms[i2].Z;
                        }
                        else { nax = nbx = ncx = fnx; nay = nby = ncy = fny; naz = nbz = ncz = fnz; }
                        if (perVert)
                        {
                            try
                            {
                                var chan = mesh.VertexColorChannels[0];
                                var ca = chan[i0]; var cb2 = chan[i1]; var cc = chan[i2];
                                ar = Clamp01(ca.R); ag = Clamp01(ca.G); ab = Clamp01(ca.B);
                                br = Clamp01(cb2.R); bg = Clamp01(cb2.G); bb = Clamp01(cb2.B);
                                cr = Clamp01(cc.R); cg = Clamp01(cc.G); cb = Clamp01(cc.B);
                            }
                            catch { perVert = false; }
                        }

                        float ua = 0, va = 0, ub = 0, vb = 0, uc = 0, vc = 0;
                        bool hasUv = false;
                        if (uvChan != null)
                        {
                            try
                            {
                                ua = uvChan[i0].X; va = uvChan[i0].Y;
                                ub = uvChan[i1].X; vb = uvChan[i1].Y;
                                uc = uvChan[i2].X; vc = uvChan[i2].Y;
                                hasUv = true;
                            }
                            catch { hasUv = false; }
                        }

                        tris.Add(new Tri
                        {
                            Ax = a.X, Ay = a.Y, Az = a.Z, Bx = b.X, By = b.Y, Bz = b.Z, Cx = c.X, Cy = c.Y, Cz = c.Z,
                            Nx = fnx, Ny = fny, Nz = fnz,
                            R = r, G = g, B = bl, Area = (float)area,
                            Nax = nax, Nay = nay, Naz = naz, Nbx = nbx, Nby = nby, Nbz = nbz, Ncx = ncx, Ncy = ncy, Ncz = ncz,
                            Ar = ar, Ag = ag, Ab = ab, Br = br, Bg = bg, Bb = bb, Cr = cr, Cg = cg, Cb = cb,
                            HasVc = perVert,
                            Ua = ua, Va = va, Ub = ub, Vb = vb, Uc = uc, Vc = vc,
                            HasUv = hasUv,
                            Tex = hasUv ? meshTex : null,
                            Fr = fr, Fg = fg, Fb = fb,
                            AlphaTest = aTest,
                            Cutoff = cut,
                        });
                        totalArea += area;
                    }
                    catch { }
                }
            }

            if (tris.Count == 0)
                throw new Exception("Assimp found meshes but no usable triangles.");

            try
            {
                return SampleTris(tris, path, texturedMeshes, meshCount);
            }
            finally
            {
                foreach (var bmp in texByMaterial.Values)
                {
                    if (bmp == null) continue;
                    try { bmp.Dispose(); } catch { }
                }
                foreach (var bmp in glbOwned)
                {
                    if (bmp == null) continue;
                    try { bmp.Dispose(); } catch { }
                }
            }
        }

        /// <summary>
        /// Surface-sample triangles into splats. Shared by the Assimp path and
        /// the legacy FBX 6.1 ASCII fallback. Unlocks texture locks on the way
        /// out; bitmap ownership stays with the caller.
        /// </summary>
        internal static PlySplatModel SampleTris(List<Tri> tris, string path, int texturedMeshes, int meshCount)
        {
            if (tris == null || tris.Count == 0)
                throw new Exception("No usable triangles.");
            double totalArea = 0;
            foreach (var t in tris) totalArea += t.Area;
            if (totalArea <= 0)
                throw new Exception("Degenerate geometry.");

            // Lock each used texture once for fast sampling.
            var texCache = new Dictionary<Bitmap, TexData>();
            try
            {
                foreach (var t in tris)
                {
                    if (t.HasUv && t.Tex != null && !texCache.ContainsKey(t.Tex))
                    {
                        try { texCache[t.Tex] = LockTexture(t.Tex); }
                        catch { }
                    }
                }

                LastTextureReport = string.Format("{0}/{1} meshes textured.", texturedMeshes, meshCount);

            var model = new PlySplatModel { SourcePath = path };
            var rng = new Random(1234);
            // Two-pass allocation: every triangle gets a minimum share so thin
            // parts (legs, antennae) are covered, then scaled to the cap.
            int minPerTri = tris.Count < 50000 ? 3 : 1;
            var counts = new int[tris.Count];
            long total = 0;
            for (int ti = 0; ti < tris.Count; ti++)
            {
                int n = Math.Max(minPerTri, (int)Math.Round(TargetPoints * (tris[ti].Area / totalArea)));
                counts[ti] = n;
                total += n;
            }
            if (total > MaxPoints)
            {
                double f = (double)MaxPoints / total;
                total = 0;
                for (int ti = 0; ti < counts.Length; ti++)
                {
                    counts[ti] = Math.Max(1, (int)(counts[ti] * f));
                    total += counts[ti];
                }
            }
            for (int ti = 0; ti < tris.Count; ti++)
            {
                var t = tris[ti];
                int n = counts[ti];
                for (int k = 0; k < n; k++)
                {
                    if (model.Points.Count >= MaxPoints) break;

                    double r1 = rng.NextDouble(), r2 = rng.NextDouble();
                    double sr1 = Math.Sqrt(r1);
                    float w0 = (float)(1.0 - sr1), w1 = (float)(sr1 * (1.0 - r2)), w2 = (float)(sr1 * r2);

                    float x = w0 * t.Ax + w1 * t.Bx + w2 * t.Cx;
                    float y = w0 * t.Ay + w1 * t.By + w2 * t.Cy;
                    float z = w0 * t.Az + w1 * t.Bz + w2 * t.Cz;

                    float nx = w0 * t.Nax + w1 * t.Nbx + w2 * t.Ncx;
                    float ny = w0 * t.Nay + w1 * t.Nby + w2 * t.Ncy;
                    float nz = w0 * t.Naz + w1 * t.Nbz + w2 * t.Ncz;
                    double nl = Math.Sqrt((double)nx * nx + (double)ny * ny + (double)nz * nz);
                    if (nl < 1e-9) { nx = t.Nx; ny = t.Ny; nz = t.Nz; }
                    else { nx = (float)(nx / nl); ny = (float)(ny / nl); nz = (float)(nz / nl); }

                    // Base = vertex color (or flat material color), times texture.
                    float r, g, bl;
                    if (t.HasVc)
                    {
                        r = w0 * t.Ar + w1 * t.Br + w2 * t.Cr;
                        g = w0 * t.Ag + w1 * t.Bg + w2 * t.Cg;
                        bl = w0 * t.Ab + w1 * t.Bb + w2 * t.Cb;
                    }
                    else { r = t.R; g = t.G; bl = t.B; }

                    TexData td = null;
                    if (t.HasUv && t.Tex != null)
                        texCache.TryGetValue(t.Tex, out td);
                    float ta = 1f;
                    if (td != null)
                    {
                        float u = w0 * t.Ua + w1 * t.Ub + w2 * t.Uc;
                        float v = w0 * t.Va + w1 * t.Vb + w2 * t.Vc;
                        float tr, tg, tb;
                        SampleTex(td, u, v, out tr, out tg, out tb, out ta);
                        r *= tr; g *= tg; bl *= tb;
                    }
                    // Alpha-cutout discard (MASK materials): skip transparent texels.
                    if (t.AlphaTest && ta < t.Cutoff) continue;
                    r *= t.Fr; g *= t.Fg; bl *= t.Fb;

                    // Size each splat to cover its territory with real overlap
                    // (gaussians, not plot points): sigma ~ 1x local spacing.
                    // Raw model units here; Normalize() scales into view space
                    // (and clamps) for us, so do NOT pre-clamp.
                    // Y handling: Normalize() assumes Y-down file data (it
                    // negates Y), but mesh data is Y-up, so pre-negate Y and
                    // normal-Y below - the double negation restores Y-up.
                    float sigma = (float)Math.Sqrt(t.Area / Math.Max(1, n));
                    model.Points.Add(new SplatPoint
                    {
                        X = x, Y = -y, Z = z,
                        R = ToByte(r), G = ToByte(g), B = ToByte(bl),
                        S0 = sigma, S1 = sigma, S2 = sigma,
                        Qw = 1, Qx = 0, Qy = 0, Qz = 0,
                        Opacity = 1f,
                        Nx = nx, Ny = -ny, Nz = nz
                    });
                }
                if (model.Points.Count >= MaxPoints) break;
            }

            if (model.Points.Count == 0)
                throw new Exception("Surface sampling produced no points.");
            model.Normalize();
            return model;
            }
            finally
            {
                // Unlock texture locks (bitmap ownership stays with the caller).
                foreach (var kvp in texCache)
                {
                    try
                    {
                        TexData td = kvp.Value;
                        if (td != null && td.Bmp != null && td.Lock != null)
                            td.Bmp.UnlockBits(td.Lock);
                    }
                    catch { }
                }
            }
        }

        private static bool IsLegacyFbx61(string path)
        {
            try
            {
                using (var sr = new StreamReader(path))
                {
                    for (int i = 0; i < 4; i++)
                    {
                        string line = sr.ReadLine();
                        if (line == null) break;
                        if (line.IndexOf("Kaydara FBX Binary", StringComparison.Ordinal) >= 0) return false;
                        if (line.Length > 0 && line[0] == ';' && line.IndexOf("FBX", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Resolve a material's diffuse texture: embedded blob first, else an
        /// external file next to the model. Returns null when unusable.
        /// </summary>
        private static Bitmap ResolveDiffuseTexture(Scene scene, Material mat, string modelDir)
        {
            if (mat == null) return null;
            TextureSlot[] slots = null;
            try { slots = mat.GetMaterialTextures(TextureType.Diffuse); }
            catch { return null; }
            if (slots == null) return null;
            foreach (var slot in slots)
            {
                if (string.IsNullOrEmpty(slot.FilePath)) continue;
                try
                {
                    string tp = slot.FilePath.Trim();
                    // Embedded blob referenced as "*N".
                    if (tp.StartsWith("*"))
                    {
                        int idx;
                        if (!int.TryParse(tp.Substring(1), out idx)) continue;
                        if (scene.Textures == null || idx < 0 || idx >= scene.Textures.Count) continue;
                        var et = scene.Textures[idx];
                        if (et == null) continue;
                        if (et.HasCompressedData && et.CompressedData != null && et.CompressedData.Length > 0)
                        {
                            using (var ms = new MemoryStream(et.CompressedData))
                            using (var raw = new Bitmap(ms))
                                return ToCanonical(raw);
                        }
                        if (et.HasNonCompressedData && et.NonCompressedData != null && et.Width > 0 && et.Height > 0)
                        {
                            var bmp = new Bitmap(et.Width, et.Height, PixelFormat.Format32bppArgb);
                            var texels = et.NonCompressedData;
                            for (int y = 0; y < et.Height && y * et.Width < texels.Length; y++)
                            {
                                for (int x = 0; x < et.Width; x++)
                                {
                                    var t = texels[y * et.Width + x];
                                    bmp.SetPixel(x, y, Color.FromArgb(t.A, t.R, t.G, t.B));
                                }
                            }
                            var canon = ToCanonical(bmp);
                            bmp.Dispose();
                            return canon;
                        }
                        continue;
                    }
                    // External file.
                    var found = FindExternalTexture(modelDir, tp);
                    if (found != null) return found;
                }
                catch { }
            }
            return null;
        }

        internal static Bitmap FindExternalTexture(string modelDir, string texPath)
        {
            try
            {
                string norm = texPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                var candidates = new List<string>();
                if (Path.IsPathRooted(norm)) candidates.Add(norm);
                if (!string.IsNullOrEmpty(modelDir))
                {
                    candidates.Add(Path.Combine(modelDir, norm));
                    string file = Path.GetFileName(norm);
                    string noExt = Path.GetFileNameWithoutExtension(norm);
                    string[] dirs = { modelDir, Path.Combine(modelDir, "textures"), Path.Combine(modelDir, "texture"), Path.Combine(modelDir, "tex") };
                    if (!string.IsNullOrEmpty(Path.GetExtension(norm)))
                    {
                        foreach (var d in dirs) candidates.Add(Path.Combine(d, file));
                    }
                    else
                    {
                        string[] exts = { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".tif", ".tiff", ".dds", ".webp" };
                        foreach (var d in dirs)
                            foreach (var e in exts)
                                candidates.Add(Path.Combine(d, noExt + e));
                    }
                }
                foreach (var c in candidates)
                {
                    try
                    {
                        if (File.Exists(c))
                        {
                            byte[] bytes = File.ReadAllBytes(c);
                            using (var ms = new MemoryStream(bytes, false))
                            using (var raw = new Bitmap(ms))
                                return ToCanonical(raw);
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        private static Bitmap ToCanonical(Bitmap src)
        {
            if (src.PixelFormat == PixelFormat.Format32bppArgb)
                return new Bitmap(src);
            var c = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(c))
                g.DrawImage(src, 0, 0, src.Width, src.Height);
            return c;
        }

        private static TexData LockTexture(Bitmap bmp)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var ld = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int bytes = Math.Abs(ld.Stride) * bmp.Height;
            var px = new byte[bytes];
            System.Runtime.InteropServices.Marshal.Copy(ld.Scan0, px, 0, bytes);
            return new TexData { Bmp = bmp, Lock = ld, Px = px, Stride = ld.Stride, W = bmp.Width, H = bmp.Height };
        }

        /// <summary>Bilinear sample, clamp wrap. Bitmap rows are top-down, UV v is up.</summary>
        private static void SampleTex(TexData t, float u, float v, out float r, out float g, out float b, out float a)
        {
            if (u < 0f) u = 0f; else if (u > 1f) u = 1f;
            if (v < 0f) v = 0f; else if (v > 1f) v = 1f;
            float x = u * (t.W - 1);
            float y = (1f - v) * (t.H - 1);
            int x0 = (int)x, y0 = (int)y;
            int x1 = Math.Min(x0 + 1, t.W - 1), y1 = Math.Min(y0 + 1, t.H - 1);
            float fx = x - x0, fy = y - y0;
            int s = t.Stride;
            float b00 = t.Px[y0 * s + x0 * 4] / 255f, g00 = t.Px[y0 * s + x0 * 4 + 1] / 255f, r00 = t.Px[y0 * s + x0 * 4 + 2] / 255f, a00 = t.Px[y0 * s + x0 * 4 + 3] / 255f;
            float b10 = t.Px[y0 * s + x1 * 4] / 255f, g10 = t.Px[y0 * s + x1 * 4 + 1] / 255f, r10 = t.Px[y0 * s + x1 * 4 + 2] / 255f, a10 = t.Px[y0 * s + x1 * 4 + 3] / 255f;
            float b01 = t.Px[y1 * s + x0 * 4] / 255f, g01 = t.Px[y1 * s + x0 * 4 + 1] / 255f, r01 = t.Px[y1 * s + x0 * 4 + 2] / 255f, a01 = t.Px[y1 * s + x0 * 4 + 3] / 255f;
            float b11 = t.Px[y1 * s + x1 * 4] / 255f, g11 = t.Px[y1 * s + x1 * 4 + 1] / 255f, r11 = t.Px[y1 * s + x1 * 4 + 2] / 255f, a11 = t.Px[y1 * s + x1 * 4 + 3] / 255f;
            b = b00 + (b10 - b00) * fx + (b01 - b00) * fy + (b00 - b10 - b01 + b11) * fx * fy;
            g = g00 + (g10 - g00) * fx + (g01 - g00) * fy + (g00 - g10 - g01 + g11) * fx * fy;
            r = r00 + (r10 - r00) * fx + (r01 - r00) * fy + (r00 - r10 - r01 + r11) * fx * fy;
            a = a00 + (a10 - a00) * fx + (a01 - a00) * fy + (a00 - a10 - a01 + a11) * fx * fy;
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        private static byte ToByte(float v)
        {
            if (v <= 0f) return 0;
            if (v >= 1f) return 255;
            return (byte)(v * 255f);
        }

        private static float ClampSigma(float v)
        {
            if (v < 0.0005f) return 0.0005f;
            if (v > 0.35f) return 0.35f;
            return v;
        }
    }
}
