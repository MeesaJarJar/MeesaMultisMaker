using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MeesaMultisMaker.Utils;

namespace MeesaMultisMaker.Controls
{
    /// <summary>Which desaturate list an armed eyedropper pick goes into.</summary>
    public enum DesaturatePickKind
    {
        Targets,
        Protected
    }

    /// <summary>Carries the pick-kind for an armed eyedropper request.</summary>
    public class EyedropperRequestedEventArgs : EventArgs
    {
        public DesaturatePickKind Kind { get; set; } = DesaturatePickKind.Targets;
    }

    public class ImageEditingPanel : UserControl
    {
        private Panel headerPanel;
        private Panel contentPanel;
        private Button collapseButton;
        private bool _isCollapsed = false;
        private int _expandedWidth = 280;

        private TrackBar brightnessTrackBar;
        private Label brightnessValueLabel;
        private TrackBar contrastTrackBar;
        private Label contrastValueLabel;

        private TrackBar hueTrackBar;
        private Label hueValueLabel;
        private TrackBar saturationTrackBar;
        private Label saturationValueLabel;

        // Background removal controls
        private CheckBox removeBackgroundCheckBox;
        private ComboBox backgroundModeComboBox;
        private TrackBar thresholdTrackBar;
        private Label thresholdValueLabel;
        private Panel colorPickerPanel;
        private Label colorPickerLabel;

        private NumericUpDown pixelSizeUpDown;
        private CheckBox pixelizeCheckBox;

        private ComboBox paletteReductionComboBox;
        private CheckBox paletteReductionCheckBox;

        // New UO-style effect controls
        private TrackBar noiseTrackBar;
        private Label noiseValueLabel;
        private TrackBar ditherTrackBar;
        private Label ditherValueLabel;
        private TrackBar edgeDarkenTrackBar;
        private Label edgeDarkenValueLabel;
        private TrackBar colorBandsTrackBar;
        private Label colorBandsValueLabel;

        // Partial-hue prep controls (desaturate color ranges + near-gray snap)
        private CheckBox desaturateCheckBox;
        private ListBox desatTargetsListBox;
        private Label desatTargetsCountLabel;
        private Button desatPickButton;
        private Button desatRemoveButton;
        private Button desatClearButton;
        private TrackBar desatToleranceTrackBar;
        private Label desatToleranceValueLabel;
        private Label desatMatchCountLabel;
        private readonly List<Color> _desatTargets = new List<Color>();
        // Protect list (negative targets): never desaturated, even within tolerance of a target.
        private ListBox desatProtectListBox;
        private Label desatProtectCountLabel;
        private Button desatProtectPickButton;
        private Button desatProtectRemoveButton;
        private Button desatProtectClearButton;
        private readonly List<Color> _desatProtected = new List<Color>();
        // Despeckle (min blob size): drops matched blobs smaller than N px.
        private TrackBar despeckleTrackBar;
        private Label despeckleValueLabel;
        private CheckBox snapNearGrayCheckBox;
        private TrackBar snapToleranceTrackBar;
        private Label snapToleranceValueLabel;

        // SAM Select gate (spatial mask for partial-hue prep).
        private Button samSelectButton;
        private Button samClearButton;
        private CheckBox showSamCheckBox;
        private Label samInfoLabel;

        private CheckBox fillHolesCheckBox;

        private CheckBox realTimePreviewCheckBox;

        private Button applyButton;
        private Button resetButton;
        private Button applyToAllSelectedButton;

        private Label statusLabel;

        public event EventHandler ApplyClicked;
        public event EventHandler ResetClicked;
        public event EventHandler ApplyToAllSelectedClicked;
        public event EventHandler EffectsChanged;

        /// <summary>
        /// Fired when the user arms the desaturate eyedropper (swatch/Pick click)
        /// and a canvas host is available. Hosts sample the next canvas click.
        /// If no host subscribes, the panel falls back to a ColorDialog.
        /// Carries which list (Targets vs Protected) the pick goes into.
        /// </summary>
        public event EventHandler<EyedropperRequestedEventArgs> EyedropperRequested;
        /// <summary>Fired when an armed eyedropper is cancelled (Esc / re-click).</summary>
        public event EventHandler EyedropperCancelled;
        /// <summary>Fired when SAM point-picking is armed. Hosts begin SAM clicks.</summary>
        public event EventHandler SamSelectRequested;
        /// <summary>Fired when SAM arming is cancelled (Esc / re-click / eyedropper). Keeps last mask.</summary>
        public event EventHandler SamSelectCancelled;
        /// <summary>Fired when Clear SAM is pressed. Hosts drop mask + points.</summary>
        public event EventHandler SamClearRequested;
        /// <summary>Fired when Show SAM toggle changes. Hosts invalidate paint.</summary>
        public event EventHandler SamShowChanged;

        public float Brightness => ((brightnessTrackBar?.Value ?? 50) - 50) / 50f;
        public float Contrast => ((contrastTrackBar?.Value ?? 50) - 50) / 50f;
        public float Hue => hueTrackBar?.Value ?? 0;
        public float Saturation => ((saturationTrackBar?.Value ?? 50) - 50) / 50f;
        public int PixelSize => (int)(pixelSizeUpDown?.Value ?? 1);
        public bool PixelizeEnabled => pixelizeCheckBox?.Checked ?? false;
        public int PaletteColors => (paletteReductionCheckBox?.Checked == true && paletteReductionComboBox?.SelectedItem != null) ? int.Parse(paletteReductionComboBox.SelectedItem.ToString()) : 0;
        public bool RealTimePreview => realTimePreviewCheckBox?.Checked ?? false;
        public bool FillHoles => fillHolesCheckBox?.Checked ?? false;

        // New UO-style effect properties
        public int NoiseIntensity => noiseTrackBar?.Value ?? 0;
        public int DitherLevels => (ditherTrackBar?.Value ?? 0) > 1 ? ditherTrackBar.Value : 0;
        public int EdgeDarkening => edgeDarkenTrackBar?.Value ?? 0;
        public int ColorBands => (colorBandsTrackBar?.Value ?? 0) > 1 ? colorBandsTrackBar.Value : 0;

        // Partial-hue prep properties (exact-gray authoring for UO partial hues)
        // The effect applies only when the checkbox is on AND at least one
        // target color has been picked.
        public bool DesaturateEnabled => (desaturateCheckBox?.Checked ?? false) && _desatTargets.Count > 0;
        /// <summary>All picked target colors (read-only view).</summary>
        public IReadOnlyList<Color> DesaturateTargets => _desatTargets.AsReadOnly();
        /// <summary>All picked protected colors (never desaturated; read-only view).</summary>
        public IReadOnlyList<Color> DesaturateProtected => _desatProtected.AsReadOnly();
        /// <summary>First-target convenience (Color.Red when the list is empty).</summary>
        public Color DesaturateColor => _desatTargets.Count > 0 ? _desatTargets[0] : Color.Red;
        /// <summary>True while the desaturate eyedropper is armed (waiting for a canvas pick).</summary>
        public bool IsEyedropperArmed { get; private set; }
        /// <summary>Which list the armed eyedropper feeds (Targets vs Protected). Only one armed at a time.</summary>
        public DesaturatePickKind ArmedPickKind { get; private set; } = DesaturatePickKind.Targets;
        /// <summary>True while SAM point-picking is armed. Separate from the eyedropper; only one armed at a time.</summary>
        public bool IsSamArmed { get; private set; }
        /// <summary>Show SAM mask overlay (paint-time only). Default true.</summary>
        public bool ShowSam => showSamCheckBox?.Checked ?? true;
        public int DesaturateTolerance => desatToleranceTrackBar?.Value ?? 30;
        /// <summary>Despeckle min blob size 0-16 (0 = off).</summary>
        public int DespeckleMinSize => despeckleTrackBar?.Value ?? 0;
        public bool SnapNearGrayEnabled => snapNearGrayCheckBox?.Checked ?? false;
        public int SnapTolerance => snapToleranceTrackBar?.Value ?? 8;

