using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    public partial class Form1
    {
        private static readonly JavaScriptSerializer _sceneJson = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        private static string GetSceneDefaultDir()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "MeesaMultisMaker",
                "Scenes");
        }

        private void SaveScene()
        {
            try
            {
                string defaultDir = GetSceneDefaultDir();
                Directory.CreateDirectory(defaultDir);
                using (var dlg = new SaveFileDialog())
                {
                    dlg.Title = "Save Scene";
                    dlg.Filter = "MMS Scene (*.mmscene)|*.mmscene";
                    dlg.DefaultExt = "mmscene";
                    dlg.FileName = "scene_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mmscene";
                    dlg.InitialDirectory = defaultDir;
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    WriteSceneFile(dlg.FileName);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save scene: " + ex.Message, "Save Scene", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadScene()
        {
            try
            {
                string defaultDir = GetSceneDefaultDir();
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Title = "Load Scene";
                    dlg.Filter = "MMS Scene (*.mmscene)|*.mmscene";
                    dlg.DefaultExt = "mmscene";
                    if (Directory.Exists(defaultDir)) dlg.InitialDirectory = defaultDir;
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    ReadSceneFile(dlg.FileName);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load scene: " + ex.Message, "Load Scene", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void WriteSceneFile(string filePath)
        {
            string artDir = Path.Combine(Path.GetDirectoryName(filePath), Path.GetFileNameWithoutExtension(filePath) + "_art");
            Directory.CreateDirectory(artDir);

            var keyByHash = new Dictionary<string, string>(StringComparer.Ordinal);
            var hashByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var usedKeys = new List<string>();
            var artImageByKey = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
            var artRows = new List<object>();
            var placementRows = new List<object>();

            foreach (var obj in placedObjects)
            {
                string artKey = null;
                if (obj.Image != null)
                {
                    string hash = ComputeSceneArtHash(obj.Image);
                    if (hash != null && !keyByHash.TryGetValue(hash, out artKey))
                    {
                        artKey = MakeUniqueSceneArtName(hash, hashByKey, keyByHash);
                        usedKeys.Add(artKey);
                        artImageByKey[artKey] = obj.Image;
                        artRows.Add(new Dictionary<string, object> { { "key", artKey } });
                    }
                }

                var row = new Dictionary<string, object>
                {
                    { "gid", obj.GraphicId ?? "" },
                    { "gx", obj.GridX },
                    { "gy", obj.GridY },
                    { "z", obj.Z },
                    { "layer", obj.Layer },
                    { "flags", obj.Flags },
                    { "hidden", obj.Hidden },
                    { "locked", lockedObjects.Contains(obj) },
                    { "scale", obj.Scale },
                    { "rotation", obj.Rotation },
                    { "flipH", obj.FlipHorizontal },
                    { "flipV", obj.FlipVertical },
                    { "offX", obj.PixelOffsetX },
                    { "offY", obj.PixelOffsetY },
                    { "artKey", artKey }
                };
                if (obj.HasSkew)
                {
                    row["skew"] = new Dictionary<string, object>
                    {
                        { "tl", ScenePointDict(obj.SkewTopLeft) },
                        { "tr", ScenePointDict(obj.SkewTopRight) },
                        { "br", ScenePointDict(obj.SkewBottomRight) },
                        { "bl", ScenePointDict(obj.SkewBottomLeft) }
                    };
                }
                placementRows.Add(row);
            }

            foreach (var key in usedKeys)
            {
                string artPath = Path.Combine(artDir, key);
                try
                {
                    new Bitmap(artImageByKey[key]).Save(artPath, ImageFormat.Png);
                }
                catch (Exception ex)
                {
                    throw new IOException("Could not write art file '" + key + "' for scene: " + ex.Message, ex);
                }
            }

            var root = new Dictionary<string, object>
            {
                { "schema", 1 },
                { "appVersion", UpdateChecker.CurrentVersionString },
                { "createdUtc", DateTime.UtcNow.ToString("o") },
                { "gridWidth", gridWidth },
                { "gridHeight", gridHeight },
                { "zoom", zoom },
                { "canvasOffsetX", canvasOffset.X },
                { "canvasOffsetY", canvasOffset.Y },
                { "art", artRows },
                { "placements", placementRows }
            };

            File.WriteAllText(filePath, _sceneJson.Serialize(root), new UTF8Encoding(false));
            outputTextBox.AppendText("Scene saved: " + Path.GetFileName(filePath) +
                " (" + placementRows.Count + " placements, " + artRows.Count + " art files)\r\n");
        }

        private void ReadSceneFile(string filePath)
        {
            string json;
            try
            {
                json = File.ReadAllText(filePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not read scene file: " + ex.Message, "Load Scene", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            object parsed;
            try
            {
                parsed = _sceneJson.DeserializeObject(json);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Not a valid scene file: " + ex.Message, "Load Scene", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var root = parsed as Dictionary<string, object>;
            if (root == null)
            {
                MessageBox.Show("Not a valid scene file (expected a JSON object).", "Load Scene", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            int schema = SceneInt(root, "schema", 0);
            if (schema != 1)
            {
                MessageBox.Show("Unsupported scene schema: " + schema + " (expected 1).", "Load Scene", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            List<object> artList = SceneList(SceneField(root, "art"));
            List<object> placements = SceneList(SceneField(root, "placements"));
            if (placements == null)
            {
                MessageBox.Show("Scene file has no placements list.", "Load Scene", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            PushUndo();

            placedObjects.Clear();
            selectedObjects.Clear();
            selectedObject = null;
            lockedObjects.Clear();
            floorsHiddenByToggle.Clear();
            ClearUnifyCandidates();
            if (hideFloorsActive)
            {
                hideFloorsActive = false;
                if (hideFloorsButton != null) hideFloorsButton.Text = "Hide Floors";
            }
            if (isSliceMode) ExitSliceMode();
            isSkewMode = false;
            skewDraggingCorner = -1;
            isRotateMode = false;
            isRotateDragging = false;

            gridWidth = Math.Max(1, SceneInt(root, "gridWidth", gridWidth));
            gridHeight = Math.Max(1, SceneInt(root, "gridHeight", gridHeight));
            widthNumericUpDown.Value = Math.Min(widthNumericUpDown.Maximum, Math.Max(widthNumericUpDown.Minimum, gridWidth));
            heightNumericUpDown.Value = Math.Min(heightNumericUpDown.Maximum, Math.Max(heightNumericUpDown.Minimum, gridHeight));
            ResizeCanvas(gridWidth, gridHeight, pushUndo: false);

            zoom = SceneFloat(root, "zoom", zoom);
            if (zoom < 0.2f) zoom = 0.2f;
            if (zoom > 4.0f) zoom = 4.0f;
            canvasOffset.X = SceneInt(root, "canvasOffsetX", canvasOffset.X);
            canvasOffset.Y = SceneInt(root, "canvasOffsetY", canvasOffset.Y);

            string sceneDir = Path.GetDirectoryName(filePath);
            string artDir = Path.Combine(sceneDir, Path.GetFileNameWithoutExtension(filePath) + "_art");
            var artInstanceByKey = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

            int loadedCount = 0;
            int fallbackCount = 0;
            int skippedCount = 0;

            foreach (var entry in placements)
            {
                var po = entry as Dictionary<string, object>;
                if (po == null)
                {
                    skippedCount++;
                    continue;
                }
                try
                {
                    string gid = SceneStr(po, "gid") ?? "";
                    int gx = SceneInt(po, "gx", 0);
                    int gy = SceneInt(po, "gy", 0);
                    int gz = SceneInt(po, "z", 0);
                    int layer = SceneInt(po, "layer", placedObjects.Count);
                    int flags = SceneInt(po, "flags", 0);
                    bool hidden = SceneBool(po, "hidden", false);

                    Image img = ResolveSceneArt(po, sceneDir, artDir, artInstanceByKey);
                    bool usedFallback = false;
                    if (img == null)
                    {
                        int gidVal;
                        if (TryParseGraphicId(gid, out gidVal) && gidVal >= 0 && gidVal <= 0xFFFF)
                        {
                            try
                            {
                                img = LoadStaticItemImage(gidVal);
                                usedFallback = img != null;
                            }
                            catch
                            {
                                img = null;
                            }
                        }
                        if (img == null)
                        {
                            skippedCount++;
                            outputTextBox.AppendText("  Scene: skipped " + gid + " at (" + gx + "," + gy + ") - art missing\r\n");
                            continue;
                        }
                    }

                    var placed = new PlacedObject
                    {
                        Image = img,
                        GraphicId = gid,
                        GridX = gx,
                        GridY = gy,
                        Z = gz,
                        Flags = flags,
                        IsoPosition = GridToIso(gx, gy),
                        Layer = layer,
                        Hidden = hidden,
                        Scale = SceneFloat(po, "scale", 1f),
                        Rotation = SceneFloat(po, "rotation", 0f),
                        FlipHorizontal = SceneBool(po, "flipH", false),
                        FlipVertical = SceneBool(po, "flipV", false),
                        PixelOffsetX = SceneInt(po, "offX", 0),
                        PixelOffsetY = SceneInt(po, "offY", 0),
                        SkewTopLeft = SceneSkewCorner(po, "tl"),
                        SkewTopRight = SceneSkewCorner(po, "tr"),
                        SkewBottomRight = SceneSkewCorner(po, "br"),
                        SkewBottomLeft = SceneSkewCorner(po, "bl")
                    };
                    if (SceneBool(po, "locked", false)) lockedObjects.Add(placed);
                    placedObjects.Add(placed);
                    loadedCount++;
                    if (usedFallback) fallbackCount++;
                }
                catch
                {
                    skippedCount++;
                }
            }

            RebuildLockLists();
            designPictureBox.Invalidate();

            outputTextBox.AppendText("Scene loaded: " + loadedCount + " placements, " +
                (artList == null ? 0 : artList.Count) + " art files, " +
                (fallbackCount + skippedCount) + " fallback/skipped\r\n");
        }

        private Image ResolveSceneArt(Dictionary<string, object> po, string sceneDir, string artDir, Dictionary<string, Image> cache)
        {
            string artKey = SceneStr(po, "artKey");
            if (string.IsNullOrWhiteSpace(artKey)) return null;

            Image cached;
            if (cache.TryGetValue(artKey, out cached)) return cached;

            string found = null;
            if (Path.IsPathRooted(artKey))
            {
                if (File.Exists(artKey)) found = artKey;
            }
            else
            {
                string p = Path.Combine(artDir, artKey);
                if (File.Exists(p))
                {
                    found = p;
                }
                else
                {
                    p = Path.Combine(sceneDir, artKey);
                    if (File.Exists(p)) found = p;
                }
            }
            if (found == null) return null;

            try
            {
                var img = LoadImageUnlocked(found);
                cache[artKey] = img;
                return img;
            }
            catch
            {
                return null;
            }
        }

        private static Dictionary<string, object> ScenePointDict(PointF p)
        {
            return new Dictionary<string, object>
            {
                { "x", p.X },
                { "y", p.Y }
            };
        }

        private static string MakeUniqueSceneArtName(string fullHashHex, Dictionary<string, string> hashByKey, Dictionary<string, string> keyByHash)
        {
            int len = 8;
            while (true)
            {
                if (len > fullHashHex.Length) len = fullHashHex.Length;
                string candidate = "img_" + fullHashHex.Substring(0, len).ToLowerInvariant() + ".png";
                string existing;
                if (!hashByKey.TryGetValue(candidate, out existing))
                {
                    hashByKey[candidate] = fullHashHex;
                    keyByHash[fullHashHex] = candidate;
                    return candidate;
                }
                if (string.Equals(existing, fullHashHex, StringComparison.Ordinal))
                {
                    keyByHash[fullHashHex] = candidate;
                    return candidate;
                }
                if (len >= fullHashHex.Length)
                    throw new IOException("Could not assign a unique art file name for the scene.");
                len += 8;
            }
        }

        private static string ComputeSceneArtHash(Image img)
        {
            try
            {
                if (img == null) return null;
                int w = img.Width;
                int h = img.Height;
                if (w <= 0 || h <= 0) return null;
                using (var bmp = new Bitmap(img))
                {
                    var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        int bytes = Math.Abs(data.Stride) * h;
                        var buffer = new byte[bytes];
                        Marshal.Copy(data.Scan0, buffer, 0, bytes);
                        using (var sha = SHA256.Create())
                        {
                            byte[] digest = sha.ComputeHash(buffer);
                            var sb = new StringBuilder(digest.Length * 2);
                            foreach (byte b in digest) sb.Append(b.ToString("x2"));
                            return sb.ToString();
                        }
                    }
                    finally
                    {
                        bmp.UnlockBits(data);
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private static object SceneField(Dictionary<string, object> dict, string key)
        {
            if (dict == null || key == null) return null;
            object exact;
            if (dict.TryGetValue(key, out exact)) return exact;
            foreach (var kvp in dict)
            {
                if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase)) return kvp.Value;
            }
            return null;
        }

        private static List<object> SceneList(object o)
        {
            if (o is object[]) return new List<object>((object[])o);
            if (o is List<object>) return (List<object>)o;
            return null;
        }

        private static bool TrySceneNum(object o, out double value)
        {
            if (o is int) { value = (int)o; return true; }
            if (o is long) { value = (long)o; return true; }
            if (o is double) { value = (double)o; return true; }
            if (o is float) { value = (float)o; return true; }
            if (o is decimal) { value = (double)(decimal)o; return true; }
            if (o is bool) { value = (bool)o ? 1d : 0d; return true; }
            var s = o as string;
            if (!string.IsNullOrWhiteSpace(s))
            {
                double d;
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                {
                    value = d;
                    return true;
                }
            }
            value = 0;
            return false;
        }

        private static int SceneInt(Dictionary<string, object> dict, string key, int fallback)
        {
            double v;
            if (TrySceneNum(SceneField(dict, key), out v)) return (int)Math.Round(v);
            return fallback;
        }

        private static float SceneFloat(Dictionary<string, object> dict, string key, float fallback)
        {
            double v;
            if (TrySceneNum(SceneField(dict, key), out v)) return (float)v;
            return fallback;
        }

        private static bool SceneBool(Dictionary<string, object> dict, string key, bool fallback)
        {
            object o = SceneField(dict, key);
            if (o is bool) return (bool)o;
            double v;
            if (TrySceneNum(o, out v)) return v != 0;
            var s = o as string;
            if (s != null)
            {
                if (s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
                if (s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            }
            return fallback;
        }

        private static string SceneStr(Dictionary<string, object> dict, string key)
        {
            return SceneField(dict, key) as string;
        }

        private static PointF SceneSkewCorner(Dictionary<string, object> po, string corner)
        {
            var skew = SceneField(po, "skew") as Dictionary<string, object>;
            var cornerDict = SceneField(skew, corner) as Dictionary<string, object>;
            if (cornerDict == null) return PointF.Empty;
            return new PointF(SceneFloat(cornerDict, "x", 0f), SceneFloat(cornerDict, "y", 0f));
        }
    }
}
