using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MeesaMultisMaker.Helpers;
using MeesaMultisMaker.JarJar;
using MeesaMultisMaker.Mul;

namespace MeesaMultisMaker
{
    partial class MapViewerForm
    {
        // ================================================================
        //  Static Palette — state
        // ================================================================
        private Panel staticPalettePanel;
        private TextBox paletteSearchTextBox;
        private ListView paletteListView;
        private ColumnHeader paletteColumnHeader;
        private Panel paletteInfoPanel;
        private PictureBox paletteInfoPreview;
        private Label paletteInfoNameLabel;
        private Label paletteInfoIdLabel;
        private Label paletteInfoPropertiesLabel;
        private Label paletteInfoFlagsLabel;
        private ToolTip paletteToolTip;

        private List<ushort> validStaticIds = new List<ushort>();
        private HashSet<ushort> validStaticIdSet = new HashSet<ushort>();
        private List<ushort> filteredStaticIds = new List<ushort>();
        private readonly Dictionary<string, Image> paletteImageCache = new Dictionary<string, Image>();
        private int paletteLastHoveredIndex = -1;
        private ushort selectedPaletteItemId;
        private bool paletteLoaded = false;
        private const string STATIC_DRAG_FORMAT = "MeesaMultisMaker.StaticItemId";

        // ================================================================
        //  Panel creation — called from Designer.cs
        // ================================================================

        internal void InitializeStaticPalettePanel()
        {
            staticPalettePanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = 280,
                Padding = new Padding(0)
            };
            HolographicTheme.ApplyToPanel(staticPalettePanel, true);