        // Background removal properties
        public bool RemoveBackgroundEnabled => removeBackgroundCheckBox?.Checked ?? false;
        public string BackgroundMode => backgroundModeComboBox?.SelectedItem?.ToString() ?? "Auto";
        public int BackgroundThreshold => thresholdTrackBar?.Value ?? 30;
        public Color BackgroundColor { get; private set; } = Color.White;

        public ImageEditingPanel()
        {
            InitializeComponent();
        }

        public void SetStatus(string message, Color? color = null)
        {
            if (statusLabel.InvokeRequired)
            {
                statusLabel.Invoke(new Action(() => SetStatus(message, color)));
                return;
            }
            statusLabel.Text = message;
            statusLabel.ForeColor = color ?? HolographicTheme.TextPrimary;
        }

        public string StatusText => statusLabel?.Text ?? string.Empty;

        public void ResetToDefaults()
        {
            if (brightnessTrackBar != null) brightnessTrackBar.Value = 50;
            if (contrastTrackBar != null) contrastTrackBar.Value = 50;
            if (hueTrackBar != null) hueTrackBar.Value = 0;
            if (saturationTrackBar != null) saturationTrackBar.Value = 50;
            if (pixelSizeUpDown != null) pixelSizeUpDown.Value = 1;
            if (pixelizeCheckBox != null) pixelizeCheckBox.Checked = false;
            if (paletteReductionCheckBox != null) paletteReductionCheckBox.Checked = false;
            if (paletteReductionComboBox != null && paletteReductionComboBox.Items.Count > 0) paletteReductionComboBox.SelectedIndex = 0;
            if (noiseTrackBar != null) noiseTrackBar.Value = 0;
            if (ditherTrackBar != null) ditherTrackBar.Value = 0;
            if (edgeDarkenTrackBar != null) edgeDarkenTrackBar.Value = 0;
            if (colorBandsTrackBar != null) colorBandsTrackBar.Value = 0;
            if (desaturateCheckBox != null) desaturateCheckBox.Checked = false;
            if (desatToleranceTrackBar != null) desatToleranceTrackBar.Value = 30;
            if (despeckleTrackBar != null) despeckleTrackBar.Value = 0;
            _desatTargets.Clear();
            _desatProtected.Clear();
            IsEyedropperArmed = false;
            ArmedPickKind = DesaturatePickKind.Targets;
            IsSamArmed = false;
            UpdateDesatTargetsDisplay();
            UpdateDesatProtectDisplay();
            UpdateEyedropperUI();
            UpdateSamUI();
            SetMatchCount(0);
            SetSamInfo(0, false, false);
            if (snapNearGrayCheckBox != null) snapNearGrayCheckBox.Checked = false;
            if (snapToleranceTrackBar != null) snapToleranceTrackBar.Value = 8;
            if (fillHolesCheckBox != null) fillHolesCheckBox.Checked = false;
            if (removeBackgroundCheckBox != null)
            {
                removeBackgroundCheckBox.Checked = false;
                backgroundModeComboBox.SelectedIndex = 0;
                thresholdTrackBar.Value = 30;
                BackgroundColor = Color.White;
                UpdateColorPickerDisplay();
            }
        }

        private void InitializeComponent()
        {
            this.BackColor = HolographicTheme.ControlBackground;

            headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 32,
                
                BackColor = HolographicTheme.ControlBackground,
                Padding = new Padding(8, 5, 5, 5)
            };

