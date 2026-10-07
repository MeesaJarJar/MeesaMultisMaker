using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MeesaMultisMaker.JarJar;

namespace MeesaMultisMaker
{
    /// <summary>
    /// Settings form for configuring application paths and defaults
    /// </summary>
    public class SettingsForm : Form
    {
        // Path controls
        private TextBox mulFolderTextBox;
        private TextBox artFolderTextBox;
        private Button browseMulButton;
        private Button browseArtButton;
        private Button autoDetectMulButton;
        private Button autoDetectArtButton;

        // ComfyUI controls
        private TextBox comfyUrlTextBox;
        private Button testConnectionButton;
        private TextBox defaultPromptTextBox;
        private TextBox defaultNegativePromptTextBox;
        private NumericUpDown defaultStepsNumeric;
        private NumericUpDown defaultCFGNumeric;
        private NumericUpDown defaultDenoiseNumeric;
        private ComboBox defaultSamplerComboBox;
        private ComboBox defaultSchedulerComboBox;
        private ComboBox defaultResolutionComboBox;

        // JarJar controls
        private TextBox jarjarUrlTextBox;
        private TextBox jarjarTokenTextBox;
        private CheckBox jarjarEnabledCheckBox;
        private CheckBox jarjarAutoPushCheckBox;
        private Button testJarJarButton;

        // Other controls
        private CheckBox autoLoadArtCheckBox;
        private CheckBox rememberWindowsCheckBox;
        private CheckBox checkForUpdatesCheckBox;

        // Buttons
        private Button saveButton;
        private Button cancelButton;
        private Button resetDefaultsButton;
        private Button checkUpdatesButton;

        // Status
        private Label statusLabel;

        // Backing fields for settings with no dedicated UI yet (round-tripped on Save)
        private bool useControlNetBacking;
        private string controlNetModelBacking;
        private double controlNetStrengthBacking;
        private double controlNetStartPercentBacking;
        private double controlNetEndPercentBacking;
        private int controlNetBlurRadiusBacking;
        private double controlNetBlurSigmaBacking;
        private double controlNetBGThresholdBacking;
        private double controlNetBGFeatherBacking;
        private string controlNetChannelModeBacking;
        private double controlNetDespillBacking;
        private string controlNetInvertMaskBacking;
        private AIBackend selectedAIBackendBacking;
        private System.Collections.Generic.List<string> uoSearchPathsBacking = new System.Collections.Generic.List<string>();
        private System.DateTime lastUpdateCheckBacking;

        public SettingsForm()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void InitializeComponent()
        {
            this.Text = "Settings";
            this.Width = 650;
            this.Height = 860;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            HolographicTheme.ApplyToForm(this);

            var mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(15),
                AutoScroll = true
            };
            HolographicTheme.ApplyToPanel(mainPanel);

            int y = 10;

            // ===== PATHS SECTION =====
            var pathsHeader = CreateSectionHeader("File Paths", 10, y);
            mainPanel.Controls.Add(pathsHeader);
            y += 30;

