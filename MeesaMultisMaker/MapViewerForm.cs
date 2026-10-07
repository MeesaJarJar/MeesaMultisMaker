using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Collections.Generic;
using MeesaMultisMaker.Mul;
using MeesaMultisMaker.AI;

namespace MeesaMultisMaker
{
    public partial class MapViewerForm : Form
    {
        // UI Controls - defined in Designer.cs
        internal ComboBox facetComboBox;
        internal PictureBox mapPictureBox;
        internal Button loadButton;
        internal Button newMapButton;
        internal Button zoomInButton;
        internal Button zoomOutButton;
        internal Label statusLabel;
        internal TrackBar zoomTrackBar;
        internal Label zoomLabel;
        internal TextBox coordXTextBox;
        internal TextBox coordYTextBox;
        internal RadioButton replaceRadioButton;
        internal RadioButton contextRadioButton;
        internal CheckBox showStaticsCheckBox;
        internal RadioButton selectLandRadioButton;
        internal RadioButton selectStaticsRadioButton;
        
        // Z Filter Controls
        internal TrackBar zMinTrackBar;
        internal TrackBar zMaxTrackBar;
        internal Label zMinLabel;
        internal Label zMaxLabel;
        internal int staticZMin = -128;
        internal int staticZMax = 127;
        // Note: Z_SCALE is defined in MapViewerForm.Rendering.cs

        // ComfyUI Settings Controls
        internal Panel comfySettingsPanel;
        internal TextBox comfyUrlTextBox;
        internal Button testConnectionButton;
        internal TextBox promptTextBox;
        internal TextBox negativePromptTextBox;
        internal NumericUpDown stepsNumeric;
        internal NumericUpDown cfgNumeric;
        internal NumericUpDown denoiseNumeric;
        internal TextBox seedTextBox;
        internal CheckBox autoRandomSeedCheckBox;
        internal ComboBox samplerComboBox;
        internal ComboBox schedulerComboBox;
        internal ComboBox resolutionComboBox;
        internal CheckBox customResolutionCheckBox;
        internal NumericUpDown resolutionWidthNumeric;
        internal NumericUpDown resolutionHeightNumeric;
        internal CheckBox overlayUseSparseDeltaCheckBox;
        internal NumericUpDown overlayFocusNumeric;
        internal NumericUpDown overlayFeatherNumeric;
        internal NumericUpDown overlayDiffThresholdNumeric;
        internal NumericUpDown overlayAlphaScaleNumeric;
        internal ComboBox backendComboBox;

        // Map data
        internal string mulFolderPath;
        internal string mapSourceFolder; // Where map/statics are loaded from (may differ from mulFolderPath after loading a save)
        internal MapData currentMap;
        internal Bitmap currentMapImage = null;

        // Reusable render buffer — avoids large GDI+ bitmap allocation every frame
        private Bitmap renderBuffer;

        // Statics data
        internal StaticsData currentStatics;
        internal bool showStatics = true;
        internal readonly Dictionary<int, Image> staticArtCache = new Dictionary<int, Image>();
        // Note: selectedStatics is defined in MapViewerForm.TileSelection.cs

        // Cached statics area — avoids reloading from disk when panning within the same region
        private int cachedStaticsStartX = -1, cachedStaticsStartY = -1;
        private int cachedStaticsEndX = -1, cachedStaticsEndY = -1;
        private StaticsData cachedStaticsData;
        private const int STATICS_CACHE_MARGIN = 64; // extra tiles loaded around the view

        // In-memory overrides for biome/brush edits.
        // Positions in this dictionary replace whatever is loaded from disk.
        // An empty list means "statics were explicitly cleared here".
        internal readonly Dictionary<(int x, int y), List<StaticTile>> staticOverrides
            = new Dictionary<(int x, int y), List<StaticTile>>();
        // Land tile overrides: positions in this dictionary replace disk data.
        internal readonly Dictionary<(int x, int y), LandTile> landTileOverrides
            = new Dictionary<(int x, int y), LandTile>();

        // Dirty tracking — true when the user has made changes that haven't been saved
        internal bool hasUnsavedChanges = false;

