using MeesaMultisMaker.Mul;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    public partial class GumpEditorForm
    {
        private void LoadMulFolder()
        {
            var config = AppConfig.Instance;
            
            // First try the configured MUL folder
            mulFolder = config.MulFolderPath;
            
            // If not configured, try to find it
            if (string.IsNullOrEmpty(mulFolder) || !Directory.Exists(mulFolder))
            {
                mulFolder = config.FindMulFolder();
            }

            if (string.IsNullOrEmpty(mulFolder))
            {
                SetStatus("Warning: MUL folder not configured. Set it in Settings.");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"GumpEditorForm: Initial MUL folder: {mulFolder}");

            // Check if gump files exist (MUL or UOP format)
            string gumpIdxPath = Path.Combine(mulFolder, "gumpidx.mul");
            string gumpMulPath = Path.Combine(mulFolder, "gumpart.mul");
            string gumpUopPath = Path.Combine(mulFolder, "gumpartLegacyMUL.uop");

            bool hasMul = File.Exists(gumpIdxPath) && File.Exists(gumpMulPath);
            bool hasUop = File.Exists(gumpUopPath);

            System.Diagnostics.Debug.WriteLine($"GumpEditorForm: MUL folder: {mulFolder}");
            System.Diagnostics.Debug.WriteLine($"GumpEditorForm: Checking gumpidx.mul: {gumpIdxPath} - Exists: {File.Exists(gumpIdxPath)}");
            System.Diagnostics.Debug.WriteLine($"GumpEditorForm: Checking gumpart.mul: {gumpMulPath} - Exists: {File.Exists(gumpMulPath)}");
            System.Diagnostics.Debug.WriteLine($"GumpEditorForm: Checking gumpartLegacyMUL.uop: {gumpUopPath} - Exists: {File.Exists(gumpUopPath)}");
            System.Diagnostics.Debug.WriteLine($"GumpEditorForm: MUL format available: {hasMul}");
            System.Diagnostics.Debug.WriteLine($"GumpEditorForm: UOP format available: {hasUop}");

            // If no gump files in current folder, check if UOForever path has them
            if (!hasMul && !hasUop)
            {
                // Try UOForever path specifically
                string uofPath = @"C:\Program Files (x86)\UOForever\UO";
                if (Directory.Exists(uofPath))
                {
                    string uofGumpUop = Path.Combine(uofPath, "gumpartLegacyMUL.uop");
                    if (File.Exists(uofGumpUop))
                    {
                        System.Diagnostics.Debug.WriteLine($"GumpEditorForm: Found gumpartLegacyMUL.uop in UOForever path, switching to: {uofPath}");
                        mulFolder = uofPath;
                        hasUop = true;
                    }
                }
            }

            // List all files in the folder that contain "gump" in the name for debugging
            try
            {
                var gumpFiles = Directory.GetFiles(mulFolder, "*gump*", SearchOption.TopDirectoryOnly);
                System.Diagnostics.Debug.WriteLine($"GumpEditorForm: Files containing 'gump' in folder:");
                foreach (var file in gumpFiles)
                {
                    System.Diagnostics.Debug.WriteLine($"  - {Path.GetFileName(file)}");
                }
            }
            catch { }

            if (!hasMul && !hasUop)
            {
                SetStatus($"No gump files found in: {mulFolder}");
                return;
            }

            LoadGumpPalette();
        }

        private async void BeginLoadMulFolder()
        {
            var config = AppConfig.Instance;
            
            // First try the configured MUL folder
            mulFolder = config.MulFolderPath;
            
            // If not configured, try to find it
            if (string.IsNullOrEmpty(mulFolder) || !Directory.Exists(mulFolder))
            {
                mulFolder = config.FindMulFolder();
            }

            if (string.IsNullOrEmpty(mulFolder))
            {
                SetStatus("Warning: MUL folder not configured. Set it in Settings.");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"GumpEditorForm.BeginLoadMulFolder: Initial folder: {mulFolder}");

            // Check if gump files exist
            string gumpUopPath = Path.Combine(mulFolder, "gumpartLegacyMUL.uop");
            string gumpIdxPath = Path.Combine(mulFolder, "gumpidx.mul");
            string gumpMulPath = Path.Combine(mulFolder, "gumpart.mul");

            bool hasUop = File.Exists(gumpUopPath);
            bool hasMul = File.Exists(gumpIdxPath) && File.Exists(gumpMulPath);

            System.Diagnostics.Debug.WriteLine($"GumpEditorForm.BeginLoadMulFolder: hasMul={hasMul}, hasUop={hasUop}");

            if (!hasMul && !hasUop)
            {
                // Try UOForever path
                string uofPath = @"C:\Program Files (x86)\UOForever\UO";
                if (Directory.Exists(uofPath))
                {
                    string uofGumpUop = Path.Combine(uofPath, "gumpartLegacyMUL.uop");
                    if (File.Exists(uofGumpUop))
                    {
                        System.Diagnostics.Debug.WriteLine($"GumpEditorForm.BeginLoadMulFolder: Found gumps in UOForever path: {uofPath}");
                        mulFolder = uofPath;
                        hasUop = true;
                    }
                }
                
                if (!hasMul && !hasUop)
                {
                    SetStatus($"No gump files found in: {mulFolder}");
                    return;
                }
            }

            SetStatus("Loading GUMP palette...");
            _isLoading = true;
            _loadingCts = new System.Threading.CancellationTokenSource();

            try
            {
                // Start precomputing hashes in parallel (background)
                var precomputeTask = Task.Run(() => UopGumpReader.PrecomputeHashes());

                // Load valid gump IDs asynchronously
                var ids = await Task.Run(() => GumpArtReader.GetValidGumpIds(mulFolder), _loadingCts.Token);

                System.Diagnostics.Debug.WriteLine($"GumpEditorForm.BeginLoadMulFolder: First attempt got {ids.Count} gumps");

                // If no gumps found and MUL returned nothing, try UOP directly
                if (ids.Count == 0 && UopGumpReader.UopFileExists(mulFolder))
                {
                    System.Diagnostics.Debug.WriteLine($"GumpEditorForm.BeginLoadMulFolder: MUL returned 0, trying UOP directly...");
                    GumpArtReader.ClearCache();
                    ids = await Task.Run(() => UopGumpReader.GetValidGumpIds(mulFolder), _loadingCts.Token);
                    System.Diagnostics.Debug.WriteLine($"GumpEditorForm.BeginLoadMulFolder: UOP returned {ids.Count} gumps");
                    
                    // Mark this folder as using UOP format for future loads
                    if (ids.Count > 0)
                    {
                        GumpArtReader.MarkAsUopFolder(mulFolder);
                    }
                }

                // If still no gumps and we're not using UOForever path, try it
                if (ids.Count == 0)
                {
                    string uofPath = @"C:\Program Files (x86)\UOForever\UO";
                    if (Directory.Exists(uofPath) && uofPath != mulFolder)
                    {
                        string uofGumpUop = Path.Combine(uofPath, "gumpartLegacyMUL.uop");
                        if (File.Exists(uofGumpUop))
                        {
                            System.Diagnostics.Debug.WriteLine($"GumpEditorForm.BeginLoadMulFolder: Trying UOForever path: {uofPath}");
                            mulFolder = uofPath;
                            GumpArtReader.ClearCache();
                            UopGumpReader.ClearCache();
                            ids = await Task.Run(() => UopGumpReader.GetValidGumpIds(mulFolder), _loadingCts.Token);
                            System.Diagnostics.Debug.WriteLine($"GumpEditorForm.BeginLoadMulFolder: UOForever UOP returned {ids.Count} gumps");
                        }
                    }
                }

                if (_loadingCts.Token.IsCancellationRequested) return;

                validGumpIds = ids;
                filteredGumpIds = new List<int>(validGumpIds);

                System.Diagnostics.Debug.WriteLine($"GumpEditorForm.BeginLoadMulFolder: Final count: {validGumpIds.Count} valid gump IDs");

                // Populate list on UI thread
                if (InvokeRequired)
                {
                    Invoke(new Action(() => PopulateGumpList()));
                }
                else
                {
                    PopulateGumpList();
                }

                SetStatus($"Loaded {validGumpIds.Count} GUMPs");
            }
            catch (OperationCanceledException)
            {
                SetStatus("Loading cancelled");
            }
            catch (Exception ex)
            {
                SetStatus($"Error loading GUMPs: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"GumpEditorForm: Error loading GUMPs: {ex}");
            }
            finally
            {
                _isLoading = false;
            }
        }

        private void LoadGumpPalette()
        {
            if (string.IsNullOrEmpty(mulFolder))
                return;

            try
            {
                SetStatus("Loading GUMP palette...");
                Application.DoEvents();

                // Check what gump files exist
                string gumpIdxPath = Path.Combine(mulFolder, "gumpidx.mul");
                string gumpMulPath = Path.Combine(mulFolder, "gumpart.mul");
                string gumpUopPath = Path.Combine(mulFolder, "gumpartLegacyMUL.uop");
                
                bool hasMul = File.Exists(gumpIdxPath) && File.Exists(gumpMulPath);
                bool hasUop = File.Exists(gumpUopPath);
                
                System.Diagnostics.Debug.WriteLine($"GumpEditorForm.LoadGumpPalette: mulFolder={mulFolder}");
                System.Diagnostics.Debug.WriteLine($"GumpEditorForm.LoadGumpPalette: hasMul={hasMul}, hasUop={hasUop}");

                // Try to load gumps
                validGumpIds = GumpArtReader.GetValidGumpIds(mulFolder);
                
                System.Diagnostics.Debug.WriteLine($"GumpEditorForm.LoadGumpPalette: First attempt got {validGumpIds.Count} gumps");
                
                // If no gumps found and we're using MUL, try forcing UOP mode
                if (validGumpIds.Count == 0 && hasMul && hasUop)
                {
                    System.Diagnostics.Debug.WriteLine($"GumpEditorForm.LoadGumpPalette: MUL returned 0 gumps but UOP exists, trying UOP directly...");
                    GumpArtReader.ClearCache();
                    validGumpIds = UopGumpReader.GetValidGumpIds(mulFolder);
                    System.Diagnostics.Debug.WriteLine($"GumpEditorForm.LoadGumpPalette: UOP returned {validGumpIds.Count} gumps");
                }
                
                // If still no gumps and we're not using UOForever path, try it
                if (validGumpIds.Count == 0)
                {
                    string uofPath = @"C:\Program Files (x86)\UOForever\UO";
                    if (Directory.Exists(uofPath) && uofPath != mulFolder)
                    {
                        string uofGumpUop = Path.Combine(uofPath, "gumpartLegacyMUL.uop");
                        if (File.Exists(uofGumpUop))
                        {
                            System.Diagnostics.Debug.WriteLine($"GumpEditorForm.LoadGumpPalette: Trying UOForever path: {uofPath}");
                            SetStatus("Trying UOForever gump files...");
                            Application.DoEvents();
                            
                            mulFolder = uofPath;
                            GumpArtReader.ClearCache();
                            UopGumpReader.ClearCache();
                            validGumpIds = GumpArtReader.GetValidGumpIds(mulFolder);
                            System.Diagnostics.Debug.WriteLine($"GumpEditorForm.LoadGumpPalette: UOForever path returned {validGumpIds.Count} gumps");
                        }
                    }
                }
                
                filteredGumpIds = new List<int>(validGumpIds);

                // Populate ListView with items (thumbnails will load on scroll)
                PopulateGumpList();

                if (validGumpIds.Count == 0)
                {
                    SetStatus($"No GUMPs found - check MUL folder settings");
                }
                else
                {
                    SetStatus($"Loaded {validGumpIds.Count} GUMPs");
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Error loading GUMPs: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"GumpEditorForm: Error loading GUMPs: {ex}");
            }
        }

        private void PopulateGumpList()
        {
            // Just set the virtual list size - items are created on demand
            gumpListView.VirtualListSize = filteredGumpIds.Count;
            gumpListView.Invalidate();
        }

        /// <summary>
        /// Virtual mode: Create list item on demand when scrolling (DO NOT load images here - do it in DrawItem)
        /// </summary>
        private void GumpListView_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            if (e.ItemIndex < 0 || e.ItemIndex >= filteredGumpIds.Count)
            {
                e.Item = new ListViewItem("?");
                return;
            }

            int gumpId = filteredGumpIds[e.ItemIndex];

            // Just create the item - image loading happens in DrawItem (lazy loading)
            e.Item = new ListViewItem($"0x{gumpId:X4}")
            {
                Tag = gumpId
            };
        }

        /// <summary>
        /// OwnerDraw: Draw item with lazy image loading (same pattern as Form1.Palette.cs)
        /// </summary>
        private void GumpListView_DrawItem(object sender, DrawListViewItemEventArgs e)
        {
            // Background
            if (e.Item.Selected)
            {
                using (var brush = new SolidBrush(HolographicTheme.SelectionCyan))
                {
                    e.Graphics.FillRectangle(brush, e.Bounds);
                }
            }
            else
            {
                using (var brush = new SolidBrush(HolographicTheme.InputBackground))
                {
                    e.Graphics.FillRectangle(brush, e.Bounds);
                }
            }

            if (!(e.Item.Tag is int gumpId))
            {
                return;
            }

            // Lazy load image during paint (same as Form1)
            Bitmap img = null;
            if (gumpCache.TryGetValue(gumpId, out var cached))
            {
                img = cached;
            }
            else
            {
                // Load on demand
                try
                {
                    img = GumpArtReader.LoadGumpArt(mulFolder, gumpId);
                    if (img != null)
                    {
                        gumpCache.TryAdd(gumpId, img);
                    }
                }
                catch { }
            }

            // Draw the image centered in the item bounds
            if (img != null)
            {
                // Calculate scaled size to fit in bounds
                int maxSize = Math.Min(e.Bounds.Width - 4, e.Bounds.Height - 20);
                float scale = Math.Min((float)maxSize / img.Width, (float)maxSize / img.Height);
                scale = Math.Min(scale, 1.0f); // Don't upscale

                int drawW = (int)(img.Width * scale);
                int drawH = (int)(img.Height * scale);
                int drawX = e.Bounds.Left + (e.Bounds.Width - drawW) / 2;
                int drawY = e.Bounds.Top + 2;

                try
                {
                    e.Graphics.DrawImage(img, drawX, drawY, drawW, drawH);
                }
                catch { }
            }

            // Build display text - include dimensions when sorting by Width/Height
            string displayText = e.Item.Text;
            int sortIdx = sortComboBox?.SelectedIndex ?? 0;
            if (sortIdx == 1 || sortIdx == 2) // Width or Height sort
            {
                var dims = GetGumpDimensions(gumpId);
                if (dims.Width > 0 && dims.Height > 0)
                    displayText = $"0x{gumpId:X4} {dims.Width}x{dims.Height}";
            }

            // Draw text below image
            var textRect = new Rectangle(
                e.Bounds.Left,
                e.Bounds.Bottom - 18,
                e.Bounds.Width,
                18
            );

            TextRenderer.DrawText(
                e.Graphics,
                displayText,
                gumpListView.Font,
                textRect,
                e.Item.Selected ? Color.White : HolographicTheme.TextPrimary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top
            );

            // Draw border
            using (var pen = new Pen(HolographicTheme.BorderDark))
            {
                e.Graphics.DrawRectangle(pen, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
            }

            // Focus rectangle
            if (e.Item.Focused)
            {
                ControlPaint.DrawFocusRectangle(e.Graphics, e.Bounds);
            }
        }

        /// <summary>
        /// Cache virtual items for smoother scrolling - pre-load images in background
        /// </summary>
        private void GumpListView_CacheVirtualItems(object sender, CacheVirtualItemsEventArgs e)
        {
            // Pre-load images for visible range in background (same as Form1)
            int start = e.StartIndex;
            int end = Math.Min(e.EndIndex, filteredGumpIds.Count - 1);

            Task.Run(() =>
            {
                for (int i = start; i <= end && i < filteredGumpIds.Count; i++)
                {
                    int gumpId = filteredGumpIds[i];

                    // Check if already cached
                    if (gumpCache.ContainsKey(gumpId))
                        continue;

                    try
                    {
                        var img = GumpArtReader.LoadGumpArt(mulFolder, gumpId);
                        if (img != null)
                        {
                            gumpCache.TryAdd(gumpId, img);
                        }
                    }
                    catch { }
                }
            });
        }

        private void SearchBox_TextChanged(object sender, EventArgs e)
        {
            string search = searchBox.Text.Trim();
            if (string.IsNullOrEmpty(search) || search == "Search...")
            {
                filteredGumpIds = new List<int>(validGumpIds);
            }
            else
            {
                string searchUpper = search.ToUpperInvariant();
                bool isHex = searchUpper.StartsWith("0X");
                string searchHex = isHex ? searchUpper.Substring(2) : searchUpper;

                filteredGumpIds = validGumpIds.Where(id =>
                {
                    string hexStr = id.ToString("X4");
                    return hexStr.Contains(searchHex) || id.ToString().Contains(searchUpper);
                }).ToList();
            }

            ApplySortToFilteredList();
            PopulateGumpList();
        }

        private void SortComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            ApplySortToFilteredList();
            PopulateGumpList();
        }

        private void SortDescCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            ApplySortToFilteredList();
            PopulateGumpList();
        }

        /// <summary>
        /// Applies the current sort selection to filteredGumpIds in-place
        /// </summary>
        private void ApplySortToFilteredList()
        {
            if (sortComboBox == null) return;

            int sortIndex = sortComboBox.SelectedIndex;
            bool descending = sortDescCheckBox?.Checked ?? false;

            switch (sortIndex)
            {
                case 0: // Sort by ID
                    if (descending)
                        filteredGumpIds.Sort((a, b) => b.CompareTo(a));
                    else
                        filteredGumpIds.Sort();
                    break;

                case 1: // Sort by Width
                    filteredGumpIds.Sort((a, b) =>
                    {
                        var sizeA = GetGumpDimensions(a);
                        var sizeB = GetGumpDimensions(b);
                        int cmp = sizeA.Width.CompareTo(sizeB.Width);
                        if (cmp == 0) cmp = a.CompareTo(b); // stable tie-break by ID
                        return descending ? -cmp : cmp;
                    });
                    break;

                case 2: // Sort by Height
                    filteredGumpIds.Sort((a, b) =>
                    {
                        var sizeA = GetGumpDimensions(a);
                        var sizeB = GetGumpDimensions(b);
                        int cmp = sizeA.Height.CompareTo(sizeB.Height);
                        if (cmp == 0) cmp = a.CompareTo(b);
                        return descending ? -cmp : cmp;
                    });
                    break;
            }
        }

        /// <summary>
        /// Gets cached gump dimensions (width/height) for sorting.
        /// Tries the fast index-based lookup first, then falls back to loading the image.
        /// </summary>
        private Size GetGumpDimensions(int gumpId)
        {
            if (_gumpDimensionCache.TryGetValue(gumpId, out var cached))
                return cached;

            int w = 0, h = 0;

            // Try fast dimension lookup from index (MUL only, no image load)
            if (!string.IsNullOrEmpty(mulFolder) && GumpArtReader.TryGetDimensions(mulFolder, gumpId, out w, out h))
            {
                var size = new Size(w, h);
                _gumpDimensionCache[gumpId] = size;
                return size;
            }

            // Fallback: check if already in image cache
            if (gumpCache.TryGetValue(gumpId, out var bmp) && bmp != null)
            {
                try
                {
                    var size = new Size(bmp.Width, bmp.Height);
                    _gumpDimensionCache[gumpId] = size;
                    return size;
                }
                catch { }
            }

            // Last resort: load the image to get dimensions
            try
            {
                bmp = GumpArtReader.LoadGumpArt(mulFolder, gumpId);
                if (bmp != null)
                {
                    var size = new Size(bmp.Width, bmp.Height);
                    _gumpDimensionCache[gumpId] = size;
                    gumpCache.TryAdd(gumpId, bmp);
                    return size;
                }
            }
            catch { }

            var fallback = new Size(0, 0);
            _gumpDimensionCache[gumpId] = fallback;
            return fallback;
        }

        private Bitmap LoadGumpThumbnail(int gumpId)
        {
            try
            {
                var bmp = GumpArtReader.LoadGumpArt(mulFolder, gumpId);
                if (bmp == null)
                {
                    return null;
                }

                // Create thumbnail with a visible background
                int thumbSize = 64;
                var thumb = new Bitmap(thumbSize, thumbSize, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(thumb))
                {
                    // Use a dark gray background so transparent areas are visible
                    g.Clear(Color.FromArgb(255, 40, 40, 45));
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                    float scale = Math.Min((float)thumbSize / bmp.Width, (float)thumbSize / bmp.Height);
                    int newW = Math.Max(1, (int)(bmp.Width * scale));
                    int newH = Math.Max(1, (int)(bmp.Height * scale));
                    int x = (thumbSize - newW) / 2;
                    int y = (thumbSize - newH) / 2;

                    g.DrawImage(bmp, x, y, newW, newH);
                }

                // Cache full image
                lock (gumpCache)
                {
                    if (!gumpCache.ContainsKey(gumpId))
                        gumpCache[gumpId] = bmp;
                    else
                        bmp.Dispose();
                }

                return thumb;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GumpEditorForm: Exception loading thumbnail for 0x{gumpId:X4}: {ex.Message}");
                return null;
            }
        }

        private Bitmap GetGumpImage(int gumpId)
        {
            lock (gumpCache)
            {
                if (gumpCache.TryGetValue(gumpId, out var cached))
                    return cached;
            }

            var bmp = GumpArtReader.LoadGumpArt(mulFolder, gumpId);
            if (bmp != null)
            {
                lock (gumpCache)
                {
                    if (!gumpCache.ContainsKey(gumpId))
                        gumpCache[gumpId] = bmp;
                }
            }
            return bmp;
        }

        private void GumpListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (gumpListView.SelectedIndices.Count > 0)
            {
                int index = gumpListView.SelectedIndices[0];
                if (index >= 0 && index < filteredGumpIds.Count)
                {
                    selectedPaletteGump = filteredGumpIds[index];
                    SetStatus($"Selected GUMP: 0x{selectedPaletteGump:X4}");
                }
            }
        }

        private void GumpListView_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            // Add selected gump to canvas
            if (selectedPaletteGump >= 0)
            {
                AddGumpToCanvas(selectedPaletteGump, canvasWidth / 2, canvasHeight / 2);
            }
        }

        /// <summary>
        /// Initiate drag operation when user starts dragging from palette
        /// </summary>
        private void GumpListView_ItemDrag(object sender, ItemDragEventArgs e)
        {
            // Collect all selected gump IDs
            var selectedIds = new List<int>();

            foreach (int index in gumpListView.SelectedIndices)
            {
                if (index >= 0 && index < filteredGumpIds.Count)
                {
                    selectedIds.Add(filteredGumpIds[index]);
                }
            }

            if (selectedIds.Count == 0 && selectedPaletteGump >= 0)
            {
                // Fallback to single selection if no indices selected
                selectedIds.Add(selectedPaletteGump);
            }

            if (selectedIds.Count > 0)
            {
                // Use int array for multiple items, or single int for single item
                if (selectedIds.Count == 1)
                {
                    gumpListView.DoDragDrop(selectedIds[0], DragDropEffects.Copy);
                }
                else
                {
                    gumpListView.DoDragDrop(selectedIds.ToArray(), DragDropEffects.Copy);
                }
            }
        }
    }
}
