using MeesaMultisMaker.Controls;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    public partial class GumpEditorForm
    {
        private CheckBox snapToGridCheckBox;
        private bool _snapToGrid = true;

        private void InitializeComponent()
        {
            this.Text = "GUMP Editor - MeesaMultisMaker";
            this.Width = 1600;  // Wider to accommodate AI panel on right
            this.Height = 900;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = HolographicTheme.DarkBackground;
            this.KeyPreview = true;
            this.KeyDown += GumpEditorForm_KeyDown;

            tooltip = new ToolTip
            {
                AutoPopDelay = 5000,
                InitialDelay = 500,
                ReshowDelay = 200,
                ShowAlways = true
            };

            // Main split container - Vertical orientation means splitter bar is vertical (left/right split)
            mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 280,
                FixedPanel = FixedPanel.Panel1,
                BackColor = HolographicTheme.DarkBackground
            };
            HolographicTheme.ApplyToSplitContainer(mainSplit);

            // Left panel - Gump palette
            palettePanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = HolographicTheme.PanelBackground
            };

            var paletteLabel = new Label
            {
                Text = "GUMP PALETTE",
                Dock = DockStyle.Top,
                Height = 25,
                ForeColor = HolographicTheme.CyanAccent,
                BackColor = HolographicTheme.ControlBackground,
                Font = new Font("Consolas", 10, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };

            searchBox = new TextBox
            {
                Dock = DockStyle.Top,
                Height = 25,
                Text = "Search...",
                ForeColor = HolographicTheme.TextMuted
            };
            HolographicTheme.ApplyToTextBox(searchBox);
            searchBox.Enter += (s, e) =>
            {
                if (searchBox.Text == "Search..." && searchBox.ForeColor == HolographicTheme.TextMuted)
                {
                    searchBox.Text = "";
                    searchBox.ForeColor = HolographicTheme.TextPrimary;
                }
            };
            searchBox.Leave += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(searchBox.Text))
                {
                    searchBox.Text = "Search...";
                    searchBox.ForeColor = HolographicTheme.TextMuted;
                }
            };
            searchBox.TextChanged += SearchBox_TextChanged;

            // Sort controls panel
            var sortPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 26,
                BackColor = HolographicTheme.PanelBackground,
                Padding = new Padding(2, 2, 2, 2)
            };

            var sortLabel = new Label
            {
                Text = "Sort:",
                Left = 4,
                Top = 4,
                Width = 32,
                Height = 18,
                ForeColor = HolographicTheme.TextSecondary,
                Font = new Font("Consolas", 8f)
            };
            sortPanel.Controls.Add(sortLabel);

            sortComboBox = new ComboBox
            {
                Left = 36,
                Top = 2,
                Width = 105,
                Height = 22,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Consolas", 8f)
            };
            sortComboBox.Items.AddRange(new object[] { "ID", "Width", "Height" });
            sortComboBox.SelectedIndex = 0;
            HolographicTheme.ApplyToComboBox(sortComboBox);
            sortComboBox.SelectedIndexChanged += SortComboBox_SelectedIndexChanged;
            sortPanel.Controls.Add(sortComboBox);

            sortDescCheckBox = new CheckBox
            {
                Text = "Desc",
                Left = 146,
                Top = 4,
                Width = 55,
                Height = 18,
                Checked = false,
                ForeColor = HolographicTheme.TextSecondary,
                BackColor = Color.Transparent,
                Font = new Font("Consolas", 8f)
            };
            sortDescCheckBox.CheckedChanged += SortDescCheckBox_CheckedChanged;
            sortPanel.Controls.Add(sortDescCheckBox);

            // Gump list view - use OwnerDraw like the main app for instant loading
            gumpImageList = new ImageList
            {
                ImageSize = new Size(64, 64),
                ColorDepth = ColorDepth.Depth32Bit
            };

            gumpListView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.LargeIcon,
                LargeImageList = gumpImageList,
                BackColor = HolographicTheme.InputBackground,
                ForeColor = HolographicTheme.TextPrimary,
                BorderStyle = BorderStyle.None,
                VirtualMode = true,
                VirtualListSize = 0,
                OwnerDraw = true,  // Use OwnerDraw like Form1 for lazy image loading
                AllowDrop = false,  // Source doesn't need AllowDrop
                MultiSelect = true  // Enable multi-selection for drag-drop of multiple items
            };
            gumpListView.RetrieveVirtualItem += GumpListView_RetrieveVirtualItem;
            gumpListView.DrawItem += GumpListView_DrawItem;
            gumpListView.SelectedIndexChanged += GumpListView_SelectedIndexChanged;
            gumpListView.MouseDoubleClick += GumpListView_MouseDoubleClick;
            gumpListView.CacheVirtualItems += GumpListView_CacheVirtualItems;
            gumpListView.ItemDrag += GumpListView_ItemDrag;

            palettePanel.Controls.Add(gumpListView);
            palettePanel.Controls.Add(sortPanel);
            palettePanel.Controls.Add(searchBox);
            palettePanel.Controls.Add(paletteLabel);

            // Right panel content
            var rightPanel = new Panel { Dock = DockStyle.Fill, BackColor = HolographicTheme.DarkBackground };

            // Controls panel (top)
            controlsPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 90,
                BackColor = HolographicTheme.ControlBackground,
                Padding = new Padding(5)
            };
            BuildControlsPanel();

            // Right tools tabs (AI + Image FX)
            rightToolsTabs = new TabControl
            {
                Dock = DockStyle.Right,
                Width = 340
            };

            var aiTab = new TabPage("AI")
            {
                BackColor = HolographicTheme.PanelBackground
            };

            aiSettingsPanel = new AISettingsPanel
            {
                Dock = DockStyle.Fill
            };
            aiSettingsPanel.AIRegenSelectedClicked += (s, e) => RegenerateSelectedGump();
            aiSettingsPanel.RevertAIChangesClicked += (s, e) => RevertAIChanges();
            aiSettingsPanel.OldNewClicked += (s, e) => ToggleOldNew();
            aiSettingsPanel.BlackPixelSettingsChanged += (s, e) => ReapplyBlackPixelRemoval();
            aiSettingsPanel.SaveToMulClicked += (s, e) => SaveGumpsToMul();
            aiSettingsPanel.PushToJarJarClicked += async (s, e) => await PushGumpsToJarJar();
            aiSettingsPanel.HideRegenAsOneButton();
            aiSettingsPanel.HideRevertOGButton();
            aiSettingsPanel.HideUnifyButton();
            aiSettingsPanel.HideSaveCanvasAsNewButton();
            aiSettingsPanel.StopAIGenerationClicked += (s, e) => CancelGumpGeneration();
            aiTab.Controls.Add(aiSettingsPanel);

            var imageFxTab = new TabPage("Image FX")
            {
                BackColor = HolographicTheme.PanelBackground
            };

            imageEditingPanel = new ImageEditingPanel
            {
                Dock = DockStyle.Fill
            };
            imageEditingPanel.ApplyClicked += (s, e) => ApplyImageEffectsToPaletteGumps(false);
            imageEditingPanel.ApplyToAllSelectedClicked += (s, e) => ApplyImageEffectsToPaletteGumps(true);
            imageEditingPanel.EffectsChanged += (s, e) =>
            {
                if (imageEditingPanel.RealTimePreview)
                    ApplyImageEffectsToPaletteGumps(false);
            };
            imageFxTab.Controls.Add(imageEditingPanel);

            rightToolsTabs.TabPages.Add(aiTab);
            rightToolsTabs.TabPages.Add(imageFxTab);

            // Layers panel (left of AI panel, right of canvas)
            layersPanel = new Panel
            {
                Dock = DockStyle.Right,
                Width = 180,
                BackColor = HolographicTheme.PanelBackground,
                Padding = new Padding(5)
            };
            BuildLayersPanel();

            // Canvas panel (center)
            canvasPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 30, 35),
                AutoScroll = false
            };

            canvasBox = new PictureBox
            {
                BackColor = Color.FromArgb(45, 45, 50),
                SizeMode = PictureBoxSizeMode.Normal,
                Location = new Point(50, 50),
                Width = canvasWidth,
                Height = canvasHeight,
                AllowDrop = true  // Enable drop on canvas
            };
            canvasBox.Paint += CanvasBox_Paint;
            canvasBox.MouseDown += CanvasBox_MouseDown;
            canvasBox.MouseMove += CanvasBox_MouseMove;
            canvasBox.MouseUp += CanvasBox_MouseUp;
            canvasBox.MouseWheel += CanvasBox_MouseWheel;
            canvasBox.MouseEnter += (s, e) => canvasBox.Focus();
            canvasBox.DragEnter += CanvasBox_DragEnter;
            canvasBox.DragOver += CanvasBox_DragOver;
            canvasBox.DragDrop += CanvasBox_DragDrop;

            canvasPanel.Controls.Add(canvasBox);
            canvasPanel.Resize += (s, e) => CenterCanvas();

            // Status panel (bottom)
            statusPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 25,
                BackColor = HolographicTheme.ControlBackground
            };

            statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = HolographicTheme.TextSecondary,
                Font = new Font("Consolas", 9),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(5, 0, 0, 0),
                Text = "Ready"
            };
            statusPanel.Controls.Add(statusLabel);

            // Add controls to right panel in correct order for docking
            rightPanel.Controls.Add(canvasPanel);    // Fill - must be added first
            rightPanel.Controls.Add(rightToolsTabs); // Right - AI + Image FX tabs
            rightPanel.Controls.Add(layersPanel);    // Right (to the left of AI panel)
            rightPanel.Controls.Add(statusPanel);    // Bottom
            rightPanel.Controls.Add(controlsPanel);  // Top

            mainSplit.Panel1.Controls.Add(palettePanel);
            mainSplit.Panel2.Controls.Add(rightPanel);

            this.Controls.Add(mainSplit);

            // Initialize canvas and layers
            CreateCanvas();
            AddLayer("Layer 0");
        }

        private void BuildControlsPanel()
        {
            int x = 10;
            int y = 10;

            // Canvas size controls
            var widthLabel = new Label { Text = "Width:", Left = x, Top = y + 3, Width = 40, AutoSize = false };
            HolographicTheme.ApplyToLabel(widthLabel);
            controlsPanel.Controls.Add(widthLabel);
            x += 45;

            canvasWidthUpDown = new NumericUpDown
            {
                Left = x,
                Top = y,
                Width = 60,
                Minimum = 100,
                Maximum = 2048,
                Value = canvasWidth
            };
            HolographicTheme.ApplyToNumericUpDown(canvasWidthUpDown);
            canvasWidthUpDown.ValueChanged += (s, e) => { canvasWidth = (int)canvasWidthUpDown.Value; CreateCanvas(); };
            tooltip.SetToolTip(canvasWidthUpDown, "Canvas width in pixels");
            controlsPanel.Controls.Add(canvasWidthUpDown);
            x += 70;

            var heightLabel = new Label { Text = "Height:", Left = x, Top = y + 3, Width = 45, AutoSize = false };
            HolographicTheme.ApplyToLabel(heightLabel);
            controlsPanel.Controls.Add(heightLabel);
            x += 50;

            canvasHeightUpDown = new NumericUpDown
            {
                Left = x,
                Top = y,
                Width = 60,
                Minimum = 100,
                Maximum = 2048,
                Value = canvasHeight
            };
            HolographicTheme.ApplyToNumericUpDown(canvasHeightUpDown);
            canvasHeightUpDown.ValueChanged += (s, e) => { canvasHeight = (int)canvasHeightUpDown.Value; CreateCanvas(); };
            tooltip.SetToolTip(canvasHeightUpDown, "Canvas height in pixels");
            controlsPanel.Controls.Add(canvasHeightUpDown);
            x += 70;

            // Grid size
            var gridLabel = new Label { Text = "Grid:", Left = x, Top = y + 3, Width = 35, AutoSize = false };
            HolographicTheme.ApplyToLabel(gridLabel);
            controlsPanel.Controls.Add(gridLabel);
            x += 40;

            gridSizeUpDown = new NumericUpDown
            {
                Left = x,
                Top = y,
                Width = 50,
                Minimum = 8,
                Maximum = 128,
                Value = gridSize
            };
            HolographicTheme.ApplyToNumericUpDown(gridSizeUpDown);
            gridSizeUpDown.ValueChanged += (s, e) => { gridSize = (int)gridSizeUpDown.Value; canvasBox.Invalidate(); };
            tooltip.SetToolTip(gridSizeUpDown, "Grid cell size in pixels");
            controlsPanel.Controls.Add(gridSizeUpDown);
            x += 60;

            showGridCheckBox = new CheckBox
            {
                Text = "Show Grid",
                Left = x,
                Top = y,
                Width = 90,
                Checked = true,
                ForeColor = HolographicTheme.TextPrimary
            };
            showGridCheckBox.CheckedChanged += (s, e) => { showGrid = showGridCheckBox.Checked; canvasBox.Invalidate(); };
            tooltip.SetToolTip(showGridCheckBox, "Toggle grid visibility on canvas");
            controlsPanel.Controls.Add(showGridCheckBox);
            x += 100;

            // Action buttons
            deleteBtn = CreateThemedButton("Delete", x, y, 65, ButtonStyle.Danger);
            deleteBtn.Click += (s, e) => DeleteSelected();
            tooltip.SetToolTip(deleteBtn, "Delete selected element (Del)");
            controlsPanel.Controls.Add(deleteBtn);
            x += 75;

            layerUpBtn = CreateThemedButton("L+", x, y, 35, ButtonStyle.Default);
            layerUpBtn.Click += (s, e) => MoveSelectedToLayer(1);
            tooltip.SetToolTip(layerUpBtn, "Move selected element up one layer");
            controlsPanel.Controls.Add(layerUpBtn);
            x += 40;

            layerDownBtn = CreateThemedButton("L-", x, y, 35, ButtonStyle.Default);
            layerDownBtn.Click += (s, e) => MoveSelectedToLayer(-1);
            tooltip.SetToolTip(layerDownBtn, "Move selected element down one layer");
            controlsPanel.Controls.Add(layerDownBtn);
            x += 45;

            exportBtn = CreateThemedButton("Export", x, y, 70, ButtonStyle.Success);
            exportBtn.Click += (s, e) => ExportCanvas();
            tooltip.SetToolTip(exportBtn, "Export canvas as PNG image");
            controlsPanel.Controls.Add(exportBtn);
            x += 80;

            var importBtn = CreateThemedButton("Import", x, y, 70, ButtonStyle.Default);
            importBtn.Click += (s, e) => ImportImage();
            tooltip.SetToolTip(importBtn, "Import an image file to canvas");
            controlsPanel.Controls.Add(importBtn);
            x += 80;

            var pasteBtn = CreateThemedButton("Paste", x, y, 60, ButtonStyle.Default);
            pasteBtn.Click += (s, e) => PasteFromClipboard();
            tooltip.SetToolTip(pasteBtn, "Paste image from clipboard (Ctrl+V)");
            controlsPanel.Controls.Add(pasteBtn);
            x += 70;

            var splitBtn = CreateThemedButton("Split", x, y, 60, ButtonStyle.Accent);
            splitBtn.Click += (s, e) => SplitSpriteSheet();
            tooltip.SetToolTip(splitBtn, "Split sprite sheet into separate layers");
            controlsPanel.Controls.Add(splitBtn);
            x += 70;

            // Row 2
            x = 10;
            y = 45;

            var undoBtn = CreateThemedButton("Undo", x, y, 60, ButtonStyle.Default);
            undoBtn.Click += (s, e) => Undo();
            tooltip.SetToolTip(undoBtn, "Undo last action (Ctrl+Z)");
            controlsPanel.Controls.Add(undoBtn);
            x += 70;

            var redoBtn = CreateThemedButton("Redo", x, y, 60, ButtonStyle.Default);
            redoBtn.Click += (s, e) => Redo();
            tooltip.SetToolTip(redoBtn, "Redo undone action (Ctrl+Y)");
            controlsPanel.Controls.Add(redoBtn);
            x += 70;

            var clearBtn = CreateThemedButton("Clear", x, y, 60, ButtonStyle.Danger);
            clearBtn.Click += (s, e) => ClearCanvas();
            tooltip.SetToolTip(clearBtn, "Remove all elements from canvas");
            controlsPanel.Controls.Add(clearBtn);
            x += 70;

            var zoomInBtn = CreateThemedButton("Zoom+", x, y, 65, ButtonStyle.Default);
            zoomInBtn.Click += (s, e) => ZoomIn();
            tooltip.SetToolTip(zoomInBtn, "Zoom in (mouse wheel up)");
            controlsPanel.Controls.Add(zoomInBtn);
            x += 70;

            var zoomOutBtn = CreateThemedButton("Zoom-", x, y, 65, ButtonStyle.Default);
            zoomOutBtn.Click += (s, e) => ZoomOut();
            tooltip.SetToolTip(zoomOutBtn, "Zoom out (mouse wheel down)");
            controlsPanel.Controls.Add(zoomOutBtn);
            x += 70;

            var zoom100Btn = CreateThemedButton("100%", x, y, 55, ButtonStyle.Default);
            zoom100Btn.Click += (s, e) => { zoom = 1.0f; UpdateCanvasSize(); };
            tooltip.SetToolTip(zoom100Btn, "Reset zoom to 100%");
            controlsPanel.Controls.Add(zoom100Btn);
            x += 65;

            // Transform buttons
            var rotateLeftBtn = CreateThemedButton("?", x, y, 30, ButtonStyle.Default);
            rotateLeftBtn.Click += (s, e) => RotateSelected(-15);
            tooltip.SetToolTip(rotateLeftBtn, "Rotate selected 15� counter-clockwise");
            controlsPanel.Controls.Add(rotateLeftBtn);
            x += 35;

            var rotateRightBtn = CreateThemedButton("?", x, y, 30, ButtonStyle.Default);
            rotateRightBtn.Click += (s, e) => RotateSelected(15);
            tooltip.SetToolTip(rotateRightBtn, "Rotate selected 15� clockwise");
            controlsPanel.Controls.Add(rotateRightBtn);
            x += 35;

            var scaleUpBtn = CreateThemedButton("S+", x, y, 35, ButtonStyle.Default);
            scaleUpBtn.Click += (s, e) => ScaleSelected(1.1f);
            tooltip.SetToolTip(scaleUpBtn, "Scale selected up 10%");
            controlsPanel.Controls.Add(scaleUpBtn);
            x += 40;

            var scaleDownBtn = CreateThemedButton("S-", x, y, 35, ButtonStyle.Default);
            scaleDownBtn.Click += (s, e) => ScaleSelected(0.9f);
            tooltip.SetToolTip(scaleDownBtn, "Scale selected down 10%");
            controlsPanel.Controls.Add(scaleDownBtn);
            x += 40;

            var resetBtn = CreateThemedButton("RST", x, y, 40, ButtonStyle.Warning);
            resetBtn.Click += (s, e) => ResetSelectedTransform();
            tooltip.SetToolTip(resetBtn, "Reset selected element's transform (scale, rotation)");
            controlsPanel.Controls.Add(resetBtn);
            x += 45;

            var sliceBtn = CreateThemedButton("Slice", x, y, 45, ButtonStyle.Accent);
            sliceBtn.Click += (s, e) => ShowSliceDialog();
            tooltip.SetToolTip(sliceBtn, "Open slice tool to cut canvas into grid pieces");
            controlsPanel.Controls.Add(sliceBtn);
            x += 50;

            // Flip buttons
            var flipHBtn = CreateThemedButton("FlipH", x, y, 45, ButtonStyle.Default);
            flipHBtn.Click += (s, e) => FlipSelectedHorizontal();
            tooltip.SetToolTip(flipHBtn, "Flip selected horizontally");
            controlsPanel.Controls.Add(flipHBtn);
            x += 50;

            var flipVBtn = CreateThemedButton("FlipV", x, y, 45, ButtonStyle.Default);
            flipVBtn.Click += (s, e) => FlipSelectedVertical();
            tooltip.SetToolTip(flipVBtn, "Flip selected vertically");
            controlsPanel.Controls.Add(flipVBtn);
            x += 50;

            // Skew button
            var skewBtn = CreateThemedButton("Skew", x, y, 45, ButtonStyle.Accent);
            skewBtn.Click += (s, e) => ShowSkewDialog();
            tooltip.SetToolTip(skewBtn, "Adjust skew/shear of selected element");
            controlsPanel.Controls.Add(skewBtn);
            x += 55;

            var sendMapUnderBtn = CreateThemedButton("Map U", x, y, 60, ButtonStyle.Accent);
            sendMapUnderBtn.Click += (s, e) => SendSelectedToMap(false);
            tooltip.SetToolTip(sendMapUnderBtn, "Send selected gump to Map Editor as underlay");
            controlsPanel.Controls.Add(sendMapUnderBtn);
            x += 65;

            var sendMapOverBtn = CreateThemedButton("Map O", x, y, 60, ButtonStyle.Accent);
            sendMapOverBtn.Click += (s, e) => SendSelectedToMap(true);
            tooltip.SetToolTip(sendMapOverBtn, "Send selected gump to Map Editor as overlay");
            controlsPanel.Controls.Add(sendMapOverBtn);
            x += 65;

            snapToGridCheckBox = new CheckBox
            {
                Text = "Snap to Grid",
                Left = x,
                Top = y,
                Width = 100,
                Checked = true,
                ForeColor = HolographicTheme.TextPrimary
            };
            snapToGridCheckBox.CheckedChanged += (s, e) => { _snapToGrid = snapToGridCheckBox.Checked; };
            tooltip.SetToolTip(snapToGridCheckBox, "Snap element positions to grid");
            controlsPanel.Controls.Add(snapToGridCheckBox);
        }

        private void BuildLayersPanel()
        {
            var layerButtonsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 70,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(2)
            };

            var addLayerBtn = CreateThemedButton("Add", 0, 0, 50, ButtonStyle.Success);
            addLayerBtn.Click += (s, e) => AddLayer();
            tooltip.SetToolTip(addLayerBtn, "Add a new layer");
            layerButtonsPanel.Controls.Add(addLayerBtn);

            var delLayerBtn = CreateThemedButton("Del", 0, 0, 50, ButtonStyle.Danger);
            delLayerBtn.Click += (s, e) => DeleteLayer(activeLayerIndex);
            tooltip.SetToolTip(delLayerBtn, "Delete active layer");
            layerButtonsPanel.Controls.Add(delLayerBtn);

            var upLayerBtn = CreateThemedButton("Up", 0, 0, 40, ButtonStyle.Default);
            upLayerBtn.Click += (s, e) => MoveLayer(-1);
            tooltip.SetToolTip(upLayerBtn, "Move layer up in stack");
            layerButtonsPanel.Controls.Add(upLayerBtn);

            var downLayerBtn = CreateThemedButton("Down", 0, 0, 50, ButtonStyle.Default);
            downLayerBtn.Click += (s, e) => MoveLayer(1);
            tooltip.SetToolTip(downLayerBtn, "Move layer down in stack");
            layerButtonsPanel.Controls.Add(downLayerBtn);

            var visLayerBtn = CreateThemedButton("Vis", 0, 0, 40, ButtonStyle.Default);
            visLayerBtn.Click += (s, e) => ToggleLayerVisibility();
            tooltip.SetToolTip(visLayerBtn, "Toggle layer visibility");
            layerButtonsPanel.Controls.Add(visLayerBtn);

            var lockLayerBtn = CreateThemedButton("Lock", 0, 0, 50, ButtonStyle.Warning);
            lockLayerBtn.Click += (s, e) => ToggleLayerLock();
            tooltip.SetToolTip(lockLayerBtn, "Lock/unlock layer (prevents editing)");
            layerButtonsPanel.Controls.Add(lockLayerBtn);

            var layersLabel = new Label
            {
                Text = "LAYERS",
                Dock = DockStyle.Top,
                Height = 25,
                ForeColor = HolographicTheme.CyanAccent,
                BackColor = HolographicTheme.ControlBackground,
                Font = new Font("Consolas", 10, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };

            layersListBox = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = HolographicTheme.InputBackground,
                ForeColor = HolographicTheme.TextPrimary,
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 9),
                ItemHeight = 20  // Fixed item height for consistent spacing
            };
            layersListBox.SelectedIndexChanged += LayersListBox_SelectedIndexChanged;

            // Add controls in correct order for proper docking:
            // 1. Bottom-docked buttons panel first
            // 2. Top-docked header label second
            // 3. Fill-docked listbox last (fills remaining space between header and buttons)
            layersPanel.Controls.Add(layersListBox);      // Fill - added first, fills remaining
            layersPanel.Controls.Add(layerButtonsPanel);  // Bottom
            layersPanel.Controls.Add(layersLabel);        // Top - added last so it's on top
        }

        private Button CreateThemedButton(string text, int left, int top, int width, ButtonStyle style = ButtonStyle.Default)
        {
            var btn = new Button
            {
                Text = text,
                Left = left,
                Top = top,
                Width = width,
                Height = 25
            };
            HolographicTheme.ApplyToButton(btn, style);
            return btn;
        }

        private void SetStatus(string message)
        {
            if (statusLabel.InvokeRequired)
            {
                statusLabel.Invoke(new Action(() => SetStatus(message)));
                return;
            }
            statusLabel.Text = message;
        }
    }
}
