using MeesaMultisMaker.Mul;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    public partial class Form1
    {
        // MUL files are the preferred format, but UOP-only is supported as fallback
        private bool useMulFiles = true;
        private bool useTecmoExpanded = false;

        // Flag to indicate we're using UOP-only mode (no art.mul, only artLegacyMUL.uop)
        private bool useUopOnly = false;

        // List of valid static item IDs when using MUL files
        // For Tecmo, we need int because item IDs can exceed 65535
        private List<int> validStaticIdsInt = new List<int>();
        private List<ushort> validStaticIds = new List<ushort>();

        // HashSet of valid IDs for fast lookup
        private HashSet<int> validStaticIdSetInt = new HashSet<int>();
        private HashSet<ushort> validStaticIdSet = new HashSet<ushort>();

        // Filtered list for search when using MUL files
        private List<int> filteredStaticIdsInt = new List<int>();
        private List<ushort> filteredStaticIds = new List<ushort>();

        // Flag to show empty slots in the palette
        private bool showEmptySlots = false;

        // Flag to track if empty slots have been calculated
        private bool emptySlotsCached = false;

        // Cached list of empty slots (only populated when needed)
        private List<int> emptyStaticIdsInt = new List<int>();
        private List<ushort> emptyStaticIds = new List<ushort>();

        // Maximum item ID found in the index (for calculating empty slots)
        private int maxItemIdInIndexInt = 0;
        private ushort maxItemIdInIndex = 0;

        // Palette size filter controls
        private NumericUpDown paletteMinWidthFilter;
        private NumericUpDown paletteMaxWidthFilter;
        private NumericUpDown paletteMinHeightFilter;
        private NumericUpDown paletteMaxHeightFilter;

        private void Form1_Load(object sender, EventArgs e)
        {
            try
            {
                // Initialize palette size filter controls
                InitializePaletteSizeFilters();

                // Initialize show empty slots checkbox
                InitializeShowEmptySlotsCheckbox();

                // Load from AppConfig first, then fall back to legacy settings
                var config = AppConfig.Instance;

                // Use MUL files only
                string mulFolder = config.FindMulFolder();
                if (!string.IsNullOrEmpty(mulFolder) && Directory.Exists(mulFolder))
                {
                    // Check if art.mul exists (preferred)
                    string artMulPath = Path.Combine(mulFolder, "art.mul");
                    string artIdxPath = Path.Combine(mulFolder, "artidx.mul");
                    string artUopPath = Path.Combine(mulFolder, "artLegacyMUL.uop");

                    // Detect art file format
                    var format = config.GetArtFileFormat(mulFolder);
                    
                    if (format == ArtFileFormat.TecmoExpanded)
                    {
                        // Tecmo Expanded Art format
                        artFolderPath = mulFolder;
                        useUopOnly = false;
                        useTecmoExpanded = true;
                        useMulFiles = true;
                        LoadArtFromTecmo();
                    }
                    else if (File.Exists(artMulPath) && File.Exists(artIdxPath))
                    {
                        // Use MUL files directly (OSI style)
                        artFolderPath = mulFolder;
                        useUopOnly = false;
                        useTecmoExpanded = false;
                        LoadArtFromMul();
                    }
                    else if (File.Exists(artUopPath))
                    {
                        // Fallback to UOP-only mode (UOForever style)
                        artFolderPath = mulFolder;
                        useUopOnly = true;
                        useTecmoExpanded = false;
                        LoadArtFromUop();
                    }
                    else
                    {
                        // No MUL or UOP files found - show error
                        MessageBox.Show($"No art files found.\n\nPlease configure the MUL folder path in Settings.\nThe folder must contain either:\n- art.mul and artidx.mul (OSI style)\n- artLegacyMUL.uop (UOForever style)\n- Tecmo Expanded Art (art.mul + artidx.mul with 262K entries)",
                            "Art Files Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                else
                {
                    // No MUL folder found - show error
                    MessageBox.Show($"No art files found.\n\nPlease configure the MUL folder path in Settings.\nThe folder must contain either:\n- art.mul and artidx.mul (OSI style)\n- artLegacyMUL.uop (UOForever style)\n- Tecmo Expanded Art (art.mul + artidx.mul with 262K entries)",
                        "Art Files Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                // Check for updates on startup (async, non-blocking)
                CheckForUpdatesOnStartup();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during initialization: {ex.Message}\n\n{ex.StackTrace}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Check for updates on startup (async, non-blocking)
        /// </summary>
        private async void CheckForUpdatesOnStartup()
        {
            try
            {
                var config = AppConfig.Instance;
                
                // Skip if user disabled update checks
                if (!config.CheckForUpdatesOnStartup)
                    return;

                // Only check once per day to avoid excessive API calls
                if ((DateTime.Now - config.LastUpdateCheck).TotalHours < 24)
                    return;

                // Update last check time
                config.LastUpdateCheck = DateTime.Now;
                config.Save();

                // Check for updates silently (only show dialog if update available)
                var updateInfo = await UpdateChecker.CheckForUpdatesAsync();
                
                if (updateInfo != null && updateInfo.UpdateAvailable)
                {
                    // Skip if user chose to skip this version
                    if (!string.IsNullOrEmpty(config.SkippedVersion) && 
                        config.SkippedVersion == updateInfo.TagName)
                        return;

                    // Show update dialog
                    await UpdateChecker.CheckAndPromptAsync(this, silent: true);
                }
            }
            catch (Exception ex)
            {
                // Silently fail - don't interrupt user with update check errors
                System.Diagnostics.Debug.WriteLine($"Update check error: {ex.Message}");
            }
        }

        /// <summary>
        /// Initialize the show empty slots checkbox
        /// </summary>
        private void InitializeShowEmptySlotsCheckbox()
        {
            if (showEmptySlotsCheckBox == null) return;

            showEmptySlotsCheckBox.Checked = false;
            showEmptySlotsCheckBox.CheckedChanged += ShowEmptySlotsCheckBox_CheckedChanged;

            // Apply theme
            HolographicTheme.ApplyToCheckBox(showEmptySlotsCheckBox);

            // Add tooltip
            var tooltip = new ToolTip();
            tooltip.SetToolTip(showEmptySlotsCheckBox, "Show empty/unassigned art slots. Click an empty slot to assign artwork from canvas.");
        }

        /// <summary>
        /// Handle show empty slots checkbox change
        /// </summary>
        private void ShowEmptySlotsCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            showEmptySlots = showEmptySlotsCheckBox.Checked;

            // Lazily calculate empty slots only when first needed
            if (showEmptySlots && !emptySlotsCached)
            {
                CalculateEmptySlots();
            }

            // Re-filter the palette
            FilterPalette(searchTextBox.Text);
        }

        /// <summary>
        /// Calculate empty slots lazily (only when user checks the checkbox)
        /// </summary>
        private void CalculateEmptySlots()
        {
            if (emptySlotsCached) return;

            emptyStaticIds.Clear();

            // Only iterate up to the max ID found in the index + some buffer
            // This is much faster than iterating all 65k IDs
            ushort maxToCheck = (ushort)Math.Min(maxItemIdInIndex + 1000, 0xFFFF);

            for (ushort id = 0; id <= maxToCheck; id++)
            {
                if (!validStaticIdSet.Contains(id))
                {
                    emptyStaticIds.Add(id);
                }
            }

            emptySlotsCached = true;
            statusLabel_SetText($"Found {emptyStaticIds.Count:N0} empty slots (up to ID 0x{maxToCheck:X4})");
        }

        /// <summary>
        /// Load art directly from MUL files (OSI style)
        /// </summary>
        private void LoadArtFromMul()
        {
            if (isLoading) return;

            try
            {
                isLoading = true;
                imageCache.Clear();
                idToPath.Clear();
                validStaticIds.Clear();
                validStaticIdSet.Clear();
                emptyStaticIds.Clear();
                emptySlotsCached = false;
                filteredStaticIds.Clear();
                maxItemIdInIndex = 0;

                // Load TileData for item names and properties
                if (!tileDataReader.IsLoaded)
                {
                    string tiledataPath = Path.Combine(artFolderPath, "tiledata.mul");
                    if (File.Exists(tiledataPath))
                    {
                        bool loaded = tileDataReader.Load(artFolderPath);
                        if (loaded && tileDataReader.IsLoaded)
                            statusLabel_SetText("TileData loaded successfully - item names available.");
                        else
                            statusLabel_SetText($"Warning: TileData failed to load from {tiledataPath}");
                    }
                    else
                    {
                        statusLabel_SetText($"Note: tiledata.mul not found at {tiledataPath} - item names unavailable.");
                    }
                }

                statusLabel_SetText("Scanning art.mul for valid items...");
                Application.DoEvents();

                string artIdxPath = Path.Combine(artFolderPath, "artidx.mul");

                // Scan the index file to find all valid static items
                // Static items start at index 0x4000 in the art files
                const int STATIC_OFFSET = 0x4000;
                const int MAX_STATIC_ID = 0xFFFF; // Maximum possible item ID

                using (var idxStream = File.OpenRead(artIdxPath))
                using (var idxReader = new BinaryReader(idxStream))
                {
                    long maxIndex = idxStream.Length / 12; // 12 bytes per entry

                    for (ushort itemId = 0; itemId <= MAX_STATIC_ID; itemId++)
                    {
                        int index = itemId + STATIC_OFFSET;

                        if (index >= maxIndex)
                            break;

                        long idxPosition = (long)index * 12;
                        idxStream.Seek(idxPosition, SeekOrigin.Begin);

                        int offset = idxReader.ReadInt32();
                        int length = idxReader.ReadInt32();
                        int extra = idxReader.ReadInt32();

                        // Valid entry check
                        if (offset >= 0 && length > 0)
                        {
                            validStaticIds.Add(itemId);
                            validStaticIdSet.Add(itemId);
                            maxItemIdInIndex = itemId;

                            // Also add to idToPath for compatibility with existing code
                            string hexId = $"0x{itemId:X4}";
                            if (!idToPath.ContainsKey(hexId))
                            {
                                idToPath[hexId] = hexId; // Store the ID itself as the "path"
                            }
                            string rawHex = itemId.ToString("X4").ToUpperInvariant();
                            if (!idToPath.ContainsKey(rawHex))
                            {
                                idToPath[rawHex] = hexId;
                            }
                        }

                        // Progress update every 5000 items (less frequent)
                        if (itemId % 5000 == 0)
                        {
                            statusLabel_SetText($"Scanning art.mul... {itemId:N0} / {MAX_STATIC_ID:N0}");
                            Application.DoEvents();
                        }
                    }
                }

                filteredStaticIds = new List<ushort>(validStaticIds);

                if (validStaticIds.Count == 0)
                {
                    MessageBox.Show($"No valid static items found in art.mul at:\n{artFolderPath}",
                        "No Items Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    isLoading = false;
                    return;
                }

                // Update palette list view
                paletteListView.VirtualListSize = filteredStaticIds.Count;
                this.Text = $"Meesa Multis Maker {UpdateChecker.CurrentVersionString} - github.com/MeesaJarJar - {validStaticIds.Count:N0} items from art.mul";

                statusLabel_SetText($"Loaded {validStaticIds.Count:N0} static items from art.mul");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading art from MUL files: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                isLoading = false;
            }
        }

        /// <summary>
        /// Load art from Tecmo Expanded Art format (art.mul + artidx.mul with 0x40000 entries)
        /// </summary>
        private void LoadArtFromTecmo()
        {
            if (isLoading) return;

            try
            {
                isLoading = true;
                imageCache.Clear();
                idToPath.Clear();
                validStaticIdsInt.Clear();
                validStaticIdSetInt.Clear();
                emptyStaticIdsInt.Clear();
                emptySlotsCached = false;
                filteredStaticIdsInt.Clear();
                maxItemIdInIndexInt = 0;
                
                // Also clear ushort versions for compatibility
                validStaticIds.Clear();
                validStaticIdSet.Clear();
                emptyStaticIds.Clear();
                filteredStaticIds.Clear();
                maxItemIdInIndex = 0;

                // Load TileData for item names and properties
                if (!tileDataReader.IsLoaded)
                {
                    string tiledataPath = Path.Combine(artFolderPath, "tiledata.mul");
                    if (File.Exists(tiledataPath))
                    {
                        bool loaded = tileDataReader.Load(artFolderPath);
                        if (loaded && tileDataReader.IsLoaded)
                            statusLabel_SetText("TileData loaded successfully - item names available.");
                        else
                            statusLabel_SetText($"Warning: TileData failed to load from {tiledataPath}");
                    }
                    else
                    {
                        statusLabel_SetText($"Note: tiledata.mul not found at {tiledataPath} - item names unavailable.");
                    }
                }

                statusLabel_SetText("Scanning Tecmo Expanded art.mul for valid items...");
                Application.DoEvents();

                // Clear TecmoArtReader cache to force re-indexing
                TecmoArtReader.ClearCache();

                // Get valid item IDs directly from the Tecmo index (fast - doesn't load images)
                var tecmoValidIds = TecmoArtReader.GetValidStaticItemIds(artFolderPath);

                if (tecmoValidIds.Count == 0)
                {
                    MessageBox.Show($"No valid static items found in Tecmo Expanded art.mul at:\n{artFolderPath}",
                        "No Items Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    isLoading = false;
                    return;
                }

                // Just add the valid IDs
                foreach (var itemId in tecmoValidIds)
                {
                    validStaticIdsInt.Add(itemId);
                    validStaticIdSetInt.Add(itemId);
                    if (itemId > maxItemIdInIndexInt)
                        maxItemIdInIndexInt = itemId;

                    // Also add to ushort lists if itemId fits in ushort (for compatibility)
                    if (itemId <= 0xFFFF)
                    {
                        validStaticIds.Add((ushort)itemId);
                        validStaticIdSet.Add((ushort)itemId);
                        if (itemId > maxItemIdInIndex)
                            maxItemIdInIndex = (ushort)itemId;
                    }

                    // Also add to idToPath for compatibility
                    string hexId = $"0x{itemId:X4}";
                    if (!idToPath.ContainsKey(hexId))
                    {
                        idToPath[hexId] = hexId;
                    }
                    string rawHex = itemId.ToString("X4").ToUpperInvariant();
                    if (!idToPath.ContainsKey(rawHex))
                    {
                        idToPath[rawHex] = hexId;
                    }
                }

                // Sort valid IDs
                validStaticIdsInt.Sort();
                validStaticIds.Sort();

                filteredStaticIdsInt = new List<int>(validStaticIdsInt);
                filteredStaticIds = new List<ushort>(validStaticIds);

                // Update palette list view
                paletteListView.VirtualListSize = filteredStaticIdsInt.Count;
                this.Text = $"Meesa Multis Maker {UpdateChecker.CurrentVersionString} - github.com/MeesaJarJar - {validStaticIdsInt.Count:N0} items from Tecmo Expanded Art";

                statusLabel_SetText($"Loaded {validStaticIdsInt.Count:N0} static items from Tecmo Expanded Art");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading art from Tecmo Expanded Art: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                isLoading = false;
            }
        }

        /// <summary>
        /// Load art from UOP file only (UOForever style - no art.mul, only artLegacyMUL.uop)
        /// </summary>
        private void LoadArtFromUop()
        {
            if (isLoading) return;

            try
            {
                isLoading = true;
                imageCache.Clear();
                idToPath.Clear();
                validStaticIds.Clear();
                validStaticIdSet.Clear();
                emptyStaticIds.Clear();
                emptySlotsCached = false;
                filteredStaticIds.Clear();
                maxItemIdInIndex = 0;

                // Load TileData for item names and properties
                if (!tileDataReader.IsLoaded)
                {
                    string tiledataPath = Path.Combine(artFolderPath, "tiledata.mul");
                    if (File.Exists(tiledataPath))
                    {
                        bool loaded = tileDataReader.Load(artFolderPath);
                        if (loaded && tileDataReader.IsLoaded)
                            statusLabel_SetText("TileData loaded successfully - item names available.");
                        else
                            statusLabel_SetText($"Warning: TileData failed to load from {tiledataPath}");
                    }
                    else
                    {
                        statusLabel_SetText($"Note: tiledata.mul not found at {tiledataPath} - item names unavailable.");
                    }
                }

                statusLabel_SetText("Loading artLegacyMUL.uop index (UOForever mode)...");
                Application.DoEvents();

                // Clear UopArtReader cache to force re-indexing
                UopArtReader.ClearCache();

                // Get valid item IDs directly from the UOP index (fast - doesn't load images)
                var uopValidIds = UopArtReader.GetValidStaticItemIds(artFolderPath);

                if (uopValidIds.Count == 0)
                {
                    MessageBox.Show($"No valid static items found in artLegacyMUL.uop at:\n{artFolderPath}",
                        "No Items Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    isLoading = false;
                    return;
                }

                // Just add the valid IDs - don't iterate all 65k
                foreach (var itemId in uopValidIds)
                {
                    validStaticIds.Add(itemId);
                    validStaticIdSet.Add(itemId);
                    if (itemId > maxItemIdInIndex)
                        maxItemIdInIndex = itemId;

                    // Also add to idToPath for compatibility
                    string hexId = $"0x{itemId:X4}";
                    if (!idToPath.ContainsKey(hexId))
                    {
                        idToPath[hexId] = hexId;
                    }
                    string rawHex = itemId.ToString("X4").ToUpperInvariant();
                    if (!idToPath.ContainsKey(rawHex))
                    {
                        idToPath[rawHex] = hexId;
                    }
                }

                // Sort valid IDs
                validStaticIds.Sort();

                filteredStaticIds = new List<ushort>(validStaticIds);

                // Update palette list view
                paletteListView.VirtualListSize = filteredStaticIds.Count;
                this.Text = $"Meesa Multis Maker {UpdateChecker.CurrentVersionString} - github.com/MeesaJarJar - {validStaticIds.Count:N0} items from artLegacyMUL.uop (UOForever)";

                statusLabel_SetText($"Loaded {validStaticIds.Count:N0} static items from artLegacyMUL.uop (UOForever mode)");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading art from UOP file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                isLoading = false;
            }
        }

        /// <summary>
        /// Helper to set status label text (handles cross-thread if needed)
        /// </summary>
        private void statusLabel_SetText(string text)
        {
            if (outputTextBox.InvokeRequired)
            {
                outputTextBox.Invoke(new Action(() => outputTextBox.AppendText(text + "\r\n")));
            }
            else
            {
                outputTextBox.AppendText(text + "\r\n");
            }
        }

        /// <summary>
        /// Load image for a static item from MUL, UOP, or Tecmo files
        /// </summary>
        internal Image LoadStaticItemImage(int itemId)
        {
            string cacheKey = $"0x{itemId:X4}";

            // Check cache first
            lock (imageCache)
            {
                if (imageCache.TryGetValue(cacheKey, out var cached))
                {
                    return cached;
                }
            }

            // Load from appropriate source based on mode
            Bitmap img;
            if (useUopOnly)
            {
                img = UopArtReader.LoadStaticArt(artFolderPath, (ushort)itemId);
            }
            else if (useTecmoExpanded)
            {
                img = TecmoArtReader.LoadStaticArt(artFolderPath, itemId);
            }
            else
            {
                img = StaticArtReader.LoadStaticArt(artFolderPath, (ushort)itemId);
            }

            if (img != null)
            {
                lock (imageCache)
                {
                    if (!imageCache.ContainsKey(cacheKey))
                    {
                        imageCache[cacheKey] = img;
                    }
                }
            }

            return img;
        }

        /// <summary>
        /// Overload for backward compatibility with ushort item IDs
        /// </summary>
        internal Image LoadStaticItemImage(ushort itemId)
        {
            return LoadStaticItemImage((int)itemId);
}
        
        /// <summary>
        /// Get the current item ID for palette index
        /// </summary>
        internal int GetItemIdAtPaletteIndex(int index)
        {
            if (useTecmoExpanded)
            {
                if (index >= 0 && index < filteredStaticIdsInt.Count)
                {
                    return filteredStaticIdsInt[index];
                }
            }
            else
            {
                if (index >= 0 && index < filteredStaticIds.Count)
                {
                    return filteredStaticIds[index];
                }
            }
            return -1;
        }

        /// <summary>
        /// Get the current item ID for palette index (ushort overload for compatibility)
        /// </summary>
        internal ushort? GetItemIdAtPaletteIndexUshort(int index)
        {
            if (index >= 0 && index < filteredStaticIds.Count)
            {
                return filteredStaticIds[index];
            }
            return null;
        }

        /// <summary>
        /// Check if a given item ID is an empty slot (uses HashSet for O(1) lookup)
        /// </summary>
        internal bool IsEmptySlot(int itemId)
        {
            if (useTecmoExpanded)
            {
                return !validStaticIdSetInt.Contains(itemId);
            }
            return !validStaticIdSet.Contains((ushort)itemId);
        }

        /// <summary>
        /// Overload for ushort compatibility
        /// </summary>
        internal bool IsEmptySlot(ushort itemId)
        {
            return IsEmptySlot((int)itemId);
        }

        private void LoadArt()
        {
            if (isLoading) return;
            try
            {
                isLoading = true;
                imageCache.Clear();
                idToPath.Clear();

                // Use MUL, UOP, or Tecmo files
                var config = AppConfig.Instance;
                string mulFolder = config.FindMulFolder();
                if (!string.IsNullOrEmpty(mulFolder))
                {
                    var format = config.GetArtFileFormat(mulFolder);
                    
                    if (format == ArtFileFormat.TecmoExpanded)
                    {
                        // Use Tecmo Expanded Art
                        artFolderPath = mulFolder;
                        useUopOnly = false;
                        useTecmoExpanded = true;
                        isLoading = false;
                        LoadArtFromTecmo();
                        return;
                    }
                    else if (format == ArtFileFormat.MulFiles)
                    {
                        // Use MUL files (OSI style)
                        artFolderPath = mulFolder;
                        useUopOnly = false;
                        useTecmoExpanded = false;
                        isLoading = false;
                        LoadArtFromMul();
                        return;
                    }
                    else if (format == ArtFileFormat.UopOnly)
                    {
                        // Use UOP only (UOForever style)
                        artFolderPath = mulFolder;
                        useUopOnly = true;
                        useTecmoExpanded = false;
                        isLoading = false;
                        LoadArtFromUop();
                        return;
                    }
                }

                // No art files found
                MessageBox.Show($"No art files found.\n\nPlease configure the MUL folder path in Settings.\nThe folder must contain either:\n- art.mul and artidx.mul (OSI style)\n- artLegacyMUL.uop (UOForever style)\n- Tecmo Expanded Art (art.mul + artidx.mul with 262K entries)",
                    "Art Files Not Found", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading art: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { isLoading = false; }
        }

        /// <summary>
        /// Filter the palette list based on search text (searches ID, hex, and item name)
        /// </summary>
        internal void FilterPalette(string searchText)
        {
            // Build source list based on showEmptySlots flag
            List<int> sourceListInt;
            List<ushort> sourceListUshort;

            if (useTecmoExpanded)
            {
                if (showEmptySlots && emptySlotsCached)
                {
                    // Combine valid + empty, sorted
                    sourceListInt = new List<int>(validStaticIdsInt.Count + emptyStaticIdsInt.Count);
                    sourceListInt.AddRange(validStaticIdsInt);
                    sourceListInt.AddRange(emptyStaticIdsInt);
                    sourceListInt.Sort();
                }
                else
                {
                    sourceListInt = validStaticIdsInt;
                }
                sourceListUshort = new List<ushort>(); // Not used for Tecmo
            }
            else
            {
                if (showEmptySlots && emptySlotsCached)
                {
                    // Combine valid + empty, sorted
                    sourceListUshort = new List<ushort>(validStaticIds.Count + emptyStaticIds.Count);
                    sourceListUshort.AddRange(validStaticIds);
                    sourceListUshort.AddRange(emptyStaticIds);
                    sourceListUshort.Sort();
                }
                else
                {
                    sourceListUshort = validStaticIds;
                }
                sourceListInt = new List<int>(); // Not used for non-Tecmo
            }

            if (string.IsNullOrWhiteSpace(searchText) || searchText == "Search...")
            {
                if (useTecmoExpanded)
                    filteredStaticIdsInt = new List<int>(sourceListInt);
                else
                    filteredStaticIds = new List<ushort>(sourceListUshort);
            }
            else
            {
                searchText = searchText.Trim();
                string searchUpper = searchText.ToUpperInvariant();

                // Try to parse as hex number
                bool isHexSearch = searchUpper.StartsWith("0X");
                string searchHex = isHexSearch ? searchUpper.Substring(2) : searchUpper;

                if (useTecmoExpanded)
                {
                    filteredStaticIdsInt = sourceListInt.Where(id =>
                    {
                        // Search by hex ID
                        string hexStr = id.ToString("X");
                        if (hexStr.Contains(searchHex) || id.ToString().Contains(searchUpper))
                            return true;

                        // Search by item name from TileData (only if fits in ushort for TileData lookup)
                        if (id <= 0xFFFF && tileDataReader.IsLoaded)
                        {
                            var itemData = tileDataReader.GetItemTile((ushort)id);
                            if (itemData != null && !string.IsNullOrEmpty(itemData.Name))
                            {
                                if (itemData.Name.ToUpperInvariant().Contains(searchUpper))
                                    return true;
                            }
                        }

                        // Also match "empty" search for empty slots
                        if (IsEmptySlot(id) && "EMPTY".Contains(searchUpper))
                            return true;

                        return false;
                    }).ToList();
                }
                else
                {
                    filteredStaticIds = sourceListUshort.Where(id =>
                    {
                        // Search by hex ID
                        string hexStr = id.ToString("X4");
                        if (hexStr.Contains(searchHex) || id.ToString().Contains(searchUpper))
                            return true;

                        // Search by item name from TileData
                        if (tileDataReader.IsLoaded)
                        {
                            var itemData = tileDataReader.GetItemTile(id);
                            if (itemData != null && !string.IsNullOrEmpty(itemData.Name))
                            {
                                if (itemData.Name.ToUpperInvariant().Contains(searchUpper))
                                    return true;
                            }
                        }

                        // Also match "empty" search for empty slots
                        if (IsEmptySlot(id) && "EMPTY".Contains(searchUpper))
                            return true;

                        return false;
                    }).ToList();
                }
            }

            // Apply size filters
            ApplyPaletteSizeFilters();

            if (useTecmoExpanded)
            {
                paletteListView.VirtualListSize = filteredStaticIdsInt.Count;
            }
            else
            {
                paletteListView.VirtualListSize = filteredStaticIds.Count;
            }
            paletteListView.Invalidate();
        }

        /// <summary>
        /// Initialize the palette size filter controls
        /// </summary>
        private void InitializePaletteSizeFilters()
        {
            if (paletteSizeFilterPanel == null) return;

            paletteSizeFilterPanel.Padding = new Padding(5);

            // Width filter row
            var widthLabel = new Label { Text = "W:", Left = 5, Top = 5, Width = 20, Height = 20, Font = new Font(this.Font.FontFamily, 7.5f) };
            var minWLabel = new Label { Text = "Min:", Left = 25, Top = 5, Width = 25, Height = 20, Font = new Font(this.Font.FontFamily, 7.5f) };
            paletteMinWidthFilter = new NumericUpDown { Left = 50, Top = 3, Width = 45, Minimum = 1, Maximum = 500, Value = 1 };
            paletteMinWidthFilter.ValueChanged += PaletteSizeFilterChanged;
            var maxWLabel = new Label { Text = "Max:", Left = 100, Top = 5, Width = 28, Height = 20, Font = new Font(this.Font.FontFamily, 7.5f) };
            paletteMaxWidthFilter = new NumericUpDown { Left = 128, Top = 3, Width = 45, Minimum = 1, Maximum = 500, Value = 500 };
            paletteMaxWidthFilter.ValueChanged += PaletteSizeFilterChanged;

            // Height filter row
            var heightLabel = new Label { Text = "H:", Left = 5, Top = 28, Width = 20, Height = 20, Font = new Font(this.Font.FontFamily, 7.5f) };
            var minHLabel = new Label { Text = "Min:", Left = 25, Top = 28, Width = 25, Height = 20, Font = new Font(this.Font.FontFamily, 7.5f) };
            paletteMinHeightFilter = new NumericUpDown { Left = 50, Top = 26, Width = 45, Minimum = 1, Maximum = 500, Value = 1 };
            paletteMinHeightFilter.ValueChanged += PaletteSizeFilterChanged;
            var maxHLabel = new Label { Text = "Max:", Left = 100, Top = 28, Width = 28, Height = 20, Font = new Font(this.Font.FontFamily, 7.5f) };
            paletteMaxHeightFilter = new NumericUpDown { Left = 128, Top = 26, Width = 45, Minimum = 1, Maximum = 500, Value = 500 };
            paletteMaxHeightFilter.ValueChanged += PaletteSizeFilterChanged;

            // Reset button
            var resetFilterBtn = new Button { Text = "Reset", Left = 5, Top = 50, Width = 55, Height = 22, Font = new Font(this.Font.FontFamily, 7.5f) };
            resetFilterBtn.Click += (s, ev) =>
            {
                paletteMinWidthFilter.Value = 1;
                paletteMaxWidthFilter.Value = 500;
                paletteMinHeightFilter.Value = 1;
                paletteMaxHeightFilter.Value = 500;
            };

            // Filter count label
            var filterCountLabel = new Label { Text = "", Left = 65, Top = 52, Width = 120, Height = 20, Font = new Font(this.Font.FontFamily, 7.5f), Name = "filterCountLabel" };

            paletteSizeFilterPanel.Controls.Add(widthLabel);
            paletteSizeFilterPanel.Controls.Add(minWLabel);
            paletteSizeFilterPanel.Controls.Add(paletteMinWidthFilter);
            paletteSizeFilterPanel.Controls.Add(maxWLabel);
            paletteSizeFilterPanel.Controls.Add(paletteMaxWidthFilter);
            paletteSizeFilterPanel.Controls.Add(heightLabel);
            paletteSizeFilterPanel.Controls.Add(minHLabel);
            paletteSizeFilterPanel.Controls.Add(paletteMinHeightFilter);
            paletteSizeFilterPanel.Controls.Add(maxHLabel);
            paletteSizeFilterPanel.Controls.Add(paletteMaxHeightFilter);
            paletteSizeFilterPanel.Controls.Add(resetFilterBtn);
            paletteSizeFilterPanel.Controls.Add(filterCountLabel);

            // Apply theme
            HolographicTheme.ApplyToPanel(paletteSizeFilterPanel);
            foreach (Control c in paletteSizeFilterPanel.Controls)
            {
                if (c is Label lbl) HolographicTheme.ApplyToLabel(lbl);
                else if (c is NumericUpDown nud) HolographicTheme.ApplyToNumericUpDown(nud);
                else if (c is Button btn) HolographicTheme.ApplyToButton(btn);
            }
        }

        /// <summary>
        /// Event handler for palette size filter changes
        /// </summary>
        private void PaletteSizeFilterChanged(object sender, EventArgs e)
        {
            // Re-filter the palette
            string searchText = searchTextBox.Text;
            FilterPalette(searchText);
        }

        /// <summary>
        /// Apply size filters to the filtered static IDs list
        /// </summary>
        private void ApplyPaletteSizeFilters()
        {
            if (paletteMinWidthFilter == null || paletteMaxWidthFilter == null ||
                paletteMinHeightFilter == null || paletteMaxHeightFilter == null)
                return;

            int minW = (int)paletteMinWidthFilter.Value;
            int maxW = (int)paletteMaxWidthFilter.Value;
            int minH = (int)paletteMinHeightFilter.Value;
            int maxH = (int)paletteMaxHeightFilter.Value;

            // Skip filtering if defaults (to avoid loading all images on startup)
            if (minW == 1 && maxW == 500 && minH == 1 && maxH == 500)
            {
                if (useTecmoExpanded)
                    UpdateFilterCountLabel(filteredStaticIdsInt.Count, 0);
                else
                    UpdateFilterCountLabel(filteredStaticIds.Count, 0);
                return;
            }

            if (useTecmoExpanded)
            {
                int beforeCount = filteredStaticIdsInt.Count;
                var filtered = new List<int>();

                foreach (var itemId in filteredStaticIdsInt)
                {
                    // Skip empty slots for size filtering (they have no size)
                    if (IsEmptySlot(itemId))
                    {
                        filtered.Add(itemId);
                        continue;
                    }

                    // Try to get cached image dimensions, or load the image
                    Image img = null;
                    string cacheKey = $"0x{itemId:X4}";

                    lock (imageCache)
                    {
                        if (imageCache.TryGetValue(cacheKey, out img))
                        {
                            // Use cached image
                        }
                    }

                    if (img == null)
                    {
                        // Load the image to check dimensions (uses appropriate reader based on mode)
                        try
                        {
                            img = LoadStaticItemImage(itemId);
                        }
                        catch { }
                    }

                    if (img != null)
                    {
                        int w = img.Width;
                        int h = img.Height;

                        if (w >= minW && w <= maxW && h >= minH && h <= maxH)
                        {
                            filtered.Add(itemId);
                        }
                    }
                    else
                    {
                        // If we can't load the image, include it anyway
                        filtered.Add(itemId);
                    }
                }

                int filteredOut = beforeCount - filtered.Count;
                filteredStaticIdsInt = filtered;
                UpdateFilterCountLabel(filtered.Count, filteredOut);
            }
            else
            {
                int beforeCount = filteredStaticIds.Count;
                var filtered = new List<ushort>();

                foreach (var itemId in filteredStaticIds)
                {
                    // Skip empty slots for size filtering (they have no size)
                    if (IsEmptySlot(itemId))
                    {
                        filtered.Add(itemId);
                        continue;
                    }

                    // Try to get cached image dimensions, or load the image
                    Image img = null;
                    string cacheKey = $"0x{itemId:X4}";

                    lock (imageCache)
                    {
                        if (imageCache.TryGetValue(cacheKey, out img))
                        {
                            // Use cached image
                        }
                    }

                    if (img == null)
                    {
                        // Load the image to check dimensions (uses appropriate reader based on mode)
                        try
                        {
                            img = LoadStaticItemImage(itemId);
                        }
                        catch { }
                    }

                    if (img != null)
                    {
                        int w = img.Width;
                        int h = img.Height;

                        if (w >= minW && w <= maxW && h >= minH && h <= maxH)
                        {
                            filtered.Add(itemId);
                        }
                    }
                    else
                    {
                        // If we can't load the image, include it anyway
                        filtered.Add(itemId);
                    }
                }

                int filteredOut = beforeCount - filtered.Count;
                filteredStaticIds = filtered;
                UpdateFilterCountLabel(filtered.Count, filteredOut);
            }
        }

        /// <summary>
        /// Update the filter count label
        /// </summary>
        private void UpdateFilterCountLabel(int showing, int filtered)
        {
            if (paletteSizeFilterPanel == null) return;

            var label = paletteSizeFilterPanel.Controls["filterCountLabel"] as Label;
            if (label != null)
            {
                if (filtered > 0)
                    label.Text = $"{showing} shown ({filtered} filtered)";
                else
                    label.Text = $"{showing} items";
            }
        }
    }
}
