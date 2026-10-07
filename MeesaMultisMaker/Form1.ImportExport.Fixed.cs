using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using MeesaMultisMaker.ComfyUI;
using System.Drawing.Drawing2D;

namespace MeesaMultisMaker
{
    public partial class Form1
    {
        private void ResizeCanvas(int newWidth, int newHeight, bool pushUndo)
        {
            if (pushUndo) PushUndo();
            gridWidth = newWidth;
            gridHeight = newHeight;
            int canvasWidth = (gridWidth + gridHeight) * (TILE_WIDTH / 2) + 400;
            int canvasHeight = (gridWidth + gridHeight) * (TILE_HEIGHT / 2) + 200;
            designPictureBox.Width = canvasWidth;
            designPictureBox.Height = canvasHeight;
            designPictureBox.Invalidate();
        }

        private void BrowseButton_Click(object sender, EventArgs e)
        {
            using (var folderDialog = new FolderBrowserDialog())
            {
                folderDialog.Description = "Select the folder containing your UO art images";
                if (!string.IsNullOrEmpty(artFolderPath)) folderDialog.SelectedPath = artFolderPath;
                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    artFolderPath = folderDialog.SelectedPath;
                    Properties.Settings.Default.ArtFolderPath = artFolderPath;
                    Properties.Settings.Default.Save();
                    LoadArt();
                }
            }
        }

        private void CreateGridButton_Click(object sender, EventArgs e)
        {
            var w = (int)widthNumericUpDown.Value;
            var h = (int)heightNumericUpDown.Value;
            ResizeCanvas(w, h, pushUndo: true);
        }

