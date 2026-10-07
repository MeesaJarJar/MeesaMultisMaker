using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MeesaMultisMaker.JarJar;
using MeesaMultisMaker.Helpers;
using MeesaMultisMaker.Mul;

namespace MeesaMultisMaker
{
    partial class Form1
    {
        // Track pending changes to save - maps itemId to modified bitmap
        private readonly Dictionary<int, Bitmap> pendingArtChanges = new Dictionary<int, Bitmap>();

        // Separate tracking for JarJar pushes — survives "Save Art" clearing pendingArtChanges
        private readonly Dictionary<int, Bitmap> jarjarPendingChanges = new Dictionary<int, Bitmap>();

        /// <summary>
        /// Mark an item's art as modified (call this after AI replacement)
        /// </summary>
        internal void MarkArtModified(int itemId, Image newImage)
        {
            if (newImage == null) return;

            // Always copy so the pending dict never holds the live instance.
            Bitmap bmp;
            try { bmp = new Bitmap(newImage); }
            catch { bmp = newImage as Bitmap; if (bmp == null) return; }
            if (pendingArtChanges.TryGetValue(itemId, out var oldBmp) && oldBmp != null && !ReferenceEquals(oldBmp, bmp))
            {
                if (!jarjarPendingChanges.TryGetValue(itemId, out var jr) || !ReferenceEquals(oldBmp, jr))
                    try { oldBmp.Dispose(); } catch { }
            }
            pendingArtChanges[itemId] = bmp;

            // Also track for JarJar push (separate copy so Save Art doesn't lose it)
            if (AppConfig.Instance.JarJarPushEnabled)
            {
                Bitmap jb;
                try { jb = new Bitmap(newImage); }
                catch { jb = bmp; }
                if (jarjarPendingChanges.TryGetValue(itemId, out var oldJb) && oldJb != null && !ReferenceEquals(oldJb, jb) && !ReferenceEquals(oldJb, bmp))
                    try { oldJb.Dispose(); } catch { }
                jarjarPendingChanges[itemId] = jb;
            }

            foreach (var o in placedObjects)
            {
                if (o == null) continue;
                if (TryParseGraphicId(o.GraphicId, out int gid) && gid == itemId)
                    o.IsEdited = true;
            }

            UpdatePendingChangesUI();

            System.Diagnostics.Debug.WriteLine($"Form1: Marked item 0x{itemId:X4} as modified. {pendingArtChanges.Count} pending changes.");
        }

        /// <summary>
        /// Clear pending art changes only. JarJar push list survives (cleared on successful push).
        /// Disposes pending bitmaps before clearing.
        /// </summary>
        internal void ClearPendingChanges()
        {
            foreach (var kvp in pendingArtChanges)
                try { kvp.Value?.Dispose(); } catch { }
            pendingArtChanges.Clear();
            UpdatePendingChangesUI();
        }

        private bool showingOriginalArt = false;

        /// <summary>
        /// Toggle display between original and modified images for placed objects
        /// </summary>
        private void ToggleOldNew()
        {
            // Determine if there are any objects with OriginalImage
            bool hasAny = placedObjects.Any(o => o.OriginalImage != null);
            if (!hasAny)
            {
                // Nothing to toggle
                return;
            }

            // Swap Image and OriginalImage for each affected placed object
            foreach (var obj in placedObjects)
            {
                if (obj.OriginalImage == null) continue;

                var temp = obj.Image;
                obj.Image = obj.OriginalImage;
                obj.OriginalImage = temp;
            }

            showingOriginalArt = !showingOriginalArt;

            // Update AI panel button text
            _aiSettingsPanel?.SetOldNewButtonText(showingOriginalArt ? "New" : "Old");

            // Refresh canvas
            designPictureBox.Invalidate();
        }

        /// <summary>
        /// Update the UI to show pending changes count (and Old/New availability)
        /// </summary>
        private void UpdatePendingChangesUI()
        {
            _aiSettingsPanel?.SetPendingChanges(pendingArtChanges.Count);

            // Push to JarJar stays enabled as long as there are unpushed items
            _aiSettingsPanel?.SetPushToJarJarEnabled(jarjarPendingChanges.Count > 0);

            // Enable Old/New toggle if any placed object has an OriginalImage
            bool hasOriginals = placedObjects.Any(o => o.OriginalImage != null);
            _aiSettingsPanel?.SetOldNewEnabled(hasOriginals || pendingArtChanges.Count > 0);

            // Ensure button text reflects current state
            _aiSettingsPanel?.SetOldNewButtonText(showingOriginalArt ? "New" : "Old");
        }