        // Save manager for iteration-based map saving
        internal MapSaveManager mapSaveManager = new MapSaveManager();

        // Facet-switch tracking: prevents double-prompt and allows cancel/revert
        internal int lastLoadedFacetIndex = 0;
        internal bool suppressFacetChange = false;

        // Undo/redo system
        internal UndoRedoManager undoRedoManager = new UndoRedoManager(200);
        private Button undoButton;
        private Button redoButton;

        // Clipboard for copy/paste
        internal MapClipboardData clipboard = null;

        // View settings
        internal float zoomFactor = 44f;  // Default zoom (44 pixels per tile)
        internal int cameraX = 1420;
        internal int cameraY = 1685;

        // Art cache
        internal readonly Dictionary<int, Image> artCache = new Dictionary<int, Image>();
        internal readonly Dictionary<string, string> idToPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        internal string artFolderPath;
        internal ArtFileFormat? cachedArtFormat; // cached to avoid File.Exists per tile

        // Texture cache (texmaps.mul textures for deformed terrain)
        internal readonly Dictionary<ushort, Image> texMapCache = new Dictionary<ushort, Image>();

        // Hued static image cache — keyed by (itemId, hue, partialHue)
        internal readonly Dictionary<(ushort itemId, ushort hue, bool partial), Bitmap> huedStaticCache
            = new Dictionary<(ushort, ushort, bool), Bitmap>();

        // TileData for item properties
        internal TileDataReader tileDataReader = new TileDataReader();

        // Hue color palettes
        internal HuesReader huesReader = new HuesReader();

        // Minimap cache for zoomed-out overview
        internal MinimapGenerator minimapGenerator = new MinimapGenerator();
        internal bool minimapGenerating = false;
        
        // Mouse state
        internal bool isPanning = false;
        internal Point panStartPoint;
        internal int panStartX;
        internal int panStartY;

        // Lasso selection state
        internal bool isLassoSelecting = false;
        internal List<Point> lassoPoints = new List<Point>();

        // ComfyUI Settings properties
        internal string ComfyUIUrl => comfyUrlTextBox?.Text ?? "http://127.0.0.1:8188";
        internal string Prompt => promptTextBox?.Text ?? "";
        internal string NegativePrompt => negativePromptTextBox?.Text ?? "";
        internal int Steps => stepsNumeric != null ? (int)stepsNumeric.Value : 20;
        internal double CFG => cfgNumeric != null ? (double)cfgNumeric.Value : 7.0;
        internal double Denoise => denoiseNumeric != null ? (double)denoiseNumeric.Value : 0.6;
        internal long? Seed => (autoRandomSeedCheckBox?.Checked ?? true) ? null : 
            long.TryParse(seedTextBox?.Text, out long seed) ? (long?)seed : null;
        internal string Sampler => samplerComboBox?.SelectedItem?.ToString() ?? "euler";
        internal string Scheduler => schedulerComboBox?.SelectedItem?.ToString() ?? "normal";
        internal string Checkpoint => checkpointComboBox?.SelectedItem?.ToString() ?? MeesaMultisMaker.ComfyUI.Text2ImageWorkflow.DEFAULT_CHECKPOINT;
        internal bool DropBlackPixels => dropBlackPixelsCheckBox?.Checked ?? true;
        internal int BlackThreshold => blackThresholdNumeric != null ? (int)blackThresholdNumeric.Value : 15;

        // ControlNet properties (read from AppConfig since MapViewer settings panel doesn't have CN controls)
        internal bool UseControlNet => useControlNetCheckBox?.Checked ?? AppConfig.Instance.UseControlNet;

        internal int ResolutionWidth
        {
            get
            {
                if (customResolutionCheckBox?.Checked == true && resolutionWidthNumeric != null)
                    return (int)resolutionWidthNumeric.Value;

                ParseResolutionSelection(out int width, out _);
                return width;
            }
        }

        internal int ResolutionHeight
        {
            get
            {
                if (customResolutionCheckBox?.Checked == true && resolutionHeightNumeric != null)
                    return (int)resolutionHeightNumeric.Value;

                ParseResolutionSelection(out _, out int height);
                return height;
            }
        }