            // ── Search box (top) ────────────────────────────
            paletteSearchTextBox = new TextBox
            {
                Dock = DockStyle.Top,
                Height = 25,
                Text = "Search statics...",
                ForeColor = Color.Gray,
                Font = new Font("Consolas", 10)
            };
            HolographicTheme.ApplyToTextBox(paletteSearchTextBox);
            paletteSearchTextBox.TextChanged += PaletteSearch_TextChanged;
            paletteSearchTextBox.Enter += (s, e) =>
            {
                if (paletteSearchTextBox.Text == "Search statics...")
                {
                    paletteSearchTextBox.Text = "";
                    paletteSearchTextBox.ForeColor = HolographicTheme.TextPrimary;
                }
            };
            paletteSearchTextBox.Leave += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(paletteSearchTextBox.Text))
                {
                    paletteSearchTextBox.Text = "Search statics...";
                    paletteSearchTextBox.ForeColor = Color.Gray;
                }
            };

            // ── Info panel (bottom) ─────────────────────────
            paletteInfoPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 200,
                Padding = new Padding(5)
            };
            HolographicTheme.ApplyToPanel(paletteInfoPanel);
            BuildPaletteInfoPanel();

            // ── List view (fill) ────────────────────────────
            paletteColumnHeader = new ColumnHeader { Text = "Item", Width = 260 };

            // Dummy ImageList to force row height — WinForms uses SmallImageList.ImageSize.Height
            var rowSpacer = new ImageList { ImageSize = new Size(1, 80) };

            paletteListView = new ListView
            {
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = true,
                OwnerDraw = true,
                View = View.Details,
                VirtualMode = true,
                HeaderStyle = ColumnHeaderStyle.None,
                SmallImageList = rowSpacer
            };
            paletteListView.Columns.Add(paletteColumnHeader);
            HolographicTheme.ApplyToListView(paletteListView);
            paletteListView.BackColor = HolographicTheme.InputBackground;
            paletteListView.ForeColor = HolographicTheme.TextPrimary;

            paletteListView.RetrieveVirtualItem += Palette_RetrieveVirtualItem;
            paletteListView.DrawItem += Palette_DrawItem;
            paletteListView.CacheVirtualItems += Palette_CacheVirtualItems;
            paletteListView.MouseDown += Palette_MouseDown;
            paletteListView.MouseDoubleClick += Palette_MouseDoubleClick;
            paletteListView.MouseMove += Palette_MouseMove;
            paletteListView.ItemDrag += Palette_ItemDrag;
            paletteListView.MouseLeave += (s, e) => { paletteLastHoveredIndex = -1; paletteToolTip?.Hide(paletteListView); };
            paletteListView.SelectedIndexChanged += Palette_SelectedIndexChanged;

            // Tooltip
            paletteToolTip = new ToolTip
            {
                AutoPopDelay = 5000,
                InitialDelay = 400,
                ReshowDelay = 200,
                ShowAlways = true
            };

            // Add in reverse dock order: search (top), info (bottom), list (fill)
            staticPalettePanel.Controls.Add(paletteListView);
            staticPalettePanel.Controls.Add(paletteInfoPanel);
            staticPalettePanel.Controls.Add(paletteSearchTextBox);

            this.Controls.Add(staticPalettePanel);
        }

        // ================================================================
        //  Populate the palette from the loaded MUL folder
        // ================================================================

        /// <summary>
        /// Scan art files and populate the static palette list.
        /// Call after mulFolderPath is set.
        /// </summary>
        internal void LoadStaticPalette()
        {
            if (string.IsNullOrEmpty(mulFolderPath)) return;
            if (paletteLoaded) return;

            validStaticIds.Clear();
            validStaticIdSet.Clear();
            filteredStaticIds.Clear();

            try
            {
                if (!cachedArtFormat.HasValue)
                    cachedArtFormat = AppConfig.Instance.GetArtFileFormat(mulFolderPath);

                var format = cachedArtFormat.Value;

                if (format == ArtFileFormat.MulFiles)
                    ScanArtMul();
                else if (format == ArtFileFormat.UopOnly)
                    ScanArtUop();
                else if (format == ArtFileFormat.TecmoExpanded)
                    ScanArtTecmo();

                filteredStaticIds = new List<ushort>(validStaticIds);
                paletteListView.VirtualListSize = filteredStaticIds.Count;
                paletteListView.Invalidate();
                paletteLoaded = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StaticPalette: Load failed: {ex.Message}");
            }
        }

        private void ScanArtTecmo()
        {
            // Use TecmoArtReader to get valid static item IDs
            TecmoArtReader.ClearCache();
            var tecmoIds = TecmoArtReader.GetValidStaticItemIds(mulFolderPath);
            foreach (var id in tecmoIds)
            {
                if (id <= 0xFFFF)
                {
                    validStaticIds.Add((ushort)id);
                    validStaticIdSet.Add((ushort)id);
                }
            }
            validStaticIds.Sort();
        }

        private void Palette_ItemDrag(object sender, ItemDragEventArgs e)
        {
            if (!(e.Item is ListViewItem item) || !(item.Tag is ushort itemId))
                return;

            selectedPaletteItemId = itemId;
            var data = new DataObject();
            data.SetData(STATIC_DRAG_FORMAT, itemId);
            data.SetData(DataFormats.Text, itemId.ToString());
            paletteListView.DoDragDrop(data, DragDropEffects.Copy);
        }

        private void MapPictureBox_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(STATIC_DRAG_FORMAT) || e.Data.GetDataPresent(DataFormats.Text))
                e.Effect = DragDropEffects.Copy;
            else
                e.Effect = DragDropEffects.None;
        }

        private void MapPictureBox_DragDrop(object sender, DragEventArgs e)
        {
            ushort itemId;
            object dragData = null;

            if (e.Data.GetDataPresent(STATIC_DRAG_FORMAT))
                dragData = e.Data.GetData(STATIC_DRAG_FORMAT);
            else if (e.Data.GetDataPresent(DataFormats.Text))
                dragData = e.Data.GetData(DataFormats.Text);

            if (dragData is ushort)
            {
                itemId = (ushort)dragData;
            }
            else
            {
                if (!ushort.TryParse(Convert.ToString(dragData), out itemId))
                    return;
            }

            Point clientPoint = mapPictureBox.PointToClient(new Point(e.X, e.Y));
            if (!ScreenToMapCoords(clientPoint.X, clientPoint.Y, out int mapX, out int mapY))
                return;

            sbyte z = GetZAt(mapX, mapY);
            AddStaticToMap(itemId, mapX, mapY, z, 0);
            statusLabel.Text = $"Placed 0x{itemId:X4} at ({mapX},{mapY}) via drag-drop";
        }

        private void ScanArtMul()
        {
            string artIdxPath = Path.Combine(mulFolderPath, "artidx.mul");
            if (!File.Exists(artIdxPath)) return;

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
                    }
                }
            }
        }

        private void ScanArtUop()
        {
            // Get valid item IDs directly from the UOP index (fast)
            UopArtReader.ClearCache();
            var uopIds = UopArtReader.GetValidStaticItemIds(mulFolderPath);
            foreach (var id in uopIds)
            {
                validStaticIds.Add(id);
                validStaticIdSet.Add(id);
            }
            validStaticIds.Sort();
        }

        // ================================================================
        //  Search / filter
        // ================================================================

        private void PaletteSearch_TextChanged(object sender, EventArgs e)
        {
            string text = paletteSearchTextBox.Text?.Trim() ?? "";
            if (text == "Search statics..." || text.Length == 0)
            {
                filteredStaticIds = new List<ushort>(validStaticIds);
            }
            else
            {
                string searchUpper = text.ToUpperInvariant();

                filteredStaticIds = validStaticIds.Where(id =>
                {
                    // Match hex ID
                    string hexStr = id.ToString("X4");
                    if (hexStr.Contains(searchUpper) || id.ToString().Contains(searchUpper))
                        return true;

                    // Match name from tiledata
                    if (tileDataReader != null && tileDataReader.IsLoaded)
                    {
                        var itemData = tileDataReader.GetItemTile(id);
                        if (itemData != null && !string.IsNullOrEmpty(itemData.Name))
                        {
                            if (itemData.Name.ToUpperInvariant().Contains(searchUpper))
                                return true;
                        }
                    }
                    return false;
                }).ToList();
            }

            paletteListView.VirtualListSize = filteredStaticIds.Count;
            paletteListView.Invalidate();
        }

        // ================================================================
        //  Virtual list events
        // ================================================================

        private void Palette_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            if (e.ItemIndex < 0 || e.ItemIndex >= filteredStaticIds.Count) return;
            ushort itemId = filteredStaticIds[e.ItemIndex];
            string graphicId = $"0x{itemId:X4}";
            e.Item = new ListViewItem(graphicId)
            {
                Tag = itemId
            };
        }

        private void Palette_DrawItem(object sender, DrawListViewItemEventArgs e)
        {
            if (!(e.Item.Tag is ushort itemId)) return;

            // Background
            Color bgColor = e.Item.Selected ? HolographicTheme.ButtonAccent : HolographicTheme.InputBackground;
            using (var brush = new SolidBrush(bgColor))
                e.Graphics.FillRectangle(brush, e.Bounds);

            // Load image
            string cacheKey = $"0x{itemId:X4}";
            Image img = null;
            lock (paletteImageCache)
            {
                if (paletteImageCache.ContainsKey(cacheKey))
                    img = paletteImageCache[cacheKey];
            }
            if (img == null)
            {
                img = LoadStaticImage(itemId);
                if (img != null)
                {
                    lock (paletteImageCache)
                    {
                        if (!paletteImageCache.ContainsKey(cacheKey))
                            paletteImageCache[cacheKey] = img;
                    }
                }
            }

            // Get name from tiledata
            string itemName = null;
            if (tileDataReader != null && tileDataReader.IsLoaded)
            {
                var data = tileDataReader.GetItemTile(itemId);
                if (data != null && !string.IsNullOrWhiteSpace(data.Name))
                    itemName = data.Name;
            }

            int textLeft;
            if (img != null)
            {
                int imgX = e.Bounds.Left + 2;
                int imgY = e.Bounds.Top + Math.Max(0, (e.Bounds.Height - img.Height) / 2);
                try { e.Graphics.DrawImage(img, imgX, imgY, img.Width, img.Height); } catch { }
                textLeft = e.Bounds.Left + Math.Max(50, img.Width + 8);
            }
            else
            {
                textLeft = e.Bounds.Left + 6;
            }

            // Text
            string displayText = !string.IsNullOrEmpty(itemName)
                ? $"0x{itemId:X4} - {itemName}"
                : $"0x{itemId:X4} ({itemId})";
            var textRect = new Rectangle(textLeft, e.Bounds.Top, e.Bounds.Width - (textLeft - e.Bounds.Left), e.Bounds.Height);
            var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
            using (var textBrush = new SolidBrush(e.Item.Selected ? HolographicTheme.TextPrimary : HolographicTheme.TextSecondary))
                e.Graphics.DrawString(displayText, this.Font, textBrush, textRect, sf);

            // Border
            using (var pen = new Pen(HolographicTheme.BorderDark))
                e.Graphics.DrawRectangle(pen, e.Bounds);
        }

        private void Palette_CacheVirtualItems(object sender, CacheVirtualItemsEventArgs e)
        {
            Task.Run(() =>
            {
                for (int i = e.StartIndex; i <= e.EndIndex && i < filteredStaticIds.Count; i++)
                {
                    ushort itemId = filteredStaticIds[i];
                    string cacheKey = $"0x{itemId:X4}";

                    bool needs;
                    lock (paletteImageCache) { needs = !paletteImageCache.ContainsKey(cacheKey); }

                    if (needs)
                    {
                        try
                        {
                            var bmp = LoadStaticImage(itemId);
                            if (bmp != null)
                            {
                                lock (paletteImageCache)
                                {
                                    if (!paletteImageCache.ContainsKey(cacheKey))
                                        paletteImageCache[cacheKey] = bmp;
                                }
                            }
                        }
                        catch { }
                    }
                }
            });
        }

        // ================================================================
        //  Mouse events — select, drag, tooltip
        // ================================================================

        private void Palette_MouseDown(object sender, MouseEventArgs e)
        {
            var item = paletteListView.GetItemAt(e.X, e.Y);
            if (item?.Tag is ushort itemId)
            {
                selectedPaletteItemId = itemId;
                UpdatePaletteInfo(itemId);

                // Right-click — show context menu with JarJar options
                if (e.Button == MouseButtons.Right && AppConfig.Instance.JarJarPushEnabled)
                {
                    ShowStaticPaletteContextMenu(itemId, e.Location);
                }
            }
        }

        /// <summary>
        /// Double-click places selected static(s) at the camera position.
        /// </summary>
        private void Palette_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (paletteListView.SelectedIndices.Count > 1)
            {
                // Collect selected IDs
                var ids = new List<ushort>();
                foreach (int idx in paletteListView.SelectedIndices)
                {
                    if (idx >= 0 && idx < filteredStaticIds.Count)
                        ids.Add(filteredStaticIds[idx]);
                }
                PlaceMultipleStaticsSpaced(ids, cameraX, cameraY);
            }
            else
            {
                var item = paletteListView.GetItemAt(e.X, e.Y);
                if (item?.Tag is ushort itemId)
                    PlaceStaticAtCamera(itemId);
            }
        }

        private void Palette_SelectedIndexChanged(object sender, EventArgs e)
        {
            int count = paletteListView.SelectedIndices.Count;
            if (count == 0) return;

            // Always track the first selected item for single-place operations
            int idx = paletteListView.SelectedIndices[0];
            if (idx >= 0 && idx < filteredStaticIds.Count)
            {
                selectedPaletteItemId = filteredStaticIds[idx];
                UpdatePaletteInfo(selectedPaletteItemId);
            }

            // Show selection count in status when multiple items selected
            if (count > 1 && statusLabel != null)
                statusLabel.Text = $"{count} items selected — double-click to place all";
        }

        private void Palette_MouseMove(object sender, MouseEventArgs e)
        {
            var item = paletteListView.GetItemAt(e.X, e.Y);
            int idx = item?.Index ?? -1;
            if (idx == paletteLastHoveredIndex) return;
            paletteLastHoveredIndex = idx;

            if (item?.Tag is ushort itemId)
            {
                string tip = BuildPaletteTooltip(itemId);
                if (!string.IsNullOrEmpty(tip))
                    paletteToolTip?.SetToolTip(paletteListView, tip);
                else
                    paletteToolTip?.Hide(paletteListView);
            }
            else
            {
                paletteToolTip?.Hide(paletteListView);
            }
        }

        // ================================================================
        //  Info panel
        // ================================================================

        private void BuildPaletteInfoPanel()
        {
            paletteInfoPreview = new PictureBox
            {
                Size = new Size(80, 80),
                Location = new Point(5, 5),
                BackColor = Color.FromArgb(30, 35, 40),
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle
            };
            paletteInfoPanel.Controls.Add(paletteInfoPreview);

            paletteInfoNameLabel = new Label
            {
                Location = new Point(90, 5),
                Size = new Size(180, 20),
                Font = new Font("Consolas", 10, FontStyle.Bold),
                ForeColor = HolographicTheme.SelectionCyan,
                Text = "Select an item..."
            };
            HolographicTheme.ApplyToLabel(paletteInfoNameLabel);
            paletteInfoPanel.Controls.Add(paletteInfoNameLabel);

            paletteInfoIdLabel = new Label
            {
                Location = new Point(90, 25),
                Size = new Size(180, 18),
                Font = new Font("Consolas", 9),
                ForeColor = HolographicTheme.TextSecondary,
                Text = ""
            };
            HolographicTheme.ApplyToLabel(paletteInfoIdLabel);
            paletteInfoPanel.Controls.Add(paletteInfoIdLabel);

            paletteInfoPropertiesLabel = new Label
            {
                Location = new Point(5, 90),
                Size = new Size(265, 65),
                Font = new Font("Consolas", 8),
                ForeColor = HolographicTheme.TextPrimary,
                Text = ""
            };
            HolographicTheme.ApplyToLabel(paletteInfoPropertiesLabel);
            paletteInfoPanel.Controls.Add(paletteInfoPropertiesLabel);

            paletteInfoFlagsLabel = new Label
            {
                Location = new Point(5, 155),
                Size = new Size(265, 40),
                Font = new Font("Consolas", 7.5f),
                ForeColor = Color.FromArgb(255, 200, 100),
                Text = ""
            };
            HolographicTheme.ApplyToLabel(paletteInfoFlagsLabel);
            paletteInfoPanel.Controls.Add(paletteInfoFlagsLabel);
        }

        private void UpdatePaletteInfo(ushort itemId)
        {
            if (paletteInfoPanel == null || paletteInfoNameLabel == null) return;

            // Preview image
            try
            {
                var img = LoadStaticImage(itemId);
                paletteInfoPreview.Image = img;
            }
            catch
            {
                paletteInfoPreview.Image = null;
            }

            if (tileDataReader == null || !tileDataReader.IsLoaded)
            {
                paletteInfoNameLabel.Text = "TileData not loaded";
                paletteInfoIdLabel.Text = $"ID: 0x{itemId:X4} ({itemId})";
                paletteInfoPropertiesLabel.Text = "";
                paletteInfoFlagsLabel.Text = "";
                return;
            }

            var itemData = tileDataReader.GetItemTile(itemId);
            if (itemData == null)
            {
                paletteInfoNameLabel.Text = "Unknown Item";
                paletteInfoIdLabel.Text = $"ID: 0x{itemId:X4} ({itemId})";
                paletteInfoPropertiesLabel.Text = "No TileData available";
                paletteInfoFlagsLabel.Text = "";
                return;
            }

            string name = !string.IsNullOrWhiteSpace(itemData.Name) ? itemData.Name : "Unnamed";
            paletteInfoNameLabel.Text = name;

            var img2 = paletteInfoPreview.Image;
            string sizeInfo = img2 != null ? $" | {img2.Width}x{img2.Height}px" : "";
            paletteInfoIdLabel.Text = $"0x{itemId:X4} ({itemId}){sizeInfo}";

            var sb = new StringBuilder();
            sb.AppendLine($"Height: {itemData.Height}  Weight: {itemData.Weight}");
            sb.AppendLine($"Animation: 0x{itemData.Animation:X4}  Quality: {itemData.Quality}");
            sb.AppendLine($"Quantity: {itemData.Quantity}  StackOff: {itemData.StackingOffset}");
            sb.Append($"Hue: {itemData.Hue}");
            paletteInfoPropertiesLabel.Text = sb.ToString();

            string flagsStr = itemData.GetFlagsString();
            if (flagsStr != "None")
            {
                var flags = flagsStr.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                var formatted = string.Join(", ", flags.Take(8));
                if (flags.Length > 8) formatted += $" +{flags.Length - 8} more";
                paletteInfoFlagsLabel.Text = $"Flags: {formatted}";
            }
            else
            {
                paletteInfoFlagsLabel.Text = "Flags: None";
            }
        }

        private string BuildPaletteTooltip(ushort itemId)
        {
            if (tileDataReader == null || !tileDataReader.IsLoaded)
                return $"0x{itemId:X4}";

            var data = tileDataReader.GetItemTile(itemId);
            if (data == null) return $"0x{itemId:X4}";

            var sb = new StringBuilder();
            sb.AppendLine($"{data.Name ?? "Unnamed"} (0x{itemId:X4})");
            sb.AppendLine($"Height: {data.Height}  Weight: {data.Weight}");
            string flags = data.GetFlagsString();
            if (flags != "None") sb.Append($"Flags: {flags}");
            return sb.ToString().TrimEnd();
        }

        // ================================================================
        //  Place / add static to map
        // ================================================================

        /// <summary>
        /// Place a new static item at the current camera position.
        /// </summary>
        private void PlaceStaticAtCamera(ushort itemId)
        {
            if (currentMap == null) return;

            int x = cameraX;
            int y = cameraY;
            sbyte z = GetZAt(x, y);

            AddStaticToMap(itemId, x, y, z, 0);
        }

        /// <summary>
        /// Place multiple statics spaced out in a grid pattern around a center point.
        /// </summary>
        private void PlaceMultipleStaticsSpaced(List<ushort> itemIds, int centerX, int centerY)
        {
            if (currentMap == null || itemIds.Count == 0) return;

            int cols = (int)Math.Ceiling(Math.Sqrt(itemIds.Count));
            int startX = centerX - cols / 2;
            int startY = centerY - cols / 2;

            for (int i = 0; i < itemIds.Count; i++)
            {
                int x = startX + (i % cols);
                int y = startY + (i / cols);
                x = Math.Max(0, Math.Min(currentMap.Width - 1, x));
                y = Math.Max(0, Math.Min(currentMap.Height - 1, y));
                sbyte z = GetZAt(x, y);
                AddStaticToMap(itemIds[i], x, y, z, 0);
            }
        }

        /// <summary>
        /// Add a static to the map at the specified world coordinates.
        /// </summary>
        internal void AddStaticToMap(ushort itemId, int worldX, int worldY, sbyte z, ushort hue)
        {
            if (currentMap == null) return;

            var key = (worldX, worldY);

            // Snapshot state before the change for undo
            bool hadBefore = staticOverrides.ContainsKey(key);
            List<StaticTile> oldList = hadBefore
                ? MapAction.CloneStaticList(staticOverrides[key])
                : null;

            if (!hadBefore)
            {
                // Copy existing statics from loaded data (if any)
                var existing = new List<StaticTile>();
                if (currentStatics != null && currentStatics.StaticsByPosition.ContainsKey(key))
                {
                    foreach (var s in currentStatics.StaticsByPosition[key])
                        existing.Add(new StaticTile
                        {
                            ItemId = s.ItemId, X = s.X, Y = s.Y,
                            Z = s.Z, Hue = s.Hue,
                            WorldX = s.WorldX, WorldY = s.WorldY
                        });
                }
                staticOverrides[key] = existing;
            }

            var tile = new StaticTile
            {
                ItemId = itemId,
                X = (byte)(worldX % 8),
                Y = (byte)(worldY % 8),
                Z = z,
                Hue = hue,
                WorldX = worldX,
                WorldY = worldY
            };
            staticOverrides[key].Add(tile);

            // Record undo action using the proper StaticChanges dictionary
            var action = new MapAction { Description = $"Place static 0x{itemId:X4} at ({worldX},{worldY})" };
            action.StaticChanges[key] = new StaticOverrideChange
            {
                HadOverrideBefore = hadBefore,
                OldOverride = oldList,
                NewOverride = MapAction.CloneStaticList(staticOverrides[key])
            };
            undoRedoManager.RecordAction(action);

            hasUnsavedChanges = true;
            cachedStaticsData = null; // invalidate statics cache
            GenerateMapImage();
            UpdateSaveButtonState();
            UpdateUndoRedoButtons();

            statusLabel.Text = $"Placed 0x{itemId:X4} at ({worldX},{worldY}) Z={z}";
        }

        /// <summary>
        /// Show context menu for a static palette item (right-click)
        /// </summary>
        private void ShowStaticPaletteContextMenu(ushort itemId, Point location)
        {
            var menu = new ContextMenuStrip();
            menu.BackColor = HolographicTheme.PanelBackground;
            menu.ForeColor = HolographicTheme.TextPrimary;

            var headerItem = new ToolStripLabel($"Static 0x{itemId:X4}");
            headerItem.ForeColor = HolographicTheme.CyanAccent;
            headerItem.Font = new Font(headerItem.Font, FontStyle.Bold);
            menu.Items.Add(headerItem);
            menu.Items.Add(new ToolStripSeparator());

            // Push single item to JarJar (grayed while the feature is off)
            var pushItem = new ToolStripMenuItem("Push to MeesaJarJar");
            pushItem.ForeColor = Color.FromArgb(180, 100, 255);
            pushItem.Tag = itemId;
            pushItem.Click += PalettePushToJarJar_Click;
            pushItem.Enabled = AppConfig.Instance.JarJarPushEnabled && pendingArtChanges.ContainsKey(itemId);
            if (!pushItem.Enabled)
                pushItem.Text += AppConfig.Instance.JarJarPushEnabled ? " (no pending changes)" : " (disabled)";
            menu.Items.Add(pushItem);

            var pullItem = new ToolStripMenuItem("Pull from MeesaJarJar");
            pullItem.ForeColor = Color.FromArgb(100, 180, 255);
            pullItem.Tag = itemId;
            pullItem.Click += PalettePullFromJarJar_Click;
            menu.Items.Add(pullItem);

            menu.Show(paletteListView, location);
        }

        /// <summary>
        /// Push a single item from the map static palette to JarJar
        /// </summary>
        private async void PalettePushToJarJar_Click(object sender, EventArgs e)
        {
            var menuItem = sender as ToolStripMenuItem;
            if (menuItem?.Tag == null) return;

            ushort itemId = (ushort)menuItem.Tag;
            if (!pendingArtChanges.ContainsKey(itemId)) return;

            var config = AppConfig.Instance;
            JarJarClient client = null;

            try
            {
                client = new JarJarClient(config.JarJarApiUrl, config.JarJarAuthToken);
                statusLabel.Text = $"Pushing 0x{itemId:X4} to JarJar...";
                Application.DoEvents();

                var result = await client.UploadSpriteAsync(itemId, pendingArtChanges[itemId]);

                if (result.Success)
                {
                    statusLabel.Text = $"Pushed 0x{itemId:X4} — clients update in ~30s";
                    System.Diagnostics.Debug.WriteLine($"Pushed 0x{itemId:X4} to JarJar");
                }
                else
                {
                    statusLabel.Text = $"Push failed: {result.Message}";
                    MessageBox.Show($"Failed to push 0x{itemId:X4}:\n\n{result.Message}",
                        "Push Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text = $"Error: {ex.Message}";
            }
            finally
            {
                client?.Dispose();
            }
        }

        /// <summary>
        /// Pull a sprite from JarJar for preview
        /// </summary>
        private async void PalettePullFromJarJar_Click(object sender, EventArgs e)
        {
            var menuItem = sender as ToolStripMenuItem;
            if (menuItem?.Tag == null) return;

            ushort itemId = (ushort)menuItem.Tag;
            var config = AppConfig.Instance;
            JarJarClient client = null;

            try
            {
                client = new JarJarClient(config.JarJarApiUrl, config.JarJarAuthToken);
                statusLabel.Text = $"Pulling 0x{itemId:X4} from JarJar...";
                Application.DoEvents();

                var entry = await client.GetSpriteInfoAsync(itemId);
                if (!entry.HasValue)
                {
                    statusLabel.Text = "Not found on server";
                    MessageBox.Show($"Sprite 0x{itemId:X4} not found in the JarJar lookup table.",
                        "Pull from JarJar", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var serverBitmap = await client.DownloadSpriteAsync(itemId);
                if (serverBitmap == null)
                {
                    statusLabel.Text = "Download failed";
                    return;
                }

                var info = entry.Value;
                statusLabel.Text = $"Pulled 0x{itemId:X4} ({info.Width}x{info.Height})";

                // Show in a quick preview form
                var form = new Form
                {
                    Text = $"JarJar — 0x{itemId:X4} (Atlas {info.AtlasIndex})",
                    Width = 300, Height = 300,
                    StartPosition = FormStartPosition.CenterParent,
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    MaximizeBox = false
                };
                HolographicTheme.ApplyToForm(form);

                var pic = new PictureBox
                {
                    Dock = DockStyle.Fill,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Image = serverBitmap,
                    BackColor = HolographicTheme.InputBackground
                };
                form.Controls.Add(pic);

                var lbl = new Label
                {
                    Dock = DockStyle.Bottom, Height = 25,
                    Text = $"Server: {info.Width}x{info.Height} — Atlas {info.AtlasIndex} @ ({info.X},{info.Y})",
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = Color.FromArgb(180, 100, 255)
                };
                form.Controls.Add(lbl);

                form.ShowDialog(this);
            }
            catch (Exception ex)
            {
                statusLabel.Text = $"Error: {ex.Message}";
            }
            finally
            {
                client?.Dispose();
            }
        }
    }
}
