using System;
using System.Drawing;
using System.Windows.Forms;

namespace MeesaMultisMaker.Controls
{
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

        public float Brightness => (brightnessTrackBar.Value - 50) / 50f;
        public float Contrast => (contrastTrackBar.Value - 50) / 50f;
        public float Hue => hueTrackBar.Value;
        public float Saturation => (saturationTrackBar.Value - 50) / 50f;
        public int PixelSize => (int)pixelSizeUpDown.Value;
        public bool PixelizeEnabled => pixelizeCheckBox.Checked;
        public int PaletteColors => paletteReductionCheckBox.Checked ? int.Parse(paletteReductionComboBox.SelectedItem.ToString()) : 0;
        public bool RealTimePreview => realTimePreviewCheckBox?.Checked ?? false;
        public bool FillHoles => fillHolesCheckBox?.Checked ?? false;

        // New UO-style effect properties
        public int NoiseIntensity => noiseTrackBar.Value;
        public int DitherLevels => ditherTrackBar.Value > 1 ? ditherTrackBar.Value : 0;
        public int EdgeDarkening => edgeDarkenTrackBar.Value;
        public int ColorBands => colorBandsTrackBar.Value > 1 ? colorBandsTrackBar.Value : 0;

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

        public void ResetToDefaults()
        {
            brightnessTrackBar.Value = 50;
            contrastTrackBar.Value = 50;
            hueTrackBar.Value = 0;
            saturationTrackBar.Value = 50;
            pixelSizeUpDown.Value = 1;
            pixelizeCheckBox.Checked = false;
            paletteReductionCheckBox.Checked = false;
            paletteReductionComboBox.SelectedIndex = 0;
            noiseTrackBar.Value = 0;
            ditherTrackBar.Value = 0;
            edgeDarkenTrackBar.Value = 0;
            colorBandsTrackBar.Value = 0;
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
    }
}