        // Back-compat for existing code paths that still expect a single value.
        internal int Resolution => ResolutionWidth;

        private void ParseResolutionSelection(out int width, out int height)
        {
            width = 512;
            height = 512;

            string raw = null;
            if (resolutionComboBox != null)
            {
                raw = resolutionComboBox.Text;
                if (string.IsNullOrWhiteSpace(raw) && resolutionComboBox.SelectedItem != null)
                    raw = resolutionComboBox.SelectedItem.ToString();
            }

            if (string.IsNullOrWhiteSpace(raw))
                return;

            raw = raw.Trim().ToLowerInvariant().Replace(" ", string.Empty);
            raw = raw.Replace('*', 'x');

            string[] parts = raw.Split('x');
            if (parts.Length == 2)
            {
                if (int.TryParse(parts[0], out int parsedW) && int.TryParse(parts[1], out int parsedH))
                {
                    width = Math.Max(64, Math.Min(4096, parsedW));
                    height = Math.Max(64, Math.Min(4096, parsedH));
                    return;
                }
            }

            if (int.TryParse(raw, out int single))
            {
                single = Math.Max(64, Math.Min(4096, single));
                width = single;
                height = single;
            }
        }
        
        /// <summary>
        /// Currently selected AI backend
        /// </summary>
        internal AIBackend SelectedBackend
        {
            get
            {
                if (backendComboBox != null && backendComboBox.SelectedIndex >= 0)
                    return (AIBackend)backendComboBox.SelectedIndex;
                return AppConfig.Instance.SelectedAIBackend;
            }
        }

        public MapViewerForm()
        {
            InitializeComponent();
            this.Load += MapViewerForm_Load;
            this.KeyPreview = true;
            this.KeyDown += MapViewerForm_KeyDown;
        }