            // MUL Folder
            var mulLabel = new Label
            {
                Text = "UO MUL Files Folder:",
                Location = new Point(10, y),
                Width = 150,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(mulLabel);
            mainPanel.Controls.Add(mulLabel);
            y += 22;

            mulFolderTextBox = new TextBox
            {
                Location = new Point(10, y),
                Width = 450,
                Height = 23
            };
            HolographicTheme.ApplyToTextBox(mulFolderTextBox);
            mainPanel.Controls.Add(mulFolderTextBox);

            browseMulButton = new Button
            {
                Text = "Browse...",
                Location = new Point(465, y - 2),
                Width = 70,
                Height = 25
            };
            browseMulButton.Click += BrowseMulButton_Click;
            HolographicTheme.ApplyToButton(browseMulButton);
            mainPanel.Controls.Add(browseMulButton);

            autoDetectMulButton = new Button
            {
                Text = "Auto",
                Location = new Point(540, y - 2),
                Width = 50,
                Height = 25
            };
            autoDetectMulButton.Click += AutoDetectMulButton_Click;
            HolographicTheme.ApplyToButton(autoDetectMulButton, ButtonStyle.Accent);
            mainPanel.Controls.Add(autoDetectMulButton);
            y += 30;

            var mulHint = new Label
            {
                Text = "Folder containing art.mul + artidx.mul (OSI) or artLegacyMUL.uop (UOForever)",
                Location = new Point(10, y),
                Width = 500,
                Height = 16,
                ForeColor = HolographicTheme.TextMuted,
                Font = new Font(this.Font.FontFamily, 8f)
            };
            mainPanel.Controls.Add(mulHint);
            y += 25;

            // Art Folder
            var artLabel = new Label
            {
                Text = "PNG Art Files Folder:",
                Location = new Point(10, y),
                Width = 150,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(artLabel);
            mainPanel.Controls.Add(artLabel);
            y += 22;

            artFolderTextBox = new TextBox
            {
                Location = new Point(10, y),
                Width = 450,
                Height = 23
            };
            HolographicTheme.ApplyToTextBox(artFolderTextBox);
            mainPanel.Controls.Add(artFolderTextBox);

            browseArtButton = new Button
            {
                Text = "Browse...",
                Location = new Point(465, y - 2),
                Width = 70,
                Height = 25
            };
            browseArtButton.Click += BrowseArtButton_Click;
            HolographicTheme.ApplyToButton(browseArtButton);
            mainPanel.Controls.Add(browseArtButton);

            autoDetectArtButton = new Button
            {
                Text = "Auto",
                Location = new Point(540, y - 2),
                Width = 50,
                Height = 25
            };
            autoDetectArtButton.Click += AutoDetectArtButton_Click;
            HolographicTheme.ApplyToButton(autoDetectArtButton, ButtonStyle.Accent);
            mainPanel.Controls.Add(autoDetectArtButton);
            y += 30;

            var artHint = new Label
            {
                Text = "Folder containing UO item PNG images (UOFItems, etc.)",
                Location = new Point(10, y),
                Width = 400,
                Height = 16,
                ForeColor = HolographicTheme.TextMuted,
                Font = new Font(this.Font.FontFamily, 8f)
            };
            mainPanel.Controls.Add(artHint);
            y += 35;

            // ===== COMFYUI SECTION =====
            var comfyHeader = CreateSectionHeader("ComfyUI Settings", 10, y);
            mainPanel.Controls.Add(comfyHeader);
            y += 30;

            // ComfyUI URL
            var urlLabel = new Label
            {
                Text = "ComfyUI URL:",
                Location = new Point(10, y),
                Width = 100,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(urlLabel);
            mainPanel.Controls.Add(urlLabel);

            comfyUrlTextBox = new TextBox
            {
                Location = new Point(120, y),
                Width = 300,
                Height = 23
            };
            HolographicTheme.ApplyToTextBox(comfyUrlTextBox);
            mainPanel.Controls.Add(comfyUrlTextBox);

            testConnectionButton = new Button
            {
                Text = "Test",
                Location = new Point(425, y - 2),
                Width = 60,
                Height = 25
            };
            testConnectionButton.Click += TestConnectionButton_Click;
            HolographicTheme.ApplyToButton(testConnectionButton);
            mainPanel.Controls.Add(testConnectionButton);
            y += 35;

            // Default Prompt
            var promptLabel = new Label
            {
                Text = "Default Prompt:",
                Location = new Point(10, y),
                Width = 150,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(promptLabel);
            mainPanel.Controls.Add(promptLabel);
            y += 22;

            defaultPromptTextBox = new TextBox
            {
                Location = new Point(10, y),
                Width = 580,
                Height = 50,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical
            };
            HolographicTheme.ApplyToTextBox(defaultPromptTextBox);
            mainPanel.Controls.Add(defaultPromptTextBox);
            y += 55;

            // Default Negative Prompt
            var negPromptLabel = new Label
            {
                Text = "Default Negative Prompt:",
                Location = new Point(10, y),
                Width = 150,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(negPromptLabel);
            mainPanel.Controls.Add(negPromptLabel);
            y += 22;

            defaultNegativePromptTextBox = new TextBox
            {
                Location = new Point(10, y),
                Width = 580,
                Height = 40,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical
            };
            HolographicTheme.ApplyToTextBox(defaultNegativePromptTextBox);
            mainPanel.Controls.Add(defaultNegativePromptTextBox);
            y += 50;

            // Steps, CFG, Denoise row
            var stepsLabel = new Label { Text = "Steps:", Location = new Point(10, y + 3), Width = 45 };
            HolographicTheme.ApplyToLabel(stepsLabel);
            mainPanel.Controls.Add(stepsLabel);

            defaultStepsNumeric = new NumericUpDown
            {
                Location = new Point(55, y),
                Width = 60,
                Minimum = 1,
                Maximum = 100,
                Value = 20
            };
            HolographicTheme.ApplyToNumericUpDown(defaultStepsNumeric);
            mainPanel.Controls.Add(defaultStepsNumeric);

            var cfgLabel = new Label { Text = "CFG:", Location = new Point(130, y + 3), Width = 35 };
            HolographicTheme.ApplyToLabel(cfgLabel);
            mainPanel.Controls.Add(cfgLabel);

            defaultCFGNumeric = new NumericUpDown
            {
                Location = new Point(170, y),
                Width = 60,
                Minimum = 1,
                Maximum = 30,
                DecimalPlaces = 1,
                Increment = 0.5m,
                Value = 7
            };
            HolographicTheme.ApplyToNumericUpDown(defaultCFGNumeric);
            mainPanel.Controls.Add(defaultCFGNumeric);

            var denoiseLabel = new Label { Text = "Denoise:", Location = new Point(245, y + 3), Width = 55 };
            HolographicTheme.ApplyToLabel(denoiseLabel);
            mainPanel.Controls.Add(denoiseLabel);

            defaultDenoiseNumeric = new NumericUpDown
            {
                Location = new Point(305, y),
                Width = 60,
                Minimum = 0,
                Maximum = 1,
                DecimalPlaces = 2,
                Increment = 0.05m,
                Value = 0.75m
            };
            HolographicTheme.ApplyToNumericUpDown(defaultDenoiseNumeric);
            mainPanel.Controls.Add(defaultDenoiseNumeric);

            var resLabel = new Label { Text = "Resolution:", Location = new Point(380, y + 3), Width = 70 };
            HolographicTheme.ApplyToLabel(resLabel);
            mainPanel.Controls.Add(resLabel);

            defaultResolutionComboBox = new ComboBox
            {
                Location = new Point(455, y),
                Width = 100,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            defaultResolutionComboBox.Items.AddRange(new object[] { "512", "768", "1024", "1280", "1536" });
            defaultResolutionComboBox.SelectedIndex = 0;
            HolographicTheme.ApplyToComboBox(defaultResolutionComboBox);
            mainPanel.Controls.Add(defaultResolutionComboBox);
            y += 35;

            // Sampler, Scheduler row
            var samplerLabel = new Label { Text = "Sampler:", Location = new Point(10, y + 3), Width = 60 };
            HolographicTheme.ApplyToLabel(samplerLabel);
            mainPanel.Controls.Add(samplerLabel);

            defaultSamplerComboBox = new ComboBox
            {
                Location = new Point(75, y),
                Width = 130,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            defaultSamplerComboBox.Items.AddRange(new object[] {
                "euler", "euler_ancestral", "heun", "dpm_2", "dpm_2_ancestral",
                "lms", "dpm_fast", "dpmpp_2m", "dpmpp_sde", "ddpm", "lcm"
            });
            defaultSamplerComboBox.SelectedIndex = 0;
            HolographicTheme.ApplyToComboBox(defaultSamplerComboBox);
            mainPanel.Controls.Add(defaultSamplerComboBox);

            var schedulerLabel = new Label { Text = "Scheduler:", Location = new Point(220, y + 3), Width = 65 };
            HolographicTheme.ApplyToLabel(schedulerLabel);
            mainPanel.Controls.Add(schedulerLabel);

            defaultSchedulerComboBox = new ComboBox
            {
                Location = new Point(290, y),
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            defaultSchedulerComboBox.Items.AddRange(new object[] {
                "normal", "karras", "exponential", "sgm_uniform", "simple", "ddim_uniform"
            });
            defaultSchedulerComboBox.SelectedIndex = 0;
            HolographicTheme.ApplyToComboBox(defaultSchedulerComboBox);
            mainPanel.Controls.Add(defaultSchedulerComboBox);
            y += 45;

            // ===== MEESAJARJAR SECTION =====
            var jarjarHeader = CreateSectionHeader("\u2601 MeesaJarJar.com Settings", 10, y);
            jarjarHeader.ForeColor = Color.FromArgb(180, 100, 255);
            mainPanel.Controls.Add(jarjarHeader);
            y += 30;

            jarjarEnabledCheckBox = new CheckBox
            {
                Text = "Enable Push to MeesaJarJar",
                Location = new Point(10, y),
                Width = 250,
                Checked = true
            };
            HolographicTheme.ApplyToCheckBox(jarjarEnabledCheckBox);
            mainPanel.Controls.Add(jarjarEnabledCheckBox);
            y += 28;

            var jarjarUrlLabel = new Label
            {
                Text = "Server URL:",
                Location = new Point(10, y + 3),
                Width = 80,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(jarjarUrlLabel);
            mainPanel.Controls.Add(jarjarUrlLabel);

            jarjarUrlTextBox = new TextBox
            {
                Location = new Point(95, y),
                Width = 320,
                Height = 23
            };
            HolographicTheme.ApplyToTextBox(jarjarUrlTextBox);
            mainPanel.Controls.Add(jarjarUrlTextBox);

            testJarJarButton = new Button
            {
                Text = "Test",
                Location = new Point(420, y - 2),
                Width = 60,
                Height = 25
            };
            testJarJarButton.Click += TestJarJarButton_Click;
            HolographicTheme.ApplyToButton(testJarJarButton);
            mainPanel.Controls.Add(testJarJarButton);
            y += 30;

            var jarjarTokenLabel = new Label
            {
                Text = "Auth Token:",
                Location = new Point(10, y + 3),
                Width = 80,
                Height = 20
            };
            HolographicTheme.ApplyToLabel(jarjarTokenLabel);
            mainPanel.Controls.Add(jarjarTokenLabel);

            jarjarTokenTextBox = new TextBox
            {
                Location = new Point(95, y),
                Width = 320,
                Height = 23,
                UseSystemPasswordChar = true
            };
            HolographicTheme.ApplyToTextBox(jarjarTokenTextBox);
            mainPanel.Controls.Add(jarjarTokenTextBox);
            y += 28;

            var jarjarHint = new Label
            {
                Text = "Pushes modified art to the live shard. Clients auto-update in ~30 seconds.",
                Location = new Point(10, y),
                Width = 500,
                Height = 16,
                ForeColor = HolographicTheme.TextMuted,
                Font = new Font(this.Font.FontFamily, 8f)
            };
            mainPanel.Controls.Add(jarjarHint);
            y += 22;

            jarjarAutoPushCheckBox = new CheckBox
            {
                Text = "Auto-push to server after AI generation",
                Location = new Point(10, y),
                Width = 300,
                Checked = false
            };
            HolographicTheme.ApplyToCheckBox(jarjarAutoPushCheckBox);
            mainPanel.Controls.Add(jarjarAutoPushCheckBox);
            y += 30;

            // ===== OTHER SETTINGS =====
            var otherHeader = CreateSectionHeader("Other Settings", 10, y);
            mainPanel.Controls.Add(otherHeader);
            y += 30;

            autoLoadArtCheckBox = new CheckBox
            {
                Text = "Auto-load art files on startup",
                Location = new Point(10, y),
                Width = 250,
                Checked = true
            };
            HolographicTheme.ApplyToCheckBox(autoLoadArtCheckBox);
            mainPanel.Controls.Add(autoLoadArtCheckBox);
            y += 25;

            rememberWindowsCheckBox = new CheckBox
            {
                Text = "Remember window positions",
                Location = new Point(10, y),
                Width = 250,
                Checked = true
            };
            HolographicTheme.ApplyToCheckBox(rememberWindowsCheckBox);
            mainPanel.Controls.Add(rememberWindowsCheckBox);
            y += 25;

            checkForUpdatesCheckBox = new CheckBox
            {
                Text = "Check for updates on startup",
                Location = new Point(10, y),
                Width = 200,
                Checked = true
            };
            HolographicTheme.ApplyToCheckBox(checkForUpdatesCheckBox);
            mainPanel.Controls.Add(checkForUpdatesCheckBox);

            checkUpdatesButton = new Button
            {
                Text = "Check Now",
                Location = new Point(220, y - 2),
                Width = 90,
                Height = 23
            };
            checkUpdatesButton.Click += async (s, ev) =>
            {
                checkUpdatesButton.Enabled = false;
                checkUpdatesButton.Text = "...";
                try
                {
                    await UpdateChecker.CheckAndPromptAsync(this, silent: false);
                }
                finally
                {
                    checkUpdatesButton.Enabled = true;
                    checkUpdatesButton.Text = "Check Now";
                }
            };
            HolographicTheme.ApplyToButton(checkUpdatesButton, ButtonStyle.Accent);
            mainPanel.Controls.Add(checkUpdatesButton);
            y += 30;

            // Version label
            var versionLabel = new Label
            {
                Text = $"Current version: {UpdateChecker.CurrentVersionString}",
                Location = new Point(10, y),
                Width = 300,
                Height = 20,
                ForeColor = HolographicTheme.TextMuted
            };
            mainPanel.Controls.Add(versionLabel);
            y += 30;

            // Status label
            statusLabel = new Label
            {
                Text = $"Config file: {AppConfig.ConfigFilePath}",
                Location = new Point(10, y),
                Width = 580,
                Height = 20,
                ForeColor = HolographicTheme.TextMuted,
                Font = new Font(this.Font.FontFamily, 8f)
            };
            mainPanel.Controls.Add(statusLabel);

            this.Controls.Add(mainPanel);

            // Bottom button panel
            var buttonPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                Padding = new Padding(10)
            };
            HolographicTheme.ApplyToPanel(buttonPanel, true);

            resetDefaultsButton = new Button
            {
                Text = "Reset to Defaults",
                Location = new Point(10, 10),
                Width = 120,
                Height = 30
            };
            resetDefaultsButton.Click += ResetDefaultsButton_Click;
            HolographicTheme.ApplyToButton(resetDefaultsButton, ButtonStyle.Warning);
            buttonPanel.Controls.Add(resetDefaultsButton);

            cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(420, 10),
                Width = 90,
                Height = 30
            };
            cancelButton.Click += (s, e) => this.DialogResult = DialogResult.Cancel;
            HolographicTheme.ApplyToButton(cancelButton);
            buttonPanel.Controls.Add(cancelButton);

            saveButton = new Button
            {
                Text = "Save",
                Location = new Point(520, 10),
                Width = 90,
                Height = 30
            };
            saveButton.Click += SaveButton_Click;
            HolographicTheme.ApplyToButton(saveButton, ButtonStyle.Success);
            buttonPanel.Controls.Add(saveButton);

            this.Controls.Add(buttonPanel);
        }

        private Label CreateSectionHeader(string text, int left, int top)
        {
            return new Label
            {
                Text = text,
                Location = new Point(left, top),
                Width = 580,
                Height = 25,
                Font = new Font(this.Font.FontFamily, 11, FontStyle.Bold),
                ForeColor = HolographicTheme.CyanAccent,
                BorderStyle = BorderStyle.None
            };
        }

        private void LoadSettings()
        {
            var config = AppConfig.Instance;

            mulFolderTextBox.Text = config.MulFolderPath;
            artFolderTextBox.Text = config.ArtFolderPath;
            comfyUrlTextBox.Text = config.ComfyUIUrl;
            defaultPromptTextBox.Text = config.DefaultPrompt;
            defaultNegativePromptTextBox.Text = config.DefaultNegativePrompt;
            defaultStepsNumeric.Value = config.DefaultSteps;
            defaultCFGNumeric.Value = (decimal)config.DefaultCFG;
            defaultDenoiseNumeric.Value = (decimal)config.DefaultDenoise;
            autoLoadArtCheckBox.Checked = config.AutoLoadArtOnStartup;
            rememberWindowsCheckBox.Checked = config.RememberWindowPositions;
            checkForUpdatesCheckBox.Checked = config.CheckForUpdatesOnStartup;

            // JarJar settings
            jarjarUrlTextBox.Text = config.JarJarApiUrl;
            jarjarTokenTextBox.Text = config.JarJarAuthToken;
            jarjarEnabledCheckBox.Checked = config.JarJarPushEnabled;
            jarjarAutoPushCheckBox.Checked = config.JarJarAutoPush;

            // Set sampler
            int samplerIndex = defaultSamplerComboBox.Items.IndexOf(config.DefaultSampler);
            defaultSamplerComboBox.SelectedIndex = samplerIndex >= 0 ? samplerIndex : 0;

            // Set scheduler
            int schedulerIndex = defaultSchedulerComboBox.Items.IndexOf(config.DefaultScheduler);
            defaultSchedulerComboBox.SelectedIndex = schedulerIndex >= 0 ? schedulerIndex : 0;

            // Set resolution
            int resIndex = defaultResolutionComboBox.Items.IndexOf(config.DefaultResolution.ToString());
            defaultResolutionComboBox.SelectedIndex = resIndex >= 0 ? resIndex : 0;

            // Settings with no dedicated UI yet: load into backing fields so Save round-trips them
            useControlNetBacking = config.UseControlNet;
            controlNetModelBacking = config.ControlNetModel;
            controlNetStrengthBacking = config.ControlNetStrength;
            controlNetStartPercentBacking = config.ControlNetStartPercent;
            controlNetEndPercentBacking = config.ControlNetEndPercent;
            controlNetBlurRadiusBacking = config.ControlNetBlurRadius;
            controlNetBlurSigmaBacking = config.ControlNetBlurSigma;
            controlNetBGThresholdBacking = config.ControlNetBGThreshold;
            controlNetBGFeatherBacking = config.ControlNetBGFeather;
            controlNetChannelModeBacking = config.ControlNetChannelMode;
            controlNetDespillBacking = config.ControlNetDespill;
            controlNetInvertMaskBacking = config.ControlNetInvertMask;
            selectedAIBackendBacking = config.SelectedAIBackend;
            uoSearchPathsBacking = new System.Collections.Generic.List<string>(config.UOSearchPaths);
            lastUpdateCheckBacking = config.LastUpdateCheck;
        }

        private void SaveButton_Click(object sender, EventArgs e)
        {
            var config = AppConfig.Instance;

            config.MulFolderPath = mulFolderTextBox.Text.Trim();
            config.ArtFolderPath = artFolderTextBox.Text.Trim();
            config.ComfyUIUrl = comfyUrlTextBox.Text.Trim();
            config.DefaultPrompt = defaultPromptTextBox.Text;
            config.DefaultNegativePrompt = defaultNegativePromptTextBox.Text;
            config.DefaultSteps = (int)defaultStepsNumeric.Value;
            config.DefaultCFG = (double)defaultCFGNumeric.Value;
            config.DefaultDenoise = (double)defaultDenoiseNumeric.Value;
            config.DefaultSampler = defaultSamplerComboBox.SelectedItem?.ToString() ?? "euler";
            config.DefaultScheduler = defaultSchedulerComboBox.SelectedItem?.ToString() ?? "normal";
            config.DefaultResolution = int.Parse(defaultResolutionComboBox.SelectedItem?.ToString() ?? "512");
            config.AutoLoadArtOnStartup = autoLoadArtCheckBox.Checked;
            config.RememberWindowPositions = rememberWindowsCheckBox.Checked;
            config.CheckForUpdatesOnStartup = checkForUpdatesCheckBox.Checked;

            // JarJar settings
            config.JarJarApiUrl = jarjarUrlTextBox.Text.Trim();
            config.JarJarAuthToken = jarjarTokenTextBox.Text.Trim();
            config.JarJarPushEnabled = jarjarEnabledCheckBox.Checked;
            config.JarJarAutoPush = jarjarAutoPushCheckBox.Checked;

            // Settings with no dedicated UI: persist backing fields unchanged
            config.UseControlNet = useControlNetBacking;
            config.ControlNetModel = controlNetModelBacking;
            config.ControlNetStrength = controlNetStrengthBacking;
            config.ControlNetStartPercent = controlNetStartPercentBacking;
            config.ControlNetEndPercent = controlNetEndPercentBacking;
            config.ControlNetBlurRadius = controlNetBlurRadiusBacking;
            config.ControlNetBlurSigma = controlNetBlurSigmaBacking;
            config.ControlNetBGThreshold = controlNetBGThresholdBacking;
            config.ControlNetBGFeather = controlNetBGFeatherBacking;
            config.ControlNetChannelMode = controlNetChannelModeBacking;
            config.ControlNetDespill = controlNetDespillBacking;
            config.ControlNetInvertMask = controlNetInvertMaskBacking;
            config.SelectedAIBackend = selectedAIBackendBacking;
            config.UOSearchPaths = new System.Collections.Generic.List<string>(uoSearchPathsBacking);
            config.LastUpdateCheck = lastUpdateCheckBacking;

            config.Save();

            this.DialogResult = DialogResult.OK;
        }

        private void BrowseMulButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select the Ultima Online folder containing MUL or UOP files";
                if (!string.IsNullOrEmpty(mulFolderTextBox.Text))
                    dialog.SelectedPath = mulFolderTextBox.Text;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    mulFolderTextBox.Text = dialog.SelectedPath;
                    
                    var format = AppConfig.Instance.GetArtFileFormat(dialog.SelectedPath);
                    
                    if (format == ArtFileFormat.MulFiles)
                    {
                        statusLabel.Text = "? Valid folder - OSI-style (art.mul + artidx.mul)";
                        statusLabel.ForeColor = Color.Green;
                    }
                    else if (format == ArtFileFormat.UopOnly)
                    {
                        statusLabel.Text = "? Valid folder - UOForever-style (artLegacyMUL.uop only)";
                        statusLabel.ForeColor = Color.Green;
                    }
                    else
                    {
                        statusLabel.Text = "? No art files found (needs art.mul or artLegacyMUL.uop)";
                        statusLabel.ForeColor = Color.Orange;
                    }
                }
            }
        }

        private void BrowseArtButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select the folder containing PNG art files";
                if (!string.IsNullOrEmpty(artFolderTextBox.Text))
                    dialog.SelectedPath = artFolderTextBox.Text;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    artFolderTextBox.Text = dialog.SelectedPath;
                    
                    // Check if folder has PNG files
                    int pngCount = Directory.GetFiles(dialog.SelectedPath, "*.png", SearchOption.AllDirectories).Length;
                    if (pngCount > 0)
                    {
                        statusLabel.Text = $"? Found {pngCount} PNG files";
                        statusLabel.ForeColor = Color.Green;
                    }
                    else
                    {
                        statusLabel.Text = "? No PNG files found in selected folder";
                        statusLabel.ForeColor = Color.Orange;
                    }
                }
            }
        }