        /// <summary>
        /// Save the currently selected edited object(s) directly to MUL/UOP art files.
        /// Intended for right-click "Save to MUL" on manually edited canvas images.
        /// </summary>
        private void SaveSelectedEditedObjectsToMul()
        {
            var editedSelections = selectedObjects
                .Where(o => o != null && o.Image != null && o.IsEdited)
                .ToList();

            if (editedSelections.Count == 0)
            {
                MessageBox.Show("No edited image is selected.", "Save to MUL",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var changes = new Dictionary<int, Bitmap>();
            foreach (var obj in editedSelections)
            {
                if (!TryParseGraphicId(obj.GraphicId, out int itemId))
                    continue;

                if (changes.TryGetValue(itemId, out var prev) && prev != null && !ReferenceEquals(prev, obj.Image))
                    try { prev.Dispose(); } catch { }
                changes[itemId] = new Bitmap(obj.Image);
            }

            if (changes.Count == 0)
            {
                MessageBox.Show("Selected image cannot be saved because it does not map to a valid MUL art ID.",
                    "Save to MUL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string mulFolder = artFolderPath;
            if (string.IsNullOrEmpty(mulFolder))
                mulFolder = AppConfig.Instance.MulFolderPath;
            if (string.IsNullOrEmpty(mulFolder))
                mulFolder = AppConfig.Instance.FindMulFolder();

            if (string.IsNullOrEmpty(mulFolder) || !Directory.Exists(mulFolder))
            {
                MessageBox.Show("MUL folder path is not set or does not exist.\n\nPlease configure it in Settings.",
                    "Save to MUL", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var confirm = MessageBox.Show(
                $"Save {changes.Count} edited image(s) to:\n\n{mulFolder}\n\n" +
                "A backup will be created before writing. Continue?",
                "Save to MUL",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
                return;

            try
            {
                // Release cached open art.mul streams before writing files.
                StaticArtReader.ClearCache();

                SetAIStatus("Creating backup...");
                Application.DoEvents();
                StaticArtWriter.BackupArtFiles(mulFolder);

                SetAIStatus($"Saving {changes.Count} edited image(s)...");
                Application.DoEvents();
                int savedCount = StaticArtWriter.SaveMultipleStaticArts(mulFolder, changes);

                if (savedCount > 0)
                {
                    foreach (var obj in editedSelections)
                    {
                        if (obj != null && obj.Image != null && TryParseGraphicId(obj.GraphicId, out _))
                            obj.IsEdited = false;
                    }

                    foreach (var id in changes.Keys)
                    {
                        string cacheKey = $"0x{id:X4}";
                        lock (imageCache)
                        {
                            imageCache.Remove(cacheKey);
                        }
                    }

                    paletteListView?.Invalidate();
                    designPictureBox?.Invalidate();

                    SetAIStatus($"Saved {savedCount} edited image(s)", HolographicTheme.ButtonSuccess);
                    OutputLog($"Saved {savedCount} edited image(s) to MUL/UOP art files");

                    MessageBox.Show(
                        $"Successfully saved {savedCount} edited image(s).",
                        "Save Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    SetAIStatus("Save failed", HolographicTheme.ButtonDanger);
                    MessageBox.Show("Failed to save edited image(s).",
                        "Save to MUL", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                foreach (var kvp in changes)
                    try { kvp.Value?.Dispose(); } catch { }
            }
            catch (Exception ex)
            {
                SetAIStatus($"Error: {ex.Message}", HolographicTheme.ButtonDanger);
                OutputLog($"Save selected edited images failed: {ex.Message}");
                MessageBox.Show($"Error saving to MUL files:\n\n{ex.Message}",
                    "Save to MUL", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Save all pending art changes to the MUL files
        /// </summary>
        private void SaveChangesToMul()
        {
            if (pendingArtChanges.Count == 0)
            {
                MessageBox.Show("No pending changes to save.", "Save to MUL", 
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Get MUL folder path — prefer artFolderPath (the loaded folder)
            string mulFolder = artFolderPath;
            if (string.IsNullOrEmpty(mulFolder))
                mulFolder = AppConfig.Instance.MulFolderPath;
            if (string.IsNullOrEmpty(mulFolder))
                mulFolder = AppConfig.Instance.FindMulFolder();

            if (string.IsNullOrEmpty(mulFolder) || !Directory.Exists(mulFolder))
            {
                MessageBox.Show("MUL folder path is not set or does not exist.\n\n" +
                    "Please configure the MUL folder path in Settings.",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Determine which art files exist
            string artMulPath = Path.Combine(mulFolder, "art.mul");
            string uopPath = Path.Combine(mulFolder, "artLegacyMUL.uop");
            string targetDesc = File.Exists(artMulPath) ? "art.mul" : "";
            if (File.Exists(uopPath))
                targetDesc = string.IsNullOrEmpty(targetDesc) ? "artLegacyMUL.uop" : "art.mul + artLegacyMUL.uop";
            if (string.IsNullOrEmpty(targetDesc))
                targetDesc = "art files";

            // Confirm with user
            var result = MessageBox.Show(
                $"This will save {pendingArtChanges.Count} modified item(s) to:\n\n" +
                $"{mulFolder}\n({targetDesc})\n\n" +
                "A backup will be created before making changes.\n\n" +
                "Do you want to continue?",
                "Save Art Files",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes)
                return;

            try
            {
                // Release cached open art.mul streams before writing files.
                StaticArtReader.ClearCache();

                SetAIStatus("Creating backup...");
                Application.DoEvents();

                // Create backup
                if (!StaticArtWriter.BackupArtFiles(mulFolder))
                {
                    var backupResult = MessageBox.Show(
                        "Failed to create backup of art files.\n\n" +
                        "Do you want to continue anyway? (Not recommended)",
                        "Backup Failed",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (backupResult != DialogResult.Yes)
                    {
                        SetAIStatus("Save cancelled");
                        return;
                    }
                }

                SetAIStatus($"Saving {pendingArtChanges.Count} items...");
                Application.DoEvents();

                // Save all changes (this updates both MUL and UOP files)
                var affectedIds = pendingArtChanges.Keys.ToList();
                int savedCount = StaticArtWriter.SaveMultipleStaticArts(mulFolder, pendingArtChanges);

                if (savedCount == pendingArtChanges.Count)
                {
                    MessageBox.Show(
                        $"Successfully saved {savedCount} item(s)!\n\n" +
                        "The changes are now permanent.\n" +
                        "Backup files were created with .bak extension.",
                        "Save Complete",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    // Clear caches BEFORE clearing pending (Refresh loop would be a no-op after).
                    ClearImageCacheForIds(affectedIds);
                    ClearIsEditedForIds(affectedIds);
                    // Clear pending changes and refresh palette
                    ClearPendingChanges();
                    RefreshPaletteAfterSave();

                    SetAIStatus($"Saved {savedCount} items", HolographicTheme.ButtonSuccess);
                    OutputLog($"Saved {savedCount} item(s) to art files");
                }
                else if (savedCount > 0)
                {
                    MessageBox.Show(
                        $"Saved {savedCount} of {pendingArtChanges.Count} items.\n\n" +
                        "Some items may have failed to save. Check the output log for details.",
                        "Partial Save",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    SetAIStatus($"Saved {savedCount}/{pendingArtChanges.Count}", HolographicTheme.ButtonWarning);
                    OutputLog($"Partial save: {savedCount}/{pendingArtChanges.Count} items saved");
                }
                else
                {
                    MessageBox.Show(
                        "Failed to save any items. Check that the MUL files are not read-only\n" +
                        "and that no other program is using them.",
                        "Save Failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);

                    SetAIStatus("Save failed!", HolographicTheme.ButtonDanger);
                    OutputLog("ERROR: Failed to save items to art.mul");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error saving to MUL files:\n\n{ex.Message}",
                    "Save Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                SetAIStatus($"Error: {ex.Message}", HolographicTheme.ButtonDanger);
                OutputLog($"ERROR: {ex.Message}");
            }
        }

        /// <summary>
        /// Output a message to the log textbox
        /// </summary>
        private void OutputLog(string message)
        {
            if (outputTextBox.InvokeRequired)
            {
                outputTextBox.Invoke(new Action(() => OutputLog(message)));
                return;
            }
            outputTextBox.AppendText($"{message}\r\n");
        }

        /// <summary>
        /// Parse a GraphicId string (e.g. "0x0A1F" or "0A1F") into an int item ID.
        /// </summary>
        internal static bool TryParseGraphicId(string graphicId, out int itemId)
        {
            itemId = 0;
            if (string.IsNullOrWhiteSpace(graphicId)) return false;
            string hex = graphicId.Trim();
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hex = hex.Substring(2);
            return int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out itemId);
        }

        /// <summary>
        /// Save pending art changes to new empty slots instead of overwriting originals.
        /// Returns a mapping from original item ID to newly assigned slot ID.
        /// </summary>
        internal Dictionary<int, int> SaveToNewSlots()
        {
            var mapping = new Dictionary<int, int>();

            if (pendingArtChanges.Count == 0)
            {
                MessageBox.Show("No pending changes to save.", "Save to New Slot",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return mapping;
            }

            string mulFolder = artFolderPath;
            if (string.IsNullOrEmpty(mulFolder))
                mulFolder = AppConfig.Instance.MulFolderPath;
            if (string.IsNullOrEmpty(mulFolder))
                mulFolder = AppConfig.Instance.FindMulFolder();

            if (string.IsNullOrEmpty(mulFolder) || !Directory.Exists(mulFolder))
            {
                MessageBox.Show("MUL/UOP folder path is not set or does not exist.",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return mapping;
            }

            // Find enough empty slots
            var emptySlots = StaticArtWriter.FindNextEmptySlots(mulFolder, pendingArtChanges.Count);
            if (emptySlots.Count < pendingArtChanges.Count)
            {
                MessageBox.Show(
                    $"Not enough empty slots available.\n\n" +
                    $"Need {pendingArtChanges.Count} but only found {emptySlots.Count}.\n" +
                    "Items beyond the index range will be added automatically.",
                    "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                // Extend by using IDs beyond current index
                int nextId = emptySlots.Count > 0 ? emptySlots[emptySlots.Count - 1] + 1 : 0x4000;
                while (emptySlots.Count < pendingArtChanges.Count && nextId < 0xFFFF)
                {
                    if (!emptySlots.Contains(nextId))
                        emptySlots.Add(nextId);
                    nextId++;
                }
            }

            // Build mapping: original ID → new empty slot ID
            int slotIndex = 0;
            var newItems = new Dictionary<int, Bitmap>();
            var originalIds = pendingArtChanges.Keys.ToList();

            foreach (var origId in originalIds)
            {
                if (slotIndex >= emptySlots.Count) break;
                int newId = emptySlots[slotIndex];
                mapping[origId] = newId;
                newItems[newId] = pendingArtChanges[origId];
                slotIndex++;
            }

            // Build display text
            var mappingText = new StringBuilder();
            foreach (var kvp in mapping)
                mappingText.AppendLine($"  0x{kvp.Key:X} → 0x{kvp.Value:X}");

            var result = MessageBox.Show(
                $"Save {mapping.Count} item(s) to new empty slots:\n\n" +
                mappingText.ToString() +
                $"\nTarget folder: {mulFolder}\n" +
                "A backup will be created. Continue?",
                "Save to New Slots",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
                return new Dictionary<int, int>();

            try
            {
                // Release cached open art.mul streams before writing files.
                StaticArtReader.ClearCache();

                SetAIStatus("Creating backup...");
                Application.DoEvents();
                StaticArtWriter.BackupArtFiles(mulFolder);

                SetAIStatus($"Saving {newItems.Count} items to new slots...");
                Application.DoEvents();

                int savedCount = StaticArtWriter.SaveMultipleStaticArts(mulFolder, newItems);

                if (savedCount > 0)
                {
                    // Log the mapping
                    foreach (var kvp in mapping)
                        OutputLog($"Saved 0x{kvp.Key:X} → new slot 0x{kvp.Value:X}");

                    // Re-point canvas objects at the new slots so Export uses the
                    // new IDs - but only on a full save. On a partial save the
                    // canvas keeps its old IDs so nothing points at an empty slot.
                    int remapped = 0;
                    if (savedCount == mapping.Count)
                    {
                        PushUndo();
                        foreach (var obj in placedObjects)
                        {
                            int oldId;
                            if (TryParseGraphicId(obj.GraphicId, out oldId))
                            {
                                int newId;
                                if (mapping.TryGetValue(oldId, out newId))
                                {
                                    obj.GraphicId = string.Format("0x{0:X4}", newId);
                                    obj.OriginalImage?.Dispose();
                                    obj.OriginalImage = null;
                                    obj.IsEdited = false;
                                    remapped++;
                                }
                            }
                        }
                    }
                    if (remapped > 0)
                    {
                        designPictureBox.Invalidate();
                        OutputLog($"Re-pointed {remapped} canvas object(s) to new IDs");
                    }

                    try
                    {
                        foreach (var kvp in TileDataWriter.CopyItemEntries(mulFolder, mapping))
                            OutputLog(kvp);
                        try { tileDataReader.Load(mulFolder); } catch { }
                    }
                    catch { }
                    ClearImageCacheForIds(newItems.Keys.ToList());
                    ClearPendingChanges();
                    RefreshPaletteAfterSave();

                    SetAIStatus($"Saved {savedCount} item(s) to new slots", HolographicTheme.ButtonSuccess);
                    MessageBox.Show(
                        $"Successfully saved {savedCount} item(s) to new slots.\n\n" +
                        (remapped > 0 ? $"{remapped} canvas object(s) now use the new IDs.\n" : "") +
                        "The palette list has been refreshed.",
                        "Save Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    SetAIStatus("Save failed!", HolographicTheme.ButtonDanger);
                    MessageBox.Show("Failed to save items.", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                SetAIStatus($"Error: {ex.Message}", HolographicTheme.ButtonDanger);
                OutputLog($"ERROR: {ex.Message}");
            }

            return mapping;
        }

        /// <summary>
        /// Save EVERYTHING currently on the canvas as brand-new art entries in
        /// unused MUL slots, then re-point the canvas objects at the new IDs so
        /// Export (multi.txt) references the new entries. Originals on disk are
        /// never touched. Objects sharing the same image share one new slot.
        /// </summary>
        internal void SaveCanvasAsNew()
        {
            var targets = placedObjects.Where(o => o != null && o.Image != null).ToList();
            if (targets.Count == 0)
            {
                MessageBox.Show("Canvas is empty - nothing to save.", "Save Canvas as New",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string mulFolder = artFolderPath;
            if (string.IsNullOrEmpty(mulFolder))
                mulFolder = AppConfig.Instance.MulFolderPath;
            if (string.IsNullOrEmpty(mulFolder))
                mulFolder = AppConfig.Instance.FindMulFolder();

            if (string.IsNullOrEmpty(mulFolder) || !Directory.Exists(mulFolder))
            {
                MessageBox.Show("MUL/UOP folder path is not set or does not exist.",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Dedupe by image instance: objects sharing one bitmap share one slot.
            var uniqueImages = new List<Image>();
            foreach (var obj in targets)
            {
                if (!uniqueImages.Contains(obj.Image))
                    uniqueImages.Add(obj.Image);
            }

            var emptySlots = StaticArtWriter.FindNextEmptySlots(mulFolder, uniqueImages.Count);
            if (emptySlots.Count == 0 && !File.Exists(Path.Combine(mulFolder, "artidx.mul")))
            {
                // UOP-only folder: occupancy can't be detected without the index,
                // so blind-filling from 0x4000 could overwrite used art. Refuse.
                MessageBox.Show(
                    "Cannot find free slots: artidx.mul is missing (UOP-only folder).\n\n" +
                    "Save Canvas as New needs a MUL-based folder to detect empty slots safely.",
                    "Save Canvas as New", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (emptySlots.Count < uniqueImages.Count)
            {
                int nextId = emptySlots.Count > 0 ? emptySlots[emptySlots.Count - 1] + 1 : 0x4000;
                while (emptySlots.Count < uniqueImages.Count && nextId <= 0xFFFF)
                {
                    if (!emptySlots.Contains(nextId))
                        emptySlots.Add(nextId);
                    nextId++;
                }
            }
            if (emptySlots.Count < uniqueImages.Count)
            {
                MessageBox.Show(
                    $"Not enough free art slots.\n\nNeed {uniqueImages.Count} but only found {emptySlots.Count}.",
                    "Save Canvas as New", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Save {targets.Count} canvas object(s) as {uniqueImages.Count} NEW art entr(ies)?\n\n" +
                "Originals on disk are left untouched. Canvas items will be\n" +
                "re-pointed to the new IDs so Export uses them.\n\n" +
                $"Target folder: {mulFolder}\n" +
                "A backup will be created. Continue?",
                "Save Canvas as New",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
                return;

            // Assign one fresh slot per unique image.
            var imageToNewId = new Dictionary<Image, int>();
            var newItems = new Dictionary<int, Bitmap>();
            var newItemsCopied = new List<Bitmap>();
            for (int i = 0; i < uniqueImages.Count; i++)
            {
                int newId = emptySlots[i];
                imageToNewId[uniqueImages[i]] = newId;
                var src = uniqueImages[i];
                var copy = new Bitmap(src);
                newItems[newId] = copy;
                newItemsCopied.Add(copy);
            }

            try
            {
                StaticArtReader.ClearCache();

                SetAIStatus("Creating backup...");
                Application.DoEvents();
                StaticArtWriter.BackupArtFiles(mulFolder);

                SetAIStatus($"Saving {newItems.Count} new art entr(ies)...");
                Application.DoEvents();

                int savedCount = StaticArtWriter.SaveMultipleStaticArts(mulFolder, newItems);

                if (savedCount == newItems.Count)
                {
                    // Best-effort tiledata copy: inherit name/flags from the canvas source ID.
                    var tileRemap = new Dictionary<int, int>();
                    foreach (var obj in targets)
                    {
                        if (TryParseGraphicId(obj.GraphicId, out int srcId))
                            tileRemap[srcId] = imageToNewId[obj.Image];
                    }
                    try
                    {
                        foreach (var line in TileDataWriter.CopyItemEntries(mulFolder, tileRemap))
                            OutputLog(line);
                        try { tileDataReader.Load(mulFolder); } catch { }
                    }
                    catch { }

                    PushUndo();
                    foreach (var obj in targets)
                    {
                        int newId = imageToNewId[obj.Image];
                        string oldLabel = obj.GraphicId;
                        obj.GraphicId = string.Format("0x{0:X4}", newId);
                        obj.OriginalImage?.Dispose();
                        obj.OriginalImage = null;
                        obj.IsEdited = false;
                        OutputLog($"Canvas {oldLabel} → new entry 0x{newId:X4}");
                    }

                    ClearImageCacheForIds(newItems.Keys.ToList());
                    ClearPendingChanges();
                    RefreshPaletteAfterSave();
                    foreach (var c in newItemsCopied)
                        try { c.Dispose(); } catch { }
                    designPictureBox.Invalidate();

                    SetAIStatus($"Saved {savedCount} new art entr(ies)", HolographicTheme.ButtonSuccess);
                    OutputLog($"Saved {savedCount} new art entr(ies); canvas re-pointed to new IDs");
                    MessageBox.Show(
                        $"Successfully saved {savedCount} new art entr(ies).\n\n" +
                        "Canvas items now use the new IDs - Export will reference them.",
                        "Save Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else if (savedCount > 0)
                {
                    // Partial save: leave the canvas on its old IDs so nothing
                    // points at an empty slot. Nothing is cleared.
                    SetAIStatus($"Saved {savedCount}/{newItems.Count}", HolographicTheme.ButtonWarning);
                    OutputLog($"Partial save: {savedCount}/{newItems.Count} new entries saved; canvas unchanged");
                    foreach (var c in newItemsCopied)
                        try { c.Dispose(); } catch { }
                    MessageBox.Show(
                        $"Saved {savedCount} of {newItems.Count} new entries.\n\n" +
                        "Canvas was left unchanged - check the output log and retry.",
                        "Partial Save", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    SetAIStatus("Save failed!", HolographicTheme.ButtonDanger);
                    MessageBox.Show("Failed to save new entries.", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    foreach (var c in newItemsCopied)
                        try { c.Dispose(); } catch { }
                }
            }
            catch (Exception ex)
            {
                SetAIStatus($"Error: {ex.Message}", HolographicTheme.ButtonDanger);
                OutputLog($"ERROR: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear cached images for the given IDs (call BEFORE ClearPendingChanges).
        /// </summary>
        private void ClearImageCacheForIds(System.Collections.Generic.List<int> ids)
        {
            if (ids == null) return;
            foreach (var itemId in ids)
            {
                string cacheKey = $"0x{itemId:X4}";
                lock (imageCache)
                {
                    if (imageCache.TryGetValue(cacheKey, out var img))
                    {
                        imageCache.Remove(cacheKey);
                        try { img?.Dispose(); } catch { }
                    }
                    else
                        imageCache.Remove(cacheKey);
                }
            }
        }

        /// <summary>
        /// Mark saved IDs as no longer edited.
        /// </summary>
        private void ClearIsEditedForIds(System.Collections.Generic.List<int> ids)
        {
            if (ids == null || ids.Count == 0) return;
            var set = new System.Collections.Generic.HashSet<int>(ids);
            foreach (var obj in placedObjects)
            {
                if (obj == null) continue;
                if (TryParseGraphicId(obj.GraphicId, out int gid) && set.Contains(gid))
                    obj.IsEdited = false;
            }
        }

        /// <summary>
        /// Refresh the palette list to reflect changes saved to art files.
        /// Callers must clear imageCache for affected IDs BEFORE ClearPendingChanges;
        /// this only resets slot caches and reloads valid IDs (Tecmo-aware).
        /// </summary>
        private void RefreshPaletteAfterSave()
        {
            try
            {
                // Reset empty slots cache (new items may have filled slots)
                emptySlotsCached = false;
                emptyStaticIds.Clear();
                emptyStaticIdsInt.Clear();

                // Re-scan valid IDs from the updated art files
                if (useTecmoExpanded)
                    ReloadValidStaticIdsTecmo();
                else if (useUopOnly)
                    ReloadValidStaticIdsUop();
                else
                    ReloadValidStaticIds();

                // Re-apply current filter to refresh the list view
                string searchText = searchTextBox?.Text?.Trim() ?? "";
                FilterPalette(searchText);

                paletteListView.Invalidate();
                int shown = useTecmoExpanded ? validStaticIdsInt.Count : validStaticIds.Count;
                statusLabel_SetText($"Palette refreshed: {shown:N0} items");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RefreshPaletteAfterSave: {ex.Message}");
                statusLabel_SetText($"Palette refresh error: {ex.Message}");
            }
        }

        /// <summary>
        /// Re-scan artidx.mul for valid static IDs (after saving new art).
        /// Also rebuilds idToPath and maxItemIdInIndex.
        /// </summary>
        private void ReloadValidStaticIds()
        {
            validStaticIds.Clear();
            validStaticIdSet.Clear();
            maxItemIdInIndex = 0;

            string artIdxPath = Path.Combine(artFolderPath, "artidx.mul");
            if (string.IsNullOrEmpty(artFolderPath) || !File.Exists(artIdxPath))
            {
                System.Diagnostics.Debug.WriteLine($"ReloadValidStaticIds: artidx.mul not found at {artFolderPath}");
                return;
            }

            const int STATIC_OFFSET = 0x4000;

            using (var idxStream = File.OpenRead(artIdxPath))
            using (var idxReader = new BinaryReader(idxStream))
            {
                long maxIndex = idxStream.Length / 12;
                for (ushort itemId = 0; itemId <= 0xFFFF; itemId++)
                {
                    int index = itemId + STATIC_OFFSET;
                    if (index >= maxIndex) break;

                    idxStream.Seek((long)index * 12, SeekOrigin.Begin);
                    int offset = idxReader.ReadInt32();
                    int length = idxReader.ReadInt32();

                    if (offset >= 0 && length > 0)
                    {
                        validStaticIds.Add(itemId);
                        validStaticIdSet.Add(itemId);
                        maxItemIdInIndex = itemId;

                        // Rebuild idToPath entries
                        string hexId = $"0x{itemId:X4}";
                        idToPath[hexId] = hexId;
                        string rawHex = itemId.ToString("X4").ToUpperInvariant();
                        idToPath[rawHex] = hexId;
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"ReloadValidStaticIds: Found {validStaticIds.Count} valid items, max ID 0x{maxItemIdInIndex:X4}");
        }

        /// <summary>
        /// Re-scan UOP for valid static IDs (after saving new art).
        /// Also rebuilds idToPath and maxItemIdInIndex.
        /// </summary>
        private void ReloadValidStaticIdsUop()
        {
            validStaticIds.Clear();
            validStaticIdSet.Clear();
            maxItemIdInIndex = 0;

            if (string.IsNullOrEmpty(artFolderPath))
            {
                System.Diagnostics.Debug.WriteLine("ReloadValidStaticIdsUop: artFolderPath is empty");
                return;
            }

            UopArtReader.ClearCache();
            var uopIds = UopArtReader.GetValidStaticItemIds(artFolderPath);
            foreach (var id in uopIds)
            {
                validStaticIds.Add(id);
                validStaticIdSet.Add(id);
                if (id > maxItemIdInIndex) maxItemIdInIndex = id;

                // Rebuild idToPath entries
                string hexId = $"0x{id:X4}";
                idToPath[hexId] = hexId;
                string rawHex = id.ToString("X4").ToUpperInvariant();
                idToPath[rawHex] = hexId;
            }
            validStaticIds.Sort();

            System.Diagnostics.Debug.WriteLine($"ReloadValidStaticIdsUop: Found {validStaticIds.Count} valid items");
        }

        /// <summary>
        /// Re-scan Tecmo Expanded Art for valid static IDs (after saving new art).
        /// Mirrors LoadArtFromTecmo: refreshes int + ushort lists, sets and idToPath.
        /// </summary>
        private void ReloadValidStaticIdsTecmo()
        {
            validStaticIdsInt.Clear();
            validStaticIdSetInt.Clear();
            maxItemIdInIndexInt = 0;
            validStaticIds.Clear();
            validStaticIdSet.Clear();
            maxItemIdInIndex = 0;

            if (string.IsNullOrEmpty(artFolderPath))
            {
                System.Diagnostics.Debug.WriteLine("ReloadValidStaticIdsTecmo: artFolderPath is empty");
                return;
            }

            try
            {
                TecmoArtReader.ClearCache();
                var tecmoIds = TecmoArtReader.GetValidStaticItemIds(artFolderPath);
                foreach (var id in tecmoIds)
                {
                    validStaticIdsInt.Add(id);
                    validStaticIdSetInt.Add(id);
                    if (id > maxItemIdInIndexInt) maxItemIdInIndexInt = id;
                    if (id <= 0xFFFF)
                    {
                        validStaticIds.Add((ushort)id);
                        validStaticIdSet.Add((ushort)id);
                        if (id > maxItemIdInIndex) maxItemIdInIndex = (ushort)id;
                    }
                    string hexId = $"0x{id:X4}";
                    idToPath[hexId] = hexId;
                    string rawHex = id.ToString("X4").ToUpperInvariant();
                    idToPath[rawHex] = hexId;
                }
                validStaticIdsInt.Sort();
                validStaticIds.Sort();
                System.Diagnostics.Debug.WriteLine($"ReloadValidStaticIdsTecmo: Found {validStaticIdsInt.Count} valid items");
            }
            catch
            {
                // At minimum keep palette usable via existing filter path.
                try
                {
                    string searchText = searchTextBox?.Text?.Trim() ?? "";
                    FilterPalette(searchText);
                }
                catch { }
            }
        }

        /// <summary>
        /// Push all pending art changes to the MeesaJarJar.com server.
        /// Uses jarjarPendingChanges which survives "Save Art" so you can
        /// save locally first, then push to the live server.
        /// </summary>
        private async Task PushToJarJar()
        {
            if (jarjarPendingChanges.Count == 0)
            {
                MessageBox.Show("No pending changes to push.", "Push to MeesaJarJar",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var config = AppConfig.Instance;
            if (!config.JarJarPushEnabled)
            {
                MessageBox.Show("Push to MeesaJarJar is disabled in settings.",
                    "Push Disabled", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Push {jarjarPendingChanges.Count} modified item(s) to MeesaJarJar.com?\n\n" +
                $"Server: {config.JarJarApiUrl}\n\n" +
                "Connected game clients will see changes within ~30 seconds.",
                "Push to MeesaJarJar",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
                return;

            JarJarClient client = null;
            try
            {
                client = new JarJarClient(config.JarJarApiUrl, config.JarJarAuthToken);

                SetAIStatus("Connecting to MeesaJarJar...");
                Application.DoEvents();

                bool connected = await client.TestConnectionAsync();
                if (!connected)
                {
                    MessageBox.Show(
                        $"Cannot connect to {config.JarJarApiUrl}\n\n" +
                        "Check your internet connection and the server URL in Settings.",
                        "Connection Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    SetAIStatus("JarJar connection failed", HolographicTheme.ButtonDanger);
                    return;
                }

                SetAIStatus("Downloading lookup table...");
                Application.DoEvents();

                var results = await client.UploadMultipleSpritesAsync(
                    jarjarPendingChanges.Where(kvp => kvp.Key <= 0xFFFF)
                        .ToDictionary(kvp => (ushort)kvp.Key, kvp => kvp.Value),
                    (completed, total) =>
                    {
                        if (this.InvokeRequired)
                            this.Invoke(new Action(() =>
                            {
                                SetAIStatus($"Pushing {completed}/{total}...");
                                Application.DoEvents();
                            }));
                        else
                        {
                            SetAIStatus($"Pushing {completed}/{total}...");
                            Application.DoEvents();
                        }
                    });

                int successCount = 0;
                int failCount = 0;
                var errors = new StringBuilder();

                foreach (var r in results)
                {
                    if (r.Success)
                    {
                        successCount++;
                        OutputLog($"Pushed {r.GraphicId} to JarJar");
                    }
                    else
                    {
                        failCount++;
                        errors.AppendLine($"  {r.GraphicId}: {r.Message}");
                        OutputLog($"FAILED {r.GraphicId}: {r.Message}");
                    }
                }

                if (failCount == 0)
                {
                    // All pushed successfully — clear the JarJar pending list
                    jarjarPendingChanges.Clear();
                    UpdatePendingChangesUI();

                    MessageBox.Show(
                        $"Successfully pushed {successCount} item(s) to MeesaJarJar!\n\n" +
                        "Game clients will auto-update within ~30 seconds.",
                        "Push Complete",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    SetAIStatus($"Pushed {successCount} to JarJar", HolographicTheme.ButtonSuccess);
                }
                else if (successCount > 0)
                {
                    // Remove successfully pushed items from JarJar pending
                    foreach (var r in results)
                    {
                        if (r.Success && r.GraphicId != null)
                        {
                            int id;
                            if (TryParseGraphicId(r.GraphicId, out id))
                                jarjarPendingChanges.Remove(id);
                        }
                    }
                    UpdatePendingChangesUI();

                    MessageBox.Show(
                        $"Pushed {successCount} of {results.Count} items.\n\n" +
                        $"Failed ({failCount}):\n{errors}",
                        "Partial Push",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    SetAIStatus($"Pushed {successCount}/{results.Count}", HolographicTheme.ButtonWarning);
                }
                else
                {
                    MessageBox.Show(
                        $"Failed to push all items:\n\n{errors}",
                        "Push Failed",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    SetAIStatus("Push failed!", HolographicTheme.ButtonDanger);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error pushing to MeesaJarJar:\n\n{ex.Message}",
                    "Push Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetAIStatus($"Error: {ex.Message}", HolographicTheme.ButtonDanger);
                OutputLog($"ERROR pushing to JarJar: {ex.Message}");
            }
            finally
            {
                client?.Dispose();
            }
        }
    }
}