        private void MapViewerForm_Load(object sender, EventArgs e)
        {
            // Initialize ComfyUI settings
            InitializeComfyUI();

            // Initialize biome brush system
            InitializeBiomeSystem();

            // Load configuration
            var config = AppConfig.Instance;

            // Try to get MUL folder from config, or auto-detect
            mulFolderPath = config.FindMulFolder();
            
            if (!string.IsNullOrEmpty(mulFolderPath))
            {
                // Try to get art folder from config, or auto-detect
                artFolderPath = config.FindArtFolder();
                if (string.IsNullOrEmpty(artFolderPath))
                {
                    artFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UOFItems");
                }
                LoadArtFiles();

                // Scan for custom maps (map6.mul, map7.mul, etc.) so they
                // appear in the facet dropdown on startup.
                PopulateCustomMaps(mulFolderPath);

                mapSourceFolder = mulFolderPath;
                LoadMap(0);
                statusLabel.Text = $"Ready | Using: {mulFolderPath}";

                // Populate saved iterations list
                RefreshSavesList();
            }
            else
            {
                statusLabel.Text = "UO folder not found. Click 'Load Map' to select.";
            }
            
            // Apply default ComfyUI settings from config
            if (comfyUrlTextBox != null)
                comfyUrlTextBox.Text = config.ComfyUIUrl;
            if (promptTextBox != null)
                promptTextBox.Text = config.DefaultPrompt;
            if (negativePromptTextBox != null)
                negativePromptTextBox.Text = config.DefaultNegativePrompt;
            if (stepsNumeric != null)
                stepsNumeric.Value = config.DefaultSteps;
            if (cfgNumeric != null)
                cfgNumeric.Value = (decimal)config.DefaultCFG;
            if (denoiseNumeric != null)
                denoiseNumeric.Value = (decimal)config.DefaultDenoise;

            if (autoRandomSeedCheckBox != null)
                autoRandomSeedCheckBox.Checked = false;
            if (seedTextBox != null)
            {
                seedTextBox.Text = "1";
                seedTextBox.Enabled = true;
            }

            if (useControlNetCheckBox != null)
                useControlNetCheckBox.Checked = true;
            
            // Set sampler
            if (samplerComboBox != null)
            {
                int samplerIndex = samplerComboBox.Items.IndexOf(config.DefaultSampler);
                if (samplerIndex >= 0) samplerComboBox.SelectedIndex = samplerIndex;
            }
            
            // Set scheduler
            if (schedulerComboBox != null)
            {
                int schedulerIndex = schedulerComboBox.Items.IndexOf(config.DefaultScheduler);
                if (schedulerIndex >= 0) schedulerComboBox.SelectedIndex = schedulerIndex;
            }
            
            // Set resolution
            if (resolutionComboBox != null)
            {
                string resString = $"{config.DefaultResolution}x{config.DefaultResolution}";
                int resIndex = -1;
                for (int i = 0; i < resolutionComboBox.Items.Count; i++)
                {
                    if (resolutionComboBox.Items[i].ToString().StartsWith(config.DefaultResolution.ToString()))
                    {
                        resIndex = i;
                        break;
                    }
                }
                if (resIndex >= 0) resolutionComboBox.SelectedIndex = resIndex;
            }

            if (resolutionWidthNumeric != null)
                resolutionWidthNumeric.Value = Math.Max(resolutionWidthNumeric.Minimum, Math.Min(resolutionWidthNumeric.Maximum, config.DefaultResolution));
            if (resolutionHeightNumeric != null)
                resolutionHeightNumeric.Value = Math.Max(resolutionHeightNumeric.Minimum, Math.Min(resolutionHeightNumeric.Maximum, config.DefaultResolution));

            if (backendComboBox != null)
            {
                int backendIndex = (int)config.SelectedAIBackend;
                if (backendIndex >= 0 && backendIndex < backendComboBox.Items.Count)
                    backendComboBox.SelectedIndex = backendIndex;
            }

            if (checkpointComboBox != null && checkpointComboBox.Items.Count > 0)
            {
                int dreamIdx = -1;
                for (int i = 0; i < checkpointComboBox.Items.Count; i++)
                {
                    string cp = checkpointComboBox.Items[i]?.ToString() ?? string.Empty;
                    if (cp.IndexOf("dreamshaper", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        dreamIdx = i;
                        break;
                    }
                }
                if (dreamIdx >= 0)
                    checkpointComboBox.SelectedIndex = dreamIdx;
            }

            ApplyBackendUIState();
        }

        private void BackendComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (backendComboBox == null || backendComboBox.SelectedIndex < 0)
                return;

            AppConfig.Instance.SelectedAIBackend = (AIBackend)backendComboBox.SelectedIndex;
            AppConfig.Instance.Save();

            ApplyBackendUIState();
        }

        private void ApplyBackendUIState()
        {
            bool isComfy = SelectedBackend == AIBackend.ComfyUI;

            if (comfyUrlTextBox != null)
                comfyUrlTextBox.Visible = isComfy;
            if (testConnectionButton != null)
                testConnectionButton.Visible = isComfy;

            if (comfySettingsPanel != null)
            {
                foreach (Control ctrl in comfySettingsPanel.Controls)
                {
                    var lbl = ctrl as Label;
                    if (lbl != null && lbl.Text == "ComfyUI URL:")
                    {
                        lbl.Visible = isComfy;
                    }
                }
            }
        }

        private bool IsMapViewerTextInputFocused()
        {
            var focused = GetMapViewerFocusedControl(this);
            if (focused == null) return false;
            if (focused is TextBox || focused is ComboBox || focused is NumericUpDown || focused is RichTextBox)
                return true;
            return false;
        }

        private Control GetMapViewerFocusedControl(Control parent)
        {
            if (parent == null) return null;
            var container = parent as IContainerControl;
            if (container != null)
            {
                var active = container.ActiveControl;
                if (active != null)
                    return GetMapViewerFocusedControl(active);
            }
            return parent.Focused ? parent : null;
        }

