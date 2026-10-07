using MeesaMultisMaker.ComfyUI;
using MeesaMultisMaker.Mul;
using MeesaMultisMaker.AI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    public partial class Form1
    {
        /// <summary>
        /// Import multi text from an external source (e.g., MulViewer)
        /// This replaces the current canvas contents
        /// </summary>
        public void ImportMultiText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("No text to import.");
                return;
            }

            // Set the text in the output box and trigger import
            outputTextBox.Text = text;
            ImportTextButton_Click(this, EventArgs.Empty);

            // Bring main form to front
            this.BringToFront();
            this.Activate();
        }

        /// <summary>
        /// Add multi text to the existing canvas without clearing it
        /// This appends items to the current canvas, expanding if needed
        /// </summary>
        public void AddMultiText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("No text to add.");
                return;
            }

            PushUndo();

            var records = new List<(string id, int x, int y, int z, int flags)>();
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                    continue;

                var content = trimmed;
                int commentIndex = content.IndexOf('#');
                if (commentIndex >= 0) content = content.Substring(0, commentIndex).Trim();
                if (string.IsNullOrWhiteSpace(content)) continue;

                var parts = content.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 5)
                    continue;

                int px, py, pz, flags;
                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out px))
                    continue;
                if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out py))
                    continue;
                if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out pz))
                    continue;
                if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out flags))
                    flags = 0;

                records.Add((parts[0], px, py, pz, flags));
            }

            if (records.Count == 0)
            {
                MessageBox.Show("No valid items found to add.", "Add to Canvas", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int maxZ = 127;
            if (this.maxZTextBox != null && !string.IsNullOrWhiteSpace(this.maxZTextBox.Text))
            {
                if (!int.TryParse(this.maxZTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out maxZ))
                    maxZ = 127;
            }

            var filtered = records.Where(r => Math.Abs(r.z) <= maxZ).ToList();

            if (filtered.Count == 0)
            {
                MessageBox.Show($"All {records.Count} items were filtered out by Max Z Height ({maxZ}).", "Add to Canvas", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Find the bounds of the new items
            int newMinX = filtered.Min(r => r.x);
            int newMinY = filtered.Min(r => r.y);
            int newMaxX = filtered.Max(r => r.x);
            int newMaxY = filtered.Max(r => r.y);

            // Shift new items so they start at 0,0 if they have negative coords
            int shiftX = newMinX < 0 ? -newMinX : 0;
            int shiftY = newMinY < 0 ? -newMinY : 0;

            // Calculate offset to place new items after existing content
            int existingMaxX = 0;
            int existingMaxY = 0;
            if (placedObjects.Count > 0)
            {
                existingMaxX = placedObjects.Max(o => o.GridX) + 1;
                existingMaxY = placedObjects.Max(o => o.GridY) + 1;
            }

            int offsetX = existingMaxX;
            int offsetY = 0;

            // Calculate required canvas size
            int requiredWidth = Math.Max(gridWidth, offsetX + (newMaxX - newMinX + 1) + shiftX);
            int requiredHeight = Math.Max(gridHeight, offsetY + (newMaxY - newMinY + 1) + shiftY);

            if (requiredWidth > gridWidth || requiredHeight > gridHeight)
            {
                gridWidth = requiredWidth;
                gridHeight = requiredHeight;
                widthNumericUpDown.Value = Math.Min(widthNumericUpDown.Maximum, Math.Max(widthNumericUpDown.Minimum, gridWidth));
                heightNumericUpDown.Value = Math.Min(heightNumericUpDown.Maximum, Math.Max(heightNumericUpDown.Minimum, gridHeight));
                ResizeCanvas(gridWidth, gridHeight, pushUndo: false);
            }

            int startLayer = placedObjects.Count > 0 ? placedObjects.Max(o => o.Layer) + 1 : 0;
            int layer = startLayer;
            int loadedCount = 0;
            int failedCount = 0;

            string mulFolderForImport = AppConfig.Instance.FindMulFolder();
            if (string.IsNullOrEmpty(mulFolderForImport) || !Directory.Exists(mulFolderForImport))
                mulFolderForImport = artFolderPath;
            if (string.IsNullOrEmpty(mulFolderForImport) || !Directory.Exists(mulFolderForImport))
                mulFolderForImport = @"C:\Program Files (x86)\UOForever\UO";

            var artFormat = AppConfig.Instance.GetArtFileFormat(mulFolderForImport);
            bool hasArtFiles = artFormat != ArtFileFormat.None;

            if (!hasArtFiles && idToPath.Count == 0)
            {
                MessageBox.Show("Cannot add items: No MUL/UOP files found and no PNG art loaded.",
                    "Add to Canvas Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            foreach (var rec in filtered)
            {
                Image img = null;

                if (img == null && idToPath.Count > 0)
                {
                    string path;
                    if (TryResolveIdToPath(rec.id, out path) && !string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        try { img = LoadImageUnlocked(path); } catch { }
                    }
                }

                if (img == null && hasArtFiles)
{
                        try
                        {
                            string idStr = rec.id.Trim();
                            if (idStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                                idStr = idStr.Substring(2);

                            if (ushort.TryParse(idStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort tileId))
                            {
                                if (artFormat == ArtFileFormat.MulFiles)
                                    img = StaticArtReader.LoadStaticArt(mulFolderForImport, tileId);
                                else if (artFormat == ArtFileFormat.UopOnly)
                                    img = UopArtReader.LoadStaticArt(mulFolderForImport, tileId);
                                else if (artFormat == ArtFileFormat.TecmoExpanded)
                                    img = TecmoArtReader.LoadStaticArt(mulFolderForImport, tileId);
                            }
                        }
                        catch { }
                    }

                if (img == null) { failedCount++; continue; }

                try
                {
                    int gx = rec.x + shiftX + offsetX;
                    int gy = rec.y + shiftY + offsetY;
                    var iso = GridToIso(gx, gy);

                    var placedObj = new PlacedObject
                    {
                        Image = img,
                        GraphicId = EnsureHexPrefix(rec.id),
                        GridX = gx,
                        GridY = gy,
                        Z = rec.z,
                        Flags = rec.flags,
                        IsoPosition = iso,
                        Layer = layer
                    };

                    placedObjects.Add(placedObj);
                    loadedCount++;
                    layer++;
                }
                catch
                {
                    img?.Dispose();
                    failedCount++;
                }
            }

            RebuildLockLists();
            designPictureBox.Invalidate();
            this.BringToFront();
            this.Activate();

            string summaryMessage = failedCount == 0
                ? $"Successfully added {loadedCount} items to canvas."
                : $"Added {loadedCount} items, but {failedCount} items failed to load.";
            MessageBox.Show(summaryMessage, "Add to Canvas Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ClearCanvas()
        {
            if (placedObjects.Count == 0)
            {
                MessageBox.Show("Canvas is already empty.", "Clear Canvas", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to remove all {placedObjects.Count} objects from the canvas?\n\nThis action can be undone with Ctrl+Z.",
                "Clear Canvas", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (result != DialogResult.Yes) return;

            PushUndo();
            foreach (var obj in placedObjects)
            {
                obj.Image?.Dispose();
                obj.OriginalImage?.Dispose();
                obj.PreEditImage?.Dispose();
                lockedObjects.Remove(obj);
            }
            placedObjects.Clear();
            selectedObjects.Clear();
            selectedObject = null;
            ClearUnifyCandidates();
            RebuildLockLists();
            designPictureBox.Invalidate();
            outputTextBox.AppendText("Canvas cleared.\r\n");
        }

        /// <summary>
        /// Evenly space all placed objects so none overlap or touch.
        /// Calculates the grid footprint of each object based on its image size
        /// and lays them out in a grid with enough spacing.
        /// </summary>
        private void AutoPadObjects()
        {
            if (placedObjects.Count == 0)
            {
                SetAIStatus("Nothing to pad", Color.Orange);
                return;
            }
            if (placedObjects.Count == 1)
            {
                SetAIStatus("Only one object — nothing to space out");
                return;
            }

            PushUndo();

            // Calculate grid cells each item occupies based on image dimensions.
            // A 44x44 tile = 1x1 grid cell. Larger images span more cells.
            int maxCellsW = 1;
            int maxCellsH = 1;

            foreach (var obj in placedObjects)
            {
                if (obj.Image == null) continue;
                int cellsW = Math.Max(1, (int)Math.Ceiling((double)obj.Image.Width / TILE_WIDTH));
                int cellsH = Math.Max(1, (int)Math.Ceiling((double)obj.Image.Height / TILE_HEIGHT));
                if (cellsW > maxCellsW) maxCellsW = cellsW;
                if (cellsH > maxCellsH) maxCellsH = cellsH;
            }

            // Add 1 cell padding between items
            int spacingX = maxCellsW + 1;
            int spacingY = maxCellsH + 1;

            // Arrange in a grid with enough columns to keep it roughly square
            int count = placedObjects.Count;
            int cols = (int)Math.Ceiling(Math.Sqrt(count));
            int rows = (int)Math.Ceiling((double)count / cols);

            // Ensure the canvas grid is large enough
            int requiredWidth = cols * spacingX + 2;
            int requiredHeight = rows * spacingY + 2;

            if (requiredWidth > gridWidth || requiredHeight > gridHeight)
            {
                int newW = Math.Max(gridWidth, requiredWidth);
                int newH = Math.Max(gridHeight, requiredHeight);
                ResizeCanvas(newW, newH, false);
            }

            // Place each object at its new grid position
            // Sort by layer to keep draw order stable
            var sorted = new List<PlacedObject>(placedObjects);
            sorted.Sort((a, b) => a.Layer.CompareTo(b.Layer));

            for (int i = 0; i < sorted.Count; i++)
            {
                int col = i % cols;
                int row = i / cols;

                int gx = 1 + col * spacingX;
                int gy = 1 + row * spacingY;

                sorted[i].GridX = gx;
                sorted[i].GridY = gy;
                sorted[i].IsoPosition = GridToIso(gx, gy);
            }

            RebuildLockLists();
            designPictureBox.Invalidate();

            SetAIStatus($"Auto-padded {count} objects ({cols}x{rows} grid, spacing {spacingX}x{spacingY})", Color.LimeGreen);
            outputTextBox.AppendText($"AutoPad: {count} objects spaced in {cols}x{rows} grid (cell spacing {spacingX}x{spacingY})\r\n");
        }

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
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("Paste the multi text into the output box, then click Import Text.");
                return;
            }

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

            if (records.Count == 0)
            {
                MessageBox.Show($"No valid items found to import. Check the format:\n\nTileID  X  Y  Z  Flags\n0x3E63  0  2  0  1", "Import", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                designPictureBox.Invalidate();
                return;
            }

            int maxZ = 127;
            if (this.maxZTextBox != null && !string.IsNullOrWhiteSpace(this.maxZTextBox.Text))
                if (!int.TryParse(this.maxZTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out maxZ)) maxZ = 127;

            var filtered = records.Where(r => Math.Abs(r.z) <= maxZ).ToList();
            if (filtered.Count == 0)
            {
                MessageBox.Show($"All {records.Count} items were filtered out by Max Z Height ({maxZ}).\n\nIncrease 'Max Z Height' and try again.", "Import", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            gridWidth = Math.Max(1, (maxX - minX + 1));
            gridHeight = Math.Max(1, (maxY - minY + 1));
            widthNumericUpDown.Value = Math.Min(widthNumericUpDown.Maximum, Math.Max(widthNumericUpDown.Minimum, gridWidth));
            heightNumericUpDown.Value = Math.Min(heightNumericUpDown.Maximum, Math.Max(heightNumericUpDown.Minimum, gridHeight));
            ResizeCanvas(gridWidth, gridHeight, pushUndo: false);

            int layer = 0, loadedCount = 0, failedCount = 0;
            string mulFolderForImport = AppConfig.Instance.FindMulFolder();
            if (string.IsNullOrEmpty(mulFolderForImport) || !Directory.Exists(mulFolderForImport)) mulFolderForImport = artFolderPath;
            if (string.IsNullOrEmpty(mulFolderForImport) || !Directory.Exists(mulFolderForImport)) mulFolderForImport = @"C:\Program Files (x86)\UOForever\UO";

            var artFormat = AppConfig.Instance.GetArtFileFormat(mulFolderForImport);
            bool hasArtFiles = artFormat != ArtFileFormat.None;
            if (!hasArtFiles && idToPath.Count == 0)
            {
                MessageBox.Show("Cannot import: No MUL/UOP files found and no PNG art loaded.", "Import Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            foreach (var rec in filtered)
            {
                Image img = null;
                if (idToPath.Count > 0)
                {
                    string path;
                    if (TryResolveIdToPath(rec.id, out path) && !string.IsNullOrEmpty(path) && File.Exists(path))
                        try { img = LoadImageUnlocked(path); } catch { }
                }
                if (img == null && hasArtFiles)
                {
                    try
                    {
                        string idStr = rec.id.Trim();
                        if (idStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) idStr = idStr.Substring(2);
                        if (ushort.TryParse(idStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort tileId))
                        {
                            if (artFormat == ArtFileFormat.MulFiles) img = StaticArtReader.LoadStaticArt(mulFolderForImport, tileId);
                            else if (artFormat == ArtFileFormat.UopOnly) img = UopArtReader.LoadStaticArt(mulFolderForImport, tileId);
                            else if (artFormat == ArtFileFormat.TecmoExpanded) img = TecmoArtReader.LoadStaticArt(mulFolderForImport, tileId);
                        }
                    }
                    catch { }
                }
                if (img == null) { failedCount++; continue; }
                try
                {
                    int gx = rec.x + shiftX, gy = rec.y + shiftY;
                    var iso = GridToIso(gx, gy);
                    placedObjects.Add(new PlacedObject { Image = img, GraphicId = EnsureHexPrefix(rec.id), GridX = gx, GridY = gy, Z = rec.z, Flags = rec.flags, IsoPosition = iso, Layer = layer });
                    loadedCount++;
                    layer++;
                }
                catch { img?.Dispose(); failedCount++; }
            }

            RebuildLockLists();
            designPictureBox.Invalidate();
            string summaryMessage = failedCount == 0 ? $"Successfully loaded all {loadedCount} items." : $"Loaded {loadedCount} items, but {failedCount} items failed to load.";
            MessageBox.Show(summaryMessage, "Import Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            if (placedObjects.Count == 0) return new Bitmap(1, 1, PixelFormat.Format32bppArgb);
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

            foreach (var obj in placedObjects)
            {
                if (obj.Hidden || obj.Image == null) continue;
                float objScale = obj.Scale;
                int zOffset = obj.Z * Z_PIXEL;
                float isoX = (obj.GridX - obj.GridY) * (TILE_WIDTH / 2f);
                float isoY = (obj.GridX + obj.GridY) * (TILE_HEIGHT / 2f);
                float screenX = isoX + obj.PixelOffsetX;
                float screenY = isoY + obj.PixelOffsetY;
                float w = obj.Image.Width * objScale;
                float h = obj.Image.Height * objScale;
                float drawX = screenX - w / 2f;
                float drawY = screenY - h + (TILE_HEIGHT / 2f) - zOffset;

                float extraPadding = 0;
                if (obj.HasSkew)
                {
                    float maxOffset = Math.Max(Math.Max(Math.Abs(obj.SkewTopLeft.X), Math.Abs(obj.SkewTopLeft.Y)),
                        Math.Max(Math.Max(Math.Abs(obj.SkewTopRight.X), Math.Abs(obj.SkewTopRight.Y)),
                            Math.Max(Math.Max(Math.Abs(obj.SkewBottomRight.X), Math.Abs(obj.SkewBottomRight.Y)),
                                Math.Max(Math.Abs(obj.SkewBottomLeft.X), Math.Abs(obj.SkewBottomLeft.Y)))));
                    extraPadding = maxOffset * objScale;
                }
                minX = Math.Min(minX, (int)(drawX - extraPadding));
                minY = Math.Min(minY, (int)(drawY - extraPadding));
                maxX = Math.Max(maxX, (int)(drawX + w + extraPadding));
                maxY = Math.Max(maxY, (int)(drawY + h + extraPadding));
            }

            int padding = 20;
            minX -= padding; minY -= padding; maxX += padding; maxY += padding;
            int width = maxX - minX, height = maxY - minY;

            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                foreach (var obj in placedObjects.OrderBy(o => o.GridX + o.GridY).ThenBy(o => o.Z).ThenBy(o => o.Layer))
                {
                    if (obj.Hidden || obj.Image == null) continue;
                    float objScale = obj.Scale;
                    int zOffset = obj.Z * Z_PIXEL;
                    float isoX = (obj.GridX - obj.GridY) * (TILE_WIDTH / 2f);
                    float isoY = (obj.GridX + obj.GridY) * (TILE_HEIGHT / 2f);
                    float screenX = isoX + obj.PixelOffsetX;
                    float screenY = isoY + obj.PixelOffsetY;
                    float w = obj.Image.Width * objScale;
                    float h = obj.Image.Height * objScale;
                    float drawX = screenX - w / 2f - minX;
                    float drawY = screenY - h + (TILE_HEIGHT / 2f) - zOffset - minY;

                    if (obj.HasSkew)
                    {
                        PointF[] destPoints = new PointF[]
                        {
                            new PointF(drawX + obj.SkewTopLeft.X * objScale, drawY + obj.SkewTopLeft.Y * objScale),
                            new PointF(drawX + w + obj.SkewTopRight.X * objScale, drawY + obj.SkewTopRight.Y * objScale),
                            new PointF(drawX + obj.SkewBottomLeft.X * objScale, drawY + h + obj.SkewBottomLeft.Y * objScale)
                        };
                        g.DrawImage(obj.Image, destPoints);
                    }
                    else if (obj.Rotation != 0f || obj.FlipHorizontal || obj.FlipVertical)
                    {
                        var savedState = g.Save();
                        float centerX = drawX + w / 2f, centerY = drawY + h / 2f;
                        var matrix = new Matrix();
                        matrix.Translate(centerX, centerY);
                        if (obj.Rotation != 0f) matrix.Rotate(obj.Rotation);
                        if (obj.FlipHorizontal || obj.FlipVertical) matrix.Scale(obj.FlipHorizontal ? -1 : 1, obj.FlipVertical ? -1 : 1);
                        matrix.Translate(-centerX, -centerY);
                        g.Transform = matrix;
                        g.DrawImage(obj.Image, new RectangleF(drawX, drawY, w, h));
                        g.Restore(savedState);
                        matrix.Dispose();
                    }
                    else if (objScale != 1.0f)
                        g.DrawImage(obj.Image, new RectangleF(drawX, drawY, w, h));
                    else
                        g.DrawImage(obj.Image, new Rectangle((int)drawX, (int)drawY, (int)w, (int)h));
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
                    using (var transformedBmp = RenderTransformedObject(obj))
                    {
                        transformedBmp.Save(savePath, ImageFormat.Png);
                        manifestLines.Add($"{fileName}\t{EnsureHexPrefix(obj.GraphicId)}\t{obj.GridX}\t{obj.GridY}\t{obj.Z}\t{obj.Layer}\t{obj.Flags}\t{transformedBmp.Width}\t{transformedBmp.Height}");
                    }
                    idx++;
                }
                var manifestPath = Path.Combine(targetRoot, "manifest.txt");
                File.WriteAllLines(manifestPath, manifestLines);
                MessageBox.Show($"Exported {idx} component(s) to:\n{targetRoot}", "Export Components", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private Bitmap RenderTransformedObject(PlacedObject obj)
        {
            if (obj.Image == null) return new Bitmap(1, 1, PixelFormat.Format32bppArgb);
            bool hasTransform = obj.Scale != 1.0f || obj.Rotation != 0f || obj.FlipHorizontal || obj.FlipVertical || obj.HasSkew;
            if (!hasTransform) return new Bitmap(obj.Image);

            int scaledW = (int)Math.Round(obj.Image.Width * obj.Scale);
            int scaledH = (int)Math.Round(obj.Image.Height * obj.Scale);
            if (scaledW <= 0) scaledW = 1;
            if (scaledH <= 0) scaledH = 1;

            int extraPadding = 0;
            if (obj.HasSkew)
            {
                float maxOffset = Math.Max(Math.Max(Math.Abs(obj.SkewTopLeft.X), Math.Abs(obj.SkewTopLeft.Y)),
                    Math.Max(Math.Max(Math.Abs(obj.SkewTopRight.X), Math.Abs(obj.SkewTopRight.Y)),
                        Math.Max(Math.Max(Math.Abs(obj.SkewBottomRight.X), Math.Abs(obj.SkewBottomRight.Y)),
                            Math.Max(Math.Abs(obj.SkewBottomLeft.X), Math.Abs(obj.SkewBottomLeft.Y)))));
                extraPadding = (int)Math.Ceiling(maxOffset * obj.Scale) + 10;
            }
            if (obj.Rotation != 0f && obj.Rotation != 180f && obj.Rotation != 360f)
            {
                double radians = obj.Rotation * Math.PI / 180.0;
                double cos = Math.Abs(Math.Cos(radians));
                double sin = Math.Abs(Math.Sin(radians));
                int rotatedW = (int)Math.Ceiling(scaledW * cos + scaledH * sin);
                int rotatedH = (int)Math.Ceiling(scaledW * sin + scaledH * cos);
                extraPadding = Math.Max(extraPadding, Math.Max(rotatedW - scaledW, rotatedH - scaledH) / 2 + 10);
            }

            int canvasW = scaledW + extraPadding * 2;
            int canvasH = scaledH + extraPadding * 2;
            var result = new Bitmap(canvasW, canvasH, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float drawX = extraPadding, drawY = extraPadding;

                if (obj.HasSkew)
                {
                    PointF[] destPoints = new PointF[]
                    {
                        new PointF(drawX + obj.SkewTopLeft.X * obj.Scale, drawY + obj.SkewTopLeft.Y * obj.Scale),
                        new PointF(drawX + scaledW + obj.SkewTopRight.X * obj.Scale, drawY + obj.SkewTopRight.Y * obj.Scale),
                        new PointF(drawX + obj.SkewBottomLeft.X * obj.Scale, drawY + scaledH + obj.SkewBottomLeft.Y * obj.Scale)
                    };
                    if (obj.FlipHorizontal) { var temp = destPoints[0]; destPoints[0] = destPoints[1]; destPoints[1] = temp; destPoints[2] = new PointF(drawX + scaledW + obj.SkewBottomRight.X * obj.Scale, drawY + scaledH + obj.SkewBottomRight.Y * obj.Scale); }
                    if (obj.FlipVertical) { var tempTL = destPoints[0]; var tempTR = destPoints[1]; destPoints[0] = destPoints[2]; destPoints[1] = new PointF(drawX + scaledW + obj.SkewBottomRight.X * obj.Scale, drawY + scaledH + obj.SkewBottomRight.Y * obj.Scale); destPoints[2] = tempTL; }
                    g.DrawImage(obj.Image, destPoints);
                }
                else if (obj.Rotation != 0f || obj.FlipHorizontal || obj.FlipVertical)
                {
                    var savedState = g.Save();
                    float centerX = drawX + scaledW / 2f, centerY = drawY + scaledH / 2f;
                    var matrix = new Matrix();
                    matrix.Translate(centerX, centerY);
                    if (obj.Rotation != 0f) matrix.Rotate(obj.Rotation);
                    if (obj.FlipHorizontal || obj.FlipVertical) matrix.Scale(obj.FlipHorizontal ? -1 : 1, obj.FlipVertical ? -1 : 1);
                    matrix.Translate(-centerX, -centerY);
                    g.Transform = matrix;
                    g.DrawImage(obj.Image, new RectangleF(drawX, drawY, scaledW, scaledH));
                    g.Restore(savedState);
                    matrix.Dispose();
                }
                else
                    g.DrawImage(obj.Image, new RectangleF(drawX, drawY, scaledW, scaledH));
            }
            return CropToContent(result);
        }

        private Bitmap CropToContent(Bitmap source)
        {
            if (source == null) return null;
            int minX = source.Width, minY = source.Height, maxX = 0, maxY = 0;
            var rect = new Rectangle(0, 0, source.Width, source.Height);
            var data = source.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int bytes = Math.Abs(data.Stride) * source.Height;
                var buffer = new byte[bytes];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, bytes);
                for (int y = 0; y < source.Height; y++)
                {
                    int row = y * data.Stride;
                    for (int x = 0; x < source.Width; x++)
                    {
                        int idx = row + x * 4;
                        byte alpha = buffer[idx + 3];
                        if (alpha > 0)
                        {
                            if (x < minX) minX = x;
                            if (y < minY) minY = y;
                            if (x > maxX) maxX = x;
                            if (y > maxY) maxY = y;
                        }
                    }
                }
            }
            finally { source.UnlockBits(data); }

            if (minX > maxX || minY > maxY) return source;
            int padding = 1;
            minX = Math.Max(0, minX - padding); minY = Math.Max(0, minY - padding);
            maxX = Math.Min(source.Width - 1, maxX + padding); maxY = Math.Min(source.Height - 1, maxY + padding);
            int cropW = maxX - minX + 1, cropH = maxY - minY + 1;
            var cropped = new Bitmap(cropW, cropH, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(cropped))
                g.DrawImage(source, new Rectangle(0, 0, cropW, cropH), new Rectangle(minX, minY, cropW, cropH), GraphicsUnit.Pixel);
            source.Dispose();
            return cropped;
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
                    items.Add(new AiComponentItem { File = parts[0], GraphicId = parts[1], GridX = gx, GridY = gy, Z = z, Layer = layer, Flags = flags, Width = w, Height = h });
                }
                if (items.Count == 0) { MessageBox.Show("Manifest contained no items.", "Import Components", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
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
                        if (!TryResolveIdToPath(it.GraphicId, out filePath) || !File.Exists(filePath)) continue;
                    try
                    {
                        var img = LoadImageUnlocked(filePath);
                        int gx = it.GridX + shiftX;
                        int gy = it.GridY + shiftY;
                        var iso = GridToIso(gx, gy);
                        placedObjects.Add(new PlacedObject { Image = img, GraphicId = EnsureHexPrefix(it.GraphicId), GridX = gx, GridY = gy, Z = it.Z, Flags = it.Flags, IsoPosition = iso, Layer = nextLayer++ });
                    }
                    catch { }
                }
                RebuildLockLists();
                designPictureBox.Invalidate();
                MessageBox.Show($"Imported {placedObjects.Count} component(s) from manifest.", "Import Components", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private class AiComponentItem
        {
            public string File { get; set; }
            public string GraphicId { get; set; }
            public int GridX { get; set; }
            public int GridY { get; set; }
            public int Z { get; set; }
            public int Layer { get; set; }
            public int Flags { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public PlacedObject SourceObject { get; set; }
        }

        private class BatchSettings
        {
            public string Url { get; set; }
            public string Prompt { get; set; }
            public string Negative { get; set; }
            public int Steps { get; set; }
            public double Cfg { get; set; }
            public double Denoise { get; set; }
            public long? Seed { get; set; }
            public string Sampler { get; set; }
            public string Scheduler { get; set; }
        }

        private async Task BatchImg2ImgFromManifest()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Select manifest.txt";
                dlg.Filter = "Manifest (manifest.txt)|manifest.txt|Text Files (*.txt)|*.txt|All Files (*.*)|*.*";
                if (dlg.ShowDialog() != DialogResult.OK) return;
                var baseDir = Path.GetDirectoryName(dlg.FileName);
                var lines = File.ReadAllLines(dlg.FileName);
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
                    items.Add(new AiComponentItem { File = parts[0], GraphicId = parts[1], GridX = gx, GridY = gy, Z = z, Layer = layer, Flags = flags, Width = w, Height = h });
                }
                if (items.Count == 0) { MessageBox.Show("Manifest contained no items.", "Batch Img2Img", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                var settings = GetAISettingsFromPanel();
                if (string.IsNullOrWhiteSpace(settings.Url)) { MessageBox.Show("Please enter a ComfyUI URL in the AI Settings panel.", "Batch Img2Img", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                await ProcessBatchImg2Img(items, settings, baseDir);
            }
        }

        private async Task ProcessBatchImg2Img(List<AiComponentItem> items, BatchSettings settings, string baseDir)
        {
            var client = new ComfyUIClient(settings.Url);
            string outputDir = null;
            var manifestOut = new List<string>();
            bool isInPlaceRegen = baseDir == null;

            if (!isInPlaceRegen)
            {
                outputDir = Path.Combine(baseDir, "ai_out");
                Directory.CreateDirectory(outputDir);
                manifestOut.Add("# AI Component Manifest (TSV)");
                manifestOut.Add($"# generatedUtc={DateTime.UtcNow:o}");
                manifestOut.Add("file\tgraphicId\tgridX\tgridY\tz\tlayer\tflags\twidth\theight");
            }

            int success = 0;
            int targetWidth = GetAIResolutionWidth();
            int targetHeight = GetAIResolutionHeight();
            int targetSize = Math.Max(targetWidth, targetHeight);

            foreach (var item in items.OrderBy(i => i.Layer).ThenBy(i => i.GridY).ThenBy(i => i.GridX))
            {
                try
                {
                    SetAIStatus($"Processing {success + 1}/{items.Count}...", Color.Blue);
                    int origW = item.Width > 0 ? item.Width : 0;
                    int origH = item.Height > 0 ? item.Height : 0;
                    Bitmap src;
                    if (item.SourceObject != null && item.SourceObject.Image != null)
                    {
                        src = new Bitmap(item.SourceObject.Image);
                        if (origW <= 0) origW = src.Width;
                        if (origH <= 0) origH = src.Height;
                    }
                    else
                    {
                        var inputPath = Path.Combine(baseDir, item.File);
                        if (!File.Exists(inputPath)) continue;
                        src = new Bitmap(inputPath);
                        if (origW <= 0) origW = src.Width;
                        if (origH <= 0) origH = src.Height;
                    }

                    using (src)
                    {
                        double scale = Math.Min((double)targetSize / src.Width, (double)targetSize / src.Height);
                        int scaledW = Math.Max(1, (int)Math.Round(src.Width * scale));
                        int scaledH = Math.Max(1, (int)Math.Round(src.Height * scale));
                        int offsetX = (targetSize - scaledW) / 2;
                        int offsetY = (targetSize - scaledH) / 2;

                        using (var padded = ResizeToFitSquare(src, targetSize))
                        using (var ms = new MemoryStream())
                        {
                            padded.Save(ms, ImageFormat.Png);
                            var uploadBytes = ms.ToArray();
                            var uploadedName = await client.UploadImage(uploadBytes, $"tile_{item.GraphicId}.png");
                            var workflow = Image2ImageWorkflow.CreateWorkflow(settings.Prompt, settings.Negative, uploadedName, targetWidth, targetHeight, settings.Steps, settings.Cfg, settings.Denoise, settings.Seed, settings.Sampler, settings.Scheduler);
                            var promptId = await client.QueuePrompt(workflow);
                            if (string.IsNullOrEmpty(promptId)) continue;

                            var images = await client.GetGeneratedImages(promptId, maxAttempts: 60, pollIntervalMs: 1000);
                            if (images.Count == 0) { outputTextBox.AppendText($"No images found for {item.GraphicId}. Skipping.\r\n"); continue; }

                            await Task.Delay(500);
                            var data = await client.DownloadImage(images[0]);
                            if (data == null || data.Length < 8 || data[0] != 0x89 || data[1] != 0x50) { outputTextBox.AppendText($"Download not PNG for {item.GraphicId}. Skipping.\r\n"); continue; }

                            using (var msOut = new MemoryStream(data))
                            using (var genBmp = new Bitmap(msOut))
                            using (var croppedAI = new Bitmap(scaledW, scaledH, PixelFormat.Format32bppArgb))
                            {
                                using (var g = Graphics.FromImage(croppedAI))
                                {
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
                                        g.DrawImage(padded, new Rectangle(0, 0, scaledW, scaledH), new Rectangle(offsetX, offsetY, scaledW, scaledH), GraphicsUnit.Pixel);
                                    ApplyAlphaMask(croppedAI, croppedOriginal);
                                }

                                using (var resizedBack = ResizeToExact(croppedAI, origW, origH))
                                {
                                    if (GetDropBlackPixelsEnabled()) DropBlackPixels(resizedBack, GetBlackPixelThreshold());
                                    if (isInPlaceRegen && item.SourceObject != null)
                                    {
                                        if (item.SourceObject.OriginalImage == null) item.SourceObject.OriginalImage = item.SourceObject.Image;
                                        else item.SourceObject.Image?.Dispose();
                                        item.SourceObject.Image = new Bitmap(resizedBack);
                                        outputTextBox.AppendText($"Regenerated {item.GraphicId}\r\n");

                                        // Track for saving to art.mul
                                        int gidVal;
                                        if (TryParseGraphicId(item.GraphicId, out gidVal))
                                            MarkArtModified(gidVal, new Bitmap(resizedBack));
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
                catch (Exception ex) { outputTextBox.AppendText($"Error on {item.GraphicId}: {ex.Message}\r\n"); }
            }

            if (!isInPlaceRegen)
            {
                var manifestOutPath = Path.Combine(outputDir, "manifest.txt");
                File.WriteAllLines(manifestOutPath, manifestOut);
                SetAIStatus($"? Done! {success}/{items.Count} generated", Color.Green);
                outputTextBox.AppendText($"Batch img2img complete. {success}/{items.Count} generated.\nOutput: {outputDir}\r\n");
            }
            else
                SetAIStatus($"? Done! {success}/{items.Count} regenerated", Color.Green);
        }

        /// <summary>
        /// Request cancellation of the in-flight AI generation (STOP button or ESC).
        /// Also sends a best-effort /interrupt to the ComfyUI server so the GPU
        /// stops working on the current prompt instead of finishing it.
        /// </summary>
        private void CancelAIGeneration()
        {
            var cts = _aiGenerationCts;
            if (cts == null) return;
            try { cts.Cancel(); } catch { }
            try
            {
                var url = _aiSettingsPanel?.ComfyUrl;
                if (!string.IsNullOrWhiteSpace(url))
                {
                    var client = new ComfyUIClient(url);
                    _ = client.Interrupt();
                }
            }
            catch { }
            SetAIStatus("Stopping...", Color.Orange);
        }

        /// <summary>
        /// Await a ComfyUI poll task while staying responsive to STOP/ESC.
        /// Throws OperationCanceledException on cancel (after a best-effort
        /// server interrupt). The orphaned poll task is left running; its late
        /// result is simply ignored.
        /// </summary>
        private async Task<T> AwaitPollWithCancel<T>(Task<T> pollTask, CancellationToken token, ComfyUIClient client)
        {
            while (!pollTask.IsCompleted)
            {
                try { await Task.Delay(500, token); }
                catch (OperationCanceledException)
                {
                    try { await client.Interrupt(); } catch { }
                    throw;
                }
            }
            return await pollTask;
        }

        private async Task AIRegenSelected()
        {
            var selected = placedObjects.Where(o => selectedObjects.Contains(o)).ToList();
            if (selected.Count == 0) { SetAIStatus("No objects selected", Color.Orange); return; }

var settings = GetAISettingsFromPanel();
            var selectedBackend = _aiSettingsPanel.SelectedBackend;

            if (selectedBackend != AIBackend.ComfyUI)
            {
                SetAIStatus("Only ComfyUI backend is supported.", Color.Red);
                return;
            }

            if (string.IsNullOrWhiteSpace(settings.Url)) { SetAIStatus("Enter ComfyUI URL", Color.Red); return; }

            RevertAIChangesForObjects(selected);
            var items = new List<AiComponentItem>();
            foreach (var obj in selected)
            {
                if (obj.Image == null) continue;
                items.Add(new AiComponentItem { File = null, GraphicId = obj.GraphicId, GridX = obj.GridX, GridY = obj.GridY, Z = obj.Z, Layer = obj.Layer, Flags = obj.Flags, Width = obj.Image.Width, Height = obj.Image.Height, SourceObject = obj });
            }

            string backendName = ImageGeneratorFactory.GetBackendName(selectedBackend);
            SetAIStatus($"Regenerating {items.Count} tiles with {backendName}...", Color.Blue);
            outputTextBox.AppendText($"Starting AI regen of {items.Count} tile(s) with {backendName}...\r\n");
            if (_aiGenerationCts != null) { SetAIStatus("Generation already running - press STOP to cancel", Color.Orange); return; }
            PushUndo();
            var generationTimer = Stopwatch.StartNew();
            _aiGenerationCts = new CancellationTokenSource();
            var cts = _aiGenerationCts;
            KeyEventHandler escHandler = (s, e) => { if (e.KeyCode == Keys.Escape) { CancelAIGeneration(); e.Handled = true; } };
            KeyDown += escHandler;
            _aiSettingsPanel?.SetStopEnabled(true);
            bool cancelled = false;
            try
            {
                // Batch img2img runs straight through ComfyUI, one item at a time.
                foreach (var item in items.OrderBy(i => i.Layer).ThenBy(i => i.GridY).ThenBy(i => i.GridX))
                {
                    try
                    {
                        // Inside the guarded region: stopping between items lands in
                        // the OperationCanceledException handler below, not the crash dialog.
                        cts.Token.ThrowIfCancellationRequested();

                        double avgPerItem = items.Count > 0 ? generationTimer.Elapsed.TotalSeconds / Math.Max(1, items.Count) : 0;
                        double estRemaining = avgPerItem * (items.Count - items.IndexOf(item) - 1);
                        string etaStr = avgPerItem > 0 ? $" ETA ~{estRemaining:F0}s" : "";
                        SetAIStatus($"[{items.IndexOf(item) + 1}/{items.Count}] {backendName}...{etaStr}", Color.Blue);
                        outputTextBox.AppendText($"[{generationTimer.Elapsed.TotalSeconds:F1}s] Processing {item.GraphicId} ({items.IndexOf(item) + 1}/{items.Count}){etaStr}\r\n");

                        int origW = item.Width > 0 ? item.Width : 0;
                        int origH = item.Height > 0 ? item.Height : 0;
                        Bitmap src;
                        if (item.SourceObject != null && item.SourceObject.Image != null)
                        {
                            src = new Bitmap(item.SourceObject.Image);
                            if (origW <= 0) origW = src.Width;
                            if (origH <= 0) origH = src.Height;
                        }
                        else continue;

                        using (src)
                        {
                            int targetWidth = GetAIResolutionWidth();
                            int targetHeight = GetAIResolutionHeight();
                            int targetSize = Math.Max(targetWidth, targetHeight);

                            double scale = Math.Min((double)targetSize / src.Width, (double)targetSize / src.Height);
                            int scaledW = Math.Max(1, (int)Math.Round(src.Width * scale));
                            int scaledH = Math.Max(1, (int)Math.Round(src.Height * scale));
                            int offsetX = (targetSize - scaledW) / 2;
                            int offsetY = (targetSize - scaledH) / 2;

                            using (var padded = ResizeToFitSquare(src, targetSize))
                            {
                                byte[] imageBytes;
                                using (var ms = new MemoryStream())
                                {
                                    padded.Save(ms, ImageFormat.Png);
                                    imageBytes = ms.ToArray();
                                }

                                var client = new ComfyUIClient(settings.Url);
                                var uploadedName = await client.UploadImage(imageBytes, $"batch_{item.GraphicId}.png");
                                var workflow = Image2ImageWorkflow.CreateWorkflow(settings.Prompt, settings.Negative, uploadedName, targetWidth, targetHeight, settings.Steps, settings.Cfg, settings.Denoise, settings.Seed, settings.Sampler, settings.Scheduler);
                                var promptId = await client.QueuePrompt(workflow);
                                if (string.IsNullOrEmpty(promptId)) { SetAIStatus("Failed to queue prompt", Color.Red); return; }
                                SetAIStatus("Generating...", Color.Blue);
                                AppendGenerationLog(generationTimer, "Waiting for AI generation...");
                                var images = await AwaitPollWithCancel(client.GetGeneratedImages(promptId, maxAttempts: 60, pollIntervalMs: 1000), cts.Token, client);
                                cts.Token.ThrowIfCancellationRequested();
                                if (images.Count == 0) { SetAIStatus("No images generated", Color.Orange); return; }
                                await Task.Delay(500);
                                var data = await client.DownloadImage(images[0]);
                                if (data == null || data.Length < 8 || data[0] != 0x89 || data[1] != 0x50) { SetAIStatus("Invalid PNG data", Color.Red); return; }
                                Bitmap generatedBitmap;
                                using (var msOut = new MemoryStream(data)) { generatedBitmap = new Bitmap(msOut); }

                                if (generatedBitmap == null) { SetAIStatus("Generation failed", Color.Red); return; }

                                using (generatedBitmap)
                                using (var croppedAI = new Bitmap(scaledW, scaledH, PixelFormat.Format32bppArgb))
                                {
                                    using (var g = Graphics.FromImage(croppedAI))
                                    {
                                        float scaleX = (float)generatedBitmap.Width / targetSize;
                                        float scaleY = (float)generatedBitmap.Height / targetSize;
                                        int cropX = (int)(offsetX * scaleX), cropY = (int)(offsetY * scaleY);
                                        int cropW = (int)(scaledW * scaleX), cropH = (int)(scaledH * scaleY);
                                        g.DrawImage(generatedBitmap, new Rectangle(0, 0, scaledW, scaledH), new Rectangle(cropX, cropY, cropW, cropH), GraphicsUnit.Pixel);
                                    }

                                    using (var croppedOriginal = new Bitmap(scaledW, scaledH, PixelFormat.Format32bppArgb))
                                    {
                                        using (var g = Graphics.FromImage(croppedOriginal))
                                            g.DrawImage(padded, new Rectangle(0, 0, scaledW, scaledH), new Rectangle(offsetX, offsetY, scaledW, scaledH), GraphicsUnit.Pixel);
                                        ApplyAlphaMask(croppedAI, croppedOriginal);
                                    }

                                    using (var resizedBack = ResizeToExact(croppedAI, origW, origH))
                                    {
                                        if (GetDropBlackPixelsEnabled()) DropBlackPixels(resizedBack, GetBlackPixelThreshold());
                                        if (item.SourceObject != null)
                                        {
                                            if (item.SourceObject.OriginalImage == null) item.SourceObject.OriginalImage = item.SourceObject.Image;
                                            else item.SourceObject.Image?.Dispose();
                                            item.SourceObject.Image = new Bitmap(resizedBack);

                                            int gidVal;
                                            if (TryParseGraphicId(item.GraphicId, out gidVal))
                                                MarkArtModified(gidVal, new Bitmap(resizedBack));
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                        AppendGenerationLog(generationTimer, $"Generation interrupted by user at {items.IndexOf(item) + 1}/{items.Count}");
                        SetAIStatus($"Stopped by user: {items.IndexOf(item) + 1}/{items.Count} regenerated", Color.Orange);
                        break;
                    }
                    catch (Exception ex) { AppendGenerationLog(generationTimer, $"Error on {item.GraphicId}: {ex.Message}"); }
                }
                if (!cancelled)
                {
                    SetAIStatus($"Done! {items.Count}/{items.Count} regenerated in {generationTimer.Elapsed.TotalSeconds:F1}s", Color.Green);
                    outputTextBox.AppendText($"Generation time: {generationTimer.Elapsed.TotalSeconds:F1}s\r\n");
                }
            }
            catch (OperationCanceledException)
            {
                // Defensive: no cancel path should ever reach the crash dialog.
                cancelled = true;
                AppendGenerationLog(generationTimer, "Generation stopped by user");
                SetAIStatus("Stopped by user", Color.Orange);
            }
            finally
            {
                KeyDown -= escHandler;
                _aiSettingsPanel?.SetStopEnabled(false);
                _aiGenerationCts = null;
                cts.Dispose();
                generationTimer.Stop();
                outputTextBox.AppendText($"Total generation time: {generationTimer.Elapsed.TotalSeconds:F1}s\r\n");
            }

            designPictureBox.Invalidate();

            if (!cancelled && AppConfig.Instance.JarJarPushEnabled && _aiSettingsPanel.AutoPushEnabled && jarjarPendingChanges.Count > 0)
                await PushToJarJar();
        }

        private async Task AIRegenSelectedAsOne()
        {
            var selected = placedObjects.Where(o => selectedObjects.Contains(o)).ToList();
            if (selected.Count == 0) { SetAIStatus("No objects selected", Color.Orange); return; }

            var settings = GetAISettingsFromPanel();
            var selectedBackend = _aiSettingsPanel.SelectedBackend;

            if (selectedBackend != AIBackend.ComfyUI)
            {
                SetAIStatus("Only ComfyUI backend is supported.", Color.Red);
                return;
            }

            if (string.IsNullOrWhiteSpace(settings.Url)) { SetAIStatus("Enter ComfyUI URL", Color.Red); return; }

            RevertAIChangesForObjects(selected);
            string backendName = ImageGeneratorFactory.GetBackendName(selectedBackend);
            SetAIStatus($"Processing as one image with {backendName}...", Color.Blue);
            if (_aiGenerationCts != null) { SetAIStatus("Generation already running - press STOP to cancel", Color.Orange); return; }
            PushUndo();
            var generationTimer = Stopwatch.StartNew();
            _aiGenerationCts = new CancellationTokenSource();
            var asOneCts = _aiGenerationCts;
            KeyEventHandler asOneEscHandler = (s, e) => { if (e.KeyCode == Keys.Escape) { CancelAIGeneration(); e.Handled = true; } };
            KeyDown += asOneEscHandler;
            _aiSettingsPanel?.SetStopEnabled(true);

            try
            {
                int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                var objectBounds = new Dictionary<PlacedObject, Rectangle>();

                foreach (var obj in selected)
                {
                    if (obj.Image == null) continue;
                    int isoX = (int)Math.Round((obj.GridX - obj.GridY) * (TILE_WIDTH / 2f));
                    int isoY = (int)Math.Round((obj.GridX + obj.GridY) * (TILE_HEIGHT / 2f));
                    int w = obj.Image.Width, h = obj.Image.Height;
                    int zOffset = obj.Z * Z_PIXEL;
                    int drawX = isoX - w / 2, drawY = isoY - h + (TILE_HEIGHT / 2) - zOffset;
                    objectBounds[obj] = new Rectangle(drawX, drawY, w, h);
                    minX = Math.Min(minX, drawX); minY = Math.Min(minY, drawY);
                    maxX = Math.Max(maxX, drawX + w); maxY = Math.Max(maxY, drawY + h);
                }

                if (objectBounds.Count == 0) { SetAIStatus("No valid objects", Color.Orange); return; }
                int compositeWidth = maxX - minX, compositeHeight = maxY - minY;

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
                        // Same painter's order as the live canvas.
                        foreach (var kvp in objectBounds.OrderBy(k => k.Key, Comparer<PlacedObject>.Create(ComparePaintOrder)))
                        {
                            var obj = kvp.Key; var bounds = kvp.Value;
                            int relX = bounds.X - minX, relY = bounds.Y - minY;
                            g.DrawImage(obj.Image, relX, relY, bounds.Width, bounds.Height);
                        }
                    }
                    originalComposite = new Bitmap(temp);
                }

                int targetWidth = GetAIResolutionWidth();
                int targetHeight = GetAIResolutionHeight();
                int targetSize = Math.Max(targetWidth, targetHeight);
                double scale = Math.Min((double)targetSize / originalComposite.Width, (double)targetSize / originalComposite.Height);
                int scaledW = Math.Max(1, (int)Math.Round(originalComposite.Width * scale));
                int scaledH = Math.Max(1, (int)Math.Round(originalComposite.Height * scale));
                int offsetX = (targetSize - scaledW) / 2, offsetY = (targetSize - scaledH) / 2;

                Bitmap generatedBitmap = null;
                using (originalComposite)
                using (var padded = ResizeToFitSquare(originalComposite, targetSize))
                {
                    if (selectedBackend != AIBackend.ComfyUI)
                    {
                        throw new NotSupportedException("Only ComfyUI backend is supported.");
                    }
                    else
                    {
                        var client = new ComfyUIClient(settings.Url);
                        using (var ms = new MemoryStream())
                        {
                            padded.Save(ms, ImageFormat.Png);
                            var uploadBytes = ms.ToArray();
                            SetAIStatus("Uploading to ComfyUI...", Color.Blue);
                            var uploadedName = await client.UploadImage(uploadBytes, "composite_multi.png");
                            var workflow = Image2ImageWorkflow.CreateWorkflow(settings.Prompt, settings.Negative, uploadedName, targetWidth, targetHeight, settings.Steps, settings.Cfg, settings.Denoise, settings.Seed, settings.Sampler, settings.Scheduler);
                            var promptId = await client.QueuePrompt(workflow);
                            if (string.IsNullOrEmpty(promptId)) { SetAIStatus("Failed to queue prompt", Color.Red); return; }
                            SetAIStatus("Generating...", Color.Blue);
                            AppendGenerationLog(generationTimer, "Waiting for AI generation...");
                            var images = await AwaitPollWithCancel(client.GetGeneratedImages(promptId, maxAttempts: 60, pollIntervalMs: 1000), asOneCts.Token, client);
                            asOneCts.Token.ThrowIfCancellationRequested();
                            if (images.Count == 0) { SetAIStatus("No images generated", Color.Orange); return; }
                            await Task.Delay(500);
                            var data = await client.DownloadImage(images[0]);
                            if (data == null || data.Length < 8 || data[0] != 0x89 || data[1] != 0x50) { SetAIStatus("Invalid PNG data", Color.Red); return; }
                            using (var msOut = new MemoryStream(data)) generatedBitmap = new Bitmap(msOut);
                        }
                    }

                    if (generatedBitmap == null) { SetAIStatus("Generation failed", Color.Red); return; }

                    using (generatedBitmap)
                    using (var croppedAI = new Bitmap(scaledW, scaledH, PixelFormat.Format32bppArgb))
                    {
                        using (var g = Graphics.FromImage(croppedAI))
                        {
                            g.InterpolationMode = InterpolationMode.NearestNeighbor;
                            g.PixelOffsetMode = PixelOffsetMode.None;
                            g.CompositingMode = CompositingMode.SourceCopy;
                            float scaleX = (float)generatedBitmap.Width / targetSize;
                            float scaleY = (float)generatedBitmap.Height / targetSize;
                            int cropX = (int)(offsetX * scaleX), cropY = (int)(offsetY * scaleY);
                            int cropW = (int)(scaledW * scaleX), cropH = (int)(scaledH * scaleY);
                            g.DrawImage(generatedBitmap, new Rectangle(0, 0, scaledW, scaledH), new Rectangle(cropX, cropY, cropW, cropH), GraphicsUnit.Pixel);
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
                            if (GetDropBlackPixelsEnabled()) DropBlackPixels(resizedComposite, GetBlackPixelThreshold());
                            bool preserveAlpha = GetPreserveAlphaMask();

                            foreach (var kvp in objectBounds)
                            {
                                var obj = kvp.Key; var bounds = kvp.Value;
                                int relX = bounds.X - minX, relY = bounds.Y - minY;
                                var slice = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                                using (var g = Graphics.FromImage(slice))
                                {
                                    g.CompositingMode = CompositingMode.SourceCopy;
                                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                                    g.PixelOffsetMode = PixelOffsetMode.None;
                                    g.DrawImage(resizedComposite, new Rectangle(0, 0, bounds.Width, bounds.Height), new Rectangle(relX, relY, bounds.Width, bounds.Height), GraphicsUnit.Pixel);
                                }

                                // Re-apply original alpha mask so each piece keeps
                                // its original transparency shape when moved
                                if (preserveAlpha && obj.Image != null)
                                {
                                    using (var origBmp = new Bitmap(obj.Image))
                                    {
                                        if (origBmp.Width == slice.Width && origBmp.Height == slice.Height)
                                            ApplyAlphaMask(slice, origBmp);
                                    }
                                }

                                if (obj.OriginalImage == null) obj.OriginalImage = obj.Image;
                                else obj.Image?.Dispose();
                                obj.Image = slice;
                                AppendGenerationLog(generationTimer, $"Replaced {obj.GraphicId}");

                                // Track for saving to art.mul
                                int gidVal;
                                if (TryParseGraphicId(obj.GraphicId, out gidVal))
                                    MarkArtModified(gidVal, new Bitmap(slice));
                            }
                        }
                    }
                }

                designPictureBox.Invalidate();
                generationTimer.Stop();
                SetAIStatus($"? Done! {objectBounds.Count} tiles regenerated in {generationTimer.Elapsed.TotalSeconds:F1}s", Color.Green);
                outputTextBox.AppendText($"Generation time: {generationTimer.Elapsed.TotalSeconds:F1}s\r\n");

                // Auto-push to JarJar if enabled
                if (AppConfig.Instance.JarJarPushEnabled && _aiSettingsPanel.AutoPushEnabled && jarjarPendingChanges.Count > 0)
                    await PushToJarJar();
            }
            catch (OperationCanceledException)
            {
                AppendGenerationLog(generationTimer, "Generation interrupted by user");
                SetAIStatus("Stopped by user", Color.Orange);
            }
            catch (Exception ex) { SetAIStatus($"Error: {ex.Message}", Color.Red); AppendGenerationLog(generationTimer, $"Error: {ex.Message}"); }
            finally
            {
                KeyDown -= asOneEscHandler;
                _aiSettingsPanel?.SetStopEnabled(false);
                _aiGenerationCts = null;
                asOneCts.Dispose();
                generationTimer.Stop();
            }
        }

        private void AppendGenerationLog(Stopwatch timer, string message)
        {
            double seconds = timer != null ? timer.Elapsed.TotalSeconds : 0;
            outputTextBox.AppendText($"[{seconds:F1}s] {message}\r\n");
        }

        /// <summary>
        /// If the user toggled Old/New to preview originals, Image/OriginalImage are
        /// currently swapped (Image=OG, Original=AI). Swap them back so that
        /// Image=AI and Original=OG before any revert/regen logic runs.
        /// Without this, a revert after previewing would keep the AI art and
        /// discard the original - leaving the user stuck on the new artwork.
        /// </summary>
        private void NormalizeAIPreviewState()
        {
            if (!showingOriginalArt) return;
            foreach (var obj in placedObjects)
            {
                if (obj.OriginalImage == null) continue;
                var temp = obj.Image;
                obj.Image = obj.OriginalImage;
                obj.OriginalImage = temp;
            }
            showingOriginalArt = false;
            _aiSettingsPanel?.SetOldNewButtonText("Old");
        }

        private void RevertAIChangesForObjects(IEnumerable<PlacedObject> objects)
        {
            NormalizeAIPreviewState();
            foreach (var obj in objects)
            {
                if (obj.OriginalImage != null)
                {
                    // The backup may alias a disposed cache entry -- never
                    // install a corpse (later use throws "Parameter is not
                    // valid"), and never dispose what we are about to keep.
                    if (!IsImageAlive(obj.OriginalImage))
                    {
                        try { obj.OriginalImage.Dispose(); }
                        catch { }
                        obj.OriginalImage = null;
                        outputTextBox.AppendText($"Revert {obj.GraphicId}: backup was unusable, skipped\r\n");
                        continue;
                    }
                    if (obj.Image != null && !ReferenceEquals(obj.Image, obj.OriginalImage))
                    {
                        try { obj.Image.Dispose(); }
                        catch { }
                    }
                    obj.Image = obj.OriginalImage;
                    obj.OriginalImage = null;
                }
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
                int stride = data.Stride, width = image.Width, height = image.Height;
                for (int y = 0; y < height; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        int idx = row + x * 4;
                        byte b = buffer[idx], g = buffer[idx + 1], r = buffer[idx + 2];
                        if (r <= threshold && g <= threshold && b <= threshold)
                        {
                            buffer[idx] = 0; buffer[idx + 1] = 0; buffer[idx + 2] = 0; buffer[idx + 3] = 0;
                        }
                    }
                }
                System.Runtime.InteropServices.Marshal.Copy(buffer, 0, data.Scan0, bytes);
            }
            finally { image.UnlockBits(data); }
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
                int offsetX = (targetSize - newW) / 2, offsetY = (targetSize - newH) / 2;
                g.DrawImage(source, new Rectangle(offsetX, offsetY, newW, newH), new Rectangle(0, 0, source.Width, source.Height), GraphicsUnit.Pixel);
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
                int width = color.Width, height = color.Height;
                int strideC = colorData.Stride, strideA = alphaData.Stride;
                for (int y = 0; y < height; y++)
                {
                    int rowC = y * strideC, rowA = y * strideA;
                    for (int x = 0; x < width; x++)
                    {
                        int idxC = rowC + x * 4, idxA = rowA + x * 4;
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
            // The Old/New toggle swaps Image/OriginalImage for previewing.
            // Normalize first so a revert after previewing restores the
            // original instead of keeping the AI art.
            NormalizeAIPreviewState();

            var selected = placedObjects.Where(o => selectedObjects.Contains(o)).ToList();
            // Revert the selection if it holds any AI state; otherwise fall back
            // to every object with AI state so the button never appears dead
            // just because the selection changed after regenerating.
            List<PlacedObject> targets = selected.Where(o => o.OriginalImage != null).ToList();
            if (targets.Count == 0)
                targets = placedObjects.Where(o => o.OriginalImage != null).ToList();
            if (targets.Count == 0) { SetAIStatus("No AI changes to revert", Color.Orange); return; }
            int revertedCount = 0;
            PushUndo();
            foreach (var obj in targets)
            {
                if (obj.OriginalImage != null)
                {
                    obj.Image?.Dispose();
                    obj.Image = obj.OriginalImage;
                    obj.OriginalImage = null;
                    revertedCount++;
                    outputTextBox.AppendText($"Reverted {obj.GraphicId} to original\r\n");

                    // Reverting is itself a change that should be pushable/savable.
                    int gidVal;
                    if (TryParseGraphicId(obj.GraphicId, out gidVal))
                        MarkArtModified(gidVal, obj.Image);
                }
            }
            if (revertedCount == 0) SetAIStatus("No AI changes to revert", Color.Orange);
            else
            {
                designPictureBox.Invalidate();
                SetAIStatus($"✓ Reverted {revertedCount} object(s)", Color.Green);
                UpdatePendingChangesUI();
            }
        }

        /// <summary>
        /// Unify repeated ItemIDs to a single style. When an ID has more than
        /// one distinct artwork, a picker dialog lets the user choose the winner
        /// (default = AI-regenerated, then largest). Losing variants are stashed
        /// so running Unify again offers them for swapping. Operates on the
        /// selection, or the whole canvas if nothing is selected.
        /// </summary>
        private void UnifySameIdArt()
        {
            NormalizeAIPreviewState();

            List<PlacedObject> targets = placedObjects.Where(o => selectedObjects.Contains(o)).ToList();
            if (targets.Count == 0)
                targets = new List<PlacedObject>(placedObjects);

            var groups = targets
                .Where(o => o != null && o.Image != null && !string.IsNullOrWhiteSpace(o.GraphicId))
                .GroupBy(o => o.GraphicId.Trim().ToUpperInvariant())
                .Where(g => g.Count() > 1)
                .ToList();

            if (groups.Count == 0) { SetAIStatus("No repeated IDs to unify", Color.Orange); return; }

            // Collect distinct variants per ID (fresh clones) + merge stashed ones.
            var pickGroups = new List<UnifyGroup>();
            var working = new Dictionary<string, List<UnifyVariant>>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in groups)
            {
                string key = g.Key;
                var members = g.ToList();
                var variants = new List<UnifyVariant>();
                foreach (var m in members)
                {
                    if (m.Image == null) continue;
                    var bmp = m.Image as Bitmap ?? new Bitmap(m.Image);
                    bool dup = false;
                    foreach (var v in variants)
                    {
                        if (ImagesEqual(v.Image, bmp)) { dup = true; break; }
                    }
                    if (dup) { try { bmp.Dispose(); } catch { } continue; }
                    variants.Add(new UnifyVariant { Image = bmp, IsRegen = m.OriginalImage != null });
                }

                // Adopt stashed variants (previously losing artworks).
                List<Bitmap> stash;
                if (unifyCandidates.TryGetValue(key, out stash))
                {
                    foreach (var s in stash)
                    {
                        bool dup = false;
                        foreach (var v in variants)
                        {
                            if (ImagesEqual(v.Image, s)) { dup = true; break; }
                        }
                        if (dup) { try { s.Dispose(); } catch { } continue; }
                        variants.Add(new UnifyVariant { Image = s, IsRegen = false });
                    }
                    unifyCandidates.Remove(key);
                }

                if (variants.Count < 2)
                {
                    foreach (var v in variants) { try { v.Image.Dispose(); } catch { } }
                    continue; // already uniform, nothing to choose
                }

                // Default pick: AI-regenerated first, then largest.
                int def = 0;
                int bestScore = -1;
                for (int i = 0; i < variants.Count; i++)
                {
                    int score = (variants[i].IsRegen ? 1000000000 : 0)
                        + variants[i].Image.Width * variants[i].Image.Height;
                    if (score > bestScore) { bestScore = score; def = i; }
                }

                pickGroups.Add(new UnifyGroup
                {
                    GraphicId = members[0].GraphicId,
                    MemberCount = members.Count,
                    Variants = variants,
                    DefaultIndex = def
                });
                working[key] = variants;
            }

            if (pickGroups.Count == 0) { SetAIStatus("Already unified", Color.Orange); return; }

            Dictionary<string, Bitmap> chosenById = null;
            using (var dlg = new UnifyPickerDialog(pickGroups))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    chosenById = new Dictionary<string, Bitmap>(dlg.ChosenById, StringComparer.OrdinalIgnoreCase);
            }

            if (chosenById == null)
            {
                // Cancelled: canvas untouched, but stash everything so running
                // Unify again offers the same swap choices.
                foreach (var kvp in working)
                    unifyCandidates[kvp.Key] = kvp.Value.Select(v => v.Image).ToList();
                SetAIStatus("Unify cancelled - choices kept, run again to swap", Color.Orange);
                return;
            }

            PushUndo();
            int unifiedIds = 0;
            int unifiedObjects = 0;
            foreach (var pg in pickGroups)
            {
                string key = pg.GraphicId.Trim().ToUpperInvariant();
                Bitmap chosen;
                if (!chosenById.TryGetValue(pg.GraphicId, out chosen) || chosen == null)
                    chosen = pg.Variants[pg.DefaultIndex].Image;

                var members = groups.First(gr => gr.Key == key).ToList();
                foreach (var obj in members)
                {
                    int w = obj.Image.Width;
                    int h = obj.Image.Height;
                    obj.Image?.Dispose();
                    if (w == chosen.Width && h == chosen.Height)
                        obj.Image = new Bitmap(chosen);
                    else
                        obj.Image = ResizeToExact(new Bitmap(chosen), w, h);
                    unifiedObjects++;
                }
                unifiedIds++;
                outputTextBox.AppendText(string.Format("Unified {0}: {1} object(s) now share one style\r\n", pg.GraphicId, members.Count));

                // Keep Save in sync: one art per ID, matching the canvas.
                int gidVal;
                if (TryParseGraphicId(pg.GraphicId, out gidVal))
                    MarkArtModified(gidVal, new Bitmap(chosen));

                // Stash all variants (including the winner) for future swaps.
                unifyCandidates[key] = working[key].Select(v => v.Image).ToList();
            }

            designPictureBox.Invalidate();
            SetAIStatus(string.Format("✓ Unified {0} ID(s) ({1} object(s))", unifiedIds, unifiedObjects), Color.Green);
            UpdatePendingChangesUI();
        }

        /// <summary>
        /// Exact pixel equality with early-out (size check first).
        /// </summary>
        private static bool ImagesEqual(Image a, Image b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Width != b.Width || a.Height != b.Height) return false;
            Bitmap ba = a as Bitmap;
            Bitmap bb = b as Bitmap;
            bool dispA = false;
            bool dispB = false;
            try
            {
                if (ba == null) { ba = new Bitmap(a); dispA = true; }
                if (bb == null) { bb = new Bitmap(b); dispB = true; }
                var rect = new Rectangle(0, 0, ba.Width, ba.Height);
                var da = ba.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var db = bb.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    int bytes = Math.Abs(da.Stride) * ba.Height;
                    var bufa = new byte[bytes];
                    var bufb = new byte[bytes];
                    System.Runtime.InteropServices.Marshal.Copy(da.Scan0, bufa, 0, bytes);
                    System.Runtime.InteropServices.Marshal.Copy(db.Scan0, bufb, 0, bytes);
                    for (int i = 0; i < bytes; i++)
                    {
                        if (bufa[i] != bufb[i]) return false;
                    }
                    return true;
                }
                finally
                {
                    ba.UnlockBits(da);
                    bb.UnlockBits(db);
                }
            }
            catch { return false; }
            finally
            {
                if (dispA && ba != null) { try { ba.Dispose(); } catch { } }
                if (dispB && bb != null) { try { bb.Dispose(); } catch { } }
            }
        }

        private void ClearUnifyCandidates()
        {
            foreach (var kvp in unifyCandidates)
            {
                if (kvp.Value == null) continue;
                foreach (var bmp in kvp.Value)
                {
                    if (bmp == null) continue;
                    try { bmp.Dispose(); } catch { }
                }
            }
            unifyCandidates.Clear();
        }

        /// <summary>
        /// Last-resort recovery: reload the ORIGINAL UO artwork for each target
        /// object straight from the configured art files (art.mul / UOP / Tecmo),
        /// regardless of whether OriginalImage backup state still exists.
        /// Use when Revert AI Changes has nothing left to restore.
        /// Targets the current selection, or every placed object if nothing is selected.
        /// </summary>
        /// <summary>
        /// Clone that tolerates null/disposed sources. Touching Width on a
        /// disposed GDI+ image throws ArgumentException ("Parameter is not
        /// valid") -- return null instead of crashing the caller.
        /// </summary>
        private static Bitmap TryCloneImage(Image src)
        {
            if (src == null) return null;
            try
            {
                int w = src.Width, h = src.Height;
                if (w <= 0 || h <= 0) return null;
                return new Bitmap(src);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Non-allocating liveness probe. Touching Width on a disposed GDI+
        /// image throws ArgumentException ("Parameter is not valid").
        /// </summary>
        private static bool IsImageAlive(Image src)
        {
            if (src == null) return false;
            try
            {
                return src.Width > 0 && src.Height > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Drop one key from the static-art cache so disk reloads it.</summary>
        private void EvictStaticItemCache(int itemId)
        {
            try
            {
                string cacheKey = $"0x{itemId:X4}";
                lock (imageCache)
                {
                    imageCache.Remove(cacheKey);
                }
            }
            catch { }
        }

        private void RevertToOriginalArt()
        {
            NormalizeAIPreviewState();

            List<PlacedObject> targets = placedObjects.Where(o => selectedObjects.Contains(o)).ToList();
            if (targets.Count == 0)
                targets = new List<PlacedObject>(placedObjects);
            if (targets.Count == 0) { SetAIStatus("Nothing to revert", Color.Orange); return; }

            int revertedCount = 0;
            int skippedCount = 0;
            PushUndo();
            foreach (var obj in targets)
            {
                int gidVal;
                if (!TryParseGraphicId(obj.GraphicId, out gidVal))
                {
                    skippedCount++;
                    continue;
                }
                // LoadStaticItemImage returns a SHARED cached instance: it must
                // never be disposed (a previous revert disposing an aliased
                // Image is what poisoned the cache and crashed here with
                // "Parameter is not valid"). Clone first, dispose only owned
                // images, and evict a dead cache entry so disk reloads it.
                Image diskArt = null;
                try { diskArt = LoadStaticItemImage(gidVal); }
                catch (Exception ex)
                {
                    outputTextBox.AppendText($"Revert OG {obj.GraphicId} failed: {ex.Message}\r\n");
                    skippedCount++;
                    continue;
                }
                Bitmap fresh = TryCloneImage(diskArt);
                if (fresh == null)
                {
                    // Cached instance is dead (disposed upstream): drop it so
                    // the retry reads from disk instead of the corpse.
                    EvictStaticItemCache(gidVal);
                    try { diskArt = LoadStaticItemImage(gidVal); }
                    catch (Exception ex)
                    {
                        outputTextBox.AppendText($"Revert OG {obj.GraphicId} failed: {ex.Message}\r\n");
                        skippedCount++;
                        continue;
                    }
                    fresh = TryCloneImage(diskArt);
                }
                if (fresh == null)
                {
                    outputTextBox.AppendText($"Revert OG {obj.GraphicId}: no usable art found on disk\r\n");
                    skippedCount++;
                    continue;
                }
                if (obj.Image != null && !ReferenceEquals(obj.Image, diskArt))
                {
                    try { obj.Image.Dispose(); }
                    catch { }
                }
                if (obj.OriginalImage != null && !ReferenceEquals(obj.OriginalImage, diskArt))
                {
                    try { obj.OriginalImage.Dispose(); }
                    catch { }
                }
                obj.Image = fresh;
                obj.OriginalImage = null;
                revertedCount++;
                outputTextBox.AppendText($"Reverted {obj.GraphicId} to original UO art\r\n");

                // Make the restoration savable/pushable (covers the case where AI
                // art was already saved to the MUL and disk needs the OG written back).
                MarkArtModified(gidVal, fresh);
            }
            if (revertedCount == 0) SetAIStatus("Nothing reverted to OG", Color.Orange);
            else
            {
                designPictureBox.Invalidate();
                SetAIStatus($"✓ Reverted {revertedCount} object(s) to OG art", Color.Green);
                UpdatePendingChangesUI();
            }
            if (skippedCount > 0)
                outputTextBox.AppendText($"Revert OG skipped {skippedCount} object(s)\r\n");
        }
    }
}
