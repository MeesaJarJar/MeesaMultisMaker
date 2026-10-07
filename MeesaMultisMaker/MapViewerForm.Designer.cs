using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    /// <summary>
    /// Panel that does not auto-scroll to a focused child control.
    /// Prevents the sidebar from jumping to the top when clicking buttons.
    /// </summary>
    internal class StableScrollPanel : Panel
    {
        protected override Point ScrollToControl(Control activeControl)
        {
            // Return current position instead of computing a new one
            return AutoScrollPosition;
        }
    }

    partial class MapViewerForm
    {
         // Action buttons
         private Button replaceTilesButton;
         private Button clearSelectionButton;
         private Button restoreOriginalButton;
         
         private Button saveToMulButton; // Button to save changes to art.mul
         private Label pendingChangesLabel; // Label showing count of pending changes
         
         // New Paste button control (Declared here, used below)
         private Button pasteButton; 
        
        // Checkpoint and Drop Black Pixels controls
        internal ComboBox checkpointComboBox;
        internal Button refreshCheckpointsButton;
        internal CheckBox dropBlackPixelsCheckBox;
        internal NumericUpDown blackThresholdNumeric;
        internal CheckBox useControlNetCheckBox;

        private void InitializeComponent()
        {
            this.Text = "UO Map Editor - github.com/MeesaJarJar";
            this.Width = 1560;  // 30% wider than 1200
            this.Height = 900;  // 100 pixels taller than 800
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.StartPosition = FormStartPosition.CenterScreen;

            HolographicTheme.ApplyToForm(this);

            var topPanel = new Panel { Dock = DockStyle.Top, Height = 100, Padding = new Padding(10) };  // Increased height from 70 to 100
            HolographicTheme.ApplyToPanel(topPanel, true);

            // First row of controls
            facetComboBox = new ComboBox
            {
                Left = 10,
                Top = 8,
                Width = 100,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            facetComboBox.Items.AddRange(new object[] { "Felucca (0)", "Trammel (1)", "Ilshenar (2)", "Malas (3)", "Tokuno (4)", "Ter Mur (5)" });
            facetComboBox.SelectedIndex = 0;
            facetComboBox.SelectedIndexChanged += FacetComboBox_SelectedIndexChanged;
            HolographicTheme.ApplyToComboBox(facetComboBox);
            topPanel.Controls.Add(facetComboBox);

             loadButton = new Button
             {
                 Text = "Load Map",
                 Left = 120,
                 Top = 6,
                 Width = 80,
                 Height = 25
             };
             loadButton.Click += LoadButton_Click;
             HolographicTheme.ApplyToButton(loadButton, ButtonStyle.Accent);
             topPanel.Controls.Add(loadButton);
             
             pasteButton = new Button
             {
                 Text = "Paste",
                 Left = 205,
                 Top = 6,
                 Width = 75,
                 Height = 25
             };
             pasteButton.Click += PasteMapImage_Click;
             HolographicTheme.ApplyToButton(pasteButton, ButtonStyle.Warning);
             topPanel.Controls.Add(pasteButton);
             
             newMapButton = new Button
             {
                 Text = "New Map",
                 Left = 285,
                 Top = 6,
                 Width = 75,
                 Height = 25
             };
             newMapButton.Click += NewMapButton_Click;
             HolographicTheme.ApplyToButton(newMapButton, ButtonStyle.Success);
             topPanel.Controls.Add(newMapButton);

            var diagButton = new Button
            {
                Text = "Diagnose",
                Left = 1275,
                Top = 6,
                Width = 80,
                Height = 25
            };
            diagButton.Click += DiagnoseButton_Click;
            HolographicTheme.ApplyToButton(diagButton, ButtonStyle.Warning);
            topPanel.Controls.Add(diagButton);

            zoomOutButton = new Button
            {
                Text = "-",
                Left = 375,
                Top = 6,
                Width = 30,
                Height = 25
            };
            zoomOutButton.Click += (s, e) => ChangeZoom(-1);
            HolographicTheme.ApplyToButton(zoomOutButton);
            topPanel.Controls.Add(zoomOutButton);

            // Zoom trackbar uses a non-linear scale for fine control at both ends
            // Value 0-1000 maps to zoom 0.1 to 128 using exponential curve
            zoomTrackBar = new TrackBar
            {
                Left = 415,
                Top = 2,
                Width = 200,
                Height = 30,
                Minimum = 0,
                Maximum = 1000,
                Value = 700,      // ~44 zoom
                TickFrequency = 100,
                AutoSize = false
            };
            zoomTrackBar.ValueChanged += ZoomTrackBar_ValueChanged;
            HolographicTheme.ApplyToTrackBar(zoomTrackBar);
            topPanel.Controls.Add(zoomTrackBar);

            zoomInButton = new Button
            {
                Text = "+",
                Left = 625,
                Top = 6,
                Width = 30,
                Height = 25
            };
            zoomInButton.Click += (s, e) => ChangeZoom(1);
            HolographicTheme.ApplyToButton(zoomInButton);
            topPanel.Controls.Add(zoomInButton);

            zoomLabel = new Label
            {
                Text = $"Zoom: {zoomTrackBar.Value}px",
                Left = 665,
                Top = 10,
                Width = 80,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(zoomLabel);
            topPanel.Controls.Add(zoomLabel);

            // Selection Mode Radio Buttons (first row, right side)
            var modeLabel = new Label
            {
                Text = "Mode:",
                Left = 755,
                Top = 10,
                Width = 45,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(modeLabel);
            topPanel.Controls.Add(modeLabel);

            replaceRadioButton = new RadioButton
            {
                Text = "Replace",
                Left = 805,
                Top = 8,
                Width = 75,
                Height = 20,
                Checked = true
            };
            replaceRadioButton.CheckedChanged += (s, e) =>
            {
                if (replaceRadioButton.Checked)
                    SetSelectionMode(TileSelectionMode.Replace);
            };
            HolographicTheme.ApplyToRadioButton(replaceRadioButton);
            topPanel.Controls.Add(replaceRadioButton);

            contextRadioButton = new RadioButton
            {
                Text = "Context",
                Left = 885,
                Top = 8,
                Width = 75,
                Height = 20
            };
            contextRadioButton.CheckedChanged += (s, e) =>
            {
                if (contextRadioButton.Checked)
                    SetSelectionMode(TileSelectionMode.Context);
            };
            HolographicTheme.ApplyToRadioButton(contextRadioButton);
            topPanel.Controls.Add(contextRadioButton);

            // Selection Target Type - Land vs Statics
            var selectTargetLabel = new Label
            {
                Text = "Select:",
                Left = 975,
                Top = 10,
                Width = 50,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(selectTargetLabel);
            topPanel.Controls.Add(selectTargetLabel);

            selectLandRadioButton = new RadioButton
            {
                Text = "Land",
                Left = 1025,
                Top = 8,
                Width = 55,
                Height = 20,
                Checked = true
            };
            selectLandRadioButton.CheckedChanged += (s, e) =>
            {
                if (selectLandRadioButton.Checked)
                    SetSelectionTargetType(SelectionTargetType.Land);
            };
            HolographicTheme.ApplyToRadioButton(selectLandRadioButton);
            topPanel.Controls.Add(selectLandRadioButton);

            selectStaticsRadioButton = new RadioButton
            {
                Text = "Statics",
                Left = 1085,
                Top = 8,
                Width = 70,
                Height = 20
            };
            selectStaticsRadioButton.CheckedChanged += (s, e) =>
            {
                if (selectStaticsRadioButton.Checked)
                    SetSelectionTargetType(SelectionTargetType.Statics);
            };
            HolographicTheme.ApplyToRadioButton(selectStaticsRadioButton);
            topPanel.Controls.Add(selectStaticsRadioButton);

            // Show Statics checkbox (moved to first row, after Select radio buttons)
            showStaticsCheckBox = new CheckBox
            {
                Text = "Show Statics",
                Left = 1165,
                Top = 8,
                Width = 100,
                Height = 20,
                Checked = true
            };
            showStaticsCheckBox.CheckedChanged += (s, e) =>
            {
                showStatics = showStaticsCheckBox.Checked;
                GenerateMapImage();
            };
            HolographicTheme.ApplyToCheckBox(showStaticsCheckBox);
            topPanel.Controls.Add(showStaticsCheckBox);

            // GoTo controls (above Z Filter row)
            var goToLabel = new Label
            {
                Text = "GoTo  X:",
                Left = 10,
                Top = 46,
                Width = 60,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(goToLabel);
            topPanel.Controls.Add(goToLabel);

            coordXTextBox = new TextBox
            {
                Left = 72,
                Top = 44,
                Width = 70,
                Height = 20,
                Text = "0"
            };
            HolographicTheme.ApplyToTextBox(coordXTextBox);
            topPanel.Controls.Add(coordXTextBox);

            var goToYLabel = new Label
            {
                Text = "Y:",
                Left = 148,
                Top = 46,
                Width = 18,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(goToYLabel);
            topPanel.Controls.Add(goToYLabel);

            coordYTextBox = new TextBox
            {
                Left = 168,
                Top = 44,
                Width = 70,
                Height = 20,
                Text = "0"
            };
            HolographicTheme.ApplyToTextBox(coordYTextBox);
            topPanel.Controls.Add(coordYTextBox);

            var goToButton = new Button
            {
                Text = "GO",
                Left = 244,
                Top = 42,
                Width = 45,
                Height = 24
            };
            goToButton.Click += GoToButton_Click;
            HolographicTheme.ApplyToButton(goToButton, ButtonStyle.Accent);
            topPanel.Controls.Add(goToButton);

            coordXTextBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                    GoToButton_Click(goToButton, EventArgs.Empty);
            };
            coordYTextBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                    GoToButton_Click(goToButton, EventArgs.Empty);
            };

            // === Third row - Z Filter (moved from ComfyUI panel) ===
            var zFilterLabel = new Label
            {
                Text = "Z Filter:",
                Left = 10,
                Top = 72,
                Width = 50,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(zFilterLabel);
            topPanel.Controls.Add(zFilterLabel);

            // Z Min
            var zMinLabelTitle = new Label
            {
                Text = "Min:",
                Left = 65,
                Top = 72,
                Width = 30,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(zMinLabelTitle);
            topPanel.Controls.Add(zMinLabelTitle);

            zMinLabel = new Label
            {
                Text = "-128",
                Left = 95,
                Top = 72,
                Width = 35,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(zMinLabel);
            topPanel.Controls.Add(zMinLabel);

            zMinTrackBar = new TrackBar
            {
                Left = 130,
                Top = 67,
                Width = 150,
                Height = 25,
                Minimum = -128,
                Maximum = 127,
                Value = -128,
                TickFrequency = 64,
                AutoSize = false
            };
            zMinTrackBar.ValueChanged += (s, e) =>
            {
                staticZMin = zMinTrackBar.Value;
                zMinLabel.Text = staticZMin.ToString();
                if (staticZMin > staticZMax)
                {
                    zMaxTrackBar.Value = staticZMin;
                }
                GenerateMapImage();
            };
            HolographicTheme.ApplyToTrackBar(zMinTrackBar);
            topPanel.Controls.Add(zMinTrackBar);

            // Z Max
            var zMaxLabelTitle = new Label
            {
                Text = "Max:",
                Left = 290,
                Top = 72,
                Width = 30,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(zMaxLabelTitle);
            topPanel.Controls.Add(zMaxLabelTitle);

            zMaxLabel = new Label
            {
                Text = "127",
                Left = 320,
                Top = 72,
                Width = 35,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(zMaxLabel);
            topPanel.Controls.Add(zMaxLabel);

            zMaxTrackBar = new TrackBar
            {
                Left = 355,
                Top = 67,
                Width = 150,
                Height = 25,
                Minimum = -128,
                Maximum = 127,
                Value = 127,
                TickFrequency = 64,
                AutoSize = false
            };
            zMaxTrackBar.ValueChanged += (s, e) =>
            {
                staticZMax = zMaxTrackBar.Value;
                zMaxLabel.Text = staticZMax.ToString();
                if (staticZMax < staticZMin)
                {
                    zMinTrackBar.Value = staticZMax;
                }
                GenerateMapImage();
            };
            HolographicTheme.ApplyToTrackBar(zMaxTrackBar);
            topPanel.Controls.Add(zMaxTrackBar);

            // Reset Z Filter button
            var resetZFilterButton = new Button
            {
                Text = "Reset Z",
                Left = 515,
                Top = 70,
                Width = 60,
                Height = 22
            };
            resetZFilterButton.Click += (s, e) =>
            {
                zMinTrackBar.Value = -128;
                zMaxTrackBar.Value = 127;
            };
            HolographicTheme.ApplyToButton(resetZFilterButton);
            topPanel.Controls.Add(resetZFilterButton);

            // Export Map button
            var exportMapButton = new Button
            {
                Text = "Export Map",
                Left = 585,
                Top = 70,
                Width = 80,
                Height = 22
            };
            exportMapButton.Click += ExportEntireMap_Click;
            HolographicTheme.ApplyToButton(exportMapButton, ButtonStyle.Success);
            topPanel.Controls.Add(exportMapButton);

            // Static Frequency button
            var staticStatsButton = new Button
            {
                Text = "Static Stats",
                Left = 585 + 85,
                Top = 70,
                Width = 80,
                Height = 22
            };
            staticStatsButton.Click += StaticStatsButton_Click;
            HolographicTheme.ApplyToButton(staticStatsButton, ButtonStyle.Accent);
            topPanel.Controls.Add(staticStatsButton);

            // Undo / Redo buttons
            undoButton = new Button
            {
                Text = "Undo",
                Left = 755,
                Top = 70,
                Width = 50,
                Height = 22,
                Enabled = false
            };
            undoButton.Click += (s, e) => PerformUndo();
            HolographicTheme.ApplyToButton(undoButton);
            topPanel.Controls.Add(undoButton);

            redoButton = new Button
            {
                Text = "Redo",
                Left = 810,
                Top = 70,
                Width = 50,
                Height = 22,
                Enabled = false
            };
            redoButton.Click += (s, e) => PerformRedo();
            HolographicTheme.ApplyToButton(redoButton);
            topPanel.Controls.Add(redoButton);

            // Help text on third row
            var numpadHelpLabel = new Label
            {
                Text = "Ctrl+Z/Y: Undo/Redo | Numpad: Move statics | Right-click: Menu",
                Left = 865,
                Top = 72,
                Width = 340,
                Height = 20,
                ForeColor = Color.Gray
            };
            HolographicTheme.ApplyToLabel(numpadHelpLabel);
            topPanel.Controls.Add(numpadHelpLabel);

            this.Controls.Add(topPanel);

            // Create ComfyUI Settings Panel (permanently docked on right side)
            CreateComfyUISettingsPanel();

            // Heightmap / Terrain editing panel (docked at bottom of the settings panel)
            InitializeHeightmapPanel();

            // World Generator panel (docked below heightmap in the settings panel)
            InitializeWorldGenPanel();

            mapPictureBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Normal,
                BackColor = HolographicTheme.CanvasBackground,
                Cursor = Cursors.Hand,
                AllowDrop = true
            };
            // Enable double buffering to reduce flicker during resize
            typeof(PictureBox).InvokeMember("DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, mapPictureBox, new object[] { true });
            
            mapPictureBox.Resize += MapPictureBox_Resize;
            mapPictureBox.MouseDown += MapPictureBox_MouseDown;
            mapPictureBox.MouseMove += MapPictureBox_MouseMove;
            mapPictureBox.MouseUp += MapPictureBox_MouseUp;
            mapPictureBox.MouseWheel += MapPictureBox_MouseWheel;
            mapPictureBox.Paint += MapPictureBox_Paint;
            mapPictureBox.DragEnter += MapPictureBox_DragEnter;
            mapPictureBox.DragDrop += MapPictureBox_DragDrop;
            this.Controls.Add(mapPictureBox);

            // Static palette panel (docked on left side)
            InitializeStaticPalettePanel();

            // Minimap overlay (inside mapPictureBox, top-left corner)
            InitializeMinimapOverlay();

            statusLabel = new Label
            {
                Text = "Ready | Left-click: Select | Right-click: Pan | Ctrl-click: Toggle | Shift-click: Range",
                Dock = DockStyle.Bottom,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0)
            };
            HolographicTheme.ApplyToLabel(statusLabel);
            this.Controls.Add(statusLabel);
        }

        private void CreateComfyUISettingsPanel()
        {
            comfySettingsPanel = new StableScrollPanel
            {
                Dock = DockStyle.Right,
                Width = 330, // Always visible with fixed width
                Padding = new Padding(10),
                AutoScroll = true,
                Visible = true
            };
            HolographicTheme.ApplyToPanel(comfySettingsPanel, true);
            
            // Disable horizontal scrollbar - only allow vertical scrolling
            comfySettingsPanel.HorizontalScroll.Enabled = false;
            comfySettingsPanel.HorizontalScroll.Visible = false;
            comfySettingsPanel.HorizontalScroll.Maximum = 0;
            comfySettingsPanel.AutoScrollMinSize = new Size(0, 0);

            int y = 10;
            int ctrlWidth = 280; // Keep controls within panel width to avoid horizontal scroll

            // Title
            var titleLabel = new Label
            {
                Text = "ComfyUI Settings",
                Location = new Point(10, y),
                Width = ctrlWidth,
                Height = 25,
                Font = new Font(this.Font.FontFamily, 12, FontStyle.Bold)
            };
            HolographicTheme.ApplyToLabel(titleLabel);
            comfySettingsPanel.Controls.Add(titleLabel);
            y += 35;

            // AI Backend
            var backendLabel = new Label
            {
                Text = "AI Backend:",
                Location = new Point(10, y),
                Width = 100,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(backendLabel);
            comfySettingsPanel.Controls.Add(backendLabel);

            backendComboBox = new ComboBox
            {
                Location = new Point(120, y - 2),
                Width = 165,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            backendComboBox.Items.AddRange(new object[] {
                "ComfyUI (Server)"
            });
            backendComboBox.SelectedIndex = 0;
            backendComboBox.SelectedIndexChanged += BackendComboBox_SelectedIndexChanged;
            HolographicTheme.ApplyToComboBox(backendComboBox);
            comfySettingsPanel.Controls.Add(backendComboBox);
            y += 35;

            // ComfyUI URL
            var urlLabel = new Label
            {
                Text = "ComfyUI URL:",
                Location = new Point(10, y),
                Width = 100,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(urlLabel);
            comfySettingsPanel.Controls.Add(urlLabel);
            y += 25;

            comfyUrlTextBox = new TextBox
            {
                Location = new Point(10, y),
                Width = 225,
                Height = 20,
                Text = "http://localhost:8188"
            };
            HolographicTheme.ApplyToTextBox(comfyUrlTextBox);
            comfySettingsPanel.Controls.Add(comfyUrlTextBox);

            testConnectionButton = new Button
            {
                Text = "Test",
                Location = new Point(240, y - 2),
                Width = 45,
                Height = 24
            };
            testConnectionButton.Click += TestConnection_Click;
            HolographicTheme.ApplyToButton(testConnectionButton);
            comfySettingsPanel.Controls.Add(testConnectionButton);
            y += 35;

            // Resolution
            var resolutionLabel = new Label
            {
                Text = "Resolution:",
                Location = new Point(10, y),
                Width = 100,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(resolutionLabel);
            comfySettingsPanel.Controls.Add(resolutionLabel);

            resolutionComboBox = new ComboBox
            {
                Location = new Point(120, y - 2),
                Width = 100,
                DropDownStyle = ComboBoxStyle.DropDown
            };
            resolutionComboBox.Items.AddRange(new object[] {
                "512x512",
                "768x768",
                "1024x1024",
                "1280x1280",
                "1536x1536"
            });
            resolutionComboBox.SelectedIndex = 0; // Default to 512x512
            HolographicTheme.ApplyToComboBox(resolutionComboBox);
            comfySettingsPanel.Controls.Add(resolutionComboBox);

            customResolutionCheckBox = new CheckBox
            {
                Text = "Custom",
                Location = new Point(225, y),
                Width = 65,
                Height = 20,
                Checked = false
            };
            HolographicTheme.ApplyToCheckBox(customResolutionCheckBox);
            comfySettingsPanel.Controls.Add(customResolutionCheckBox);
            y += 24;

            resolutionWidthNumeric = new NumericUpDown
            {
                Location = new Point(120, y),
                Width = 70,
                Minimum = 64,
                Maximum = 4096,
                Value = 512,
                Enabled = false
            };
            HolographicTheme.ApplyToNumericUpDown(resolutionWidthNumeric);
            comfySettingsPanel.Controls.Add(resolutionWidthNumeric);

            var resolutionXLabel = new Label
            {
                Text = "x",
                Location = new Point(194, y + 2),
                Width = 10,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(resolutionXLabel);
            comfySettingsPanel.Controls.Add(resolutionXLabel);

            resolutionHeightNumeric = new NumericUpDown
            {
                Location = new Point(208, y),
                Width = 70,
                Minimum = 64,
                Maximum = 4096,
                Value = 512,
                Enabled = false
            };
            HolographicTheme.ApplyToNumericUpDown(resolutionHeightNumeric);
            comfySettingsPanel.Controls.Add(resolutionHeightNumeric);

            customResolutionCheckBox.CheckedChanged += (s, e) =>
            {
                bool isCustom = customResolutionCheckBox.Checked;
                resolutionComboBox.Enabled = !isCustom;
                resolutionWidthNumeric.Enabled = isCustom;
                resolutionHeightNumeric.Enabled = isCustom;
            };

            y += 30;

            // Prompt
            var promptLabel = new Label
            {
                Text = "Prompt:",
                Location = new Point(10, y),
                Width = ctrlWidth,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(promptLabel);
            comfySettingsPanel.Controls.Add(promptLabel);
            y += 25;

            promptTextBox = new TextBox
            {
                Location = new Point(10, y),
                Width = ctrlWidth,
                Height = 60,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Text = "high quality terrain texture, isometric game tile, detailed, realistic"
            };
            HolographicTheme.ApplyToTextBox(promptTextBox);
            comfySettingsPanel.Controls.Add(promptTextBox);
            y += 70;

            // Negative Prompt
            var negPromptLabel = new Label
            {
                Text = "Negative Prompt:",
                Location = new Point(10, y),
                Width = ctrlWidth,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(negPromptLabel);
            comfySettingsPanel.Controls.Add(negPromptLabel);
            y += 25;

            negativePromptTextBox = new TextBox
            {
                Location = new Point(10, y),
                Width = ctrlWidth,
                Height = 50,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Text = "blurry, low quality, distorted, text, watermark"
            };
            HolographicTheme.ApplyToTextBox(negativePromptTextBox);
            comfySettingsPanel.Controls.Add(negativePromptTextBox);
            y += 60;

            // Steps
            var stepsLabel = new Label
            {
                Text = "Steps:",
                Location = new Point(10, y),
                Width = 100,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(stepsLabel);
            comfySettingsPanel.Controls.Add(stepsLabel);

            stepsNumeric = new NumericUpDown
            {
                Location = new Point(120, y),
                Width = 70,
                Minimum = 1,
                Maximum = 100,
                Value = 20
            };
            HolographicTheme.ApplyToNumericUpDown(stepsNumeric);
            comfySettingsPanel.Controls.Add(stepsNumeric);
            y += 30;

            // CFG
            var cfgLabel = new Label
            {
                Text = "CFG Scale:",
                Location = new Point(10, y),
                Width = 100,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(cfgLabel);
            comfySettingsPanel.Controls.Add(cfgLabel);

            cfgNumeric = new NumericUpDown
            {
                Location = new Point(120, y),
                Width = 70,
                Minimum = 1,
                Maximum = 30,
                Value = 7,
                DecimalPlaces = 1,
                Increment = 0.5m
            };
            HolographicTheme.ApplyToNumericUpDown(cfgNumeric);
            comfySettingsPanel.Controls.Add(cfgNumeric);
            y += 30;

            // Denoise
            var denoiseLabel = new Label
            {
                Text = "Denoise:",
                Location = new Point(10, y),
                Width = 100,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(denoiseLabel);
            comfySettingsPanel.Controls.Add(denoiseLabel);

            denoiseNumeric = new NumericUpDown
            {
                Location = new Point(120, y),
                Width = 70,
                Minimum = 0,
                Maximum = 1,
                Value = 0.75m, // Higher default for better texture generation
                DecimalPlaces = 2,
                Increment = 0.05m
            };
            HolographicTheme.ApplyToNumericUpDown(denoiseNumeric);
            comfySettingsPanel.Controls.Add(denoiseNumeric);
            y += 30;

            // Seed
            var seedLabel = new Label
            {
                Text = "Seed:",
                Location = new Point(10, y),
                Width = 100,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(seedLabel);
            comfySettingsPanel.Controls.Add(seedLabel);

            seedTextBox = new TextBox
            {
                Location = new Point(120, y),
                Width = 120,
                Height = 20,
                Enabled = false
            };
            HolographicTheme.ApplyToTextBox(seedTextBox);
            comfySettingsPanel.Controls.Add(seedTextBox);
            y += 30;

            autoRandomSeedCheckBox = new CheckBox
            {
                Text = "Auto Random Seed",
                Location = new Point(10, y),
                Width = 150,
                Checked = true
            };
            autoRandomSeedCheckBox.CheckedChanged += (s, e) =>
            {
                seedTextBox.Enabled = !autoRandomSeedCheckBox.Checked;
            };
            HolographicTheme.ApplyToCheckBox(autoRandomSeedCheckBox);
            comfySettingsPanel.Controls.Add(autoRandomSeedCheckBox);
            y += 30;

            // Sampler
            var samplerLabel = new Label
            {
                Text = "Sampler:",
                Location = new Point(10, y),
                Width = 100,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(samplerLabel);
            comfySettingsPanel.Controls.Add(samplerLabel);
            y += 25;

            samplerComboBox = new ComboBox
            {
                Location = new Point(10, y),
                Width = 180,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            samplerComboBox.Items.AddRange(new object[] {
                "euler", "euler_ancestral", "heun", "dpm_2", "dpm_2_ancestral",
                "lms", "dpm_fast", "dpmpp_2m", "dpmpp_sde", "ddpm", "lcm"
            });
            samplerComboBox.SelectedIndex = 0;
            HolographicTheme.ApplyToComboBox(samplerComboBox);
            comfySettingsPanel.Controls.Add(samplerComboBox);
            y += 35;

            // Scheduler
            var schedulerLabel = new Label
            {
                Text = "Scheduler:",
                Location = new Point(10, y),
                Width = 100,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(schedulerLabel);
            comfySettingsPanel.Controls.Add(schedulerLabel);
            y += 25;

            schedulerComboBox = new ComboBox
            {
                Location = new Point(10, y),
                Width = 180,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            schedulerComboBox.Items.AddRange(new object[] {
                "normal", "karras", "exponential", "sgm_uniform", "simple", "ddim_uniform"
            });
            schedulerComboBox.SelectedIndex = 0;
            HolographicTheme.ApplyToComboBox(schedulerComboBox);
            comfySettingsPanel.Controls.Add(schedulerComboBox);
            y += 35;

            // Checkpoint
            var checkpointLabel = new Label
            {
                Text = "Checkpoint:",
                Location = new Point(10, y),
                Width = 100,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(checkpointLabel);
            comfySettingsPanel.Controls.Add(checkpointLabel);
            y += 25;

            checkpointComboBox = new ComboBox
            {
                Location = new Point(10, y),
                Width = 225,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DropDownWidth = 300
            };
            checkpointComboBox.Items.Add(MeesaMultisMaker.ComfyUI.Text2ImageWorkflow.DEFAULT_CHECKPOINT);
            checkpointComboBox.SelectedIndex = 0;
            HolographicTheme.ApplyToComboBox(checkpointComboBox);
            comfySettingsPanel.Controls.Add(checkpointComboBox);

            refreshCheckpointsButton = new Button
            {
                Text = "?",
                Location = new Point(240, y - 2),
                Width = 30,
                Height = 24,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            };
            refreshCheckpointsButton.Click += RefreshCheckpoints_Click;
            HolographicTheme.ApplyToButton(refreshCheckpointsButton);
            comfySettingsPanel.Controls.Add(refreshCheckpointsButton);
            y += 35;

            // Use ControlNet checkbox
            useControlNetCheckBox = new CheckBox
            {
                Text = "Use ControlNet (Depth)",
                Location = new Point(10, y),
                Width = 180,
                Height = 20,
                Checked = AppConfig.Instance.UseControlNet
            };
            HolographicTheme.ApplyToCheckBox(useControlNetCheckBox);
            comfySettingsPanel.Controls.Add(useControlNetCheckBox);
            y += 30;

            // Drop Black Pixels section
            var dropBlackSeparator = new Label
            {
                Location = new Point(10, y),
                Width = ctrlWidth,
                Height = 1,
                BackColor = HolographicTheme.BorderCyan
            };
            comfySettingsPanel.Controls.Add(dropBlackSeparator);
            y += 10;

            dropBlackPixelsCheckBox = new CheckBox
            {
                Text = "Drop Black Pixels",
                Location = new Point(10, y),
                Width = 130,
                Height = 20,
                Checked = true
            };
            HolographicTheme.ApplyToCheckBox(dropBlackPixelsCheckBox);
            comfySettingsPanel.Controls.Add(dropBlackPixelsCheckBox);

            var thresholdLabel = new Label
            {
                Text = "Thresh:",
                Location = new Point(145, y + 2),
                Width = 50,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(thresholdLabel);
            comfySettingsPanel.Controls.Add(thresholdLabel);

            blackThresholdNumeric = new NumericUpDown
            {
                Location = new Point(195, y),
                Width = 55,
                Minimum = 0,
                Maximum = 255,
                Value = 15,
                Increment = 5
            };
            HolographicTheme.ApplyToNumericUpDown(blackThresholdNumeric);
            comfySettingsPanel.Controls.Add(blackThresholdNumeric);
            y += 25;

            var thresholdHelpLabel = new Label
            {
                Text = "Removes pixels where R,G,B = threshold",
                Location = new Point(10, y),
                Width = ctrlWidth,
                Height = 16,
                ForeColor = Color.Gray,
                Font = new Font(this.Font.FontFamily, 7.5f)
            };
            comfySettingsPanel.Controls.Add(thresholdHelpLabel);
            y += 30;

            // AI action shortcuts (moved up since Z filter was removed)
            var aiActionsLabel = new Label
            {
                Text = "AI Actions",
                Location = new Point(10, y),
                Width = ctrlWidth,
                Height = 20,
                Font = new Font(this.Font.FontFamily, 10, FontStyle.Bold)
            };
            HolographicTheme.ApplyToLabel(aiActionsLabel);
            comfySettingsPanel.Controls.Add(aiActionsLabel);
            y += 24;

            var generateTilesButton = new Button
            {
                Text = "Generate Tiles",
                Location = new Point(10, y),
                Width = 125,
                Height = 28
            };
            generateTilesButton.Click += async (s, e) =>
            {
                if (replaceTiles.Count == 0 && contextTiles.Count == 0)
                {
                    MessageBox.Show(this, "Select tiles to replace (and optional context) first.", "AI Generate Tiles", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                await ReplaceSelectedTilesWithAI();
            };
            HolographicTheme.ApplyToButton(generateTilesButton, ButtonStyle.Accent);
            comfySettingsPanel.Controls.Add(generateTilesButton);

            var generateStaticsButton = new Button
            {
                Text = "Generate Statics",
                Location = new Point(145, y),
                Width = 125,
                Height = 28
            };
            generateStaticsButton.Click += async (s, e) =>
            {
                if (selectedStatics.Count == 0)
                {
                    MessageBox.Show(this, "Select statics to replace first.", "AI Generate Statics", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                await ReplaceSelectedStaticsWithAI();
            };
            HolographicTheme.ApplyToButton(generateStaticsButton, ButtonStyle.Success);
            comfySettingsPanel.Controls.Add(generateStaticsButton);
            y += 32;

            var generateStaticsAsOneButton = new Button
            {
                Text = "Gen Statics As One",
                Location = new Point(10, y),
                Width = 260,
                Height = 28
            };
            generateStaticsAsOneButton.Click += async (s, e) =>
            {
                if (selectedStatics.Count == 0)
                {
                    MessageBox.Show(this, "Select statics to replace first.", "AI Generate Statics As One", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                await ReplaceSelectedStaticsAsOne();
            };
            HolographicTheme.ApplyToButton(generateStaticsAsOneButton, ButtonStyle.Warning);
            comfySettingsPanel.Controls.Add(generateStaticsAsOneButton);
            y += 36;

            var generateUnderlayButton = new Button
            {
                Text = "Generate Underlay (Void)",
                Location = new Point(10, y),
                Width = 260,
                Height = 28
            };
            generateUnderlayButton.Click += async (s, e) =>
            {
                await GenerateUnderlayPlaneWithAI();
            };
            HolographicTheme.ApplyToButton(generateUnderlayButton, ButtonStyle.Accent);
            comfySettingsPanel.Controls.Add(generateUnderlayButton);
            y += 36;

            var showUnderlayCheckBox = new CheckBox
            {
                Text = "Show Underlay Layer",
                Location = new Point(10, y),
                Width = 180,
                Height = 20,
                Checked = true
            };
            showUnderlayCheckBox.CheckedChanged += (s, e) =>
            {
                showUnderlayPlanes = showUnderlayCheckBox.Checked;
                GenerateMapImage();
            };
            HolographicTheme.ApplyToCheckBox(showUnderlayCheckBox);
            comfySettingsPanel.Controls.Add(showUnderlayCheckBox);
            y += 24;

            var clearUnderlayAreaButton = new Button
            {
                Text = "Clear Underlay in Selection",
                Location = new Point(10, y),
                Width = 260,
                Height = 24
            };
            clearUnderlayAreaButton.Click += (s, e) => ClearUnderlayAreaInSelection();
            HolographicTheme.ApplyToButton(clearUnderlayAreaButton, ButtonStyle.Warning);
            comfySettingsPanel.Controls.Add(clearUnderlayAreaButton);
            y += 28;

            var removeUnderlaySelectionButton = new Button
            {
                Text = "Remove Underlay Planes in Selection",
                Location = new Point(10, y),
                Width = 260,
                Height = 24
            };
            removeUnderlaySelectionButton.Click += (s, e) => RemoveUnderlaysInSelection();
            HolographicTheme.ApplyToButton(removeUnderlaySelectionButton, ButtonStyle.Warning);
            comfySettingsPanel.Controls.Add(removeUnderlaySelectionButton);
            y += 28;

            var clearAllUnderlaysButton = new Button
            {
                Text = "Clear All Underlays",
                Location = new Point(10, y),
                Width = 260,
                Height = 24
            };
            clearAllUnderlaysButton.Click += (s, e) => ClearAllUnderlays();
            HolographicTheme.ApplyToButton(clearAllUnderlaysButton, ButtonStyle.Danger);
            comfySettingsPanel.Controls.Add(clearAllUnderlaysButton);
            y += 32;

            var generateOverlayButton = new Button
            {
                Text = "Generate Land Overlay",
                Location = new Point(10, y),
                Width = 260,
                Height = 28
            };
            generateOverlayButton.Click += async (s, e) => await GenerateLandOverlayWithAI();
            HolographicTheme.ApplyToButton(generateOverlayButton, ButtonStyle.Accent);
            comfySettingsPanel.Controls.Add(generateOverlayButton);
            y += 34;

            overlayUseSparseDeltaCheckBox = new CheckBox
            {
                Text = "Sparse Detail Overlay",
                Location = new Point(10, y),
                Width = 160,
                Height = 20,
                Checked = true
            };
            HolographicTheme.ApplyToCheckBox(overlayUseSparseDeltaCheckBox);
            comfySettingsPanel.Controls.Add(overlayUseSparseDeltaCheckBox);

            var focusLbl = new Label { Text = "Focus %:", Location = new Point(172, y + 2), Width = 48, Height = 18 };
            HolographicTheme.ApplyToLabel(focusLbl);
            comfySettingsPanel.Controls.Add(focusLbl);

            overlayFocusNumeric = new NumericUpDown
            {
                Location = new Point(224, y),
                Width = 46,
                Minimum = 10,
                Maximum = 100,
                Value = 70
            };
            HolographicTheme.ApplyToNumericUpDown(overlayFocusNumeric);
            comfySettingsPanel.Controls.Add(overlayFocusNumeric);
            y += 24;

            var featherLbl = new Label { Text = "Feather:", Location = new Point(10, y + 2), Width = 48, Height = 18 };
            HolographicTheme.ApplyToLabel(featherLbl);
            comfySettingsPanel.Controls.Add(featherLbl);
            overlayFeatherNumeric = new NumericUpDown
            {
                Location = new Point(62, y),
                Width = 52,
                Minimum = 0,
                Maximum = 256,
                Value = 36
            };
            HolographicTheme.ApplyToNumericUpDown(overlayFeatherNumeric);
            comfySettingsPanel.Controls.Add(overlayFeatherNumeric);

            var diffLbl = new Label { Text = "Diff:", Location = new Point(120, y + 2), Width = 32, Height = 18 };
            HolographicTheme.ApplyToLabel(diffLbl);
            comfySettingsPanel.Controls.Add(diffLbl);
            overlayDiffThresholdNumeric = new NumericUpDown
            {
                Location = new Point(154, y),
                Width = 48,
                Minimum = 0,
                Maximum = 255,
                Value = 8
            };
            HolographicTheme.ApplyToNumericUpDown(overlayDiffThresholdNumeric);
            comfySettingsPanel.Controls.Add(overlayDiffThresholdNumeric);

            var alphaLbl = new Label { Text = "Alpha:", Location = new Point(208, y + 2), Width = 36, Height = 18 };
            HolographicTheme.ApplyToLabel(alphaLbl);
            comfySettingsPanel.Controls.Add(alphaLbl);
            overlayAlphaScaleNumeric = new NumericUpDown
            {
                Location = new Point(244, y),
                Width = 50,
                Minimum = 0,
                Maximum = 300,
                DecimalPlaces = 0,
                Value = 140,
                Increment = 5
            };
            HolographicTheme.ApplyToNumericUpDown(overlayAlphaScaleNumeric);
            comfySettingsPanel.Controls.Add(overlayAlphaScaleNumeric);
            y += 28;

            var showOverlayCheckBox = new CheckBox
            {
                Text = "Show Land Overlay Layer",
                Location = new Point(10, y),
                Width = 200,
                Height = 20,
                Checked = true
            };
            showOverlayCheckBox.CheckedChanged += (s, e) =>
            {
                showOverlayPlanes = showOverlayCheckBox.Checked;
                GenerateMapImage();
            };
            HolographicTheme.ApplyToCheckBox(showOverlayCheckBox);
            comfySettingsPanel.Controls.Add(showOverlayCheckBox);
            y += 24;

            var clearOverlayAreaButton = new Button
            {
                Text = "Clear Overlay in Selection",
                Location = new Point(10, y),
                Width = 260,
                Height = 24
            };
            clearOverlayAreaButton.Click += (s, e) => ClearOverlayAreaInSelection();
            HolographicTheme.ApplyToButton(clearOverlayAreaButton, ButtonStyle.Warning);
            comfySettingsPanel.Controls.Add(clearOverlayAreaButton);
            y += 28;

            var removeOverlaySelectionButton = new Button
            {
                Text = "Remove Overlay Planes in Selection",
                Location = new Point(10, y),
                Width = 260,
                Height = 24
            };
            removeOverlaySelectionButton.Click += (s, e) => RemoveOverlaysInSelection();
            HolographicTheme.ApplyToButton(removeOverlaySelectionButton, ButtonStyle.Warning);
            comfySettingsPanel.Controls.Add(removeOverlaySelectionButton);
            y += 28;

            var clearAllOverlaysButton = new Button
            {
                Text = "Clear All Overlays",
                Location = new Point(10, y),
                Width = 260,
                Height = 24
            };
            clearAllOverlaysButton.Click += (s, e) => ClearAllOverlays();
            HolographicTheme.ApplyToButton(clearAllOverlaysButton, ButtonStyle.Danger);
            comfySettingsPanel.Controls.Add(clearAllOverlaysButton);
            y += 32;

            var restoreSelectedButton = new Button
            {
                Text = "Restore Selected",
                Location = new Point(10, y),
                Width = 125,
                Height = 26
            };
            restoreSelectedButton.Click += (s, e) => RestoreOriginalButton_Click(s, e);
            HolographicTheme.ApplyToButton(restoreSelectedButton, ButtonStyle.Warning);
            comfySettingsPanel.Controls.Add(restoreSelectedButton);
            y += 35;

            // === Biome Brush Section ===
            var biomeSeparator = new Label
            {
                Location = new Point(10, y),
                Width = ctrlWidth,
                Height = 1,
                BackColor = HolographicTheme.BorderCyan
            };
            comfySettingsPanel.Controls.Add(biomeSeparator);
            y += 10;

            var biomeSectionTitle = new Label
            {
                Text = "Biome Brushes",
                Location = new Point(10, y),
                Width = ctrlWidth,
                Height = 25,
                Font = new Font(this.Font.FontFamily, 10, FontStyle.Bold)
            };
            HolographicTheme.ApplyToLabel(biomeSectionTitle);
            comfySettingsPanel.Controls.Add(biomeSectionTitle);
            y += 24;

            sampleBiomeButton = new Button
            {
                Text = "Sample Biome",
                Location = new Point(10, y),
                Width = 125,
                Height = 28
            };
            sampleBiomeButton.Click += (s, e) => SampleBiome();
            HolographicTheme.ApplyToButton(sampleBiomeButton, ButtonStyle.Accent);
            comfySettingsPanel.Controls.Add(sampleBiomeButton);

            applyBiomeButton = new Button
            {
                Text = "Apply Biome",
                Location = new Point(145, y),
                Width = 125,
                Height = 28,
                Enabled = false
            };
            applyBiomeButton.Click += (s, e) => ApplyBiome();
            HolographicTheme.ApplyToButton(applyBiomeButton, ButtonStyle.Success);
            comfySettingsPanel.Controls.Add(applyBiomeButton);
            y += 34;

            biomeListBox = new ListBox
            {
                Location = new Point(10, y),
                Width = ctrlWidth,
                Height = 80
            };
            biomeListBox.SelectedIndexChanged += (s, e) =>
            {
                selectedBiomeBrush = biomeListBox.SelectedItem as Biome.BiomeBrush;
                UpdateBiomeButtons();
            };
            HolographicTheme.ApplyToListBox(biomeListBox);
            comfySettingsPanel.Controls.Add(biomeListBox);
            y += 85;

            biomeInfoLabel = new Label
            {
                Text = "No biome selected",
                Location = new Point(10, y),
                Width = ctrlWidth - 80,
                Height = 18,
                ForeColor = Color.Gray,
                Font = new Font(this.Font.FontFamily, 7.5f)
            };
            comfySettingsPanel.Controls.Add(biomeInfoLabel);

            deleteBiomeButton = new Button
            {
                Text = "Delete",
                Location = new Point(ctrlWidth - 60, y - 2),
                Width = 60,
                Height = 22,
                Enabled = false
            };
            deleteBiomeButton.Click += (s, e) => DeleteSelectedBiome();
            HolographicTheme.ApplyToButton(deleteBiomeButton, ButtonStyle.Danger);
            comfySettingsPanel.Controls.Add(deleteBiomeButton);
            y += 26;

            // Paint mode controls
            biomePaintModeCheckBox = new CheckBox
            {
                Text = "Paint Mode",
                Location = new Point(10, y),
                Width = 100,
                Height = 20,
                Enabled = false
            };
            biomePaintModeCheckBox.CheckedChanged += (s, e) =>
            {
                isBiomePaintMode = biomePaintModeCheckBox.Checked;
                mapPictureBox.Cursor = isBiomePaintMode ? Cursors.Cross : Cursors.Hand;
                if (statusLabel != null)
                    statusLabel.Text = isBiomePaintMode
                        ? $"Paint Mode: Click/drag to paint with '{selectedBiomeBrush?.Name}'"
                        : "Paint mode disabled";
            };
            HolographicTheme.ApplyToCheckBox(biomePaintModeCheckBox);
            comfySettingsPanel.Controls.Add(biomePaintModeCheckBox);

            var brushRadiusLabel = new Label
            {
                Text = "Radius:",
                Location = new Point(115, y + 2),
                Width = 45,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(brushRadiusLabel);
            comfySettingsPanel.Controls.Add(brushRadiusLabel);

            biomeBrushRadiusNumeric = new NumericUpDown
            {
                Location = new Point(165, y),
                Width = 50,
                Minimum = 1,
                Maximum = 25,
                Value = 5
            };
            biomeBrushRadiusNumeric.ValueChanged += (s, e) =>
            {
                biomeBrushRadius = (int)biomeBrushRadiusNumeric.Value;
            };
            HolographicTheme.ApplyToNumericUpDown(biomeBrushRadiusNumeric);
            comfySettingsPanel.Controls.Add(biomeBrushRadiusNumeric);

            biomeReplaceStaticsCheckBox = new CheckBox
            {
                Text = "Replace Statics",
                Location = new Point(220, y),
                Width = 110,
                Height = 20,
                Checked = true
            };
            HolographicTheme.ApplyToCheckBox(biomeReplaceStaticsCheckBox);
            comfySettingsPanel.Controls.Add(biomeReplaceStaticsCheckBox);
            y += 30;

            var biomeHelpLabel = new Label
            {
                Text = "Select tiles ? Sample | Select target ? Apply\nPaint Mode: click/drag to paint biome on map",
                Location = new Point(10, y),
                Width = ctrlWidth,
                Height = 30,
                ForeColor = Color.Gray,
                Font = new Font(this.Font.FontFamily, 7.5f)
            };
            comfySettingsPanel.Controls.Add(biomeHelpLabel);
            y += 35;

            // === Save Map Section ===
            var saveSeparator = new Label
            {
                Location = new Point(10, y),
                Width = ctrlWidth,
                Height = 1,
                BackColor = HolographicTheme.BorderCyan
            };
            comfySettingsPanel.Controls.Add(saveSeparator);
            y += 10;

            var saveSectionTitle = new Label
            {
                Text = "Save Map",
                Location = new Point(10, y),
                Width = ctrlWidth,
                Height = 25,
                Font = new Font(this.Font.FontFamily, 10, FontStyle.Bold)
            };
            HolographicTheme.ApplyToLabel(saveSectionTitle);
            comfySettingsPanel.Controls.Add(saveSectionTitle);
            y += 24;

            // Save status label
            saveStatusLabel = new Label
            {
                Text = "No unsaved changes",
                Location = new Point(10, y),
                Width = ctrlWidth,
                Height = 18,
                ForeColor = HolographicTheme.TextSecondary,
                Font = new Font(this.Font.FontFamily, 7.5f)
            };
            comfySettingsPanel.Controls.Add(saveStatusLabel);
            y += 22;

            // Save Map button -> saves to iteration folder
            saveMapButton = new Button
            {
                Text = "Save Map",
                Location = new Point(10, y),
                Width = 85,
                Height = 28,
                Enabled = false
            };
            saveMapButton.Click += (s, e) => SaveMapIteration();
            HolographicTheme.ApplyToButton(saveMapButton, ButtonStyle.Success);
            comfySettingsPanel.Controls.Add(saveMapButton);

            // Load Save button
            loadSaveButton = new Button
            {
                Text = "Load Save",
                Location = new Point(100, y),
                Width = 85,
                Height = 28
            };
            loadSaveButton.Click += (s, e) => LoadSavedIteration();
            HolographicTheme.ApplyToButton(loadSaveButton, ButtonStyle.Accent);
            comfySettingsPanel.Controls.Add(loadSaveButton);

            // Deploy to UO folder button
            deployButton = new Button
            {
                Text = "Deploy",
                Location = new Point(190, y),
                Width = 80,
                Height = 28,
                Enabled = false
            };
            deployButton.Click += (s, e) => DeployToGameFolder();
            HolographicTheme.ApplyToButton(deployButton, ButtonStyle.Warning);
            comfySettingsPanel.Controls.Add(deployButton);
            y += 34;

            // Saved iterations list
            savesListBox = new ListBox
            {
                Location = new Point(10, y),
                Width = ctrlWidth,
                Height = 70
            };
            savesListBox.SelectedIndexChanged += (s, e) =>
            {
                if (deleteSaveButton != null)
                    deleteSaveButton.Enabled = savesListBox.SelectedItem != null;
            };
            HolographicTheme.ApplyToListBox(savesListBox);
            comfySettingsPanel.Controls.Add(savesListBox);
            y += 75;

            // Delete save + Open folder buttons
            deleteSaveButton = new Button
            {
                Text = "Delete",
                Location = new Point(10, y),
                Width = 60,
                Height = 22,
                Enabled = false
            };
            deleteSaveButton.Click += (s, e) => DeleteSelectedSave();
            HolographicTheme.ApplyToButton(deleteSaveButton, ButtonStyle.Danger);
            comfySettingsPanel.Controls.Add(deleteSaveButton);

            var openFolderButton = new Button
            {
                Text = "Open Folder",
                Location = new Point(75, y),
                Width = 85,
                Height = 22
            };
            openFolderButton.Click += (s, e) => OpenSavesFolder();
            HolographicTheme.ApplyToButton(openFolderButton);
            comfySettingsPanel.Controls.Add(openFolderButton);
            y += 30;

            // === Art Changes Sub-Section ===
            var artSeparator = new Label
            {
                Location = new Point(10, y),
                Width = ctrlWidth,
                Height = 1,
                BackColor = Color.FromArgb(60, HolographicTheme.BorderCyan)
            };
            comfySettingsPanel.Controls.Add(artSeparator);
            y += 8;

            pendingChangesLabel = new Label
            {
                Text = "No pending art changes",
                Location = new Point(10, y),
                Width = ctrlWidth,
                Height = 18,
                ForeColor = HolographicTheme.TextSecondary,
                Font = new Font(this.Font.FontFamily, 7.5f)
            };
            comfySettingsPanel.Controls.Add(pendingChangesLabel);
            y += 22;

            saveToMulButton = new Button
            {
                Text = "Save Art",
                Location = new Point(10, y),
                Width = 85,
                Height = 24,
                Enabled = false
            };
            saveToMulButton.Click += (s, e) => SaveChangesToMul();
            HolographicTheme.ApplyToButton(saveToMulButton, ButtonStyle.Success);
            comfySettingsPanel.Controls.Add(saveToMulButton);

            var pushToJarJarButton = new Button
            {
                Text = "Push to JarJar",
                Location = new Point(10, y + 28),
                Width = 175,
                Height = 24,
                Enabled = false
            };
            pushToJarJarButton.FlatStyle = FlatStyle.Flat;
            pushToJarJarButton.BackColor = Color.FromArgb(80, 0, 140);
            pushToJarJarButton.ForeColor = HolographicTheme.TextPrimary;
            pushToJarJarButton.Font = new Font(this.Font.FontFamily, 7.5f, FontStyle.Bold);
            pushToJarJarButton.FlatAppearance.BorderColor = Color.FromArgb(160, 0, 255);
            pushToJarJarButton.FlatAppearance.BorderSize = 1;
            pushToJarJarButton.Enabled = AppConfig.Instance.JarJarPushEnabled;
            pushToJarJarButton.Click += async (s, e) => await PushToJarJar();
            this._pushToJarJarButton = pushToJarJarButton;
            comfySettingsPanel.Controls.Add(pushToJarJarButton);

            var pullFromJarJarButton = new Button
            {
                Text = "Pull U/O",
                Location = new Point(192, y + 28),
                Width = 118,
                Height = 24
            };
            pullFromJarJarButton.FlatStyle = FlatStyle.Flat;
            pullFromJarJarButton.BackColor = Color.FromArgb(0, 80, 140);
            pullFromJarJarButton.ForeColor = HolographicTheme.TextPrimary;
            pullFromJarJarButton.Font = new Font(this.Font.FontFamily, 7.5f, FontStyle.Bold);
            pullFromJarJarButton.FlatAppearance.BorderColor = Color.FromArgb(0, 160, 255);
            pullFromJarJarButton.FlatAppearance.BorderSize = 1;
            pullFromJarJarButton.Visible = AppConfig.Instance.JarJarPushEnabled;
            pullFromJarJarButton.Click += async (s, e) => await PullMapLayersFromJarJar();
            comfySettingsPanel.Controls.Add(pullFromJarJarButton);

            _autoPushJarJarCheckBox = new CheckBox
            {
                Text = "Auto-push after AI gen",
                Location = new Point(10, y + 28 + 26),
                Width = 175,
                Height = 18,
                ForeColor = Color.FromArgb(180, 100, 255),
                Font = new Font(this.Font.FontFamily, 7f),
                Checked = AppConfig.Instance.JarJarAutoPush && AppConfig.Instance.JarJarPushEnabled,
                Enabled = AppConfig.Instance.JarJarPushEnabled
            };
            _autoPushJarJarCheckBox.CheckedChanged += (s, e) =>
            {
                AppConfig.Instance.JarJarAutoPush = _autoPushJarJarCheckBox.Checked;
                AppConfig.Instance.Save();
            };
            comfySettingsPanel.Controls.Add(_autoPushJarJarCheckBox);

            var discardChangesButton = new Button
            {
                Text = "Discard Art",
                Location = new Point(100, y),
                Width = 85,
                Height = 24
            };
            discardChangesButton.Click += (s, e) => DiscardPendingChanges();
            HolographicTheme.ApplyToButton(discardChangesButton, ButtonStyle.Danger);
            comfySettingsPanel.Controls.Add(discardChangesButton);
            y += 32;

            var saveHelpLabel = new Label
            {
                Text = "Save Map = iteration to AppData\nDeploy = overwrite UO folder (with backup)",
                Location = new Point(10, y),
                Width = ctrlWidth,
                Height = 28,
                ForeColor = Color.Gray,
                Font = new Font(this.Font.FontFamily, 7.5f)
            };
            comfySettingsPanel.Controls.Add(saveHelpLabel);
            y += 35;

            this.Controls.Add(comfySettingsPanel);

            // Handle form closing to check for unsaved changes
            this.FormClosing += (s, e) => CheckUnsavedChangesOnClose(e);
        }

        private async void RefreshCheckpoints_Click(object sender, EventArgs e)
        {
            if (refreshCheckpointsButton != null)
                refreshCheckpointsButton.Enabled = false;
            
            try
            {
                var client = new MeesaMultisMaker.ComfyUI.ComfyUIClient(comfyUrlTextBox.Text);
                var checkpoints = await client.GetAvailableCheckpoints();
                
                if (checkpoints != null && checkpoints.Count > 0)
                {
                    string currentSelection = checkpointComboBox?.SelectedItem?.ToString();
                    checkpointComboBox.Items.Clear();
                    
                    foreach (var cp in checkpoints)
                    {
                        checkpointComboBox.Items.Add(cp);
                    }

                    // Try to restore selection
                    if (!string.IsNullOrEmpty(currentSelection))
                    {
                        int idx = checkpointComboBox.Items.IndexOf(currentSelection);
                        if (idx >= 0)
                        {
                            checkpointComboBox.SelectedIndex = idx;
                        }
                        else
                        {
                            // Default to dreamshaper if available, else first item
                            int defaultIdx = checkpointComboBox.Items.IndexOf(MeesaMultisMaker.ComfyUI.Text2ImageWorkflow.DEFAULT_CHECKPOINT);
                            checkpointComboBox.SelectedIndex = defaultIdx >= 0 ? defaultIdx : 0;
                        }
                    }
                    else
                    {
                        int defaultIdx = checkpointComboBox.Items.IndexOf(MeesaMultisMaker.ComfyUI.Text2ImageWorkflow.DEFAULT_CHECKPOINT);
                        checkpointComboBox.SelectedIndex = defaultIdx >= 0 ? defaultIdx : 0;
                    }
                    
                    statusLabel.Text = $"Loaded {checkpoints.Count} checkpoints from ComfyUI";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refreshing checkpoints: {ex.Message}");
                statusLabel.Text = "Failed to load checkpoints";
            }
            finally
            {
                if (refreshCheckpointsButton != null)
                    refreshCheckpointsButton.Enabled = true;
            }
        }

        private async void TestConnection_Click(object sender, EventArgs e)
        {
            testConnectionButton.Enabled = false;
            testConnectionButton.Text = "Testing...";

            try
            {
                comfyClient = new MeesaMultisMaker.ComfyUI.ComfyUIClient(comfyUrlTextBox.Text);
                bool connected = await comfyClient.TestConnection();

                if (connected)
                {
                    MessageBox.Show($"? Successfully connected to ComfyUI at {comfyUrlTextBox.Text}",
                        "Connection Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show($"? Could not connect to ComfyUI at {comfyUrlTextBox.Text}\n\nPlease check that ComfyUI is running.",
                        "Connection Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error testing connection: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                testConnectionButton.Enabled = true;
                testConnectionButton.Text = "Test";
            }
        }

        private void RestoreOriginalButton_Click(object sender, EventArgs e)
        {
            if (selectionTargetType == SelectionTargetType.Statics)
            {
                // Restore statics
                if (selectedStatics.Count == 0)
                {
                    MessageBox.Show("No statics selected!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var result = MessageBox.Show(
                    $"Restore {selectedStatics.Count} selected static(s) to original artwork?\n\n" +
                    "This will reload the original textures from art.mul.",
                    "Confirm Restore",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    var staticGroups = selectedStatics.GroupBy(s => s.ItemId).ToList();
                    foreach (var group in staticGroups)
                    {
                        RestoreStaticOriginalArt(group.Key);
                    }

                    GenerateMapImage();
                    MessageBox.Show($"Restored {selectedStatics.Count} static(s) to original artwork!",
                        "Restore Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            else
            {
                // Restore land tiles
                if (selectedTiles.Count == 0)
                {
                    MessageBox.Show("No tiles selected!", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var result = MessageBox.Show(
                    $"Restore {selectedTiles.Count} selected tile(s) to original artwork?\n\n" +
                    "This will reload the original textures from art.mul.",
                    "Confirm Restore",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    var tileGroups = selectedTiles.GroupBy(t => t.TileId).ToList();
                    foreach (var group in tileGroups)
                    {
                        RestoreTileOriginalArt(group.Key);
                    }

                    GenerateMapImage();
                    MessageBox.Show($"Restored {selectedTiles.Count} tile(s) to original artwork!",
                        "Restore Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }
    }
}