        private void MapViewerForm_KeyDown(object sender, KeyEventArgs e)
        {
            // Don't hijack typing in text inputs (Ctrl+C/V/Z, single keys); Escape still works.
            if (e.KeyCode != Keys.Escape && IsMapViewerTextInputFocused())
                return;
            // Alt + arrows/[ ]/, . : move/scale/rotate last painter-sent plane
            if (e.Alt)
            {
                bool handledPlane = true;
                switch (e.KeyCode)
                {
                    case Keys.Left: AdjustLastPainterPlane(-1f, 0f, 1f, 0f); break;
                    case Keys.Right: AdjustLastPainterPlane(1f, 0f, 1f, 0f); break;
                    case Keys.Up: AdjustLastPainterPlane(0f, -1f, 1f, 0f); break;
                    case Keys.Down: AdjustLastPainterPlane(0f, 1f, 1f, 0f); break;
                    case Keys.OemOpenBrackets: AdjustLastPainterPlane(0f, 0f, 0.95f, 0f); break;
                    case Keys.Oem6: AdjustLastPainterPlane(0f, 0f, 1.05f, 0f); break;
                    case Keys.Oemcomma: AdjustLastPainterPlane(0f, 0f, 1f, -5f); break;
                    case Keys.OemPeriod: AdjustLastPainterPlane(0f, 0f, 1f, 5f); break;
                    default: handledPlane = false; break;
                }

                if (handledPlane)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                }
            }

            // ESC key cancels lasso selection
            if (e.KeyCode == Keys.Escape && isLassoSelecting)
            {
                CancelLassoSelection();
                e.Handled = true;
                return;
            }