        private void ExportButton_Click(object sender, EventArgs e)
        {
            if (placedObjects.Count == 0)
            {
                MessageBox.Show("No objects to export. Place some items on the canvas first.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var sb = new StringBuilder();
            foreach (var obj in placedObjects.OrderBy(o => o.GridX).ThenBy(o => o.GridY).ThenBy(o => o.Z))
            {
                var id = EnsureHexPrefix(obj.GraphicId);
                sb.AppendLine($"{id}\t{obj.GridX}\t{obj.GridY}\t{obj.Z}\t{obj.Flags}");
            }
            outputTextBox.Text = sb.ToString();
            using (var saveDialog = new SaveFileDialog())
            {
                saveDialog.Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*";
                saveDialog.DefaultExt = "txt";
                saveDialog.FileName = "multi.txt";
                if (saveDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        File.WriteAllText(saveDialog.FileName, sb.ToString());
                        MessageBox.Show($"Successfully exported {placedObjects.Count} items to {saveDialog.FileName}", "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error exporting: {ex.Message}", "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void ImportTextButton_Click(object sender, EventArgs e)
        {
            var text = outputTextBox.Text;
            if (string.IsNullOrWhiteSpace(text)) { MessageBox.Show("Paste the multi text into the output box, then click Import Text."); return; }
            PushUndo();
            placedObjects.Clear();
            var records = new List<(string id, int x, int y, int z, int flags)>();
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#")) continue;
                var content = trimmed;
                int commentIndex = content.IndexOf('#');
                if (commentIndex >= 0) content = content.Substring(0, commentIndex).Trim();
                if (string.IsNullOrWhiteSpace(content)) continue;
                var parts = content.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5) continue;
                int px, py, pz, flags;
                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out px)) continue;
                if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out py)) continue;
                if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out pz)) continue;
                if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out flags)) flags = 0;
                records.Add((parts[0], px, py, pz, flags));
            }
            if (records.Count == 0) { designPictureBox.Invalidate(); return; }

            int maxZ = 5;
            if (this.maxZTextBox != null)
            {
                if (!int.TryParse(this.maxZTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out maxZ))
                    maxZ = 5;
            }

            var filtered = records.Where(r => Math.Abs(r.z) <= maxZ).ToList();
            if (filtered.Count == 0)
            {
                RebuildLockLists();
                designPictureBox.Invalidate();
                return;
            }

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var rec in filtered)
            {
                if (rec.x < minX) minX = rec.x;
                if (rec.y < minY) minY = rec.y;
                if (rec.x > maxX) maxX = rec.x;
                if (rec.y > maxY) maxY = rec.y;
            }

            int shiftX = minX < 0 ? -minX : 0;
            int shiftY = minY < 0 ? -minY : 0;
            int newWidth = (maxX - minX + 1);
            int newHeight = (maxY - minY + 1);
            gridWidth = Math.Max(1, newWidth);
            gridHeight = Math.Max(1, newHeight);
            widthNumericUpDown.Value = Math.Min(widthNumericUpDown.Maximum, Math.Max(widthNumericUpDown.Minimum, gridWidth));
            heightNumericUpDown.Value = Math.Min(heightNumericUpDown.Maximum, Math.Max(heightNumericUpDown.Minimum, gridHeight));
            ResizeCanvas(gridWidth, gridHeight, pushUndo: false);
            int layer = 0;
            foreach (var rec in filtered)
            {
                string path;
                if (!TryResolveIdToPath(rec.id, out path)) continue;
                try
                {
                    var img = LoadImageUnlocked(path);
                    int gx = rec.x + shiftX;
                    int gy = rec.y + shiftY;
                    var iso = GridToIso(gx, gy);
                    placedObjects.Add(new PlacedObject { Image = img, GraphicId = EnsureHexPrefix(rec.id), GridX = gx, GridY = gy, Z = rec.z, Flags = rec.flags, IsoPosition = iso, Layer = layer++ });
                }
                catch { }
            }
            RebuildLockLists();
            designPictureBox.Invalidate();
        }

        public void ImportFromTextString(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action<string>(ImportFromTextString), text);
                return;
            }
            try
            {
                outputTextBox.Text = text;
                ImportTextButton_Click(this, EventArgs.Empty);
                this.Activate();
                designPictureBox.Select();
            }
            finally { }
        }

        /// <summary>
        /// Import multi data from the output textbox. Parses text in format:
        /// GraphicId   X   Y   Z   Flags   [Extra]
        /// e.g. "0x3E63   0   2  0  1  0"
        /// </summary>
        private void ImportMultiTextFromOutputBox()
        {
            var text = outputTextBox.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("The output textbox is empty.\n\nPaste multi text in the format:\n0x3E63   0   2  0  1  0", 
                    "Import Multi Text", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ImportMultiTextFromString(text);
        }

        /// <summary>
        /// Import multi data from text string with extended 6-column format support.
        /// Format: GraphicId   X   Y   Z   Flags   [Extra]
        /// Loads art from MUL files.
        /// </summary>
        private void ImportMultiTextFromString(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            var records = new List<(string id, int x, int y, int z, int flags)>();
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                    continue;

                // Skip common log messages
                if (trimmed.Contains("Scanning") || trimmed.Contains("...") || trimmed.Contains("Loading") || trimmed.Contains("items"))
                    continue;

                var content = trimmed;
                int commentIndex = content.IndexOf('#');
                if (commentIndex >= 0) content = content.Substring(0, commentIndex).Trim();
                if (string.IsNullOrWhiteSpace(content)) continue;

                var parts = content.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                // Need at least 5 parts: ID, X, Y, Z, Flags (6th is optional extra flag)
                if (parts.Length < 5) continue;

                // First part must look like a hex ID
                if (!parts[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase) && 
                    !System.Text.RegularExpressions.Regex.IsMatch(parts[0], @"^[0-9A-Fa-f]{4}$"))
                    continue;

                int px, py, pz, flags;
                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out px)) continue;
                if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out py)) continue;
                if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out pz)) continue;
                if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out flags)) flags = 0;

                records.Add((parts[0], px, py, pz, flags));
            }

            if (records.Count == 0)
            {
                MessageBox.Show("No valid items found to import.\n\nExpected format:\n0x3E63   0   2  0  1  0", 
                    "Import Multi Text", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            PushUndo();
            placedObjects.Clear();

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var rec in records)
            {
                if (rec.x < minX) minX = rec.x;
                if (rec.y < minY) minY = rec.y;
                if (rec.x > maxX) maxX = rec.x;
                if (rec.y > maxY) maxY = rec.y;
            }

            int shiftX = minX < 0 ? -minX : 0;
            int shiftY = minY < 0 ? -minY : 0;
            int newWidth = (maxX - minX + 1);
            int newHeight = (maxY - minY + 1);

            gridWidth = Math.Max(1, newWidth);
            gridHeight = Math.Max(1, newHeight);
            widthNumericUpDown.Value = Math.Min(widthNumericUpDown.Maximum, Math.Max(widthNumericUpDown.Minimum, gridWidth));
            heightNumericUpDown.Value = Math.Min(heightNumericUpDown.Maximum, Math.Max(heightNumericUpDown.Minimum, gridHeight));
            ResizeCanvas(gridWidth, gridHeight, pushUndo: false);

            // Get MUL folder path
            var config = AppConfig.Instance;
            string mulFolderForImport = artFolderPath;
            if (string.IsNullOrEmpty(mulFolderForImport) || !Directory.Exists(mulFolderForImport))
            {
                mulFolderForImport = config.MulFolderPath;
            }

            string artMulPath = Path.Combine(mulFolderForImport ?? string.Empty, "art.mul");
            string artIdxPath = Path.Combine(mulFolderForImport ?? string.Empty, "artidx.mul");
            bool hasMul = !string.IsNullOrEmpty(mulFolderForImport) && File.Exists(artMulPath) && File.Exists(artIdxPath);

            // Detect art format (MUL, UOP, or Tecmo)
            var artFormat = hasMul ? ArtFileFormat.MulFiles : ArtFileFormat.None;
            if (!hasMul)
            {
                // Check for Tecmo format (artidx.mul with 0x40000 entries)
                if (File.Exists(artIdxPath))
                {
                    try
                    {
                        using (var idxStream = File.OpenRead(artIdxPath))
                        {
                            long count = idxStream.Length / 12;
                            if (count >= 0x40000)
                            {
                                hasMul = true;
                                artFormat = ArtFileFormat.TecmoExpanded;
                            }
                        }
                    }
                    catch { }
                }
            }
            
            if (!hasMul)
            {
                // Check for UOP
                string artUopPath = Path.Combine(mulFolderForImport ?? string.Empty, "artLegacyMUL.uop");
                if (File.Exists(artUopPath))
                {
                    hasMul = true;
                    artFormat = ArtFileFormat.UopOnly;
                }
            }

            if (!hasMul)
            {
                MessageBox.Show("Cannot import: No MUL or UOP files found.\n\nPlease configure the MUL folder path in Settings.",
                    "Import Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            int layer = 0;
            int loadedCount = 0;
            int failedCount = 0;

            foreach (var rec in records)
            {
                Image img = null;
                string idStr = rec.id.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? rec.id.Substring(2) : rec.id;

                if (ushort.TryParse(idStr, NumberStyles.HexNumber, null, out ushort itemId))
                {
                    try
                    {
                        if (artFormat == ArtFileFormat.MulFiles)
                            img = Mul.StaticArtReader.LoadStaticArt(mulFolderForImport, itemId);
                        else if (artFormat == ArtFileFormat.UopOnly)
                            img = Mul.UopArtReader.LoadStaticArt(mulFolderForImport, itemId);
                        else if (artFormat == ArtFileFormat.TecmoExpanded)
                            img = Mul.TecmoArtReader.LoadStaticArt(mulFolderForImport, itemId);
                    }
                    catch { }
                }

                int gx = rec.x + shiftX;
                int gy = rec.y + shiftY;
                var iso = GridToIso(gx, gy);

                placedObjects.Add(new PlacedObject
                {
                    Image = img,
                    GraphicId = EnsureHexPrefix(rec.id),
                    GridX = gx,
                    GridY = gy,
                    Z = rec.z,
                    Flags = rec.flags,
                    IsoPosition = iso,
                    Layer = layer++
                });
                loadedCount++;
            }

            RebuildLockLists();
            designPictureBox.Invalidate();

            string summaryMessage = failedCount == 0
                ? $"Successfully loaded all {loadedCount} items from text."
                : $"Loaded {loadedCount} items, but {failedCount} items failed to load.";
            
            // Clear the output and show summary
            outputTextBox.Clear();
            outputTextBox.AppendText(summaryMessage + "\r\n");
            
            MessageBox.Show(summaryMessage, "Import Multi Text", MessageBoxButtons.OK, 
                failedCount == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void ExportCanvasImage()
        {
            if (placedObjects.Count == 0)
            {
                MessageBox.Show("No objects to export. Place some items on the canvas first.", "Export Canvas", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = "PNG Image|*.png|All Files|*.*";
                sfd.DefaultExt = "png";
                sfd.FileName = $"canvas_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        RenderCanvasToImage().Save(sfd.FileName, ImageFormat.Png);
                        MessageBox.Show($"Successfully exported canvas to {sfd.FileName}", "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error exporting canvas: {ex.Message}", "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private Bitmap RenderCanvasToImage()
        {
            if (placedObjects.Count == 0)
                return new Bitmap(1, 1, PixelFormat.Format32bppArgb);

            int minX = int.MaxValue, minY = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue;

            foreach (var obj in placedObjects)
            {
                if (obj.Hidden || obj.Image == null) continue;
                Point iso = GridToIso(obj.GridX, obj.GridY);
                int w = obj.Image.Width;
                int h = obj.Image.Height;
                int zOffset = obj.Z * Z_PIXEL;
                int drawX = iso.X - w / 2;
                int drawY = iso.Y - h + (TILE_HEIGHT / 2) - zOffset;
                minX = Math.Min(minX, drawX);
                minY = Math.Min(minY, drawY);
                maxX = Math.Max(maxX, drawX + w);
                maxY = Math.Max(maxY, drawY + h);
            }

            int padding = 20;
            minX -= padding;
            minY -= padding;
            maxX += padding;
            maxY += padding;

            int width = maxX - minX;
            int height = maxY - minY;

            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;

                foreach (var obj in placedObjects.OrderBy(o => o.GridX + o.GridY).ThenBy(o => o.Z).ThenBy(o => o.Layer))
                {
                    if (obj.Hidden || obj.Image == null) continue;
                    int zOffset = obj.Z * Z_PIXEL;
                    Point iso = GridToIso(obj.GridX, obj.GridY);
                    int w = obj.Image.Width;
                    int h = obj.Image.Height;
                    int drawX = iso.X - w / 2 - minX;
                    int drawY = iso.Y - h + (TILE_HEIGHT / 2) - zOffset - minY;
                    g.DrawImage(obj.Image, new Rectangle(drawX, drawY, w, h));
                }
            }
            return bitmap;
        }

        private void ExportComponentsForAi()
        {
            if (placedObjects.Count == 0)
            {
                MessageBox.Show("No objects to export.", "Export Components", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select a folder to export per-component PNGs and manifest";
                if (fbd.ShowDialog() != DialogResult.OK) return;
                var targetRoot = Path.Combine(fbd.SelectedPath, $"multi_export_{DateTime.Now:yyyyMMdd_HHmmss}");
                Directory.CreateDirectory(targetRoot);
                var manifestLines = new List<string>();
                manifestLines.Add("# AI Component Manifest (TSV)");
                manifestLines.Add($"# generatedUtc={DateTime.UtcNow:o}");
                manifestLines.Add("file\tgraphicId\tgridX\tgridY\tz\tlayer\tflags\twidth\theight");
                int idx = 0;
                foreach (var obj in placedObjects.OrderBy(o => o.Layer).ThenBy(o => o.GridY).ThenBy(o => o.GridX))
                {
                    if (obj.Image == null) continue;
                    var safeId = EnsureHexPrefix(obj.GraphicId).Replace(":", "_").Replace("/", "_");
                    var fileName = $"{idx:D3}_{safeId}.png";
                    var savePath = Path.Combine(targetRoot, fileName);
                    using (var bmp = new Bitmap(obj.Image))
                    {
                        bmp.Save(savePath, ImageFormat.Png);
                    }
                    manifestLines.Add($"{fileName}\t{EnsureHexPrefix(obj.GraphicId)}\t{obj.GridX}\t{obj.GridY}\t{obj.Z}\t{obj.Layer}\t{obj.Flags}\t{obj.Image.Width}\t{obj.Image.Height}");
                    idx++;
                }
                var manifestPath = Path.Combine(targetRoot, "manifest.txt");
                File.WriteAllLines(manifestPath, manifestLines);
                MessageBox.Show($"Exported {idx} component(s) to:\n{targetRoot}", "Export Components", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ImportComponentsFromAi()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "Select manifest.txt";
                ofd.Filter = "Manifest (manifest.txt)|manifest.txt|Text Files (*.txt)|*.txt|All Files (*.*)|*.*";
                if (ofd.ShowDialog() != DialogResult.OK) return;
                var baseDir = Path.GetDirectoryName(ofd.FileName);
                var lines = File.ReadAllLines(ofd.FileName);
                var items = new List<AiComponentItem>();
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    var parts = line.Split('\t');
                    if (parts.Length < 9) continue;
                    int gx, gy, z, layer, flags, w, h;
                    if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out gx)) continue;
                    if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out gy)) continue;
                    if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out z)) continue;
                    if (!int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out layer)) continue;
                    if (!int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out flags)) flags = 0;
                    if (!int.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out w)) w = 0;
                    if (!int.TryParse(parts[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out h)) h = 0;
                    items.Add(new AiComponentItem
                    {
                        File = parts[0],
                        GraphicId = parts[1],
                        GridX = gx,
                        GridY = gy,
                        Z = z,
                        Layer = layer,
                        Flags = flags,
                        Width = w,
                        Height = h
                    });
                }
                if (items.Count == 0)
                {
                    MessageBox.Show("Manifest contained no items.", "Import Components", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                PushUndo();
                placedObjects.Clear();
                int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                foreach (var it in items)
                {
                    if (it.GridX < minX) minX = it.GridX;
                    if (it.GridY < minY) minY = it.GridY;
                    if (it.GridX > maxX) maxX = it.GridX;
                    if (it.GridY > maxY) maxY = it.GridY;
                }
                int shiftX = minX < 0 ? -minX : 0;
                int shiftY = minY < 0 ? -minY : 0;
                gridWidth = Math.Max(1, (maxX - minX + 1));
                gridHeight = Math.Max(1, (maxY - minY + 1));
                widthNumericUpDown.Value = Math.Min(widthNumericUpDown.Maximum, Math.Max(widthNumericUpDown.Minimum, gridWidth));
                heightNumericUpDown.Value = Math.Min(heightNumericUpDown.Maximum, Math.Max(heightNumericUpDown.Minimum, gridHeight));
                ResizeCanvas(gridWidth, gridHeight, pushUndo: false);
                int nextLayer = 0;
                foreach (var it in items.OrderBy(i => i.Layer).ThenBy(i => i.GridY).ThenBy(i => i.GridX))
                {
                    var filePath = Path.Combine(baseDir, it.File ?? string.Empty);
                    if (!File.Exists(filePath))
                    {
                        if (!TryResolveIdToPath(it.GraphicId, out filePath) || !File.Exists(filePath)) continue;
                    }
                    try
                    {
                        var img = LoadImageUnlocked(filePath);
                        int gx = it.GridX + shiftX;
                        int gy = it.GridY + shiftY;
                        var iso = GridToIso(gx, gy);
                        placedObjects.Add(new PlacedObject
                        {
                            Image = img,
                            GraphicId = EnsureHexPrefix(it.GraphicId),
                            GridX = gx,
                            GridY = gy,
                            Z = it.Z,
                            Flags = it.Flags,
                            IsoPosition = iso,
                            Layer = nextLayer++
                        });
                    }
                    catch { }
                }
                RebuildLockLists();
                designPictureBox.Invalidate();
                MessageBox.Show($"Imported {placedObjects.Count} component(s) from manifest.", "Import Components", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                    }
                                    ApplyAlphaMask(croppedAI, croppedOriginal);
                                }

                                using (var resizedBack = ResizeToExact(croppedAI, origW, origH))
                                {
                                    if (GetDropBlackPixelsEnabled())
                                    {
                                        DropBlackPixels(resizedBack, GetBlackPixelThreshold());
                                    }

                                    if (isInPlaceRegen && item.SourceObject != null)
                                    {
                                        if (item.SourceObject.OriginalImage == null)
                                        {
                                            item.SourceObject.OriginalImage = item.SourceObject.Image;
                                        }
                                        else
                                        {
                                            item.SourceObject.Image?.Dispose();
                                        }
                                        item.SourceObject.Image = new Bitmap(resizedBack);
                                        outputTextBox.AppendText($"Regenerated {item.GraphicId}\r\n");

                                        // Track as pending change for saving back to MUL/UOP
                                        try
                                        {
                                            string gidStr = (item.GraphicId ?? string.Empty).Trim();
                                            if (gidStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                                                gidStr = gidStr.Substring(2);
                                            if (ushort.TryParse(gidStr, System.Globalization.NumberStyles.HexNumber, null, out ushort gidVal))
                                            {
                                                // Pass a copy to avoid disposal issues
                                                MarkArtModified(gidVal, new Bitmap(resizedBack));
                                            }
                                        }
                                        catch { }
                                    }
                                    else
                                    {
                                        var outPath = Path.Combine(outputDir, item.File);
                                        resizedBack.Save(outPath, ImageFormat.Png);
                                        manifestOut.Add($"{item.File}\t{item.GraphicId}\t{item.GridX}\t{item.GridY}\t{item.Z}\t{item.Layer}\t{item.Flags}\t{origW}\t{origH}");
                                    }
                                }
                            }
                        }
                    }
                    success++;
                }
                catch (Exception ex)
                {
                    outputTextBox.AppendText($"Error on {item.GraphicId}: {ex.Message}\r\n");
                }
            }

            if (!isInPlaceRegen)
            {
                var manifestOutPath = Path.Combine(outputDir, "manifest.txt");
                File.WriteAllLines(manifestOutPath, manifestOut);
                SetAIStatus($"? Done! {success}/{items.Count} generated", Color.Green);
                outputTextBox.AppendText($"Batch img2img complete. {success}/{items.Count} generated.\nOutput: {outputDir}\r\n");
            }
            else
            {
                SetAIStatus($"? Done! {success}/{items.Count} regenerated", Color.Green);
            }
        }

        private async Task AIRegenSelected()
        {
            var selected = placedObjects.Where(o => selectedObjects.Contains(o)).ToList();
            if (selected.Count == 0)
            {
                SetAIStatus("No objects selected", Color.Orange);
                return;
            }

            var settings = GetAISettingsFromPanel();
            if (string.IsNullOrWhiteSpace(settings.Url))
            {
                SetAIStatus("Enter ComfyUI URL", Color.Red);
                return;
            }

            var items = new List<AiComponentItem>();
            foreach (var obj in selected)
            {
                if (obj.Image == null) continue;
                items.Add(new AiComponentItem
                {
                    File = null,
                    GraphicId = obj.GraphicId,
                    GridX = obj.GridX,
                    GridY = obj.GridY,
                    Z = obj.Z,
                    Layer = obj.Layer,
                    Flags = obj.Flags,
                    Width = obj.Image.Width,
                    Height = obj.Image.Height,
                    SourceObject = obj
                });
            }

            SetAIStatus($"Regenerating {items.Count} tiles...", Color.Blue);
            PushUndo();
            await ProcessBatchImg2Img(items, settings, null);
            designPictureBox.Invalidate();
        }

        private async Task AIRegenSelectedAsOne()
        {
            var selected = placedObjects.Where(o => selectedObjects.Contains(o)).ToList();
            if (selected.Count == 0)
            {
                SetAIStatus("No objects selected", Color.Orange);
                return;
            }

            var settings = GetAISettingsFromPanel();
            if (string.IsNullOrWhiteSpace(settings.Url))
            {
                SetAIStatus("Enter ComfyUI URL", Color.Red);
                return;
            }

            SetAIStatus("Processing as one image...", Color.Blue);
            PushUndo();

            try
            {
                int minX = int.MaxValue, minY = int.MaxValue;
                int maxX = int.MinValue, maxY = int.MinValue;
                var objectBounds = new Dictionary<PlacedObject, Rectangle>();

                foreach (var obj in selected)
                {
                    if (obj.Image == null) continue;
                    int isoX = (int)Math.Round((obj.GridX - obj.GridY) * (TILE_WIDTH / 2f));
                    int isoY = (int)Math.Round((obj.GridX + obj.GridY) * (TILE_HEIGHT / 2f));
                    int w = obj.Image.Width;
                    int h = obj.Image.Height;
                    int zOffset = obj.Z * Z_PIXEL;
                    int drawX = isoX - w / 2;
                    int drawY = isoY - h + (TILE_HEIGHT / 2) - zOffset;
                    objectBounds[obj] = new Rectangle(drawX, drawY, w, h);
                    minX = Math.Min(minX, drawX);
                    minY = Math.Min(minY, drawY);
                    maxX = Math.Max(maxX, drawX + w);
                    maxY = Math.Max(maxY, drawY + h);
                }

                if (objectBounds.Count == 0)
                {
                    SetAIStatus("No valid objects", Color.Orange);
                    return;
                }

                int compositeWidth = maxX - minX;
                int compositeHeight = maxY - minY;

                Bitmap originalComposite;
                using (var temp = new Bitmap(compositeWidth, compositeHeight, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(temp))
                    {
                        g.Clear(Color.Transparent);
                        g.CompositingMode = CompositingMode.SourceOver;
                        g.CompositingQuality = CompositingQuality.HighQuality;
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.None;

                        foreach (var kvp in objectBounds.OrderBy(k => k.Key.Layer))
                        {
                            var obj = kvp.Key;
                            var bounds = kvp.Value;
                            int relX = bounds.X - minX;
                            int relY = bounds.Y - minY;
                            g.DrawImage(obj.Image, relX, relY, bounds.Width, bounds.Height);
                        }
                    }
                    originalComposite = new Bitmap(temp);
                }

                int targetWidth = GetAIResolutionWidth();
                int targetHeight = GetAIResolutionHeight();
                int targetSize = Math.Max(targetWidth, targetHeight);
                var client = new ComfyUIClient(settings.Url);

                double scale = Math.Min((double)targetSize / originalComposite.Width, (double)targetSize / originalComposite.Height);
                int scaledW = Math.Max(1, (int)Math.Round(originalComposite.Width * scale));
                int scaledH = Math.Max(1, (int)Math.Round(originalComposite.Height * scale));
                int offsetX = (targetSize - scaledW) / 2;
                int offsetY = (targetSize - scaledH) / 2;

                using (originalComposite)
                using (var padded = ResizeToFitSquare(originalComposite, targetSize))
                using (var ms = new MemoryStream())
                {
                    padded.Save(ms, ImageFormat.Png);
                    var uploadBytes = ms.ToArray();

                    SetAIStatus("Uploading to ComfyUI...", Color.Blue);
                    var uploadedName = await client.UploadImage(uploadBytes, "composite_multi.png");

                    var workflow = Image2ImageWorkflow.CreateWorkflow(settings.Prompt, settings.Negative, uploadedName, targetWidth, targetHeight, settings.Steps, settings.Cfg, settings.Denoise, settings.Seed, settings.Sampler, settings.Scheduler);
                    var promptId = await client.QueuePrompt(workflow);
                    if (string.IsNullOrEmpty(promptId))
                    {
                        SetAIStatus("Failed to queue prompt", Color.Red);
                        return;
                    }

                    SetAIStatus("Generating...", Color.Blue);
                    outputTextBox.AppendText("Waiting for AI generation...\r\n");
                    var images = await client.GetGeneratedImages(promptId, maxAttempts: 60, pollIntervalMs: 1000);
                    if (images.Count == 0)
                    {
                        SetAIStatus("No images generated", Color.Orange);
                        return;
                    }

                    await Task.Delay(500);
                    var data = await client.DownloadImage(images[0]);
                    if (data == null || data.Length < 8 || data[0] != 0x89 || data[1] != 0x50)
                    {
                        SetAIStatus("Invalid PNG data", Color.Red);
                        return;
                    }

                    using (var msOut = new MemoryStream(data))
                    using (var genBmp = new Bitmap(msOut))
                    using (var croppedAI = new Bitmap(scaledW, scaledH, PixelFormat.Format32bppArgb))
                    {
                        using (var g = Graphics.FromImage(croppedAI))
                        {
                            g.InterpolationMode = InterpolationMode.NearestNeighbor;
                            g.PixelOffsetMode = PixelOffsetMode.None;
                            g.CompositingMode = CompositingMode.SourceCopy;
                            float scaleX = (float)genBmp.Width / targetSize;
                            float scaleY = (float)genBmp.Height / targetSize;
                            int cropX = (int)(offsetX * scaleX);
                            int cropY = (int)(offsetY * scaleY);
                            int cropW = (int)(scaledW * scaleX);
                            int cropH = (int)(scaledH * scaleY);
                            g.DrawImage(genBmp, new Rectangle(0, 0, scaledW, scaledH), new Rectangle(cropX, cropY, cropW, cropH), GraphicsUnit.Pixel);
                        }

                        using (var croppedOriginal = new Bitmap(scaledW, scaledH, PixelFormat.Format32bppArgb))
                        {
                            using (var g = Graphics.FromImage(croppedOriginal))
                            {
                                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                                g.PixelOffsetMode = PixelOffsetMode.None;
                                g.CompositingMode = CompositingMode.SourceCopy;
                                g.DrawImage(padded, new Rectangle(0, 0, scaledW, scaledH), new Rectangle(offsetX, offsetY, scaledW, scaledH), GraphicsUnit.Pixel);
                            }
                            ApplyAlphaMask(croppedAI, croppedOriginal);
                        }

                        using (var resizedComposite = ResizeToExact(croppedAI, compositeWidth, compositeHeight))
                        {
                            if (GetDropBlackPixelsEnabled())
                            {
                                DropBlackPixels(resizedComposite, GetBlackPixelThreshold());
                            }

                            foreach (var kvp in objectBounds)
                            {
                                var obj = kvp.Key;
                                var bounds = kvp.Value;
                                int relX = bounds.X - minX;
                                int relY = bounds.Y - minY;

                                var slice = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                                using (var g = Graphics.FromImage(slice))
                                {
                                    g.CompositingMode = CompositingMode.SourceCopy;
                                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                                    g.PixelOffsetMode = PixelOffsetMode.None;
                                    g.CompositingQuality = CompositingQuality.HighSpeed;
                                    g.DrawImage(resizedComposite, new Rectangle(0, 0, bounds.Width, bounds.Height), new Rectangle(relX, relY, bounds.Width, bounds.Height), GraphicsUnit.Pixel);
                                }

                                if (obj.OriginalImage == null)
                                {
                                    obj.OriginalImage = obj.Image;
                                }
                                else
                                {
                                    obj.Image?.Dispose();
                                }
                                obj.Image = slice;
                                outputTextBox.AppendText($"Replaced {obj.GraphicId}\r\n");

                                // Track as pending change for saving back to MUL/UOP
                                try
                                {
                                    string gidStr = (obj.GraphicId ?? string.Empty).Trim();
                                    if (gidStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                                        gidStr = gidStr.Substring(2);
                                    if (ushort.TryParse(gidStr, System.Globalization.NumberStyles.HexNumber, null, out ushort gidVal))
                                    {
                                        // Pass a copy to avoid disposal issues
                                        MarkArtModified(gidVal, new Bitmap(slice));
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }

                designPictureBox.Invalidate();
                SetAIStatus($"? Done! {objectBounds.Count} tiles regenerated", Color.Green);
            }
            catch (Exception ex)
            {
                SetAIStatus($"Error: {ex.Message}", Color.Red);
                outputTextBox.AppendText($"Error: {ex.Message}\r\n");
            }
        }

        private void DropBlackPixels(Bitmap image, int threshold)
        {
            if (image == null || threshold < 0) return;

            var rect = new Rectangle(0, 0, image.Width, image.Height);
            var data = image.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

            try
            {
                int bytes = Math.Abs(data.Stride) * image.Height;
                var buffer = new byte[bytes];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, bytes);

                int stride = data.Stride;
                int width = image.Width;
                int height = image.Height;

                for (int y = 0; y < height; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        int idx = row + x * 4;
                        byte b = buffer[idx];
                        byte g = buffer[idx + 1];
                        byte r = buffer[idx + 2];

                        if (r <= threshold && g <= threshold && b <= threshold)
                        {
                            buffer[idx] = 0;
                            buffer[idx + 1] = 0;
                            buffer[idx + 2] = 0;
                            buffer[idx + 3] = 0;
                        }
                    }
                }

                System.Runtime.InteropServices.Marshal.Copy(buffer, 0, data.Scan0, bytes);
            }
            finally
            {
                image.UnlockBits(data);
            }
        }

        private static Bitmap ResizeToFitSquare(Bitmap source, int targetSize)
        {
            if (source == null) return null;
            var result = new Bitmap(targetSize, targetSize, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                double scale = Math.Min((double)targetSize / source.Width, (double)targetSize / source.Height);
                int newW = Math.Max(1, (int)Math.Round(source.Width * scale));
                int newH = Math.Max(1, (int)Math.Round(source.Height * scale));
                int offsetX = (targetSize - newW) / 2;
                int offsetY = (targetSize - newH) / 2;
                var destRect = new Rectangle(offsetX, offsetY, newW, newH);
                g.DrawImage(source, destRect, new Rectangle(0, 0, source.Width, source.Height), GraphicsUnit.Pixel);
            }
            return result;
        }

        private static Bitmap ResizeToExact(Bitmap source, int targetWidth, int targetHeight)
        {
            if (source == null || targetWidth <= 0 || targetHeight <= 0) return source;
            var result = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.DrawImage(source, new Rectangle(0, 0, targetWidth, targetHeight), new Rectangle(0, 0, source.Width, source.Height), GraphicsUnit.Pixel);
            }
            return result;
        }

        private static void ApplyAlphaMask(Bitmap color, Bitmap alphaSource)
        {
            if (color == null || alphaSource == null) return;
            if (color.Width != alphaSource.Width || color.Height != alphaSource.Height) return;

            var rect = new Rectangle(0, 0, color.Width, color.Height);
            var colorData = color.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            var alphaData = alphaSource.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                int bytesC = Math.Abs(colorData.Stride) * color.Height;
                int bytesA = Math.Abs(alphaData.Stride) * alphaSource.Height;
                var bufferC = new byte[bytesC];
                var bufferA = new byte[bytesA];
                System.Runtime.InteropServices.Marshal.Copy(colorData.Scan0, bufferC, 0, bytesC);
                System.Runtime.InteropServices.Marshal.Copy(alphaData.Scan0, bufferA, 0, bytesA);

                int width = color.Width;
                int height = color.Height;
                int strideC = colorData.Stride;
                int strideA = alphaData.Stride;

                for (int y = 0; y < height; y++)
                {
                    int rowC = y * strideC;
                    int rowA = y * strideA;
                    for (int x = 0; x < width; x++)
                    {
                        int idxC = rowC + x * 4;
                        int idxA = rowA + x * 4;
                        bufferC[idxC + 3] = bufferA[idxA + 3];
                    }
                }

                System.Runtime.InteropServices.Marshal.Copy(bufferC, 0, colorData.Scan0, bytesC);
            }
            finally
            {
                color.UnlockBits(colorData);
                alphaSource.UnlockBits(alphaData);
            }
        }

        private void RevertAIChanges()
        {
            var selected = placedObjects.Where(o => selectedObjects.Contains(o)).ToList();
            if (selected.Count == 0)
            {
                SetAIStatus("No objects selected", Color.Orange);
                return;
            }

            int revertedCount = 0;
            PushUndo();

            foreach (var obj in selected)
            {
                if (obj.OriginalImage != null)
                {
                    obj.Image?.Dispose();
                    obj.Image = obj.OriginalImage;
                    obj.OriginalImage = null;
                    revertedCount++;
                    outputTextBox.AppendText($"Reverted {obj.GraphicId} to original\r\n");
                }
            }

            if (revertedCount == 0)
            {
                SetAIStatus("No AI changes to revert", Color.Orange);
            }
            else
            {
                designPictureBox.Invalidate();
                SetAIStatus($"? Reverted {revertedCount} object(s)", Color.Green);
            }
        }
    }
}
