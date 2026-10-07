using MeesaMultisMaker.JarJar;
using MeesaMultisMaker.Helpers;
using MeesaMultisMaker.Mul;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    public partial class Form1
    {
        private void PaletteListView_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            // MUL/UOP-based loading
            if (e.ItemIndex < 0) return;
            
            if (useTecmoExpanded)
            {
                if (e.ItemIndex >= filteredStaticIdsInt.Count) return;

                int itemId = filteredStaticIdsInt[e.ItemIndex];
                string graphicId = $"0x{itemId:X}";
                bool isEmpty = IsEmptySlot(itemId);

                var item = new ListViewItem(graphicId)
                {
                    Tag = new ImageInfo
                    {
                        FilePath = graphicId,
                        GraphicId = graphicId,
                        ItemId = itemId,
                        IsMulItem = true,
                        IsEmptySlot = isEmpty
                    }
                };
                e.Item = item;
            }
            else
            {
                if (e.ItemIndex >= filteredStaticIds.Count) return;

                ushort itemId = filteredStaticIds[e.ItemIndex];
                string graphicId = $"0x{itemId:X4}";
                bool isEmpty = IsEmptySlot(itemId);

                var item = new ListViewItem(graphicId)
                {
                    Tag = new ImageInfo
                    {
                        FilePath = graphicId,
                        GraphicId = graphicId,
                        ItemId = itemId,
                        IsMulItem = true,
                        IsEmptySlot = isEmpty
                    }
                };
                e.Item = item;
            }
        }

        private void PaletteListView_DrawItem(object sender, DrawListViewItemEventArgs e)
        {
            var imageInfo = e.Item.Tag as ImageInfo;
            bool isEmpty = imageInfo?.IsEmptySlot ?? false;

            // Use holographic theme colors, with distinct color for empty slots
            if (e.Item.Selected)
            {
                using (var brush = new SolidBrush(isEmpty ? Color.FromArgb(80, 150, 80) : HolographicTheme.ButtonAccent))
                { e.Graphics.FillRectangle(brush, e.Bounds); }
            }
            else
            {
                using (var brush = new SolidBrush(isEmpty ? Color.FromArgb(40, 50, 40) : HolographicTheme.InputBackground))
                { e.Graphics.FillRectangle(brush, e.Bounds); }
            }

            if (imageInfo != null)
            {
                Image raw = null;
                string cacheKey = imageInfo.GraphicId;

                // Only try to load image for non-empty slots
                if (!isEmpty)
                {
                    lock (imageCache)
                    {
                        if (imageCache.ContainsKey(cacheKey)) raw = imageCache[cacheKey];
                    }

                    if (raw == null)
                    {
                        try
                        {
                            // Load from MUL or UOP file based on mode
                            raw = LoadStaticItemImage(imageInfo.ItemId);

                            if (raw != null)
                            {
                                lock (imageCache)
                                {
                                    if (!imageCache.ContainsKey(cacheKey)) imageCache[cacheKey] = raw;
                                }
                            }
                        }
                        catch { }
                    }
                }

                // Get item name from TileData
                string itemName = null;
                if (tileDataReader.IsLoaded && imageInfo.ItemId <= 0xFFFF)
                {
                    var itemData = tileDataReader.GetItemTile((ushort)imageInfo.ItemId);
                    if (itemData != null && !string.IsNullOrWhiteSpace(itemData.Name))
                    {
                        itemName = itemData.Name;
                    }
                }

                if (isEmpty)
                {
                    // Draw empty slot indicator
                    int imgX = e.Bounds.Left + 2;
                    int imgY = e.Bounds.Top + (e.Bounds.Height - 44) / 2;

                    // Draw a dashed rectangle placeholder for empty slot
                    using (var pen = new Pen(Color.FromArgb(100, 180, 100), 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                    {
                        e.Graphics.DrawRectangle(pen, imgX, imgY, 44, 44);
                    }

                    // Draw "+" in the center to indicate "click to add"
                    using (var font = new Font("Consolas", 18, FontStyle.Bold))
                    using (var brush = new SolidBrush(Color.FromArgb(100, 180, 100)))
                    {
                        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                        e.Graphics.DrawString("+", font, brush, new RectangleF(imgX, imgY, 44, 44), sf);
                    }

                    int textLeft = e.Bounds.Left + 54;
                    var textRect = new Rectangle(textLeft, e.Bounds.Top, e.Bounds.Width - (textLeft - e.Bounds.Left), e.Bounds.Height);
                    var sf2 = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };

                    string displayText = $"{imageInfo.GraphicId} [EMPTY]";
                    if (!string.IsNullOrEmpty(itemName))
                        displayText = $"{imageInfo.GraphicId} - {itemName} [EMPTY]";

                    using (var textBrush = new SolidBrush(Color.FromArgb(100, 180, 100)))
                    {
                        e.Graphics.DrawString(displayText, this.Font, textBrush, textRect, sf2);
                    }
                }
                else if (raw != null)
                {
                    int imgX = e.Bounds.Left + 2;
                    int imgY = e.Bounds.Top + Math.Max(0, (e.Bounds.Height - raw.Height) / 2);
                    try { e.Graphics.DrawImage(raw, imgX, imgY, raw.Width, raw.Height); } catch { }
                    int textLeft = e.Bounds.Left + Math.Max(50, raw.Width + 8);
                    var textRect = new Rectangle(textLeft, e.Bounds.Top, e.Bounds.Width - (textLeft - e.Bounds.Left), e.Bounds.Height);
                    var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };

                    // Show hex ID, decimal ID, and item name if available
                    string displayText;
                    if (!string.IsNullOrEmpty(itemName))
                        displayText = $"{imageInfo.GraphicId} - {itemName}";
                    else
                        displayText = $"{imageInfo.GraphicId} ({imageInfo.ItemId})";

                    using (var textBrush = new SolidBrush(e.Item.Selected ? HolographicTheme.TextPrimary : HolographicTheme.TextSecondary))
                    {
                        e.Graphics.DrawString(displayText, this.Font, textBrush, textRect, sf);
                    }
                }
                else
                {
                    var textRect = new Rectangle(e.Bounds.Left + 2, e.Bounds.Top, e.Bounds.Width, e.Bounds.Height);
                    var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };

                    // Show hex ID, decimal ID, and item name even when no image
                    string displayText;
                    if (!string.IsNullOrEmpty(itemName))
                        displayText = $"{imageInfo.GraphicId} - {itemName}";
                    else
                        displayText = $"{imageInfo.GraphicId} ({imageInfo.ItemId})";

                    using (var textBrush = new SolidBrush(e.Item.Selected ? HolographicTheme.TextPrimary : HolographicTheme.TextSecondary))
                    {
                        e.Graphics.DrawString(displayText, this.Font, textBrush, textRect, sf);
                    }
                }
            }
            // Draw border with theme color
            using (var pen = new Pen(HolographicTheme.BorderDark))
            { e.Graphics.DrawRectangle(pen, e.Bounds); }
        }

        private void PaletteListView_CacheVirtualItems(object sender, CacheVirtualItemsEventArgs e)
        {
            Task.Run(() =>
            {
                if (useTecmoExpanded)
                {
                    // Tecmo Expanded Art - use int IDs
                    for (int i = e.StartIndex; i <= e.EndIndex && i < filteredStaticIdsInt.Count; i++)
                    {
                        int itemId = filteredStaticIdsInt[i];

                        // Skip empty slots - they have no image to cache
                        if (IsEmptySlot(itemId))
                            continue;

                        string cacheKey = $"0x{itemId:X}";

                        bool needsCache = false;
                        lock (imageCache) { needsCache = !imageCache.ContainsKey(cacheKey); }

                        if (needsCache)
                        {
                            try
                            {
                                // Load from Tecmo art
                                Bitmap raw = TecmoArtReader.LoadStaticArt(artFolderPath, itemId);

                                if (raw != null)
                                {
                                    lock (imageCache)
                                    {
                                        if (!imageCache.ContainsKey(cacheKey))
                                            imageCache[cacheKey] = raw;
                                        else
                                            raw.Dispose();
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
                else
                {
                    // MUL/UOP-based caching
                    for (int i = e.StartIndex; i <= e.EndIndex && i < filteredStaticIds.Count; i++)
                    {
                        ushort itemId = filteredStaticIds[i];

                        // Skip empty slots - they have no image to cache
                        if (IsEmptySlot(itemId))
                            continue;

                        string cacheKey = $"0x{itemId:X4}";

                        bool needsCache = false;
                        lock (imageCache) { needsCache = !imageCache.ContainsKey(cacheKey); }

                        if (needsCache)
                        {
                            try
                            {
                                // Load from MUL, UOP, or Tecmo based on mode
                                Bitmap raw;
                                if (useUopOnly)
                                    raw = UopArtReader.LoadStaticArt(artFolderPath, itemId);
                                else
                                    raw = StaticArtReader.LoadStaticArt(artFolderPath, itemId);

                                if (raw != null)
                                {
                                    lock (imageCache)
                                    {
                                        if (!imageCache.ContainsKey(cacheKey))
                                            imageCache[cacheKey] = raw;
                                        else
                                            raw.Dispose();
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
            });
        }

        private void PaletteListView_MouseDown(object sender, MouseEventArgs e)
        {
            var item = paletteListView.GetItemAt(e.X, e.Y);
            if (item == null) return;

            var imageInfo = item.Tag as ImageInfo;
            if (imageInfo == null) return;

            // Update selected item and info panel
            selectedPaletteItemId = imageInfo.ItemId;
            UpdatePaletteInfoPanel(imageInfo.ItemId);

            // Handle empty slot click - offer to assign art
            if (imageInfo.IsEmptySlot)
            {
                HandleEmptySlotClick(imageInfo.ItemId, e);
                return;
            }

            // Right-click on non-empty item - show context menu
            if (e.Button == MouseButtons.Right)
            {
                ShowPaletteItemContextMenu(imageInfo.ItemId, e.Location);
                return;
            }

            // Ctrl/Shift clicks are for selection - don't start drag yet.
            // The ListView handles multi-select automatically.
            if ((ModifierKeys & (Keys.Control | Keys.Shift)) != 0)
                return;

            // Collect all selected non-empty item IDs for drag
            var dragItemIds = new List<int>();

            // If clicked item isn't in the current selection, drag just this one
            bool clickedIsSelected = paletteListView.SelectedIndices.Count > 0 &&
                paletteListView.SelectedIndices.Contains(item.Index);

            if (clickedIsSelected && paletteListView.SelectedIndices.Count > 1)
            {
                // Collect all selected item IDs
                foreach (int idx in paletteListView.SelectedIndices)
                {
                    var li = paletteListView.Items[idx];
                    var info = li.Tag as ImageInfo;
                    if (info != null && !info.IsEmptySlot)
                        dragItemIds.Add(info.ItemId);
                }
            }
            else
            {
                dragItemIds.Add(imageInfo.ItemId);
            }

            if (dragItemIds.Count == 0) return;

            // Load the first item's image for the drag preview
            Image firstImage = null;
            try { firstImage = LoadStaticItemImage(dragItemIds[0]); }
            catch { return; }
            if (firstImage == null) return;

            var data = new DataObject();
            data.SetData(DataFormats.Bitmap, firstImage);
            data.SetData("GraphicId", imageInfo.GraphicId);
            data.SetData("ItemId", dragItemIds[0]);

            // Store all IDs as a ushort array for multi-item drops
            if (dragItemIds.Count > 1)
                data.SetData("MultiItemIds", dragItemIds.ToArray());

            paletteListView.DoDragDrop(data, DragDropEffects.Copy);
        }

        /// <summary>
        /// Handle click on an empty slot - offer to assign artwork
        /// </summary>
        private void HandleEmptySlotClick(int itemId, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right || (e.Button == MouseButtons.Left && selectedObject != null))
            {
                // Show context menu with option to assign art
                ShowAssignArtContextMenu(itemId, e.Location);
            }
            else if (e.Button == MouseButtons.Left)
            {
                // Show info about how to assign art
                if (selectedObject == null)
                {
                    outputTextBox.AppendText($"Empty slot 0x{itemId:X4} - Select an object on the canvas first, then click this slot to assign its artwork.\r\n");
                }
            }
        }

        /// <summary>
        /// Show context menu for assigning art to an empty slot
        /// </summary>
        private void ShowAssignArtContextMenu(int itemId, Point location)
        {
            var menu = new ContextMenuStrip();
            menu.BackColor = HolographicTheme.PanelBackground;
            menu.ForeColor = HolographicTheme.TextPrimary;

            var headerItem = new ToolStripLabel($"Empty Slot 0x{itemId:X4}");
            headerItem.ForeColor = Color.FromArgb(100, 180, 100);
            headerItem.Font = new Font(headerItem.Font, FontStyle.Bold);
            menu.Items.Add(headerItem);
            menu.Items.Add(new ToolStripSeparator());

            if (selectedObject != null && selectedObject.Image != null)
            {
                var assignItem = new ToolStripMenuItem($"Assign selected canvas object artwork");
                assignItem.Tag = new Tuple<int, PlacedObject>(itemId, selectedObject);
                assignItem.Click += AssignArtToSlot_Click;
                menu.Items.Add(assignItem);
            }
            else
            {
                var noSelectionItem = new ToolStripMenuItem("(Select an object on canvas first)");
                noSelectionItem.Enabled = false;
                menu.Items.Add(noSelectionItem);
            }

            // Option to import from file
            var importItem = new ToolStripMenuItem("Import from PNG file...");
            importItem.Tag = itemId;
            importItem.Click += ImportArtToSlot_Click;
            menu.Items.Add(importItem);

            menu.Show(paletteListView, location);
        }

        /// <summary>
        /// Assign artwork from selected canvas object to empty slot
        /// </summary>
        private void AssignArtToSlot_Click(object sender, EventArgs e)
        {
            var menuItem = sender as ToolStripMenuItem;
            if (menuItem?.Tag == null) return;

            var data = menuItem.Tag as Tuple<ushort, PlacedObject>;
            if (data == null) return;

            ushort itemId = data.Item1;
            PlacedObject obj = data.Item2;

            if (obj?.Image == null)
            {
                MessageBox.Show("Selected object has no image.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Convert the image to a Bitmap if necessary
            Bitmap bmp = obj.Image as Bitmap ?? new Bitmap(obj.Image);

            // Add to pending changes
            if (!pendingArtChanges.ContainsKey(itemId))
            {
                pendingArtChanges[itemId] = bmp;
            }
            else
            {
                pendingArtChanges[itemId]?.Dispose();
                pendingArtChanges[itemId] = bmp;
            }

            // Update the image cache so the palette shows the new image
            string cacheKey = $"0x{itemId:X4}";
            lock (imageCache)
            {
                imageCache[cacheKey] = bmp;
            }

            // Add to valid list and set (no longer empty)
            if (!validStaticIdSet.Contains(itemId))
            {
                validStaticIdSet.Add(itemId);
                validStaticIds.Add(itemId);
                validStaticIds.Sort();
            }

            // Remove from empty list if present
            emptyStaticIds.Remove(itemId);

            // Update UI
            UpdatePendingChangesUI();
            paletteListView.Invalidate();

            outputTextBox.AppendText($"Assigned artwork to slot 0x{itemId:X4}. Click 'Save to MUL' to write changes.\r\n");
        }

        /// <summary>
        /// Import artwork from PNG file to empty slot
        /// </summary>
        private void ImportArtToSlot_Click(object sender, EventArgs e)
        {
            var menuItem = sender as ToolStripMenuItem;
            if (menuItem?.Tag == null) return;

            ushort itemId = (ushort)menuItem.Tag;

            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = $"Select PNG image for slot 0x{itemId:X4}";
                ofd.Filter = "PNG Images|*.png|All Images|*.png;*.bmp;*.jpg;*.jpeg;*.gif";
                ofd.FilterIndex = 1;

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        var img = new Bitmap(ofd.FileName);

                        // Add to pending changes
                        if (!pendingArtChanges.ContainsKey(itemId))
                        {
                            pendingArtChanges[itemId] = img;
                        }
                        else
                        {
                            pendingArtChanges[itemId]?.Dispose();
                            pendingArtChanges[itemId] = img;
                        }

                        // Update the image cache
                        string cacheKey = $"0x{itemId:X4}";
                        lock (imageCache)
                        {
                            imageCache[cacheKey] = img;
                        }

                        // Add to valid list and set (no longer empty)
                        if (!validStaticIdSet.Contains(itemId))
                        {
                            validStaticIdSet.Add(itemId);
                            validStaticIds.Add(itemId);
                            validStaticIds.Sort();
                        }

                        // Remove from empty list if present
                        emptyStaticIds.Remove(itemId);

                        // Update UI
                        UpdatePendingChangesUI();
                        paletteListView.Invalidate();

                        outputTextBox.AppendText($"Imported artwork to slot 0x{itemId:X4} from {Path.GetFileName(ofd.FileName)}. Click 'Save to MUL' to write changes.\r\n");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error importing image: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        /// <summary>
        /// Handle selection change in palette list view
        /// </summary>
        private void PaletteListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (paletteListView.SelectedIndices.Count > 0)
            {
                int index = paletteListView.SelectedIndices[0];
                if (index >= 0 && index < filteredStaticIds.Count)
                {
                    ushort itemId = filteredStaticIds[index];
                    selectedPaletteItemId = itemId;
                    UpdatePaletteInfoPanel(itemId);
                }
            }
        }

        /// <summary>
        /// Initialize the info panel for showing selected item TileData
        /// </summary>
        private void InitializePaletteInfoPanel()
        {
            if (paletteInfoPanel == null) return;

            paletteInfoPanel.BackColor = HolographicTheme.PanelBackground;
            paletteInfoPanel.Padding = new Padding(5);

            // Preview image box
            paletteInfoPreview = new PictureBox
            {
                Size = new Size(80, 80),
                Location = new Point(5, 5),
                BackColor = Color.FromArgb(30, 35, 40),
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle
            };
            paletteInfoPanel.Controls.Add(paletteInfoPreview);

            // Name label (header)
            paletteInfoNameLabel = new Label
            {
                Location = new Point(90, 5),
                Size = new Size(paletteInfoPanel.Width - 100, 20),
                Font = new Font("Consolas", 10, FontStyle.Bold),
                ForeColor = HolographicTheme.SelectionCyan,
                Text = "Select an item..."
            };
            paletteInfoPanel.Controls.Add(paletteInfoNameLabel);

            // ID label
            paletteInfoIdLabel = new Label
            {
                Location = new Point(90, 25),
                Size = new Size(paletteInfoPanel.Width - 100, 18),
                Font = new Font("Consolas", 9),
                ForeColor = HolographicTheme.TextSecondary,
                Text = ""
            };
            paletteInfoPanel.Controls.Add(paletteInfoIdLabel);

            // Properties label (multiline)
            paletteInfoPropertiesLabel = new Label
            {
                Location = new Point(5, 90),
                Size = new Size(paletteInfoPanel.Width - 15, 70),
                Font = new Font("Consolas", 8),
                ForeColor = HolographicTheme.TextPrimary,
                Text = ""
            };
            paletteInfoPanel.Controls.Add(paletteInfoPropertiesLabel);

            // Flags label (multiline)
            paletteInfoFlagsLabel = new Label
            {
                Location = new Point(5, 160),
                Size = new Size(paletteInfoPanel.Width - 15, 55),
                Font = new Font("Consolas", 7.5f),
                ForeColor = Color.FromArgb(255, 200, 100),
                Text = ""
            };
            paletteInfoPanel.Controls.Add(paletteInfoFlagsLabel);

            // Apply theme
            HolographicTheme.ApplyToPanel(paletteInfoPanel);
        }

        /// <summary>
        /// Update the palette info panel with TileData for the given item
        /// </summary>
        private void UpdatePaletteInfoPanel(int itemId)
        {
            if (paletteInfoPanel == null || paletteInfoNameLabel == null) return;

            // Load image for preview
            Image img = null;
            try
            {
                img = LoadStaticItemImage(itemId);
                
                // Validate image before assigning to PictureBox
                if (img != null)
                {
                    try
                    {
                        // Test if image is valid by accessing properties that PictureBox uses
                        // FrameDimensionsList is what triggers the exception in PictureBox.set_Image
                        var _ = img.Width;
                        var __ = img.Height;
                        var ___ = img.FrameDimensionsList;
                        
                        // Dispose existing image before assigning new one
                        var oldImage = paletteInfoPreview.Image;
                        paletteInfoPreview.Image = img;
                        // Don't dispose oldImage if it's from the cache
                    }
                    catch
                    {
                        // Image validation failed - don't assign it
                        paletteInfoPreview.Image = null;
                        img?.Dispose();
                        img = null;
                    }
                }
                else
                {
                    paletteInfoPreview.Image = null;
                }
            }
            catch (Exception)
            {
                // Image loading failed - don't assign it
                paletteInfoPreview.Image = null;
                img?.Dispose();
                img = null;
            }

            // Get TileData
            if (!tileDataReader.IsLoaded || itemId > 0xFFFF)
            {
                paletteInfoNameLabel.Text = itemId > 0xFFFF ? "TileData not available (ID > 65535)" : "TileData not loaded";
                paletteInfoIdLabel.Text = $"ID: 0x{itemId:X} ({itemId})";
                paletteInfoPropertiesLabel.Text = "";
                paletteInfoFlagsLabel.Text = "";
                return;
            }

            var itemData = tileDataReader.GetItemTile((ushort)itemId);
            if (itemData == null)
            {
                paletteInfoNameLabel.Text = "Unknown Item";
                paletteInfoIdLabel.Text = $"ID: 0x{itemId:X4} ({itemId})";
                paletteInfoPropertiesLabel.Text = "No TileData available";
                paletteInfoFlagsLabel.Text = "";
                return;
            }

            // Update name
            string name = !string.IsNullOrWhiteSpace(itemData.Name) ? itemData.Name : "Unnamed";
            paletteInfoNameLabel.Text = name;

            // Update ID with pixel dimensions
            string sizeInfo = img != null ? $" | {img.Width}x{img.Height}px" : "";
            paletteInfoIdLabel.Text = $"0x{itemId:X4} ({itemId}){sizeInfo}";

            // Update properties
            var propsBuilder = new StringBuilder();
            propsBuilder.AppendLine($"Height: {itemData.Height}  Weight: {itemData.Weight}");
            propsBuilder.AppendLine($"Animation: 0x{itemData.Animation:X4}  Quality: {itemData.Quality}");
            propsBuilder.AppendLine($"Quantity: {itemData.Quantity}  StackOff: {itemData.StackingOffset}");
            propsBuilder.Append($"Hue: {itemData.Hue}");
            paletteInfoPropertiesLabel.Text = propsBuilder.ToString();

            // Update flags
            string flagsStr = itemData.GetFlagsString();
            if (flagsStr != "None")
            {
                // Format flags to fit in the panel
                var flags = flagsStr.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                var flagsFormatted = string.Join(", ", flags.Take(8));
                if (flags.Length > 8)
                    flagsFormatted += $" +{flags.Length - 8} more";
                paletteInfoFlagsLabel.Text = $"Flags: {flagsFormatted}";
            }
            else
            {
                paletteInfoFlagsLabel.Text = "Flags: None";
            }
        }

        /// <summary>
        /// Initialize the tooltip for palette items
        /// </summary>
        private void InitializePaletteTooltip()
        {
            paletteToolTip = new ToolTip
            {
                AutoPopDelay = 15000,  // Show for 15 seconds
                InitialDelay = 300,    // Show after 300ms hover
                ReshowDelay = 100,
                ShowAlways = true,
                UseAnimation = true,
                UseFading = true
            };
        }

        /// <summary>
        /// Handle mouse move over palette list to show tooltip
        /// </summary>
        private void PaletteListView_MouseMove(object sender, MouseEventArgs e)
        {
            var item = paletteListView.GetItemAt(e.X, e.Y);
            int currentIndex = item != null ? item.Index : -1;

            if (currentIndex == lastHoveredIndex)
                return;

            lastHoveredIndex = currentIndex;

            if (item == null || item.Tag == null)
            {
                paletteToolTip?.Hide(paletteListView);
                return;
            }

            var imageInfo = item.Tag as ImageInfo;
            if (imageInfo == null)
            {
                paletteToolTip?.Hide(paletteListView);
                return;
            }

            // Build tooltip text with TileData properties
            string tooltipText = BuildTileDataTooltip(imageInfo.ItemId);

            if (!string.IsNullOrEmpty(tooltipText))
            {
                paletteToolTip?.SetToolTip(paletteListView, tooltipText);
            }
            else
            {
                paletteToolTip?.Hide(paletteListView);
            }
        }

        /// <summary>
        /// Build a tooltip string with TileData properties for an item
        /// </summary>
        private string BuildTileDataTooltip(int itemId)
        {
            if (!tileDataReader.IsLoaded || itemId > 0xFFFF)
                return null;

            var itemData = tileDataReader.GetItemTile((ushort)itemId);
            if (itemData == null)
                return $"ID: 0x{itemId:X} ({itemId})\nNo TileData available";

            var sb = new StringBuilder();

            // Name and ID
            if (!string.IsNullOrWhiteSpace(itemData.Name))
                sb.AppendLine($"Name: {itemData.Name}");
            sb.AppendLine($"ID: 0x{itemId:X4} ({itemId})");
            sb.AppendLine("?????????????????????");

            // Basic properties
            sb.AppendLine($"Height: {itemData.Height}");
            sb.AppendLine($"Weight: {itemData.Weight}");
            sb.AppendLine($"Animation: 0x{itemData.Animation:X4}");
            sb.AppendLine($"Quality: {itemData.Quality}");
            sb.AppendLine($"Quantity: {itemData.Quantity}");
            sb.AppendLine($"Stacking Offset: {itemData.StackingOffset}");
            sb.AppendLine($"Hue: {itemData.Hue}");

            // Flags
            string flagsStr = itemData.GetFlagsString();
            if (flagsStr != "None")
            {
                sb.AppendLine("?????????????????????");
                sb.AppendLine("Flags:");

                // Split flags into readable list
                var flags = flagsStr.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var flag in flags)
                {
                    sb.AppendLine($"  - {flag}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Show context menu for a non-empty palette item (right-click)
        /// </summary>
        private void ShowPaletteItemContextMenu(int itemId, Point location)
        {
            var menu = new ContextMenuStrip();
            menu.BackColor = HolographicTheme.PanelBackground;
            menu.ForeColor = HolographicTheme.TextPrimary;

            var headerItem = new ToolStripLabel($"Item 0x{itemId:X4}");
            headerItem.ForeColor = HolographicTheme.CyanAccent;
            headerItem.Font = new Font(headerItem.Font, FontStyle.Bold);
            menu.Items.Add(headerItem);
            menu.Items.Add(new ToolStripSeparator());

            // Push single item to JarJar (grayed while the feature is off)
            {
                var pushItem = new ToolStripMenuItem("Push to MeesaJarJar");
                pushItem.ForeColor = Color.FromArgb(180, 100, 255);
                pushItem.Tag = itemId;
                pushItem.Click += PushSingleItemToJarJar_Click;

                // Enable if item has unpushed changes (survives Save Art)
                pushItem.Enabled = AppConfig.Instance.JarJarPushEnabled &&
                    (jarjarPendingChanges.ContainsKey(itemId) || pendingArtChanges.ContainsKey(itemId));
                if (!pushItem.Enabled)
                    pushItem.Text += AppConfig.Instance.JarJarPushEnabled ? " (no pending changes)" : " (disabled)";

                menu.Items.Add(pushItem);

                var pullItem = new ToolStripMenuItem("Pull from MeesaJarJar");
                pullItem.ForeColor = Color.FromArgb(100, 180, 255);
                pullItem.Tag = itemId;
                pullItem.Click += PullSingleItemFromJarJar_Click;
                menu.Items.Add(pullItem);

                menu.Items.Add(new ToolStripSeparator());
            }

            // Export as PNG
            var exportItem = new ToolStripMenuItem("Export as PNG...");
            exportItem.Tag = itemId;
            exportItem.Click += ExportPaletteItemAsPng_Click;
            menu.Items.Add(exportItem);

            menu.Show(paletteListView, location);
        }

        /// <summary>
        /// Push a single modified item to MeesaJarJar
        /// </summary>
        private async void PushSingleItemToJarJar_Click(object sender, EventArgs e)
        {
            var menuItem = sender as ToolStripMenuItem;
            if (menuItem?.Tag == null) return;

            ushort itemId = (ushort)menuItem.Tag;

            // Try jarjarPendingChanges first (survives Save Art), then pendingArtChanges
            Bitmap pushBitmap = null;
            if (jarjarPendingChanges.ContainsKey(itemId))
                pushBitmap = jarjarPendingChanges[itemId];
            else if (pendingArtChanges.ContainsKey(itemId))
                pushBitmap = pendingArtChanges[itemId];

            if (pushBitmap == null)
            {
                MessageBox.Show($"No pending changes for 0x{itemId:X4}.", "Push to JarJar",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var config = AppConfig.Instance;
            JarJarClient client = null;

            try
            {
                client = new JarJarClient(config.JarJarApiUrl, config.JarJarAuthToken);

                SetAIStatus($"Pushing 0x{itemId:X4} to JarJar...");

                var result = await client.UploadSpriteAsync(itemId, pushBitmap);

                if (result.Success)
                {
                    jarjarPendingChanges.Remove(itemId);
                    UpdatePendingChangesUI();
                    SetAIStatus($"Pushed 0x{itemId:X4}", HolographicTheme.ButtonSuccess);
                    OutputLog($"Pushed 0x{itemId:X4} to JarJar - clients update in ~30s");
                }
                else
                {
                    SetAIStatus($"Push failed: {result.Message}", HolographicTheme.ButtonDanger);
                    OutputLog($"FAILED pushing 0x{itemId:X4}: {result.Message}");
                    MessageBox.Show($"Failed to push 0x{itemId:X4}:\n\n{result.Message}",
                        "Push Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                SetAIStatus($"Error: {ex.Message}", HolographicTheme.ButtonDanger);
                OutputLog($"ERROR pushing 0x{itemId:X4}: {ex.Message}");
            }
            finally
            {
                client?.Dispose();
            }
        }

        /// <summary>
        /// Pull the current sprite from MeesaJarJar and display it for comparison
        /// </summary>
        private async void PullSingleItemFromJarJar_Click(object sender, EventArgs e)
        {
            var menuItem = sender as ToolStripMenuItem;
            if (menuItem?.Tag == null) return;

            ushort itemId = (ushort)menuItem.Tag;
            var config = AppConfig.Instance;
            JarJarClient client = null;

            try
            {
                client = new JarJarClient(config.JarJarApiUrl, config.JarJarAuthToken);

                SetAIStatus($"Pulling 0x{itemId:X4} from JarJar...");

                var entry = await client.GetSpriteInfoAsync(itemId);
                if (!entry.HasValue)
                {
                    SetAIStatus("Not found on server", HolographicTheme.ButtonWarning);
                    MessageBox.Show($"Sprite 0x{itemId:X4} not found in the JarJar lookup table.",
                        "Pull from JarJar", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var serverBitmap = await client.DownloadSpriteAsync(itemId);
                if (serverBitmap == null)
                {
                    SetAIStatus("Download failed", HolographicTheme.ButtonDanger);
                    return;
                }

                var info = entry.Value;
                SetAIStatus($"Pulled 0x{itemId:X4} ({info.Width}x{info.Height})", HolographicTheme.ButtonSuccess);

                // Show a comparison form
                ShowJarJarComparisonForm(itemId, serverBitmap, info);
            }
            catch (Exception ex)
            {
                SetAIStatus($"Error: {ex.Message}", HolographicTheme.ButtonDanger);
                OutputLog($"ERROR pulling 0x{itemId:X4}: {ex.Message}");
            }
            finally
            {
                client?.Dispose();
            }
        }

        /// <summary>
        /// Show a comparison form between local and server artwork
        /// </summary>
        private void ShowJarJarComparisonForm(ushort itemId, Bitmap serverBitmap, LookupEntry entry)
        {
            var form = new Form
            {
                Text = $"JarJar Comparison - 0x{itemId:X4} (Atlas {entry.AtlasIndex} @ {entry.X},{entry.Y})",
                Width = 500,
                Height = 350,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false
            };
            HolographicTheme.ApplyToForm(form);

            var serverLabel = new Label
            {
                Text = $"Server ({entry.Width}x{entry.Height})",
                Location = new Point(50, 10),
                Width = 180,
                TextAlign = ContentAlignment.MiddleCenter
            };
            HolographicTheme.ApplyToLabel(serverLabel);
            form.Controls.Add(serverLabel);

            var serverPic = new PictureBox
            {
                Location = new Point(50, 35),
                Width = 180,
                Height = 180,
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = serverBitmap,
                BackColor = HolographicTheme.InputBackground,
                BorderStyle = BorderStyle.FixedSingle
            };
            form.Controls.Add(serverPic);

            // Try to load local version
            Image localImage = null;
            if (pendingArtChanges.ContainsKey(itemId))
                localImage = pendingArtChanges[itemId];
            else
                try { localImage = LoadStaticItemImage(itemId); } catch { }

            var localLabel = new Label
            {
                Text = localImage != null ? $"Local ({localImage.Width}x{localImage.Height})" : "Local (not found)",
                Location = new Point(270, 10),
                Width = 180,
                TextAlign = ContentAlignment.MiddleCenter
            };
            HolographicTheme.ApplyToLabel(localLabel);
            form.Controls.Add(localLabel);

            var localPic = new PictureBox
            {
                Location = new Point(270, 35),
                Width = 180,
                Height = 180,
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = localImage,
                BackColor = HolographicTheme.InputBackground,
                BorderStyle = BorderStyle.FixedSingle
            };
            form.Controls.Add(localPic);

            // "Use Server Art" button - replaces local with server version
            var useServerBtn = new Button
            {
                Text = "Use Server Art",
                Location = new Point(50, 230),
                Width = 180,
                Height = 30
            };
            HolographicTheme.ApplyToButton(useServerBtn, ButtonStyle.Accent);
            useServerBtn.Click += (s, ev) =>
            {
                var copy = new Bitmap(serverBitmap);
                pendingArtChanges[itemId] = copy;

                string cacheKey = $"0x{itemId:X4}";
                lock (imageCache)
                {
                    imageCache[cacheKey] = copy;
                }

                UpdatePendingChangesUI();
                paletteListView.Invalidate();
                OutputLog($"Replaced local 0x{itemId:X4} with server art");
                form.Close();
            };
            form.Controls.Add(useServerBtn);

            var closeBtn = new Button
            {
                Text = "Close",
                Location = new Point(270, 230),
                Width = 180,
                Height = 30
            };
            HolographicTheme.ApplyToButton(closeBtn);
            closeBtn.Click += (s, ev) => form.Close();
            form.Controls.Add(closeBtn);

            form.ShowDialog(this);
        }

        /// <summary>
        /// Export a palette item as a PNG file
        /// </summary>
        private void ExportPaletteItemAsPng_Click(object sender, EventArgs e)
        {
            var menuItem = sender as ToolStripMenuItem;
            if (menuItem?.Tag == null) return;

            ushort itemId = (ushort)menuItem.Tag;
            Image img = null;

            if (pendingArtChanges.ContainsKey(itemId))
                img = pendingArtChanges[itemId];
            else
                try { img = LoadStaticItemImage(itemId); } catch { }

            if (img == null)
            {
                MessageBox.Show($"Could not load image for 0x{itemId:X4}.", "Export",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = $"Export 0x{itemId:X4} as PNG";
                sfd.Filter = "PNG Image|*.png";
                sfd.FileName = $"0x{itemId:X4}.png";

                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    img.Save(sfd.FileName, System.Drawing.Imaging.ImageFormat.Png);
                    OutputLog($"Exported 0x{itemId:X4} to {sfd.FileName}");
                }
            }
        }
    }
}