            var titleLabel = new Label
            {
                Text = "IMAGE EDITING",
                ForeColor = HolographicTheme.CyanAccent,
                Font = new Font("Consolas", 10, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            collapseButton = new Button
            {
                Text = "<",
                Width = 26,
                Height = 24,
                Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat,
                ForeColor = HolographicTheme.CyanAccent,
                BackColor = HolographicTheme.ButtonBackground,
                Font = new Font(this.Font.FontFamily, 9, FontStyle.Bold)
            };
            collapseButton.FlatAppearance.BorderColor = HolographicTheme.BorderCyan;
            collapseButton.FlatAppearance.BorderSize = 1;
            collapseButton.Click += CollapseButton_Click;

            headerPanel.Controls.Add(titleLabel);
            headerPanel.Controls.Add(collapseButton);

            contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(8),
                BackColor = HolographicTheme.PanelBackground
            };

            int y = 5;

            realTimePreviewCheckBox = new CheckBox
            {
                Text = "Real-time Preview",
                Left = 10,
                Top = y,
                Width = 160,
                Checked = true,
                ForeColor = HolographicTheme.CyanAccent,
                BackColor = Color.Transparent,
                Font = new Font("Consolas", 9, FontStyle.Bold)
            };
            realTimePreviewCheckBox.CheckedChanged += (s, e) =>
            {
                if (realTimePreviewCheckBox.Checked)
                {
                    OnEffectsChanged();
                }
            };
            contentPanel.Controls.Add(realTimePreviewCheckBox);
            y += 35;

            // === BASIC ADJUSTMENTS SECTION ===
            var basicHeader = CreateSectionHeader("BASIC ADJUSTMENTS", 10, y);
            contentPanel.Controls.Add(basicHeader);
            y += 25;

            var brightnessLabel = CreateThemedLabel("Brightness", 10, y, 120);
            contentPanel.Controls.Add(brightnessLabel);
            y += 20;

            brightnessTrackBar = CreateThemedTrackBar(10, y, 195, 0, 100, 50);
            brightnessTrackBar.ValueChanged += (s, e) =>
            {
                brightnessValueLabel.Text = $"{(brightnessTrackBar.Value - 50):+0;-0;0}";
                OnEffectsChanged();
            };
            contentPanel.Controls.Add(brightnessTrackBar);

            brightnessValueLabel = CreateValueLabel(210, y + 5, 50);
            brightnessValueLabel.Text = "0";
            contentPanel.Controls.Add(brightnessValueLabel);
            y += 50; // Extra padding after Brightness

            var contrastLabel = CreateThemedLabel("Contrast", 10, y, 120);
            contentPanel.Controls.Add(contrastLabel);
            y += 20;

            contrastTrackBar = CreateThemedTrackBar(10, y, 195, 0, 100, 50);
            contrastTrackBar.ValueChanged += (s, e) =>
            {
                contrastValueLabel.Text = $"{(contrastTrackBar.Value - 50):+0;-0;0}";
                OnEffectsChanged();
            };
            contentPanel.Controls.Add(contrastTrackBar);

            contrastValueLabel = CreateValueLabel(210, y + 5, 50);
            contrastValueLabel.Text = "0";
            contentPanel.Controls.Add(contrastValueLabel);
            y += 40;

            // === COLOR SECTION ===
            contentPanel.Controls.Add(CreateSeparator(10, y, 250));
            y += 10;
            var colorHeader = CreateSectionHeader("COLOR", 10, y);
            contentPanel.Controls.Add(colorHeader);
            y += 25;

            var hueLabel = CreateThemedLabel("Hue Shift", 10, y, 120);
            contentPanel.Controls.Add(hueLabel);
            y += 20;

            hueTrackBar = CreateThemedTrackBar(10, y, 195, 0, 360, 0);
            hueTrackBar.ValueChanged += (s, e) =>
            {
                hueValueLabel.Text = $"{hueTrackBar.Value}";
                OnEffectsChanged();
            };
            contentPanel.Controls.Add(hueTrackBar);

            hueValueLabel = CreateValueLabel(210, y + 5, 50);
            hueValueLabel.Text = "0";
            contentPanel.Controls.Add(hueValueLabel);
            y += 50; // Extra padding after Hue Shift

            var saturationLabel = CreateThemedLabel("Saturation", 10, y, 120);
            contentPanel.Controls.Add(saturationLabel);
            y += 20;

            saturationTrackBar = CreateThemedTrackBar(10, y, 195, 0, 100, 50);
            saturationTrackBar.ValueChanged += (s, e) =>
            {
                saturationValueLabel.Text = $"{(saturationTrackBar.Value - 50):+0;-0;0}";
                OnEffectsChanged();
            };
            contentPanel.Controls.Add(saturationTrackBar);

            saturationValueLabel = CreateValueLabel(210, y + 5, 50);
            saturationValueLabel.Text = "0";
            contentPanel.Controls.Add(saturationValueLabel);
            y += 40;

            // === BACKGROUND REMOVAL SECTION ===
            contentPanel.Controls.Add(CreateSeparator(10, y, 250));
            y += 10;
            var bgRemovalHeader = CreateSectionHeader("BACKGROUND REMOVAL", 10, y);
            contentPanel.Controls.Add(bgRemovalHeader);
            y += 25;

            // Enable checkbox
            removeBackgroundCheckBox = new CheckBox
            {
                Text = "Remove Background",
                Left = 10,
                Top = y,
                Width = 180,
                Checked = false,
                ForeColor = HolographicTheme.TextSecondary,
                BackColor = Color.Transparent
            };
            removeBackgroundCheckBox.CheckedChanged += (s, e) =>
            {
                bool enabled = removeBackgroundCheckBox.Checked;
                backgroundModeComboBox.Enabled = enabled;
                thresholdTrackBar.Enabled = enabled;
                colorPickerPanel.Enabled = enabled;
                OnEffectsChanged();
            };
            contentPanel.Controls.Add(removeBackgroundCheckBox);
            y += 30;

            // Mode selection
            var modeLabel = CreateThemedLabel("Mode:", 10, y + 3, 50);
            contentPanel.Controls.Add(modeLabel);

            backgroundModeComboBox = CreateThemedComboBox(70, y, 140);
            backgroundModeComboBox.Items.AddRange(new object[] { "Auto (corners)", "White", "Black", "Custom Color" });
            backgroundModeComboBox.SelectedIndex = 0;
            backgroundModeComboBox.Enabled = false;
            backgroundModeComboBox.SelectedIndexChanged += (s, e) =>
            {
                bool isCustom = backgroundModeComboBox.SelectedItem.ToString() == "Custom Color";
                colorPickerPanel.Visible = isCustom;
                OnEffectsChanged();
            };
            contentPanel.Controls.Add(backgroundModeComboBox);
            y += 35;

            // Color picker for custom mode
            var colorLabel = CreateThemedLabel("Color:", 10, y + 3, 50);
            contentPanel.Controls.Add(colorLabel);

            colorPickerPanel = new Panel
            {
                Left = 70,
                Top = y,
                Width = 30,
                Height = 25,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                Cursor = Cursors.Hand,
                Enabled = false,
                Visible = false
            };
            colorPickerPanel.Click += ColorPickerPanel_Click;
            contentPanel.Controls.Add(colorPickerPanel);

            colorPickerLabel = CreateThemedLabel("(Click to pick)", 110, y + 5, 100);
            colorPickerLabel.Visible = false;
            contentPanel.Controls.Add(colorPickerLabel);
            y += 35;

            // Threshold slider
            var thresholdLabel = CreateThemedLabel("Tolerance", 10, y, 120);
            contentPanel.Controls.Add(thresholdLabel);
            y += 20;

            thresholdTrackBar = CreateThemedTrackBar(10, y, 195, 0, 255, 30);
            thresholdTrackBar.Enabled = false;
            thresholdTrackBar.ValueChanged += (s, e) =>
            {
                thresholdValueLabel.Text = $"{thresholdTrackBar.Value}";
                OnEffectsChanged();
            };
            contentPanel.Controls.Add(thresholdTrackBar);

            thresholdValueLabel = CreateValueLabel(210, y + 5, 50);
            thresholdValueLabel.Text = "30";
            contentPanel.Controls.Add(thresholdValueLabel);
            y += 50;

            // === UO STYLE EFFECTS SECTION ===
            contentPanel.Controls.Add(CreateSeparator(10, y, 250));
            y += 10;
            var uoHeader = CreateSectionHeader("UO STYLE EFFECTS", 10, y);
            contentPanel.Controls.Add(uoHeader);
            y += 25;

            // Fill Holes
            fillHolesCheckBox = new CheckBox
            {
                Text = "Fill Small Holes (1-2px)",
                Left = 10,
                Top = y,
                Width = 200,
                Checked = false,
                ForeColor = HolographicTheme.TextSecondary,
                BackColor = Color.Transparent
            };
            fillHolesCheckBox.CheckedChanged += (s, e) => OnEffectsChanged();
            contentPanel.Controls.Add(fillHolesCheckBox);
            y += 30;

            // Pixelize
            pixelizeCheckBox = new CheckBox
            {
                Text = "Pixelize",
                Left = 10,
                Top = y,
                Width = 80,
                Checked = false,
                ForeColor = HolographicTheme.TextSecondary,
                BackColor = Color.Transparent
            };
            pixelizeCheckBox.CheckedChanged += (s, e) => OnEffectsChanged();
            contentPanel.Controls.Add(pixelizeCheckBox);

            var pixelSizeLabel = CreateThemedLabel("Size:", 100, y + 3, 40);
            contentPanel.Controls.Add(pixelSizeLabel);

            pixelSizeUpDown = CreateThemedNumericUpDown(145, y, 50, 1, 16, 1, 1);
            pixelSizeUpDown.ValueChanged += (s, e) => OnEffectsChanged();
            contentPanel.Controls.Add(pixelSizeUpDown);
            y += 35;

            // Noise
            var noiseLabel = CreateThemedLabel("Noise/Grain", 10, y, 120);
            contentPanel.Controls.Add(noiseLabel);
            y += 20;

            noiseTrackBar = CreateThemedTrackBar(10, y, 195, 0, 100, 0);
            noiseTrackBar.ValueChanged += (s, e) =>
            {
                noiseValueLabel.Text = noiseTrackBar.Value > 0 ? $"{noiseTrackBar.Value}%" : "Off";
                OnEffectsChanged();
            };
            contentPanel.Controls.Add(noiseTrackBar);

            noiseValueLabel = CreateValueLabel(210, y + 5, 50);
            noiseValueLabel.Text = "Off";
            contentPanel.Controls.Add(noiseValueLabel);
            y += 50; // Extra padding after Noise

            // Dither
            var ditherLabel = CreateThemedLabel("Dither (Retro)", 10, y, 120);
            contentPanel.Controls.Add(ditherLabel);
            y += 20;

            ditherTrackBar = CreateThemedTrackBar(10, y, 195, 0, 16, 0);
            ditherTrackBar.ValueChanged += (s, e) =>
            {
                ditherValueLabel.Text = ditherTrackBar.Value > 1 ? $"{ditherTrackBar.Value}" : "Off";
                OnEffectsChanged();
            };
            contentPanel.Controls.Add(ditherTrackBar);

            ditherValueLabel = CreateValueLabel(210, y + 5, 50);
            ditherValueLabel.Text = "Off";
            contentPanel.Controls.Add(ditherValueLabel);
            y += 50; // Extra padding after Dither

            // Color Banding
            var bandsLabel = CreateThemedLabel("Color Bands", 10, y, 120);
            contentPanel.Controls.Add(bandsLabel);
            y += 20;

            colorBandsTrackBar = CreateThemedTrackBar(10, y, 195, 0, 32, 0);
            colorBandsTrackBar.ValueChanged += (s, e) =>
            {
                colorBandsValueLabel.Text = colorBandsTrackBar.Value > 1 ? $"{colorBandsTrackBar.Value}" : "Off";
                OnEffectsChanged();
            };
            contentPanel.Controls.Add(colorBandsTrackBar);

            colorBandsValueLabel = CreateValueLabel(210, y + 5, 50);
            colorBandsValueLabel.Text = "Off";
            contentPanel.Controls.Add(colorBandsValueLabel);
            y += 50; // Extra padding after Color Bands

            // Edge Darkening
            var edgeLabel = CreateThemedLabel("Edge Darken", 10, y, 120);
            contentPanel.Controls.Add(edgeLabel);
            y += 20;

            edgeDarkenTrackBar = CreateThemedTrackBar(10, y, 195, 0, 100, 0);
            edgeDarkenTrackBar.ValueChanged += (s, e) =>
            {
                edgeDarkenValueLabel.Text = edgeDarkenTrackBar.Value > 0 ? $"{edgeDarkenTrackBar.Value}%" : "Off";
                OnEffectsChanged();
            };
            contentPanel.Controls.Add(edgeDarkenTrackBar);

            edgeDarkenValueLabel = CreateValueLabel(210, y + 5, 50);
            edgeDarkenValueLabel.Text = "Off";
            contentPanel.Controls.Add(edgeDarkenValueLabel);
            y += 50; // Extra padding after Edge Darken

            // === PARTIAL HUE PREP SECTION (exact-gray authoring) ===
            contentPanel.Controls.Add(CreateSeparator(10, y, 250));
            y += 10;
            var huePrepHeader = CreateSectionHeader("PARTIAL HUE PREP", 10, y);
            contentPanel.Controls.Add(huePrepHeader);
            y += 25;

            desaturateCheckBox = new CheckBox
            {
                Text = "Desaturate Color Range",
                Left = 10,
                Top = y,
                Width = 200,
                Checked = false,
                ForeColor = HolographicTheme.TextSecondary,
                BackColor = Color.Transparent
            };
            desaturateCheckBox.CheckedChanged += (s, e) => OnEffectsChanged();
            contentPanel.Controls.Add(desaturateCheckBox);
            y += 30;

            var desatTargetsLabel = CreateThemedLabel("Targets:", 10, y, 120);
            contentPanel.Controls.Add(desatTargetsLabel);

            desatTargetsCountLabel = CreateValueLabel(140, y, 120);
            desatTargetsCountLabel.Text = "0 picked";
            contentPanel.Controls.Add(desatTargetsCountLabel);
            y += 22;

            desatTargetsListBox = new ListBox
            {
                Left = 10,
                Top = y,
                Width = 250,
                Height = 66,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 20,
                IntegralHeight = false,
                SelectionMode = System.Windows.Forms.SelectionMode.One,
                BackColor = HolographicTheme.InputBackground,
                ForeColor = HolographicTheme.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 8.5f)
            };
            desatTargetsListBox.DrawItem += DesatTargetsListBox_DrawItem;
            desatTargetsListBox.SelectedIndexChanged += (s, e) =>
            {
                if (desatRemoveButton != null)
                    desatRemoveButton.Enabled = desatTargetsListBox.SelectedIndex >= 0;
            };
            contentPanel.Controls.Add(desatTargetsListBox);
            y += 72;