        private void AutoDetectMulButton_Click(object sender, EventArgs e)
        {
            var found = AppConfig.Instance.FindMulFolder();
            if (!string.IsNullOrEmpty(found))
            {
                mulFolderTextBox.Text = found;
                statusLabel.Text = $"? Auto-detected UO folder";
                statusLabel.ForeColor = Color.Green;
            }
            else
            {
                statusLabel.Text = "? Could not auto-detect UO folder";
                statusLabel.ForeColor = Color.Red;
            }
        }

        private void AutoDetectArtButton_Click(object sender, EventArgs e)
        {
            var found = AppConfig.Instance.FindArtFolder();
            if (!string.IsNullOrEmpty(found))
            {
                artFolderTextBox.Text = found;
                statusLabel.Text = $"? Auto-detected art folder";
                statusLabel.ForeColor = Color.Green;
            }
            else
            {
                statusLabel.Text = "? Could not auto-detect art folder";
                statusLabel.ForeColor = Color.Red;
            }
        }

        private async void TestConnectionButton_Click(object sender, EventArgs e)
        {
            testConnectionButton.Enabled = false;
            testConnectionButton.Text = "...";
            statusLabel.Text = "Testing connection...";
            statusLabel.ForeColor = HolographicTheme.TextPrimary;

            try
            {
                var client = new ComfyUI.ComfyUIClient(comfyUrlTextBox.Text.Trim());
                bool connected = await client.TestConnection();

                if (connected)
                {
                    statusLabel.Text = $"? Connected to ComfyUI at {comfyUrlTextBox.Text}";
                    statusLabel.ForeColor = Color.Green;
                }
                else
                {
                    statusLabel.Text = $"? Could not connect to ComfyUI";
                    statusLabel.ForeColor = Color.Red;
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text = $"? Error: {ex.Message}";
                statusLabel.ForeColor = Color.Red;
            }
            finally
            {
                testConnectionButton.Enabled = true;
                testConnectionButton.Text = "Test";
            }
        }

        private async void TestJarJarButton_Click(object sender, EventArgs e)
        {
            testJarJarButton.Enabled = false;
            testJarJarButton.Text = "...";
            statusLabel.Text = "Testing MeesaJarJar connection...";
            statusLabel.ForeColor = HolographicTheme.TextPrimary;

            try
            {
                using (var client = new JarJarClient(jarjarUrlTextBox.Text.Trim(), jarjarTokenTextBox.Text.Trim()))
                {
                    bool connected = await client.TestConnectionAsync();

                    if (connected)
                    {
                        var lookup = await client.GetLookupTableAsync();
                        statusLabel.Text = $"\u2713 Connected to MeesaJarJar — {lookup.EntryCount} sprites in lookup table";
                        statusLabel.ForeColor = Color.FromArgb(180, 100, 255);
                    }
                    else
                    {
                        statusLabel.Text = "\u2717 Could not connect to MeesaJarJar server";
                        statusLabel.ForeColor = Color.Red;
                    }
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text = $"\u2717 JarJar error: {ex.Message}";
                statusLabel.ForeColor = Color.Red;
            }
            finally
            {
                testJarJarButton.Enabled = true;
                testJarJarButton.Text = "Test";
            }
        }

        private void ResetDefaultsButton_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "Reset all settings to defaults?\n\nThis will clear saved paths and restore default ComfyUI settings.",
                "Reset Settings",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                var newConfig = new AppConfig();
                
                mulFolderTextBox.Text = "";
                artFolderTextBox.Text = "";
                comfyUrlTextBox.Text = newConfig.ComfyUIUrl;
                defaultPromptTextBox.Text = newConfig.DefaultPrompt;
                defaultNegativePromptTextBox.Text = newConfig.DefaultNegativePrompt;
                defaultStepsNumeric.Value = newConfig.DefaultSteps;
                defaultCFGNumeric.Value = (decimal)newConfig.DefaultCFG;
                defaultDenoiseNumeric.Value = (decimal)newConfig.DefaultDenoise;
                defaultSamplerComboBox.SelectedIndex = 0;
                defaultSchedulerComboBox.SelectedIndex = 0;
                defaultResolutionComboBox.SelectedIndex = 0;
                autoLoadArtCheckBox.Checked = newConfig.AutoLoadArtOnStartup;
                rememberWindowsCheckBox.Checked = newConfig.RememberWindowPositions;
                checkForUpdatesCheckBox.Checked = newConfig.CheckForUpdatesOnStartup;

                jarjarUrlTextBox.Text = newConfig.JarJarApiUrl;
                jarjarTokenTextBox.Text = newConfig.JarJarAuthToken;
                jarjarEnabledCheckBox.Checked = newConfig.JarJarPushEnabled;
                jarjarAutoPushCheckBox.Checked = newConfig.JarJarAutoPush;

                useControlNetBacking = newConfig.UseControlNet;
                controlNetModelBacking = newConfig.ControlNetModel;
                controlNetStrengthBacking = newConfig.ControlNetStrength;
                controlNetStartPercentBacking = newConfig.ControlNetStartPercent;
                controlNetEndPercentBacking = newConfig.ControlNetEndPercent;
                controlNetBlurRadiusBacking = newConfig.ControlNetBlurRadius;
                controlNetBlurSigmaBacking = newConfig.ControlNetBlurSigma;
                controlNetBGThresholdBacking = newConfig.ControlNetBGThreshold;
                controlNetBGFeatherBacking = newConfig.ControlNetBGFeather;
                controlNetChannelModeBacking = newConfig.ControlNetChannelMode;
                controlNetDespillBacking = newConfig.ControlNetDespill;
                controlNetInvertMaskBacking = newConfig.ControlNetInvertMask;
                selectedAIBackendBacking = newConfig.SelectedAIBackend;
                uoSearchPathsBacking = new System.Collections.Generic.List<string>(newConfig.UOSearchPaths);
                lastUpdateCheckBacking = newConfig.LastUpdateCheck;

                statusLabel.Text = "Settings reset to defaults (not saved yet)";
                statusLabel.ForeColor = Color.Orange;
            }
        }
    }
}
