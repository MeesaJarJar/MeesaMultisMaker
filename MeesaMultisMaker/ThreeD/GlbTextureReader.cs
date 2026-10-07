using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace MeesaMultisMaker.ThreeD
{
    /// <summary>
    /// Reads diffuse/base-color textures from glTF 2.0 files directly instead
    /// of relying on Assimp (whose 5.0-era glTF importer drops embedded
    /// images and leaves empty texture paths). Supports .glb (embedded BIN
    /// chunk) and .gltf (external URIs + data: URIs). Keyed by material name,
    /// with index-order fallback. Bitmaps are caller-owned.
    /// </summary>
    public class GlbMaterial
    {
        public string Name = "";
        public Bitmap Tex;
        public float Fr = 1f, Fg = 1f, Fb = 1f;
        public int TexCoord;
        public bool AlphaTest;
        public float Cutoff = 0.5f;
    }

    public class GlbModel
    {
        public List<GlbMaterial> Ordered = new List<GlbMaterial>();
        public Dictionary<string, GlbMaterial> ByName =
            new Dictionary<string, GlbMaterial>(StringComparer.Ordinal);
    }

    public static class GlbTextureReader
    {
        public static GlbModel Load(string path)
        {
            string ext = "";
            try { ext = Path.GetExtension(path).ToLowerInvariant(); }
            catch { }
            string modelDir = "";
            try { modelDir = Path.GetDirectoryName(Path.GetFullPath(path)); }
            catch { modelDir = ""; }

            Dictionary<string, object> root;
            byte[] bin = null;
            if (ext == ".glb")
            {
                byte[] all = File.ReadAllBytes(path);
                if (all.Length < 12) throw new Exception("Not a GLB file.");
                string magic = Encoding.ASCII.GetString(all, 0, 4);
                if (magic != "glTF") throw new Exception("Not a GLB file.");
                int pos = 12;
                string json = null;
                while (pos + 8 <= all.Length)
                {
                    int len = BitConverter.ToInt32(all, pos);
                    uint type = BitConverter.ToUInt32(all, pos + 4);
                    pos += 8;
                    if (len < 0 || pos + len > all.Length) break;
                    if (type == 0x4E4F534A) json = Encoding.UTF8.GetString(all, pos, len);
                    else if (type == 0x004E4942 && bin == null)
                    {
                        bin = new byte[len];
                        Buffer.BlockCopy(all, pos, bin, 0, len);
                    }
                    pos += len;
                }
                if (json == null) throw new Exception("GLB has no JSON chunk.");
                root = ParseJson(json);
            }
            else
            {
                root = ParseJson(File.ReadAllText(path));
            }

            var images = GetList(root, "images");
            var textures = GetList(root, "textures");
            var materials = GetList(root, "materials");
            var bufferViews = GetList(root, "bufferViews");

            // Decode every image once.
            var decoded = new Dictionary<int, Bitmap>();
            try
            {
                for (int i = 0; i < images.Count; i++)
                {
                    try
                    {
                        var img = images[i] as Dictionary<string, object>;
                        if (img == null) continue;
                        Bitmap bmp = null;
                        object uriObj;
                        if (img.TryGetValue("uri", out uriObj) && uriObj is string)
                        {
                            bmp = LoadImageUri((string)uriObj, modelDir);
                        }
                        else if (img.ContainsKey("bufferView"))
                        {
                            int bv = ToInt(img["bufferView"]);
                            int byteOffset = img.ContainsKey("byteOffset") ? ToInt(img["byteOffset"]) : 0;
                            if (bin != null && bv >= 0 && bv < bufferViews.Count)
                            {
                                var view = bufferViews[bv] as Dictionary<string, object>;
                                if (view != null)
                                {
                                    int viewOff = view.ContainsKey("byteOffset") ? ToInt(view["byteOffset"]) : 0;
                                    int viewLen = view.ContainsKey("byteLength") ? ToInt(view["byteLength"]) : 0;
                                    int off = viewOff + byteOffset;
                                    // bufferViews may address buffer 0 (the BIN chunk) only.
                                    int buf = view.ContainsKey("buffer") ? ToInt(view["buffer"]) : 0;
                                    if (buf == 0 && off >= 0 && viewLen > 0 && off + viewLen <= bin.Length)
                                    {
                                        byte[] slice = new byte[viewLen];
                                        Buffer.BlockCopy(bin, off, slice, 0, viewLen);
                                        bmp = DecodeBytes(slice);
                                    }
                                }
                            }
                        }
                        if (bmp != null) decoded[i] = bmp;
                    }
                    catch { }
                }

                var model = new GlbModel();
                for (int mi = 0; mi < materials.Count; mi++)
                {
                    try
                    {
                        var m = materials[mi] as Dictionary<string, object>;
                        if (m == null) continue;
                        var gm = new GlbMaterial();
                        object nm;
                        if (m.TryGetValue("name", out nm) && nm is string) gm.Name = (string)nm;

                        object am;
                        if (m.TryGetValue("alphaMode", out am) && am is string)
                            gm.AlphaTest = string.Equals((string)am, "MASK", StringComparison.OrdinalIgnoreCase);
                        object ac;
                        if (m.TryGetValue("alphaCutoff", out ac)) gm.Cutoff = ToFloat(ac, 0.5f);

                        object pbr;
                        if (m.TryGetValue("pbrMetallicRoughness", out pbr) && pbr is Dictionary<string, object>)
                        {
                            var pr = (Dictionary<string, object>)pbr;
                            object bf;
                            if (pr.TryGetValue("baseColorFactor", out bf) && bf is object[])
                            {
                                var bl = (object[])bf;
                                if (bl.Length > 0) gm.Fr = ToFloat(bl[0], 1f);
                                if (bl.Length > 1) gm.Fg = ToFloat(bl[1], 1f);
                                if (bl.Length > 2) gm.Fb = ToFloat(bl[2], 1f);
                            }
                            object bct;
                            if (pr.TryGetValue("baseColorTexture", out bct) && bct is Dictionary<string, object>)
                            {
                                var bt = (Dictionary<string, object>)bct;
                                object ti;
                                if (bt.TryGetValue("index", out ti))
                                {
                                    int texIdx = ToInt(ti);
                                    if (bt.ContainsKey("texCoord")) gm.TexCoord = ToInt(bt["texCoord"]);
                                    if (texIdx >= 0 && texIdx < textures.Count)
                                    {
                                        var tx = textures[texIdx] as Dictionary<string, object>;
                                        if (tx != null && tx.ContainsKey("source"))
                                        {
                                            int imgIdx = ToInt(tx["source"]);
                                            Bitmap found;
                                            if (decoded.TryGetValue(imgIdx, out found)) gm.Tex = found;
                                        }
                                    }
                                }
                            }
                        }
                        model.Ordered.Add(gm);
                        if (!string.IsNullOrEmpty(gm.Name) && !model.ByName.ContainsKey(gm.Name))
                            model.ByName[gm.Name] = gm;
                    }
                    catch { }
                }
                return model;
            }
            catch
            {
                foreach (var b in decoded.Values)
                {
                    try { if (b != null) b.Dispose(); } catch { }
                }
                throw;
            }
        }

        private static Bitmap LoadImageUri(string uri, string modelDir)
        {
            if (string.IsNullOrEmpty(uri)) return null;
            if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                int comma = uri.IndexOf(',');
                if (comma < 0) return null;
                byte[] bytes = Convert.FromBase64String(uri.Substring(comma + 1));
                return DecodeBytes(bytes);
            }
            string p = uri.Replace('/', Path.DirectorySeparatorChar);
            var candidates = new List<string>();
            if (Path.IsPathRooted(p)) candidates.Add(p);
            if (!string.IsNullOrEmpty(modelDir))
            {
                candidates.Add(Path.Combine(modelDir, p));
                candidates.Add(Path.Combine(modelDir, Path.GetFileName(p)));
            }
            foreach (var c in candidates)
            {
                try
                {
                    if (File.Exists(c)) return DecodeBytes(File.ReadAllBytes(c));
                }
                catch { }
            }
            return null;
        }

        private static Bitmap DecodeBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 16) return null;
            using (var ms = new MemoryStream(bytes, false))
            using (var raw = new Bitmap(ms))
            {
                if (raw.PixelFormat == PixelFormat.Format32bppArgb)
                    return new Bitmap(raw);
                var c = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(c))
                    g.DrawImage(raw, 0, 0, raw.Width, raw.Height);
                return c;
            }
        }

        private static Dictionary<string, object> ParseJson(string json)
        {
            var ser = new JavaScriptSerializer();
            ser.MaxJsonLength = int.MaxValue;
            var root = ser.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null) throw new Exception("Invalid JSON.");
            return root;
        }

        private static List<object> GetList(Dictionary<string, object> root, string key)
        {
            object v;
            if (!root.TryGetValue(key, out v) || v == null) return new List<object>();
            // JavaScriptSerializer yields object[] (never ArrayList).
            if (v is object[])
            {
                var r = new List<object>();
                foreach (var o in (object[])v) r.Add(o);
                return r;
            }
            if (v is ArrayList)
            {
                var r = new List<object>();
                foreach (var o in (ArrayList)v) r.Add(o);
                return r;
            }
            return new List<object>();
        }

        private static int ToInt(object o)
        {
            try { return Convert.ToInt32(o); }
            catch { return 0; }
        }

        private static float ToFloat(object o, float def)
        {
            try { return Convert.ToSingle(o); }
            catch { return def; }
        }
    }
}