            desatPickButton = new Button
            {
                Left = 10,
                Top = y,
                Width = 70,
                Height = 25,
                Text = "Pick",
                FlatStyle = FlatStyle.Flat,
                BackColor = HolographicTheme.ButtonBackground,
                ForeColor = HolographicTheme.TextPrimary,
                Font = new Font("Consolas", 8, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            desatPickButton.FlatAppearance.BorderColor = HolographicTheme.BorderCyan;
            desatPickButton.FlatAppearance.BorderSize = 1;
            desatPickButton.Click += DesatEyedropperArmClick;
            contentPanel.Controls.Add(desatPickButton);

            desatRemoveButton = new Button
            {
                Left = 85,
                Top = y,
                Width = 82,
                Height = 25,
                Text = "Remove",
                FlatStyle = FlatStyle.Flat,
                BackColor = HolographicTheme.ButtonBackground,
                ForeColor = HolographicTheme.TextPrimary,
                Font = new Font("Consolas", 8, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            desatRemoveButton.FlatAppearance.BorderColor = HolographicTheme.BorderCyan;
            desatRemoveButton.FlatAppearance.BorderSize = 1;
            desatRemoveButton.Click += (s, e) => RemoveSelectedDesaturateTarget();
            contentPanel.Controls.Add(desatRemoveButton);

            desatClearButton = new Button
            {
                Left = 172,
                Top = y,
                Width = 88,
                Height = 25,
                Text = "Clear",
                FlatStyle = FlatStyle.Flat,
                BackColor = HolographicTheme.ButtonBackground,
                ForeColor = HolographicTheme.TextPrimary,
                Font = new Font("Consolas", 8, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            desatClearButton.FlatAppearance.BorderColor = HolographicTheme.BorderCyan;
            desatClearButton.FlatAppearance.BorderSize = 1;
            desatClearButton.Click += (s, e) => ClearDesaturateTargets();
            contentPanel.Controls.Add(desatClearButton);
            y += 31;

            var desatProtectLabel = CreateThemedLabel("Protect (never touch):", 10, y, 150);
            contentPanel.Controls.Add(desatProtectLabel);

            desatProtectCountLabel = CreateValueLabel(165, y, 95);
            desatProtectCountLabel.Text = "0 picked";
            contentPanel.Controls.Add(desatProtectCountLabel);
            y += 22;

            desatProtectListBox = new ListBox
            {
                Left = 10,
                Top = y,
                Width = 250,
                Height = 52,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 20,
                IntegralHeight = false,
                SelectionMode = System.Windows.Forms.SelectionMode.One,
                BackColor = HolographicTheme.InputBackground,
                ForeColor = HolographicTheme.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 8.5f)
            };
            desatProtectListBox.DrawItem += DesatProtectListBox_DrawItem;
            desatProtectListBox.SelectedIndexChanged += (s, e) =>
            {
                if (desatProtectRemoveButton != null)
                    desatProtectRemoveButton.Enabled = desatProtectListBox.SelectedIndex >= 0;
            };
            contentPanel.Controls.Add(desatProtectListBox);
            y += 58;

            desatProtectPickButton = new Button
            {
                Left = 10,
                Top = y,
                Width = 70,
                Height = 25,
                Text = "Pick",
                FlatStyle = FlatStyle.Flat,
                BackColor = HolographicTheme.ButtonBackground,
                ForeColor = HolographicTheme.TextPrimary,
                Font = new Font("Consolas", 8, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            desatProtectPickButton.FlatAppearance.BorderColor = HolographicTheme.BorderCyan;
            desatProtectPickButton.FlatAppearance.BorderSize = 1;
            desatProtectPickButton.Click += DesatProtectEyedropperArmClick;
            contentPanel.Controls.Add(desatProtectPickButton);

            desatProtectRemoveButton = new Button
            {
                Left = 85,
                Top = y,
                Width = 82,
                Height = 25,
                Text = "Remove",
                FlatStyle = FlatStyle.Flat,
                BackColor = HolographicTheme.ButtonBackground,
                ForeColor = HolographicTheme.TextPrimary,
                Font = new Font("Consolas", 8, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            desatProtectRemoveButton.FlatAppearance.BorderColor = HolographicTheme.BorderCyan;
            desatProtectRemoveButton.FlatAppearance.BorderSize = 1;
            desatProtectRemoveButton.Click += (s, e) => RemoveSelectedDesaturateProtected();
            contentPanel.Controls.Add(desatProtectRemoveButton);

            desatProtectClearButton = new Button
            {
                Left = 172,
                Top = y,
                Width = 88,
                Height = 25,
                Text = "Clear",
                FlatStyle = FlatStyle.Flat,
                BackColor = HolographicTheme.ButtonBackground,
                ForeColor = HolographicTheme.TextPrimary,
                Font = new Font("Consolas", 8, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            desatProtectClearButton.FlatAppearance.BorderColor = HolographicTheme.BorderCyan;
            desatProtectClearButton.FlatAppearance.BorderSize = 1;
            desatProtectClearButton.Click += (s, e) => ClearDesaturateProtected();
            contentPanel.Controls.Add(desatProtectClearButton);
            y += 31;

            var desatTolLabel = CreateThemedLabel("Match Tolerance", 10, y, 100);
            contentPanel.Controls.Add(desatTolLabel);

            desatMatchCountLabel = CreateValueLabel(110, y, 100);
            desatMatchCountLabel.Text = "~0 px";
            desatMatchCountLabel.Font = new Font("Consolas", 8f);
            contentPanel.Controls.Add(desatMatchCountLabel);
            y += 20;

            desatToleranceTrackBar = CreateThemedTrackBar(10, y, 195, 0, 255, 30);
            desatToleranceTrackBar.ValueChanged += (s, e) =>
            {
                if (desatToleranceValueLabel != null)
                    desatToleranceValueLabel.Text = $"{desatToleranceTrackBar.Value}";
                OnEffectsChanged();
            };
            contentPanel.Controls.Add(desatToleranceTrackBar);

            desatToleranceValueLabel = CreateValueLabel(210, y + 5, 50);
            desatToleranceValueLabel.Text = "30";
            contentPanel.Controls.Add(desatToleranceValueLabel);
            y += 50;

            var despeckleLabel = CreateThemedLabel("Despeckle (min blob)", 10, y, 150);
            contentPanel.Controls.Add(despeckleLabel);
            y += 20;

            despeckleTrackBar = CreateThemedTrackBar(10, y, 195, 0, 16, 0);
            despeckleTrackBar.ValueChanged += (s, e) =>
            {
                if (despeckleValueLabel != null)
                    despeckleValueLabel.Text = despeckleTrackBar.Value > 0 ? $"{despeckleTrackBar.Value}" : "Off";
                OnEffectsChanged();
            };
            contentPanel.Controls.Add(despeckleTrackBar);

            despeckleValueLabel = CreateValueLabel(210, y + 5, 50);
            despeckleValueLabel.Text = "Off";
            contentPanel.Controls.Add(despeckleValueLabel);
            y += 50;

            snapNearGrayCheckBox = new CheckBox
            {
                Text = "Snap Near-Gray (fringe fix)",
                Left = 10,
                Top = y,
                Width = 220,
                Checked = false,
                ForeColor = HolographicTheme.TextSecondary,
                BackColor = Color.Transparent
            };
            snapNearGrayCheckBox.CheckedChanged += (s, e) => OnEffectsChanged();
            contentPanel.Controls.Add(snapNearGrayCheckBox);
            y += 30;

            var snapTolLabel = CreateThemedLabel("Gray Spread", 10, y, 120);
            contentPanel.Controls.Add(snapTolLabel);
            y += 20;

            snapToleranceTrackBar = CreateThemedTrackBar(10, y, 195, 0, 32, 8);
            snapToleranceTrackBar.ValueChanged += (s, e) =>
            {
                if (snapToleranceValueLabel != null)
                    snapToleranceValueLabel.Text = $"{snapToleranceTrackBar.Value}";
                OnEffectsChanged();
            };
            contentPanel.Controls.Add(snapToleranceTrackBar);

            snapToleranceValueLabel = CreateValueLabel(210, y + 5, 50);
            snapToleranceValueLabel.Text = "8";
            contentPanel.Controls.Add(snapToleranceValueLabel);
            y += 50;

            // --- SAM Select gate (spatial mask for partial-hue prep) ---
            var samHeader = CreateThemedLabel("SAM Select gate:", 10, y, 150);
            contentPanel.Controls.Add(samHeader);
            y += 20;

            samSelectButton = new Button
            {
                Left = 10,
                Top = y,
                Width = 88,
                Height = 25,
                Text = "SAM Select",
                FlatStyle = FlatStyle.Flat,
                BackColor = HolographicTheme.ButtonBackground,
                ForeColor = HolographicTheme.TextPrimary,
                Font = new Font("Consolas", 8, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            samSelectButton.FlatAppearance.BorderColor = HolographicTheme.BorderCyan;
            samSelectButton.FlatAppearance.BorderSize = 1;
            samSelectButton.Click += (s, e) => ToggleSamArming();
            contentPanel.Controls.Add(samSelectButton);

            samClearButton = new Button
            {
                Left = 103,
                Top = y,
                Width = 88,
                Height = 25,
                Text = "Clear SAM",
                FlatStyle = FlatStyle.Flat,
                BackColor = HolographicTheme.ButtonBackground,
                ForeColor = HolographicTheme.TextPrimary,
                Font = new Font("Consolas", 8, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Enabled = false
            };
            samClearButton.FlatAppearance.BorderColor = HolographicTheme.BorderCyan;
            samClearButton.FlatAppearance.BorderSize = 1;
            samClearButton.Click += (s, e) => ClearSam();
            contentPanel.Controls.Add(samClearButton);
            y += 30;

            showSamCheckBox = new CheckBox
            {
                Text = "Show SAM",
                Left = 10,
                Top = y,
                Width = 100,
                Checked = true,
                ForeColor = HolographicTheme.TextSecondary,
                BackColor = Color.Transparent
            };
            showSamCheckBox.CheckedChanged += (s, e) => SamShowChanged?.Invoke(this, EventArgs.Empty);
            contentPanel.Controls.Add(showSamCheckBox);

            samInfoLabel = CreateValueLabel(110, y + 2, 150);
            samInfoLabel.Text = "No SAM mask";
            samInfoLabel.Font = new Font("Consolas", 8f);
            contentPanel.Controls.Add(samInfoLabel);
            y += 30;

            var samHint = CreateThemedLabel("L-click: +FG  R/Sft-click: +BG", 10, y, 250);
            samHint.Font = new Font("Consolas", 7.5f);
            contentPanel.Controls.Add(samHint);
            y += 20;

            // === PALETTE SECTION ===
            contentPanel.Controls.Add(CreateSeparator(10, y, 250));
            y += 10;
            var paletteHeader = CreateSectionHeader("COLOR PALETTE", 10, y);
            contentPanel.Controls.Add(paletteHeader);
            y += 25;

            paletteReductionCheckBox = new CheckBox
            {
                Text = "Reduce Colors",
                Left = 10,
                Top = y,
                Width = 120,
                Checked = false,
                ForeColor = HolographicTheme.TextSecondary,
                BackColor = Color.Transparent
            };
            paletteReductionCheckBox.CheckedChanged += (s, e) => OnEffectsChanged();
            contentPanel.Controls.Add(paletteReductionCheckBox);

            paletteReductionComboBox = CreateThemedComboBox(140, y, 70);
            paletteReductionComboBox.Items.AddRange(new object[] { "8", "16", "32", "64", "128", "256" });
            paletteReductionComboBox.SelectedIndex = 1; // Default to 16
            paletteReductionComboBox.SelectedIndexChanged += (s, e) => OnEffectsChanged();
            contentPanel.Controls.Add(paletteReductionComboBox);
            y += 35;

            // === BUTTONS ===
            contentPanel.Controls.Add(CreateSeparator(10, y, 250));
            y += 15;

            applyButton = new Button
            {
                Text = "Apply",
                Left = 10,
                Top = y,
                Width = 120,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = HolographicTheme.ButtonSuccess,
                ForeColor = HolographicTheme.TextPrimary,
                Font = new Font("Consolas", 9, FontStyle.Bold),
                Enabled = true
            };
            applyButton.FlatAppearance.BorderColor = Color.FromArgb(0, 255, 200);
            applyButton.FlatAppearance.BorderSize = 1;
            applyButton.Click += (s, e) => ApplyClicked?.Invoke(this, EventArgs.Empty);
            contentPanel.Controls.Add(applyButton);

            resetButton = new Button
            {
                Text = "Reset",
                Left = 140,
                Top = y,
                Width = 120,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = HolographicTheme.ButtonBackground,
                ForeColor = HolographicTheme.TextPrimary,
                Font = new Font("Consolas", 9, FontStyle.Bold)
            };
            resetButton.FlatAppearance.BorderColor = HolographicTheme.BorderCyan;
            resetButton.FlatAppearance.BorderSize = 1;
            resetButton.Click += (s, e) => ResetClicked?.Invoke(this, EventArgs.Empty);
            contentPanel.Controls.Add(resetButton);
            y += 35;

            applyToAllSelectedButton = new Button
            {
                Text = "Apply to All Selected",
                Left = 10,
                Top = y,
                Width = 250,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = HolographicTheme.ButtonAccent,
                ForeColor = HolographicTheme.TextPrimary,
                Font = new Font("Consolas", 9, FontStyle.Bold)
            };
            applyToAllSelectedButton.FlatAppearance.BorderColor = HolographicTheme.CyanAccent;
            applyToAllSelectedButton.FlatAppearance.BorderSize = 1;
            applyToAllSelectedButton.Click += (s, e) => ApplyToAllSelectedClicked?.Invoke(this, EventArgs.Empty);
            contentPanel.Controls.Add(applyToAllSelectedButton);
            y += 35;

            statusLabel = new Label
            {
                Left = 10,
                Top = y,
                Width = 250,
                Height = 35,
                Text = "Ready",
                ForeColor = HolographicTheme.TextPrimary,
                Font = new Font("Consolas", 8.5f),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = HolographicTheme.InputBackground,
                Padding = new Padding(3),
                TextAlign = ContentAlignment.MiddleCenter
            };
            contentPanel.Controls.Add(statusLabel);

            // Set the virtual scrollable height for AutoScroll
            contentPanel.AutoScrollMinSize = new Size(0, y + 50);

            // Set control size
            this.Width = 280;
            this.MinimumSize = new Size(280, 200);

            this.Controls.Add(contentPanel);
            this.Controls.Add(headerPanel);
        }

        private Label CreateSectionHeader(string text, int left, int top)
        {
            return new Label
            {
                Text = "-- " + text + " --",
                Left = left,
                Top = top,
                Width = 250,
                Height = 20,
                ForeColor = HolographicTheme.CyanAccent,
                BackColor = Color.Transparent,
                Font = new Font("Consolas", 9, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private Label CreateThemedLabel(string text, int left, int top, int width)
        {
            return new Label
            {
                Text = text,
                Left = left,
                Top = top,
                Width = width,
                Height = 18,
                ForeColor = HolographicTheme.TextSecondary,
                BackColor = Color.Transparent,
                Font = new Font("Consolas", 8.5f)
            };
        }

        private Label CreateValueLabel(int left, int top, int width)
        {
            return new Label
            {
                Left = left,
                Top = top,
                Width = width,
                Height = 18,
                ForeColor = HolographicTheme.CyanAccent,
                BackColor = Color.Transparent,
                Font = new Font("Consolas", 9, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleRight
            };
        }

        private TrackBar CreateThemedTrackBar(int left, int top, int width, int min, int max, int value)
        {
            var trackBar = new TrackBar
            {
                Left = left,
                Top = top,
                Width = width,
                Minimum = min,
                Maximum = max,
                Value = value,
                TickFrequency = 1,
                SmallChange = 1,
                LargeChange = 5,
                BackColor = HolographicTheme.PanelBackground
            };
            return trackBar;
        }

        private NumericUpDown CreateThemedNumericUpDown(int left, int top, int width, decimal min, decimal max, decimal value, decimal increment)
        {
            var nud = new NumericUpDown
            {
                Left = left,
                Top = top,
                Width = width,
                Minimum = min,
                Maximum = max,
                Value = value,
                Increment = increment,
                BackColor = HolographicTheme.InputBackground,
                ForeColor = HolographicTheme.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9)
            };
            return nud;
        }

        private ComboBox CreateThemedComboBox(int left, int top, int width)
        {
            var cb = new ComboBox
            {
                Left = left,
                Top = top,
                Width = width,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = HolographicTheme.InputBackground,
                ForeColor = HolographicTheme.TextPrimary,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Consolas", 9)
            };
            return cb;
        }

        private Label CreateSeparator(int left, int top, int width)
        {
            return new Label
            {
                Left = left,
                Top = top,
                Width = width,
                Height = 1,
                BackColor = HolographicTheme.BorderCyan
            };
        }

        private void CollapseButton_Click(object sender, EventArgs e)
        {
            _isCollapsed = !_isCollapsed;

            if (_isCollapsed)
            {
                _expandedWidth = this.Width;
                this.Width = 30;
                contentPanel.Visible = false;
                collapseButton.Text = ">";
            }
            else
            {
                this.Width = _expandedWidth;
                contentPanel.Visible = true;
                collapseButton.Text = "<";
            }
        }

        private void OnEffectsChanged()
        {
            if (realTimePreviewCheckBox?.Checked == true)
            {
                EffectsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ColorPickerPanel_Click(object sender, EventArgs e)
        {
            using (var colorDialog = new ColorDialog())
            {
                colorDialog.Color = BackgroundColor;
                colorDialog.FullOpen = true;

                if (colorDialog.ShowDialog() == DialogResult.OK)
                {
                    BackgroundColor = colorDialog.Color;
                    UpdateColorPickerDisplay();
                    OnEffectsChanged();
                }
            }
        }

        private void UpdateColorPickerDisplay()
        {
            if (colorPickerPanel != null)
            {
                colorPickerPanel.BackColor = BackgroundColor;

                // Update label to show RGB values
                if (colorPickerLabel != null)
                {
                    colorPickerLabel.Text = $"RGB({BackgroundColor.R},{BackgroundColor.G},{BackgroundColor.B})";
                    colorPickerLabel.Visible = backgroundModeComboBox?.SelectedItem?.ToString() == "Custom Color";
                }
            }
        }

        private void DesatEyedropperArmClick(object sender, EventArgs e)
        {
            ArmEyedropper(DesaturatePickKind.Targets);
        }

        private void DesatProtectEyedropperArmClick(object sender, EventArgs e)
        {
            ArmEyedropper(DesaturatePickKind.Protected);
        }

        private void ArmEyedropper(DesaturatePickKind kind)
        {
            // SAM and eyedropper are mutually exclusive: arming one disarms the other.
            if (IsSamArmed)
                DisarmSamInternal();
            // Re-click while armed disarms either kind (Esc path calls CancelEyedropper directly).
            // Clicking the other Pick button while armed switches kinds (only one armed at a time).
            if (IsEyedropperArmed)
            {
                if (ArmedPickKind == kind)
                {
                    CancelEyedropper();
                    return;
                }
                ArmedPickKind = kind;
                UpdateEyedropperUI();
                EyedropperRequested?.Invoke(this, new EyedropperRequestedEventArgs { Kind = kind });
                return;
            }

            // No canvas host subscribed (e.g. Gump editor): keep the old ColorDialog
            // so the tool still works everywhere. The chosen color is appended
            // to the list being picked into (Targets behavior unchanged).
            if (EyedropperRequested == null)
            {
                using (var colorDialog = new ColorDialog())
                {
                    colorDialog.Color = kind == DesaturatePickKind.Protected && _desatProtected.Count > 0
                        ? _desatProtected[_desatProtected.Count - 1] : DesaturateColor;
                    colorDialog.FullOpen = true;

                    if (colorDialog.ShowDialog() == DialogResult.OK)
                    {
                        if (kind == DesaturatePickKind.Protected)
                            AddDesaturateProtected(colorDialog.Color);
                        else
                            AddDesaturateTarget(colorDialog.Color);
                    }
                }
                return;
            }

            IsEyedropperArmed = true;
            ArmedPickKind = kind;
            UpdateEyedropperUI();
            EyedropperRequested?.Invoke(this, new EyedropperRequestedEventArgs { Kind = kind });
        }

        /// <summary>
        /// Appends a target color (exact duplicates are ignored). Fires a
        /// preview. The eyedropper stays armed for rapid multi-picks; Esc or
        /// the Pick/Stop button disarms. Returns false for duplicates.
        /// </summary>
        public bool AddDesaturateTarget(Color picked)
        {
            Color opaque = Color.FromArgb(picked.R, picked.G, picked.B);
            foreach (var existing in _desatTargets)
            {
                if (existing.ToArgb() == opaque.ToArgb())
                    return false;
            }
            _desatTargets.Add(opaque);
            UpdateDesatTargetsDisplay();
            if (desatTargetsListBox != null && desatTargetsListBox.Items.Count > 0)
                desatTargetsListBox.SelectedIndex = desatTargetsListBox.Items.Count - 1;
            OnEffectsChanged();
            return true;
        }

        /// <summary>
        /// Host calls this with each canvas-sampled color. Appends to the armed
        /// list (or Targets when kind is omitted for back-compat) and STAYS
        /// ARMED so the user can rapidly pick several shades.
        /// </summary>
        public void ConfirmEyedropper(Color picked)
        {
            ConfirmEyedropper(picked, ArmedPickKind);
        }

        /// <summary>Kind-aware host delivery entry point. Appends to the right list.</summary>
        public void ConfirmEyedropper(Color picked, DesaturatePickKind kind)
        {
            SetDesaturateColor(picked, kind);
        }

        /// <summary>Alias for ConfirmEyedropper (host delivery entry point). Append semantics.</summary>
        public void SetDesaturateColor(Color picked)
        {
            SetDesaturateColor(picked, ArmedPickKind);
        }

        /// <summary>Kind-aware alias. Appends to the right list; stays armed.</summary>
        public void SetDesaturateColor(Color picked, DesaturatePickKind kind)
        {
            if (kind == DesaturatePickKind.Protected)
                AddDesaturateProtected(picked);
            else
                AddDesaturateTarget(picked);
            // Stay armed: Esc / Stop / Pick-button disarms.
        }

        /// <summary>Removes the first list entry matching the color. Returns true when removed.</summary>
        public bool RemoveDesaturateTarget(Color target)
        {
            Color opaque = Color.FromArgb(target.R, target.G, target.B);
            for (int i = 0; i < _desatTargets.Count; i++)
            {
                if (_desatTargets[i].ToArgb() == opaque.ToArgb())
                {
                    RemoveDesaturateTargetAt(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Removes the target at the given list index (used by the Remove button).</summary>
        public void RemoveDesaturateTargetAt(int index)
        {
            if (index < 0 || index >= _desatTargets.Count)
                return;
            _desatTargets.RemoveAt(index);
            UpdateDesatTargetsDisplay();
            OnEffectsChanged();
        }

        /// <summary>Removes the currently selected list entry, if any.</summary>
        public void RemoveSelectedDesaturateTarget()
        {
            if (desatTargetsListBox != null)
                RemoveDesaturateTargetAt(desatTargetsListBox.SelectedIndex);
        }

        /// <summary>Clears all target colors.</summary>
        public void ClearDesaturateTargets()
        {
            if (_desatTargets.Count == 0)
                return;
            _desatTargets.Clear();
            UpdateDesatTargetsDisplay();
            OnEffectsChanged();
        }

        /// <summary>Disarm without changing the color (Esc / re-click).</summary>
        public void CancelEyedropper()
        {
            if (!IsEyedropperArmed)
                return;
            IsEyedropperArmed = false;
            UpdateEyedropperUI();
            EyedropperCancelled?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Toggle SAM point-picking. Only one of SAM/eyedropper armed at a time.</summary>
        public void ToggleSamArming()
        {
            if (IsSamArmed)
            {
                CancelSamArming();
                return;
            }
            ArmSam();
        }

        private void ArmSam()
        {
            // Missing-files guard: show the missing path, arm nothing.
            string missing = SamService.GetMissingPath();
            if (missing != null)
            {
                SetStatus("SAM files missing: " + missing, Color.Orange);
                return;
            }
            // No host subscribed: arm nothing (no fallback for SAM points).
            if (SamSelectRequested == null)
            {
                SetStatus("SAM host not available", Color.Orange);
                return;
            }
            if (IsEyedropperArmed)
            {
                IsEyedropperArmed = false;
                UpdateEyedropperUI();
                EyedropperCancelled?.Invoke(this, EventArgs.Empty);
            }
            IsSamArmed = true;
            UpdateSamUI();
            SamSelectRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Disarm SAM arming but KEEP the last mask (Esc path).</summary>
        public void CancelSamArming()
        {
            if (!IsSamArmed)
                return;
            IsSamArmed = false;
            UpdateSamUI();
            SamSelectCancelled?.Invoke(this, EventArgs.Empty);
        }

        private void DisarmSamInternal()
        {
            // Internal mutual-exclusion disarm (no event; the arming path raises its own).
            IsSamArmed = false;
            UpdateSamUI();
        }

        /// <summary>Clear SAM button: disarms arming and asks hosts to drop mask + points.</summary>
        public void ClearSam()
        {
            bool wasArmed = IsSamArmed;
            IsSamArmed = false;
            UpdateSamUI();
            if (wasArmed)
                SamSelectCancelled?.Invoke(this, EventArgs.Empty);
            SamClearRequested?.Invoke(this, EventArgs.Empty);
            SetSamInfo(0, false, false);
        }

        private void UpdateSamUI()
        {
            try
            {
                if (samSelectButton != null)
                    samSelectButton.Text = IsSamArmed ? "Stop SAM" : "SAM Select";
            }
            catch { }
        }

        /// <summary>Host updates the SAM readout. Thread-safe.</summary>
        public void SetSamInfo(int pointCount, bool hasMask, bool busy)
        {
            try
            {
                if (samInfoLabel == null && samClearButton == null) return;
                Action apply = () =>
                {
                    try
                    {
                        if (samInfoLabel != null)
                        {
                            if (busy) samInfoLabel.Text = "SAM segmenting…";
                            else if (hasMask) samInfoLabel.Text = pointCount + " pts + mask";
                            else if (pointCount > 0) samInfoLabel.Text = pointCount + " pts";
                            else samInfoLabel.Text = "No SAM mask";
                        }
                        if (samClearButton != null)
                            samClearButton.Enabled = pointCount > 0 || hasMask || busy;
                    }
                    catch { }
                };
                if (samInfoLabel != null && samInfoLabel.InvokeRequired)
                    samInfoLabel.Invoke(apply);
                else
                    apply();
            }
            catch { }
        }

        private void UpdateEyedropperUI()
        {
            if (desatPickButton != null)
                desatPickButton.Text = (IsEyedropperArmed && ArmedPickKind == DesaturatePickKind.Targets) ? "Stop" : "Pick";
            if (desatProtectPickButton != null)
                desatProtectPickButton.Text = (IsEyedropperArmed && ArmedPickKind == DesaturatePickKind.Protected) ? "Stop" : "Pick";
        }

        /// <summary>
        /// Appends a protected color (exact duplicates are ignored). Fires a
        /// preview. The eyedropper stays armed for rapid multi-picks.
        /// Returns false for duplicates.
        /// </summary>
        public bool AddDesaturateProtected(Color picked)
        {
            Color opaque = Color.FromArgb(picked.R, picked.G, picked.B);
            foreach (var existing in _desatProtected)
            {
                if (existing.ToArgb() == opaque.ToArgb())
                    return false;
            }
            _desatProtected.Add(opaque);
            UpdateDesatProtectDisplay();
            if (desatProtectListBox != null && desatProtectListBox.Items.Count > 0)
                desatProtectListBox.SelectedIndex = desatProtectListBox.Items.Count - 1;
            OnEffectsChanged();
            return true;
        }

        /// <summary>Removes the first protected entry matching the color. Returns true when removed.</summary>
        public bool RemoveDesaturateProtected(Color target)
        {
            Color opaque = Color.FromArgb(target.R, target.G, target.B);
            for (int i = 0; i < _desatProtected.Count; i++)
            {
                if (_desatProtected[i].ToArgb() == opaque.ToArgb())
                {
                    RemoveDesaturateProtectedAt(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Removes the protected entry at the given list index.</summary>
        public void RemoveDesaturateProtectedAt(int index)
        {
            if (index < 0 || index >= _desatProtected.Count)
                return;
            _desatProtected.RemoveAt(index);
            UpdateDesatProtectDisplay();
            OnEffectsChanged();
        }

        /// <summary>Removes the currently selected protected entry, if any.</summary>
        public void RemoveSelectedDesaturateProtected()
        {
            if (desatProtectListBox != null)
                RemoveDesaturateProtectedAt(desatProtectListBox.SelectedIndex);
        }

        /// <summary>Clears all protected colors.</summary>
        public void ClearDesaturateProtected()
        {
            if (_desatProtected.Count == 0)
                return;
            _desatProtected.Clear();
            UpdateDesatProtectDisplay();
            OnEffectsChanged();
        }

        /// <summary>Updates the "~N px will change" readout next to Match Tolerance.</summary>
        public void SetMatchCount(int count)
        {
            if (desatMatchCountLabel == null)
                return;
            if (desatMatchCountLabel.InvokeRequired)
            {
                desatMatchCountLabel.Invoke(new Action(() => SetMatchCount(count)));
                return;
            }
            desatMatchCountLabel.Text = "~" + count + " px";
        }

        private void DesatTargetsListBox_DrawItem(object sender, DrawItemEventArgs e)
        {
            DrawColorListBoxItem(desatTargetsListBox, e);
        }

        private void DesatProtectListBox_DrawItem(object sender, DrawItemEventArgs e)
        {
            DrawColorListBoxItem(desatProtectListBox, e);
        }

        private void DrawColorListBoxItem(ListBox listBox, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0 || listBox == null || e.Index >= listBox.Items.Count)
                return;
            if (!(listBox.Items[e.Index] is Color))
                return;
            Color c = (Color)listBox.Items[e.Index];

            Rectangle chip = new Rectangle(e.Bounds.Left + 4, e.Bounds.Top + 3, 26, Math.Max(4, e.Bounds.Height - 6));
            using (var brush = new SolidBrush(c))
                e.Graphics.FillRectangle(brush, chip);
            e.Graphics.DrawRectangle(Pens.Gray, chip);

            string text = string.Format("RGB({0},{1},{2})", c.R, c.G, c.B);
            using (var textBrush = new SolidBrush(HolographicTheme.TextPrimary))
                e.Graphics.DrawString(text, e.Font, textBrush, e.Bounds.Left + 36, e.Bounds.Top + 3);
            e.DrawFocusRectangle();
        }

        private void UpdateDesatTargetsDisplay()
        {
            if (desatTargetsListBox != null)
            {
                int keepSelection = desatTargetsListBox.SelectedIndex;
                desatTargetsListBox.BeginUpdate();
                try
                {
                    desatTargetsListBox.Items.Clear();
                    foreach (var c in _desatTargets)
                        desatTargetsListBox.Items.Add(c);
                    if (keepSelection >= 0 && keepSelection < desatTargetsListBox.Items.Count)
                        desatTargetsListBox.SelectedIndex = keepSelection;
                }
                finally
                {
                    desatTargetsListBox.EndUpdate();
                }
                if (desatRemoveButton != null)
                    desatRemoveButton.Enabled = desatTargetsListBox.SelectedIndex >= 0;
            }
            if (desatTargetsCountLabel != null)
                desatTargetsCountLabel.Text = _desatTargets.Count == 1 ? "1 picked" : _desatTargets.Count + " picked";
            if (desatClearButton != null)
                desatClearButton.Enabled = _desatTargets.Count > 0;
        }

        private void UpdateDesatProtectDisplay()
        {
            if (desatProtectListBox != null)
            {
                int keepSelection = desatProtectListBox.SelectedIndex;
                desatProtectListBox.BeginUpdate();
                try
                {
                    desatProtectListBox.Items.Clear();
                    foreach (var c in _desatProtected)
                        desatProtectListBox.Items.Add(c);
                    if (keepSelection >= 0 && keepSelection < desatProtectListBox.Items.Count)
                        desatProtectListBox.SelectedIndex = keepSelection;
                }
                finally
                {
                    desatProtectListBox.EndUpdate();
                }
                if (desatProtectRemoveButton != null)
                    desatProtectRemoveButton.Enabled = desatProtectListBox.SelectedIndex >= 0;
            }
            if (desatProtectCountLabel != null)
                desatProtectCountLabel.Text = _desatProtected.Count == 1 ? "1 picked" : _desatProtected.Count + " picked";
            if (desatProtectClearButton != null)
                desatProtectClearButton.Enabled = _desatProtected.Count > 0;
        }
    }
}