            // Copy / Paste
            if (e.Control && e.KeyCode == Keys.C)
            {
                CopySelection();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            if (e.Control && e.KeyCode == Keys.V)
            {
                PasteAtCamera();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            // Undo / Redo
            if (e.Control && e.KeyCode == Keys.Z)
            {
                PerformUndo();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            if (e.Control && e.KeyCode == Keys.Y)
            {
                PerformRedo();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            // Z height adjustment (+/-) works for both land tiles and statics
            if (e.KeyCode == Keys.Add || e.KeyCode == Keys.Oemplus ||
                e.KeyCode == Keys.Subtract || e.KeyCode == Keys.OemMinus)
            {
                int dz = (e.KeyCode == Keys.Add || e.KeyCode == Keys.Oemplus) ? 1 : -1;

                if (selectedStatics.Count > 0)
                {
                    MoveSelectedStatics(0, 0, dz);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                }

                if (selectedTiles.Count > 0)
                {
                    AdjustSelectedLandZ(dz);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                }
            }

            // Numpad directional movement for statics only
            if (selectedStatics.Count == 0)
                return;

            int deltaX = 0, deltaY = 0;

            switch (e.KeyCode)
            {
                case Keys.NumPad8: deltaX = -1; deltaY = -1; break;
                case Keys.NumPad2: deltaX = 1; deltaY = 1; break;
                case Keys.NumPad4: deltaX = -1; deltaY = 1; break;
                case Keys.NumPad6: deltaX = 1; deltaY = -1; break;
                case Keys.NumPad7: deltaX = -1; break;
                case Keys.NumPad9: deltaY = -1; break;
                case Keys.NumPad1: deltaY = 1; break;
                case Keys.NumPad3: deltaX = 1; break;
                default: return;
            }

            if (deltaX != 0 || deltaY != 0)
            {
                MoveSelectedStatics(deltaX, deltaY, 0);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void MoveSelectedStatics(int deltaX, int deltaY, int deltaZ)
        {
            var action = new MapAction { Description = $"Move {selectedStatics.Count} static(s)" };

            // Phase 1: snapshot old override state for every affected position
            foreach (var s in selectedStatics)
            {
                CaptureOverrideState(action, (s.X, s.Y));
                CaptureOverrideState(action, (s.X + deltaX, s.Y + deltaY));
            }

            // Phase 2: perform the move
            foreach (var s in selectedStatics)
            {
                int oldX = s.X;
                int oldY = s.Y;
                sbyte oldZ = s.Z;

                s.X += deltaX;
                s.Y += deltaY;
                s.Z = (sbyte)Math.Max(-128, Math.Min(127, s.Z + deltaZ));

                CommitStaticMoveToOverrides(oldX, oldY, oldZ, s);
            }

            // Phase 3: snapshot new override state and record
            FinalizeOverrideState(action);
            undoRedoManager.RecordAction(action);

            hasUnsavedChanges = true;
            InvalidateStaticsCache();
            GenerateMapImage();
            UpdateSelectionStatus();
            UpdateSaveButtonState();
            UpdateUndoRedoButtons();

            string moveInfo = "";
            if (deltaX != 0 || deltaY != 0) moveInfo += $"Moved X:{deltaX:+#;-#;0} Y:{deltaY:+#;-#;0} ";
            if (deltaZ != 0) moveInfo += $"Z:{deltaZ:+#;-#;0}";

            if (statusLabel != null)
                statusLabel.Text = $"Target: STATICS | {selectedStatics.Count} selected | {moveInfo} (unsaved)";
        }

        /// <summary>
        /// Adjust the Z height of every currently-selected land tile by the
        /// given delta.  Records a single undo-able action.
        /// </summary>
        private void AdjustSelectedLandZ(int deltaZ)
        {
            if (selectedTiles.Count == 0 || currentMap == null) return;

            var action = new MapAction { Description = $"Adjust Z of {selectedTiles.Count} land tile(s) by {deltaZ:+#;-#;0}" };

            foreach (var t in selectedTiles)
            {
                if (t.X < 0 || t.X >= currentMap.Width || t.Y < 0 || t.Y >= currentMap.Height)
                    continue;

                var tile = currentMap.Tiles[t.X, t.Y];
                if (tile == null) continue;

                var lk = (t.X, t.Y);
                if (!action.LandChanges.ContainsKey(lk))
                {
                    sbyte newZ = (sbyte)Math.Max(-128, Math.Min(127, tile.Z + deltaZ));
                    action.LandChanges[lk] = new LandTileChange
                    {
                        OldTileId = tile.TileId,
                        OldZ = tile.Z,
                        NewTileId = tile.TileId,
                        NewZ = newZ
                    };

                    tile.Z = newZ;
                    t.Z = newZ;   // keep the selection object in sync
                }
            }

            undoRedoManager.RecordAction(action);

            hasUnsavedChanges = true;
            GenerateMapImage();
            UpdateSelectionStatus();
            UpdateSaveButtonState();
            UpdateUndoRedoButtons();

            if (statusLabel != null)
                statusLabel.Text = $"Target: LAND | {selectedTiles.Count} selected | Z {deltaZ:+#;-#;0} (unsaved)";
        }

        /// <summary>
        /// Remove a static from its old position and add it at its new position
        /// inside the <see cref="staticOverrides"/> dictionary so the change
        /// is reflected in future renders and can be saved to disk.
        /// </summary>
        private void CommitStaticMoveToOverrides(int oldX, int oldY, sbyte oldZ, SelectedStatic s)
        {
            // --- Remove from old position ---
            var oldKey = (oldX, oldY);
            List<StaticTile> oldList;

            if (staticOverrides.TryGetValue(oldKey, out oldList))
            {
                oldList = new List<StaticTile>(oldList);
            }
            else
            {
                var disk = currentStatics?.GetStaticsAt(oldX, oldY) ?? new List<StaticTile>();
                oldList = new List<StaticTile>(disk);
            }

            // Remove the matching entry (first match only)
            for (int i = 0; i < oldList.Count; i++)
            {
                if (oldList[i].ItemId == s.ItemId && oldList[i].Z == oldZ && oldList[i].Hue == s.Hue)
                {
                    oldList.RemoveAt(i);
                    break;
                }
            }
            staticOverrides[oldKey] = oldList;

            // --- Add at new position ---
            var newKey = (s.X, s.Y);
            List<StaticTile> newList;

            if (staticOverrides.TryGetValue(newKey, out newList))
            {
                newList = new List<StaticTile>(newList);
            }
            else
            {
                var disk = currentStatics?.GetStaticsAt(s.X, s.Y) ?? new List<StaticTile>();
                newList = new List<StaticTile>(disk);
            }

            newList.Add(new StaticTile
            {
                ItemId = s.ItemId,
                X = (byte)(s.X % 8),
                Y = (byte)(s.Y % 8),
                Z = s.Z,
                Hue = s.Hue,
                WorldX = s.X,
                WorldY = s.Y
            });
            staticOverrides[newKey] = newList;
        }

        /// <summary>
        /// Mark the current map state as having unsaved changes.
        /// Called by biome apply, static moves, etc.
        /// </summary>
        internal void MarkMapDirty()
        {
            hasUnsavedChanges = true;
            UpdateSaveButtonState();
        }

        // ================================================================
        //  Undo / Redo
        // ================================================================

        internal void PerformUndo()
        {
            var action = undoRedoManager.Undo();
            if (action == null) return;

            // Restore old land tiles
            foreach (var kvp in action.LandChanges)
            {
                if (kvp.Key.x < 0 || kvp.Key.x >= currentMap.Width ||
                    kvp.Key.y < 0 || kvp.Key.y >= currentMap.Height) continue;
                var tile = currentMap.Tiles[kvp.Key.x, kvp.Key.y];
                if (tile != null)
                {
                    tile.TileId = kvp.Value.OldTileId;
                    tile.Z = kvp.Value.OldZ;
                }
            }

            // Restore old static overrides
            foreach (var kvp in action.StaticChanges)
            {
                if (kvp.Value.HadOverrideBefore)
                    staticOverrides[kvp.Key] = MapAction.CloneStaticList(kvp.Value.OldOverride);
                else
                    staticOverrides.Remove(kvp.Key);
            }

            // Clear selection — undo invalidates the current selection context
            selectedStatics.Clear();
            replaceTiles.Clear();
            contextTiles.Clear();

            hasUnsavedChanges = true;
            InvalidateStaticsCache();
            InvalidateMinimaps();
            GenerateMapImage();
            UpdateSaveButtonState();
            UpdateUndoRedoButtons();

            if (statusLabel != null)
                statusLabel.Text = $"Undo: {action.Description} ({undoRedoManager.UndoCount} remaining)";
        }

        internal void PerformRedo()
        {
            var action = undoRedoManager.Redo();
            if (action == null) return;

            // Re-apply new land tiles
            foreach (var kvp in action.LandChanges)
            {
                if (kvp.Key.x < 0 || kvp.Key.x >= currentMap.Width ||
                    kvp.Key.y < 0 || kvp.Key.y >= currentMap.Height) continue;
                var tile = currentMap.Tiles[kvp.Key.x, kvp.Key.y];
                if (tile != null)
                {
                    tile.TileId = kvp.Value.NewTileId;
                    tile.Z = kvp.Value.NewZ;
                }
            }

            // Re-apply new static overrides
            foreach (var kvp in action.StaticChanges)
            {
                if (kvp.Value.NewOverride != null)
                    staticOverrides[kvp.Key] = MapAction.CloneStaticList(kvp.Value.NewOverride);
                else
                    staticOverrides.Remove(kvp.Key);
            }

            selectedStatics.Clear();
            replaceTiles.Clear();
            contextTiles.Clear();

            hasUnsavedChanges = true;
            InvalidateStaticsCache();
            InvalidateMinimaps();
            GenerateMapImage();
            UpdateSaveButtonState();
            UpdateUndoRedoButtons();

            if (statusLabel != null)
                statusLabel.Text = $"Redo: {action.Description} ({undoRedoManager.RedoCount} redo remaining)";
        }

        /// <summary>
        /// Snapshot the current override state at a position BEFORE any modifications.
        /// Safe to call multiple times for the same position — only the first call records.
        /// </summary>
        internal void CaptureOverrideState(MapAction action, (int x, int y) pos)
        {
            if (action.StaticChanges.ContainsKey(pos)) return;

            List<Mul.StaticTile> existing;
            bool had = staticOverrides.TryGetValue(pos, out existing);

            action.StaticChanges[pos] = new StaticOverrideChange
            {
                HadOverrideBefore = had,
                OldOverride = had ? MapAction.CloneStaticList(existing) : null
            };
        }

        /// <summary>
        /// Fill in the NewOverride for every position already captured in the action.
        /// Call AFTER all modifications are complete.
        /// </summary>
        internal void FinalizeOverrideState(MapAction action)
        {
            foreach (var kvp in action.StaticChanges)
            {
                List<Mul.StaticTile> current;
                kvp.Value.NewOverride = staticOverrides.TryGetValue(kvp.Key, out current)
                    ? MapAction.CloneStaticList(current)
                    : null;
            }
        }

        internal void UpdateUndoRedoButtons()
        {
            if (undoButton != null)
                undoButton.Enabled = undoRedoManager.CanUndo;
            if (redoButton != null)
                redoButton.Enabled = undoRedoManager.CanRedo;
        }

        internal bool IsStaticInZRange(int z)
        {
            return z >= staticZMin && z <= staticZMax;
        }
        
        internal Image LoadStaticImage(int itemId)
        {
            if (staticArtCache.TryGetValue(itemId, out var cached))
                return cached;
            
            if (string.IsNullOrEmpty(mulFolderPath))
                return null;
            
            Bitmap img = null;
            
            // Check which format is available (cached to avoid File.Exists per tile)
            if (!cachedArtFormat.HasValue)
                cachedArtFormat = AppConfig.Instance.GetArtFileFormat(mulFolderPath);
            var format = cachedArtFormat.Value;
            
            if (format == ArtFileFormat.MulFiles)
            {
                // OSI-style: use StaticArtReader
                img = StaticArtReader.LoadStaticArt(mulFolderPath, (ushort)itemId);
            }
            else if (format == ArtFileFormat.UopOnly)
            {
                // UOForever-style: use UopArtReader
                img = UopArtReader.LoadStaticArt(mulFolderPath, (ushort)itemId);
            }
            else if (format == ArtFileFormat.TecmoExpanded)
            {
                // Tecmo Expanded Art: use TecmoArtReader
                img = TecmoArtReader.LoadStaticArt(mulFolderPath, itemId);
            }
            
            if (img != null)
            {
                staticArtCache[itemId] = img;
            }
            return img;
        }

        /// <summary>
        /// Overload for backward compatibility with ushort item IDs
        /// </summary>
        internal Image LoadStaticImage(ushort itemId)
        {
            return LoadStaticImage((int)itemId);
        }

        /// <summary>
        /// Invalidates the cached statics area so the next GenerateMapImage
        /// will reload statics from disk and re-apply overrides.
        /// Call this after modifying staticOverrides.
        /// </summary>
        internal void InvalidateStaticsCache()
        {
            cachedStaticsData = null;
            cachedStaticsStartX = -1;
            cachedStaticsStartY = -1;
            cachedStaticsEndX = -1;
            cachedStaticsEndY = -1;
        }

        /// <summary>
        /// Invalidates the zoomed-out minimap cache and refreshes the
        /// minimap overlay so both reflect the current map data.
        /// Call this after modifying land tiles or statics.
        /// </summary>
        internal void InvalidateMinimaps()
        {
            minimapGenerator.Invalidate();
            minimapGenerating = false;
            GenerateMinimapOverlay();
        }

        internal void LoadStaticsForArea(int startX, int startY, int endX, int endY)
        {
            string loadFolder = mapSourceFolder ?? mulFolderPath;
            if (string.IsNullOrEmpty(loadFolder))
                return;

            int mapIndex = facetComboBox?.SelectedIndex ?? 0;
            currentStatics = StaticsReader.LoadArea(loadFolder, mapIndex, startX, startY, endX, endY);

            System.Diagnostics.Debug.WriteLine($"MapViewerForm: Loaded statics for area ({startX},{startY}) to ({endX},{endY}): {currentStatics?.StaticsByPosition.Count ?? 0} positions");
        }

        private void PasteMapImage_Click(object sender, EventArgs e)
        {
            // Paste the working tile-clipboard at the camera (see PasteAtCamera).
            if (clipboard == null || clipboard.IsEmpty)
            {
                MessageBox.Show(this, "Nothing to paste - copy tiles first (Ctrl+C).", "Paste",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            PasteAtCamera();
        }
    }
}
