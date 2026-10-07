using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using MeesaMultisMaker.ComfyUI;
using MeesaMultisMaker.AI;

namespace MeesaMultisMaker.Controls
{
    /// <summary>
    /// Docked panel containing AI/ComfyUI settings for batch image generation.
    /// Replaces the popup BatchSettingsDialog.
    /// </summary>
    public class AISettingsPanel : UserControl
    {
        // AI Backend selection
        private ComboBox aiBackendComboBox;
        private Label aiBackendStatusLabel;
        
        private TextBox urlBox;
        private TextBox promptBox;
        private TextBox negBox;
        private NumericUpDown stepsUpDown;
        private NumericUpDown cfgUpDown;
        private NumericUpDown denoiseUpDown;
        private TextBox seedBox;
        private ComboBox samplerBox;
        private ComboBox schedulerBox;
        private ComboBox checkpointBox;
        private NumericUpDown widthUpDown;
        private NumericUpDown heightUpDown;
        
        private Panel headerPanel;
        private Panel contentPanel;
        private Button collapseButton;
        private bool _isCollapsed = false;
        private int _expandedWidth = 280;
        
        // Control width constant
        private const int ctrlWidth = 260;
        
        // Action buttons
        private Button aiRegenSelectedBtn;
        private Button aiRegenAsOneBtn;
        private Button aiStopBtn;
        private Button revertAiBtn;
        private Button revertOgBtn;
        private Button unifyIdsBtn;
        private Button saveToMulBtn;
        private Button saveAsNewBtn;
        private Button saveCanvasAsNewBtn;
        private Button pushToJarJarBtn;
        private CheckBox autoPushJarJarCheckBox;
        private Button oldNewBtn;
        
        // Status label
        private Label statusLabel;
        private Label pendingChangesLabel;

        // Drop black pixels controls
        private CheckBox dropBlackPixelsCheckBox;
        private NumericUpDown blackThresholdUpDown;
        private CheckBox preserveAlphaCheckBox;

        // ControlNet controls
        private CheckBox useControlNetCheckBox;
        private TextBox controlNetModelBox;
        private NumericUpDown cnStrengthUpDown;
        private NumericUpDown cnStartPercentUpDown;
        private NumericUpDown cnEndPercentUpDown;
        private NumericUpDown cnBlurRadiusUpDown;
        private NumericUpDown cnBlurSigmaUpDown;
        private NumericUpDown cnBGThresholdUpDown;
        private NumericUpDown cnBGFeatherUpDown;
        private ComboBox cnChannelModeBox;
        private NumericUpDown cnDespillUpDown;
        private ComboBox cnInvertMaskBox;
        private Panel controlNetSettingsPanel;
        private int controlNetExpandedHeight;
        private int controlNetReservedHeight;

        // ComfyUI connection status indicator
        private Panel comfyStatusIndicator;
        private Panel urlStatusIndicator;
        private Timer comfyStatusTimer;
        private bool _isCheckingComfyStatus = false;
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        // Since we only support ComfyUI now, local backends are not available
        private static bool IsLocalBackend(AIBackend backend)
        {
            return false;
        }

        private static bool IsSelectedLocalBackendAvailable(AIBackend backend)
        {
            return false;
        }

        public string ComfyUrl => urlBox.Text.Trim();
        public string Prompt => promptBox.Text.Trim();
        public string NegativePrompt => negBox.Text.Trim();
        public int Steps => (int)stepsUpDown.Value;
        public double Cfg => (double)cfgUpDown.Value;
        public double Denoise => (double)denoiseUpDown.Value;
        public string Sampler => samplerBox.SelectedItem?.ToString() ?? "euler";
        public string Scheduler => schedulerBox.SelectedItem?.ToString() ?? "simple";
        public string Checkpoint => checkpointBox?.SelectedItem?.ToString() ?? Text2ImageWorkflow.DEFAULT_CHECKPOINT;
        public int ResolutionWidth => (int)widthUpDown.Value;
        public int ResolutionHeight => (int)heightUpDown.Value;
        
        // Drop black pixels properties
        public bool DropBlackPixels => dropBlackPixelsCheckBox.Checked;
        public int BlackThreshold => (int)blackThresholdUpDown.Value;
        public bool PreserveAlphaMask => preserveAlphaCheckBox?.Checked ?? true;

        // ControlNet properties
        public bool UseControlNet => useControlNetCheckBox?.Checked ?? false;
        public string ControlNetModel => controlNetModelBox?.Text?.Trim() ?? ControlNetDepthWorkflow.DEFAULT_CONTROLNET_MODEL;
        public double ControlNetStrength => (double)(cnStrengthUpDown?.Value ?? 0.5M);
        public double ControlNetStartPercent => (double)(cnStartPercentUpDown?.Value ?? 0M);
        public double ControlNetEndPercent => (double)(cnEndPercentUpDown?.Value ?? 1M);
        public int ControlNetBlurRadius => (int)(cnBlurRadiusUpDown?.Value ?? 7);
        public double ControlNetBlurSigma => (double)(cnBlurSigmaUpDown?.Value ?? 5M);
        public double ControlNetBGThreshold => (double)(cnBGThresholdUpDown?.Value ?? 0.03M);
        public double ControlNetBGFeather => (double)(cnBGFeatherUpDown?.Value ?? 0M);
        public string ControlNetChannelMode => cnChannelModeBox?.SelectedItem?.ToString() ?? "rgb_max";
        public double ControlNetDespill => (double)(cnDespillUpDown?.Value ?? 0M);
        public string ControlNetInvertMask => cnInvertMaskBox?.SelectedItem?.ToString() ?? "no";

        // AI Backend selection property
        public AIBackend SelectedBackend
        {
            get
            {
                if (aiBackendComboBox == null || aiBackendComboBox.SelectedIndex < 0)
                    return AIBackend.ComfyUI;
                return (AIBackend)aiBackendComboBox.SelectedIndex;
            }
            set
            {
                if (aiBackendComboBox != null)
                {
                    aiBackendComboBox.SelectedIndex = (int)value;
                }
            }
        }
        
        public long? Seed
        {
            get
            {
                long parsedSeed;
                if (!string.IsNullOrWhiteSpace(seedBox.Text) && long.TryParse(seedBox.Text, out parsedSeed))
                    return parsedSeed < 0 ? (long?)null : parsedSeed;
                return null;
            }
        }

        // Events for button clicks
        public event EventHandler AIRegenSelectedClicked;
        public event EventHandler AIRegenAsOneClicked;
        public event EventHandler StopAIGenerationClicked;
        public event EventHandler RevertAIChangesClicked;
        public event EventHandler RevertOGClicked;
        public event EventHandler UnifyIdsClicked;
        public event EventHandler SaveToMulClicked;
        public event EventHandler SaveToNewSlotClicked;
        public event EventHandler SaveCanvasAsNewClicked;
        public event EventHandler PushToJarJarClicked;
        public event EventHandler OldNewClicked;

        /// <summary>
        /// Whether auto-push to JarJar is enabled
        /// </summary>
        public bool AutoPushEnabled => autoPushJarJarCheckBox?.Checked ?? false;
        
        // Events for black pixel settings changes (for post-generation adjustment)
        public event EventHandler BlackPixelSettingsChanged;

        public AISettingsPanel()
        {
            InitializeComponent();
            LoadDefaultsFromConfig();
            
            // Apply the initial backend UI state AFTER all controls are created
            // This ensures ComfyUI-specific controls are hidden when Z Image is default
            ApplyBackendUIState(SelectedBackend);
            
            InitializeComfyStatusTimer();
        }

        /// <summary>
        /// Apply UI visibility state based on selected backend
        /// </summary>
        private void ApplyBackendUIState(AIBackend backend)
        {
            if (IsLocalBackend(backend))
            {
                // Hide ComfyUI URL controls for local backends
                if (urlBox != null)
                {
                    urlBox.Visible = false;
                    urlBox.Enabled = false;
                }
                if (urlStatusIndicator != null) urlStatusIndicator.Visible = false;
                if (checkpointBox != null)
                {
                    checkpointBox.Visible = true;
                    checkpointBox.Enabled = false;
                }

                // Hide ControlNet controls (ComfyUI only)
                if (useControlNetCheckBox != null)
                {
                    useControlNetCheckBox.Visible = true;
                    useControlNetCheckBox.Enabled = false;
                }
                if (controlNetSettingsPanel != null) controlNetSettingsPanel.Visible = false;

                // Keep sampler/scheduler visible
                if (samplerBox != null) samplerBox.Visible = true;
                if (schedulerBox != null) schedulerBox.Visible = true;
                
                // Keep labels visible and disable ComfyUI action buttons
                foreach (Control ctrl in contentPanel.Controls)
                {
                    if (ctrl is Label lbl)
                    {
                        if (lbl.Text == "ComfyUI URL" || lbl.Text == "Checkpoint" ||
                            lbl.Text == "Sampler" || lbl.Text == "Scheduler")
                        {
                            lbl.Visible = lbl.Text != "ComfyUI URL";
                        }
                    }
                    if (ctrl is Button btn)
                    {
                        // Hide URL refresh button; keep checkpoint refresh visible but disabled
                        if (btn.Left == ctrlWidth - 20 || btn.Left == 230)
                        {
                            if (btn.Left == ctrlWidth - 20)
                            {
                                btn.Visible = false;
                            }
                            else
                            {
                                btn.Visible = true;
                                btn.Enabled = false;
                            }
                        }
                    }
                }
                
                // Update status
                aiBackendStatusLabel.Text = "? Ready";
                aiBackendStatusLabel.ForeColor = Color.FromArgb(0, 255, 100);
            }
            else // ComfyUI
            {
                // Show all ComfyUI controls
                if (urlBox != null) urlBox.Visible = true;
                if (urlBox != null) urlBox.Enabled = true;
                if (urlStatusIndicator != null) urlStatusIndicator.Visible = true;
                if (samplerBox != null) samplerBox.Visible = true;
                if (schedulerBox != null) schedulerBox.Visible = true;
                if (checkpointBox != null) checkpointBox.Visible = true;
                if (checkpointBox != null) checkpointBox.Enabled = true;

                // Show ControlNet checkbox (panel depends on checkbox state)
                if (useControlNetCheckBox != null)
                {
                    useControlNetCheckBox.Visible = true;
                    useControlNetCheckBox.Enabled = true;
                }
                if (controlNetSettingsPanel != null)
                    controlNetSettingsPanel.Visible = useControlNetCheckBox?.Checked ?? false;
                
                // Show all labels and buttons
                foreach (Control ctrl in contentPanel.Controls)
                {
                    if (ctrl is Label lbl)
                    {
                        if (lbl.Text == "ComfyUI URL" || lbl.Text == "Sampler" || 
                            lbl.Text == "Scheduler" || lbl.Text == "Checkpoint")
                        {
                            lbl.Visible = true;
                        }
                    }
                    if (ctrl is Button btn)
                    {
                        if (btn.Left == ctrlWidth - 20 || btn.Left == 230)
                        {
                            btn.Visible = true;
                            btn.Enabled = true;
                        }
                    }
                }
                
                aiBackendStatusLabel.Text = "??? Server";
                aiBackendStatusLabel.ForeColor = HolographicTheme.TextMuted;
            }

            UpdateControlNetLayout();
        }

        private void UpdateControlNetLayout()
        {
            if (controlNetSettingsPanel == null || contentPanel == null)
                return;

            bool showControlNetDetails = useControlNetCheckBox != null &&
                                         useControlNetCheckBox.Visible &&
                                         useControlNetCheckBox.Enabled &&
                                         useControlNetCheckBox.Checked;

            int targetReservedHeight = showControlNetDetails ? controlNetExpandedHeight : 0;
            if (targetReservedHeight == controlNetReservedHeight)
                return;

            int oldBoundaryTop = controlNetSettingsPanel.Top + controlNetReservedHeight;
            int delta = targetReservedHeight - controlNetReservedHeight;

            foreach (Control ctrl in contentPanel.Controls)
            {
                if (ctrl == controlNetSettingsPanel)
                    continue;

                if (ctrl.Top >= oldBoundaryTop)
                    ctrl.Top += delta;
            }

            controlNetSettingsPanel.Visible = showControlNetDetails;
            controlNetReservedHeight = targetReservedHeight;
        }

        /// <summary>
        /// Initialize the timer for checking ComfyUI status
        /// </summary>
        private void InitializeComfyStatusTimer()
        {
            comfyStatusTimer = new Timer();
            comfyStatusTimer.Interval = 60000; // 60 seconds
            comfyStatusTimer.Tick += async (s, e) => await CheckComfyUIStatus();
            
            // Only start timer if ComfyUI backend is selected
            // This prevents checking ComfyUI when Z Image is active
            if (AppConfig.Instance.SelectedAIBackend == AIBackend.ComfyUI)
            {
                comfyStatusTimer.Start();
                
                // Also check immediately on startup (after a short delay to let UI initialize)
                Task.Delay(1000).ContinueWith(_ => 
                {
                    if (!this.IsDisposed && this.IsHandleCreated)
                    {
                        this.BeginInvoke(new Action(async () => await CheckComfyUIStatus()));
                    }
                });
            }
            else
            {
                // Local backend is selected, show appropriate status
                string backendName = ImageGeneratorFactory.GetBackendName(SelectedBackend);
                SetStatus(backendName + " selected", Color.FromArgb(0, 200, 255));
                SetComfyStatusIndicator(true, backendName);
                SetUrlStatusIndicator(true, backendName);
            }
        }

        /// <summary>
        /// Check if ComfyUI server is alive and update the status indicator
        /// </summary>
        private async Task CheckComfyUIStatus()
        {
            if (_isCheckingComfyStatus) return;
            
            // Skip ComfyUI check if a local backend is selected
            if (IsLocalBackend(SelectedBackend))
            {
                string backendName = ImageGeneratorFactory.GetBackendName(SelectedBackend);
                SetComfyStatusIndicator(true, backendName);
                SetUrlStatusIndicator(true, backendName);
                return;
            }
            
            _isCheckingComfyStatus = true;
            
            try
            {
                string url = ComfyUrl;
                System.Diagnostics.Debug.WriteLine($"[ComfyUI Test] Starting connection test. URL: {url}");
                if (string.IsNullOrWhiteSpace(url))
                {
                    System.Diagnostics.Debug.WriteLine("[ComfyUI Test] No URL provided");
                    SetComfyStatusIndicator(false, "No URL");
                    SetUrlStatusIndicator(false, "No URL");
                    return;
                }
                
                // Normalize URL
                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                    url = "http://" + url;
                System.Diagnostics.Debug.WriteLine($"[ComfyUI Test] Normalized URL: {url}");
                
                // Try to hit the ComfyUI root endpoint (simple connectivity check)
                string checkUrl = url.TrimEnd('/');
                System.Diagnostics.Debug.WriteLine($"[ComfyUI Test] Testing endpoint: {checkUrl}");
                
                using (var cts = new System.Threading.CancellationTokenSource())
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(3)); // 3 seconds timeout for quick feedback
                    System.Diagnostics.Debug.WriteLine("[ComfyUI Test] Sending HTTP GET request...");
                    var response = await _httpClient.GetAsync(checkUrl, cts.Token);
                    bool isAlive = response.IsSuccessStatusCode;
                    System.Diagnostics.Debug.WriteLine($"[ComfyUI Test] Response received. Status: {response.StatusCode}, Success: {isAlive}");
                    
                    SetComfyStatusIndicator(isAlive, isAlive ? "Connected" : $"Error: {response.StatusCode}");
                    SetUrlStatusIndicator(isAlive, isAlive ? "Connected" : $"Error: {response.StatusCode}"); // Update URL status indicator
                    
                    // If connected, refresh the checkpoint list automatically
                    if (isAlive)
                    {
                        System.Diagnostics.Debug.WriteLine("[ComfyUI Test] Connection successful, refreshing checkpoints...");
                        await RefreshCheckpoints();
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ComfyUI Test] HTTP Request Exception: {ex.Message}");
                SetComfyStatusIndicator(false, "Offline");
                SetUrlStatusIndicator(false, "Offline"); // Update URL status indicator
            }
            catch (TaskCanceledException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ComfyUI Test] Timeout Exception: {ex.Message}");
                SetComfyStatusIndicator(false, "Timeout");
                SetUrlStatusIndicator(false, "Timeout"); // Update URL status indicator
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ComfyUI Test] General Exception: {ex.Message}");
                SetComfyStatusIndicator(false, $"Error: {ex.Message}");
                SetUrlStatusIndicator(false, $"Error: {ex.Message}"); // Update URL status indicator
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine("[ComfyUI Test] Test completed");
                _isCheckingComfyStatus = false;
            }
        }

        /// <summary>
        /// Update the ComfyUI status indicator color
        /// </summary>
        private void SetComfyStatusIndicator(bool isOnline, string tooltip)
        {
            if (urlStatusIndicator == null) return;
            
            if (urlStatusIndicator.InvokeRequired)
            {
                urlStatusIndicator.Invoke(new Action(() => SetComfyStatusIndicator(isOnline, tooltip)));
                return;
            }
            
            urlStatusIndicator.BackColor = isOnline ? Color.FromArgb(0, 255, 100) : Color.FromArgb(255, 60, 60);
            
            // Update tooltip
            var existingTooltip = urlStatusIndicator.Tag as ToolTip;
            if (existingTooltip == null)
            {
                existingTooltip = new ToolTip();
                urlStatusIndicator.Tag = existingTooltip;
            }
            existingTooltip.SetToolTip(urlStatusIndicator, $"ComfyUI: {tooltip}");
        }

        /// <summary>
        /// Update the URL status indicator color
        /// </summary>
        private void SetUrlStatusIndicator(bool isOnline, string tooltip)
        {
            if (urlStatusIndicator == null) return;
            
            if (urlStatusIndicator.InvokeRequired)
            {
                urlStatusIndicator.Invoke(new Action(() => SetUrlStatusIndicator(isOnline, tooltip)));
                return;
            }
            
            urlStatusIndicator.BackColor = isOnline ? Color.FromArgb(0, 255, 100) : Color.FromArgb(255, 60, 60);
            
            // Update tooltip
            var existingTooltip = urlStatusIndicator.Tag as ToolTip;
            if (existingTooltip == null)
            {
                existingTooltip = new ToolTip();
                urlStatusIndicator.Tag = existingTooltip;
            }
            existingTooltip.SetToolTip(urlStatusIndicator, $"URL: {tooltip}");
        }

        /// <summary>
        /// Force an immediate check of ComfyUI status
        /// </summary>
        public async Task RefreshComfyStatus()
        {
            // If local backend is selected, check local model status instead
            if (IsLocalBackend(SelectedBackend))
            {
                bool available = IsSelectedLocalBackendAvailable(SelectedBackend);
                string backendName = ImageGeneratorFactory.GetBackendName(SelectedBackend);
                if (available)
                {
                    SetStatus(backendName + " ready", Color.FromArgb(0, 255, 100));
                    SetComfyStatusIndicator(true, backendName + " ready");
                    SetUrlStatusIndicator(true, backendName + " ready");
                }
                else
                {
                    SetStatus("Local models not downloaded", Color.Orange);
                    SetComfyStatusIndicator(false, "Models needed");
                    SetUrlStatusIndicator(false, "Models needed");
                }
                return;
            }
            
            await CheckComfyUIStatus();
        }

        /// <summary>
        /// Loads default values from AppConfig
        /// </summary>
        private void LoadDefaultsFromConfig()
        {
            var config = AppConfig.Instance;
            
            // Load AI backend selection
            if (aiBackendComboBox != null)
            {
                aiBackendComboBox.SelectedIndex = (int)config.SelectedAIBackend;
            }
            
            if (urlBox != null)
                urlBox.Text = config.ComfyUIUrl;
            if (promptBox != null)
                promptBox.Text = config.DefaultPrompt;
            if (negBox != null)
                negBox.Text = config.DefaultNegativePrompt;
            if (stepsUpDown != null)
                stepsUpDown.Value = config.DefaultSteps;
            if (cfgUpDown != null)
                cfgUpDown.Value = (decimal)config.DefaultCFG;
            if (denoiseUpDown != null)
                denoiseUpDown.Value = (decimal)config.DefaultDenoise;
            if (widthUpDown != null)
                widthUpDown.Value = config.DefaultResolution;
            if (heightUpDown != null)
                heightUpDown.Value = config.DefaultResolution;
                
            // Set sampler
            if (samplerBox != null)
            {
                int samplerIndex = samplerBox.Items.IndexOf(config.DefaultSampler);
                if (samplerIndex >= 0) samplerBox.SelectedIndex = samplerIndex;
            }
            
            // Set scheduler
            if (schedulerBox != null)
            {
                int schedulerIndex = schedulerBox.Items.IndexOf(config.DefaultScheduler);
                if (schedulerIndex >= 0) schedulerBox.SelectedIndex = schedulerIndex;
            }

            // Set checkpoint to default if available
            if (checkpointBox != null && checkpointBox.Items.Count > 0)
            {
                int defaultIdx = checkpointBox.Items.IndexOf(Text2ImageWorkflow.DEFAULT_CHECKPOINT);
                checkpointBox.SelectedIndex = defaultIdx >= 0 ? defaultIdx : 0;
            }

            // Load ControlNet settings
            if (useControlNetCheckBox != null)
                useControlNetCheckBox.Checked = config.UseControlNet;
            if (controlNetModelBox != null)
                controlNetModelBox.Text = config.ControlNetModel;
            if (cnStrengthUpDown != null)
                cnStrengthUpDown.Value = (decimal)config.ControlNetStrength;
            if (cnStartPercentUpDown != null)
                cnStartPercentUpDown.Value = (decimal)config.ControlNetStartPercent;
            if (cnEndPercentUpDown != null)
                cnEndPercentUpDown.Value = (decimal)config.ControlNetEndPercent;
            if (cnBlurRadiusUpDown != null)
                cnBlurRadiusUpDown.Value = config.ControlNetBlurRadius;
            if (cnBlurSigmaUpDown != null)
                cnBlurSigmaUpDown.Value = (decimal)config.ControlNetBlurSigma;
            if (cnBGThresholdUpDown != null)
                cnBGThresholdUpDown.Value = (decimal)config.ControlNetBGThreshold;
            if (cnBGFeatherUpDown != null)
                cnBGFeatherUpDown.Value = (decimal)config.ControlNetBGFeather;
            if (cnChannelModeBox != null)
            {
                int idx = cnChannelModeBox.Items.IndexOf(config.ControlNetChannelMode);
                if (idx >= 0) cnChannelModeBox.SelectedIndex = idx;
            }
            if (cnDespillUpDown != null)
                cnDespillUpDown.Value = (decimal)config.ControlNetDespill;
            if (cnInvertMaskBox != null)
            {
                int idx = cnInvertMaskBox.Items.IndexOf(config.ControlNetInvertMask);
                if (idx >= 0) cnInvertMaskBox.SelectedIndex = idx;
            }
        }

        /// <summary>
        /// Sets the status message displayed at the bottom of the panel
        /// </summary>
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

        /// <summary>
        /// Updates the pending changes count display
        /// </summary>
        public void SetPendingChanges(int count)
        {
            if (pendingChangesLabel == null || saveToMulBtn == null) return;

            if (pendingChangesLabel.InvokeRequired)
            {
                pendingChangesLabel.Invoke(new Action(() => SetPendingChanges(count)));
                return;
            }

            if (count == 0)
            {
                pendingChangesLabel.Text = "No pending changes";
                pendingChangesLabel.ForeColor = HolographicTheme.TextMuted;
                saveToMulBtn.Enabled = false;
                if (saveAsNewBtn != null) saveAsNewBtn.Enabled = false;
            }
            else
            {
                pendingChangesLabel.Text = $"{count} item(s) modified";
                pendingChangesLabel.ForeColor = HolographicTheme.ButtonWarning;
                saveToMulBtn.Enabled = true;
                if (saveAsNewBtn != null) saveAsNewBtn.Enabled = true;
            }
        }

        /// <summary>
        /// Enable or disable the Push to JarJar button independently of Save Art state.
        /// </summary>
        public void SetPushToJarJarEnabled(bool enabled)
        {
            if (pushToJarJarBtn == null) return;

            if (pushToJarJarBtn.InvokeRequired)
            {
                pushToJarJarBtn.Invoke(new Action(() => SetPushToJarJarEnabled(enabled)));
                return;
            }

            pushToJarJarBtn.Enabled = enabled;
        }

        private void InitializeComponent()
        {
            // Use holographic theme colors
            this.BackColor = HolographicTheme.PanelBackground;
            this.Dock = DockStyle.Fill;
            this.MinimumSize = new Size(30, 0);

            // Create shared tooltip for all controls
            var panelTooltip = new ToolTip
            {
                AutoPopDelay = 5000,
                InitialDelay = 500,
                ReshowDelay = 200,
                ShowAlways = true
            };

            // Header panel with collapse button - holographic style
            headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 32,
                BackColor = HolographicTheme.ControlBackground,
                Padding = new Padding(8, 5, 5, 5)
            };

            var titleLabel = new Label
            {
                Text = "AI SETTINGS",
                ForeColor = HolographicTheme.CyanAccent,
                Font = new Font("Consolas", 10, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            collapseButton = new Button
            {
                Text = "?",
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
            panelTooltip.SetToolTip(collapseButton, "Show help / collapse panel");

            headerPanel.Controls.Add(titleLabel);
            headerPanel.Controls.Add(collapseButton);

            // Content panel (scrollable - vertical only)
            contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(8),
                BackColor = HolographicTheme.PanelBackground
            };

            // Wider control widths for better readability
            int rowWidth = 265;

            int y = 5;

            // AI Backend Selection - NEW!
            var lblBackend = CreateThemedLabel("AI Backend", 5, y, 80, true);
            panelTooltip.SetToolTip(lblBackend, "Select which AI backend to use for generation");
            contentPanel.Controls.Add(lblBackend);
            y += 20;

            aiBackendComboBox = CreateThemedComboBox(5, y, 180);
            aiBackendComboBox.Items.AddRange(new object[] { "ComfyUI (Server)" });
            aiBackendComboBox.SelectedIndex = 0;
            aiBackendComboBox.SelectedIndexChanged += AIBackendComboBox_SelectedIndexChanged;
            panelTooltip.SetToolTip(aiBackendComboBox, "ComfyUI requires a running server.");
            contentPanel.Controls.Add(aiBackendComboBox);

            aiBackendStatusLabel = new Label
            {
                Left = 190,
                Top = y + 3,
                Width = 100,
                Height = 18,
                Text = "? Ready",
                ForeColor = HolographicTheme.TextMuted,
                Font = new Font("Consolas", 7.5f)
            };
            panelTooltip.SetToolTip(aiBackendStatusLabel, "Backend availability status");
            contentPanel.Controls.Add(aiBackendStatusLabel);
            y += 30;

            // Separator
            var backendSeparator = new Label
            {
                Left = 5,
                Top = y,
                Width = rowWidth,
                Height = 1,
                BackColor = HolographicTheme.BorderCyan
            };
            contentPanel.Controls.Add(backendSeparator);
            y += 12;

            // ComfyUI URL
            var lblUrl = CreateThemedLabel("ComfyUI URL", 5, y, 100, true);
            panelTooltip.SetToolTip(lblUrl, "URL of your local or remote ComfyUI server");
            contentPanel.Controls.Add(lblUrl);
            
            // Status indicator (green/red circle) next to label
            urlStatusIndicator = new Panel
            {
                Left = 110,
                Top = y + 2,
                Width = 12,
                Height = 12,
                BackColor = Color.Gray, // Initial state: unknown
            };
            urlStatusIndicator.Paint += (s, pe) =>
            {
                pe.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(urlStatusIndicator.BackColor))
                {
                    pe.Graphics.FillEllipse(brush, 0, 0, 11, 11);
                }
                using (var pen = new Pen(Color.FromArgb(100, 255, 255, 255), 1))
                {
                    pe.Graphics.DrawEllipse(pen, 0, 0, 11, 11);
                }
            };
            panelTooltip.SetToolTip(urlStatusIndicator, "Connection status indicator (green=connected, red=offline)");
            contentPanel.Controls.Add(urlStatusIndicator);
            y += 22;

            urlBox = CreateThemedTextBox(5, y, ctrlWidth - 30, "http://localhost:8188");
            urlBox.TextChanged += async (s, ev) => 
            {
                await Task.Delay(500);
                await CheckComfyUIStatus();
            };
            panelTooltip.SetToolTip(urlBox, "URL of your ComfyUI server (e.g., http://127.0.0.1:8188)");
            contentPanel.Controls.Add(urlBox);
            
            // Refresh button next to URL
            var refreshBtn = new Button
            {
                Left = ctrlWidth - 20,
                Top = y,
                Width = 24,
                Height = 22,
                Text = "?",
                FlatStyle = FlatStyle.Flat,
                BackColor = HolographicTheme.ButtonBackground,
                ForeColor = HolographicTheme.CyanAccent,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Padding = new Padding(0)
            };
            refreshBtn.FlatAppearance.BorderColor = HolographicTheme.BorderCyan;
            refreshBtn.FlatAppearance.BorderSize = 1;
            refreshBtn.Click += async (s, ev) => await CheckComfyUIStatus();
            panelTooltip.SetToolTip(refreshBtn, "Test ComfyUI connection");
            contentPanel.Controls.Add(refreshBtn);
            y += 30;

            // Prompt
            var lblPrompt = CreateThemedLabel("Prompt", 5, y, 80, true);
            lblPrompt.Font = new Font("Consolas", 9, FontStyle.Bold);
            panelTooltip.SetToolTip(lblPrompt, "Positive prompt - describe what you want the AI to generate");
            contentPanel.Controls.Add(lblPrompt);
            y += 20;

            promptBox = CreateThemedTextBox(5, y, ctrlWidth, "isometric ship in new art style");
            promptBox.Height = 72;
            promptBox.Multiline = true;
            promptBox.ScrollBars = ScrollBars.Vertical;
            panelTooltip.SetToolTip(promptBox, "Positive prompt describing what you want to generate");
            contentPanel.Controls.Add(promptBox);
            y += 80;

            // Negative
            var lblNeg = CreateThemedLabel("Negative", 5, y, 80);
            panelTooltip.SetToolTip(lblNeg, "Negative prompt - describe what to avoid in the output");
            contentPanel.Controls.Add(lblNeg);
            y += 20;

            negBox = CreateThemedTextBox(5, y, ctrlWidth, "blurry, lowres");
            negBox.Height = 52;
            negBox.Multiline = true;
            negBox.ScrollBars = ScrollBars.Vertical;
            panelTooltip.SetToolTip(negBox, "Negative prompt describing what to avoid");
            contentPanel.Controls.Add(negBox);
            y += 60;

            // Resolution Width/Height row - better spacing
            var lblWidth = CreateThemedLabel("Width", 5, y + 3, 40);
            panelTooltip.SetToolTip(lblWidth, "Output image width in pixels (64-2048)");
            contentPanel.Controls.Add(lblWidth);

            widthUpDown = CreateThemedNumericUpDown(50, y, 55, 64, 2048, 512, 64);
            panelTooltip.SetToolTip(widthUpDown, "Output image width in pixels");
            contentPanel.Controls.Add(widthUpDown);

            var lblHeight = CreateThemedLabel("Height", 115, y + 3, 45);
            panelTooltip.SetToolTip(lblHeight, "Output image height in pixels (64-2048)");
            contentPanel.Controls.Add(lblHeight);

            heightUpDown = CreateThemedNumericUpDown(165, y, 55, 64, 2048, 512, 64);
            panelTooltip.SetToolTip(heightUpDown, "Output image height in pixels");
            contentPanel.Controls.Add(heightUpDown);

            // Square button to link/set same
            var squareBtn = CreateThemedButton("=", 225, y, 28, 22);
            squareBtn.Click += (s, e) => { heightUpDown.Value = widthUpDown.Value; };
            panelTooltip.SetToolTip(squareBtn, "Set Height = Width (make square)");
            contentPanel.Controls.Add(squareBtn);
            y += 30;

            // Steps, CFG, Denoise row - improved layout
            var lblSteps = CreateThemedLabel("Steps", 5, y + 3, 38);
            panelTooltip.SetToolTip(lblSteps, "Diffusion steps - more steps = better quality but slower (15-30 typical)");
            contentPanel.Controls.Add(lblSteps);

            stepsUpDown = CreateThemedNumericUpDown(45, y, 45, 1, 100, 20);
            panelTooltip.SetToolTip(stepsUpDown, "Number of diffusion steps (more = better quality, slower)");
            contentPanel.Controls.Add(stepsUpDown);

            var lblCfg = CreateThemedLabel("CFG", 100, y + 3, 30);
            panelTooltip.SetToolTip(lblCfg, "Classifier-Free Guidance scale - how closely to follow prompt (5-10 typical)");
            contentPanel.Controls.Add(lblCfg);

            cfgUpDown = CreateThemedNumericUpDown(130, y, 50, 1, 30, 6, 0.5M, 1);
            panelTooltip.SetToolTip(cfgUpDown, "CFG scale - how closely to follow prompt (7-8 typical)");
            contentPanel.Controls.Add(cfgUpDown);

            var lblDenoise = CreateThemedLabel("Denoise", 190, y + 3, 50);
            panelTooltip.SetToolTip(lblDenoise, "Denoise strength - 0=no change, 1=full regeneration (0.5-0.8 typical)");
            contentPanel.Controls.Add(lblDenoise);

            denoiseUpDown = CreateThemedNumericUpDown(245, y, 50, 0, 1, 0.50M, 0.05M, 2);
            panelTooltip.SetToolTip(denoiseUpDown, "Denoise strength (0=no change, 1=full regeneration)");
            contentPanel.Controls.Add(denoiseUpDown);
            y += 30;

            // Seed - full width
            var lblSeed = CreateThemedLabel("Seed", 5, y + 3, 40);
            panelTooltip.SetToolTip(lblSeed, "Random seed - use same seed to reproduce results");
            contentPanel.Controls.Add(lblSeed);

            var lblSeedHint = CreateThemedLabel("(empty = random)", 50, y + 3, 100);
            lblSeedHint.ForeColor = HolographicTheme.TextMuted;
            lblSeedHint.Font = new Font("Consolas", 7.5f);
            panelTooltip.SetToolTip(lblSeedHint, "Leave empty for random seed each generation");
            contentPanel.Controls.Add(lblSeedHint);

            seedBox = CreateThemedTextBox(155, y, ctrlWidth - 150, "");
            panelTooltip.SetToolTip(seedBox, "Random seed for reproducible results (leave empty for random)");
            contentPanel.Controls.Add(seedBox);
            y += 30;

            // Sampler row - better spacing
            var lblSampler = CreateThemedLabel("Sampler", 5, y + 3, 55);
            panelTooltip.SetToolTip(lblSampler, "Sampling algorithm - affects quality and style of output");
            contentPanel.Controls.Add(lblSampler);

            samplerBox = CreateThemedComboBox(65, y, 90);
            // Full list of samplers supported by both ComfyUI and sd-cli
            samplerBox.Items.AddRange(new object[] { 
                "euler",           // Fast, good general purpose
                "euler_ancestral", // More creative/varied
                "heun",            // Higher quality, slower
                "dpm2",            // Good quality
                "dpm2_ancestral",  // More varied
                "dpmpp_2m",        // High quality, good for img2img
                "dpmpp_2m_sde",    // Stochastic version
                "dpmpp_sde",       // SDE variant
                "dpmpp_2s_ancestral", // Ancestral version
                "uni_pc",          // Fast, good quality
                "lcm"              // Very fast (needs LCM model)
            });
            samplerBox.SelectedIndex = 0; // euler default
            panelTooltip.SetToolTip(samplerBox, "euler=fast, dpmpp_2m=quality/structure, uni_pc=balanced");
            contentPanel.Controls.Add(samplerBox);

            var lblScheduler = CreateThemedLabel("Scheduler", 165, y + 3, 60);
            panelTooltip.SetToolTip(lblScheduler, "Noise schedule - affects how noise is applied during generation");
            contentPanel.Controls.Add(lblScheduler);

            schedulerBox = CreateThemedComboBox(230, y, 70);
            // Full list of schedulers supported by both ComfyUI and sd-cli
            schedulerBox.Items.AddRange(new object[] { 
                "simple",      // Basic linear schedule
                "normal",      // Standard schedule
                "karras",      // Karras et al. - often better detail
                "sgm_uniform", // Uniform SGM - good for img2img
                "exponential", // Exponential schedule
                "ddim_uniform" // DDIM uniform
            });
            schedulerBox.SelectedIndex = 0; // simple default
            panelTooltip.SetToolTip(schedulerBox, "simple=basic, karras=detail, sgm_uniform=img2img structure");
            contentPanel.Controls.Add(schedulerBox);
            y += 32;

            // Checkpoint row - improved
            var lblCheckpoint = CreateThemedLabel("Checkpoint", 5, y + 3, 70);
            panelTooltip.SetToolTip(lblCheckpoint, "AI model checkpoint - different models produce different styles");
            contentPanel.Controls.Add(lblCheckpoint);

            checkpointBox = CreateThemedComboBox(80, y, 145);
            checkpointBox.Items.AddRange(GetCheckpointOptions());
            checkpointBox.SelectedIndex = 0;
            checkpointBox.DropDownWidth = 300;
            panelTooltip.SetToolTip(checkpointBox, "AI model checkpoint to use for generation");
            contentPanel.Controls.Add(checkpointBox);
            
            // Refresh checkpoints button
            var refreshCkptBtn = new Button
            {
                Left = 230,
                Top = y,
                Width = 24,
                Height = 22,
                Text = "?",
                FlatStyle = FlatStyle.Flat,
                BackColor = HolographicTheme.ButtonBackground,
                ForeColor = HolographicTheme.CyanAccent,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Padding = new Padding(0)
            };
            refreshCkptBtn.FlatAppearance.BorderColor = HolographicTheme.BorderCyan;
            refreshCkptBtn.FlatAppearance.BorderSize = 1;
            refreshCkptBtn.Click += async (s, ev) => 
            {
                refreshCkptBtn.Enabled = false;
                await RefreshCheckpoints();
                refreshCkptBtn.Enabled = true;
            };
            panelTooltip.SetToolTip(refreshCkptBtn, "Refresh checkpoint list from ComfyUI server");
            contentPanel.Controls.Add(refreshCkptBtn);
            y += 32;

            // ===== ControlNet Section =====
            var cnSeparator = new Label
            {
                Left = 5,
                Top = y,
                Width = rowWidth,
                Height = 1,
                BackColor = HolographicTheme.BorderCyan
            };
            contentPanel.Controls.Add(cnSeparator);
            y += 8;

            useControlNetCheckBox = new CheckBox
            {
                Text = "Use ControlNet (Depth)",
                Left = 5,
                Top = y,
                Width = 180,
                Checked = false,
                ForeColor = Color.FromArgb(255, 180, 50),
                BackColor = Color.Transparent,
                Font = new Font("Consolas", 9f, FontStyle.Bold)
            };
            useControlNetCheckBox.CheckedChanged += (s, e) =>
            {
                UpdateControlNetLayout();
            };
            panelTooltip.SetToolTip(useControlNetCheckBox, "Enable ControlNet depth workflow with mask + blur preprocessing");
            contentPanel.Controls.Add(useControlNetCheckBox);
            y += 24;

            // ControlNet settings container panel
            controlNetSettingsPanel = new Panel
            {
                Left = 0,
                Top = y,
                Width = rowWidth + 10,
                AutoSize = true,
                BackColor = Color.Transparent,
                Visible = false
            };
            int cy = 0; // local y within the panel

            // ControlNet Model
            var lblCnModel = CreateThemedLabel("CN Model", 5, cy + 3, 60);
            panelTooltip.SetToolTip(lblCnModel, "ControlNet model file (depth model)");
            controlNetSettingsPanel.Controls.Add(lblCnModel);

            controlNetModelBox = CreateThemedTextBox(70, cy, 195, ControlNetDepthWorkflow.DEFAULT_CONTROLNET_MODEL);
            controlNetModelBox.Font = new Font("Consolas", 7.5f);
            panelTooltip.SetToolTip(controlNetModelBox, "ControlNet model path, e.g. 1.5\\control_v11f1p_sd15_depth_fp16.safetensors");
            controlNetSettingsPanel.Controls.Add(controlNetModelBox);
            cy += 26;

            // Strength, Start%, End% row
            var lblCnStr = CreateThemedLabel("Strength", 5, cy + 3, 55);
            panelTooltip.SetToolTip(lblCnStr, "ControlNet conditioning strength (0-1)");
            controlNetSettingsPanel.Controls.Add(lblCnStr);

            cnStrengthUpDown = CreateThemedNumericUpDown(65, cy, 50, 0, 1, 0.50M, 0.05M, 2);
            panelTooltip.SetToolTip(cnStrengthUpDown, "ControlNet strength (0=none, 1=full)");
            controlNetSettingsPanel.Controls.Add(cnStrengthUpDown);

            var lblCnStart = CreateThemedLabel("Start", 125, cy + 3, 35);
            panelTooltip.SetToolTip(lblCnStart, "ControlNet start percent (when to begin applying)");
            controlNetSettingsPanel.Controls.Add(lblCnStart);

            cnStartPercentUpDown = CreateThemedNumericUpDown(162, cy, 45, 0, 1, 0M, 0.05M, 3);
            panelTooltip.SetToolTip(cnStartPercentUpDown, "Start percent (0=from beginning)");
            controlNetSettingsPanel.Controls.Add(cnStartPercentUpDown);

            var lblCnEnd = CreateThemedLabel("End", 215, cy + 3, 30);
            panelTooltip.SetToolTip(lblCnEnd, "ControlNet end percent (when to stop applying)");
            controlNetSettingsPanel.Controls.Add(lblCnEnd);

            cnEndPercentUpDown = CreateThemedNumericUpDown(245, cy, 45, 0, 1, 1M, 0.05M, 3);
            panelTooltip.SetToolTip(cnEndPercentUpDown, "End percent (1=until end)");
            controlNetSettingsPanel.Controls.Add(cnEndPercentUpDown);
            cy += 28;

            // Blur Radius, Sigma row
            var lblBlurR = CreateThemedLabel("Blur Radius", 5, cy + 3, 75);
            panelTooltip.SetToolTip(lblBlurR, "Image blur radius for preprocessing (must be odd)");
            controlNetSettingsPanel.Controls.Add(lblBlurR);

            cnBlurRadiusUpDown = CreateThemedNumericUpDown(85, cy, 45, 1, 51, 7, 2);
            panelTooltip.SetToolTip(cnBlurRadiusUpDown, "Blur radius (higher = more blur)");
            controlNetSettingsPanel.Controls.Add(cnBlurRadiusUpDown);

            var lblBlurS = CreateThemedLabel("Sigma", 140, cy + 3, 40);
            panelTooltip.SetToolTip(lblBlurS, "Blur sigma (gaussian spread)");
            controlNetSettingsPanel.Controls.Add(lblBlurS);

            cnBlurSigmaUpDown = CreateThemedNumericUpDown(182, cy, 50, 0, 20, 5M, 0.5M, 1);
            panelTooltip.SetToolTip(cnBlurSigmaUpDown, "Blur sigma value");
            controlNetSettingsPanel.Controls.Add(cnBlurSigmaUpDown);
            cy += 28;

            // BG Threshold, Feather row
            var lblBGThresh = CreateThemedLabel("BG Threshold", 5, cy + 3, 85);
            panelTooltip.SetToolTip(lblBGThresh, "Background removal threshold for MeesaJarJar node");
            controlNetSettingsPanel.Controls.Add(lblBGThresh);

            cnBGThresholdUpDown = CreateThemedNumericUpDown(95, cy, 55, 0, 1, 0.03M, 0.01M, 2);
            panelTooltip.SetToolTip(cnBGThresholdUpDown, "Background removal threshold");
            controlNetSettingsPanel.Controls.Add(cnBGThresholdUpDown);

            var lblBGFeather = CreateThemedLabel("Feather", 160, cy + 3, 50);
            panelTooltip.SetToolTip(lblBGFeather, "Background removal feather (edge softness)");
            controlNetSettingsPanel.Controls.Add(lblBGFeather);

            cnBGFeatherUpDown = CreateThemedNumericUpDown(215, cy, 50, 0, 100, 0M, 1M, 1);
            panelTooltip.SetToolTip(cnBGFeatherUpDown, "Edge feather amount");
            controlNetSettingsPanel.Controls.Add(cnBGFeatherUpDown);
            cy += 28;

            // Channel Mode, Despill row
            var lblChanMode = CreateThemedLabel("Channel", 5, cy + 3, 50);
            panelTooltip.SetToolTip(lblChanMode, "Channel mode for background detection");
            controlNetSettingsPanel.Controls.Add(lblChanMode);

            cnChannelModeBox = CreateThemedComboBox(60, cy, 80);
            cnChannelModeBox.Items.AddRange(new object[] { "rgb_max", "rgb_avg", "red", "green", "blue", "alpha" });
            cnChannelModeBox.SelectedIndex = 0;
            panelTooltip.SetToolTip(cnChannelModeBox, "Channel mode for background removal");
            controlNetSettingsPanel.Controls.Add(cnChannelModeBox);

            var lblDespill = CreateThemedLabel("Despill", 150, cy + 3, 45);
            panelTooltip.SetToolTip(lblDespill, "Color despill amount");
            controlNetSettingsPanel.Controls.Add(lblDespill);

            cnDespillUpDown = CreateThemedNumericUpDown(200, cy, 50, 0, 1, 0M, 0.1M, 2);
            panelTooltip.SetToolTip(cnDespillUpDown, "Despill strength");
            controlNetSettingsPanel.Controls.Add(cnDespillUpDown);
            cy += 28;

            // Invert Mask
            var lblInvMask = CreateThemedLabel("Invert Mask", 5, cy + 3, 70);
            panelTooltip.SetToolTip(lblInvMask, "Whether to invert the background removal mask");
            controlNetSettingsPanel.Controls.Add(lblInvMask);

            cnInvertMaskBox = CreateThemedComboBox(80, cy, 55);
            cnInvertMaskBox.Items.AddRange(new object[] { "no", "yes" });
            cnInvertMaskBox.SelectedIndex = 0;
            panelTooltip.SetToolTip(cnInvertMaskBox, "Invert mask (no=normal, yes=inverted)");
            controlNetSettingsPanel.Controls.Add(cnInvertMaskBox);
            cy += 28;

            controlNetSettingsPanel.Height = cy;
            contentPanel.Controls.Add(controlNetSettingsPanel);
            controlNetExpandedHeight = cy + 4;
            controlNetReservedHeight = controlNetExpandedHeight;
            y += cy + 4;

            // Drop Black Pixels section - separator
            var separatorPost = new Label 
            { 
                Left = 5, 
                Top = y, 
                Width = rowWidth, 
                Height = 1, 
                BackColor = HolographicTheme.BorderCyan 
            };
            contentPanel.Controls.Add(separatorPost);
            y += 12;

            dropBlackPixelsCheckBox = new CheckBox
            {
                Text = "Drop Black Pixels",
                Left = 5,
                Top = y,
                Width = 130,
                Checked = true,
                ForeColor = HolographicTheme.TextSecondary,
                BackColor = Color.Transparent
            };
            dropBlackPixelsCheckBox.CheckedChanged += (s, e) => BlackPixelSettingsChanged?.Invoke(this, EventArgs.Empty);
            panelTooltip.SetToolTip(dropBlackPixelsCheckBox, "Remove near-black pixels from AI output (improves transparency)");
            contentPanel.Controls.Add(dropBlackPixelsCheckBox);

            var lblThreshold = CreateThemedLabel("Threshold", 145, y + 3, 60);
            panelTooltip.SetToolTip(lblThreshold, "Black pixel threshold - pixels with R,G,B all below this are removed");
            contentPanel.Controls.Add(lblThreshold);

            blackThresholdUpDown = CreateThemedNumericUpDown(210, y, 50, 0, 255, 15, 5);
            blackThresholdUpDown.ValueChanged += (s, e) => BlackPixelSettingsChanged?.Invoke(this, EventArgs.Empty);
            panelTooltip.SetToolTip(blackThresholdUpDown, "Pixels with R,G,B all below this value are removed");
            contentPanel.Controls.Add(blackThresholdUpDown);
            y += 28;

            var lblThresholdInfo = new Label
            {
                Text = "Removes pixels where R,G,B ? threshold",
                Left = 5,
                Top = y,
                Width = rowWidth,
                Height = 16,
                ForeColor = HolographicTheme.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font("Consolas", 7.5f)
            };
            contentPanel.Controls.Add(lblThresholdInfo);
            y += 24;

            preserveAlphaCheckBox = new CheckBox
            {
                Text = "Preserve Alpha Mask",
                Left = 5,
                Top = y,
                Width = 160,
                Checked = true,
                ForeColor = HolographicTheme.TextSecondary,
                BackColor = Color.Transparent
            };
            panelTooltip.SetToolTip(preserveAlphaCheckBox, "Re-apply original transparency after AI Regen As One.\nKeeps each object's original shape when splitting the composite.");
            contentPanel.Controls.Add(preserveAlphaCheckBox);
            y += 24;

            // Separator before buttons
            var separator = new Label 
            { 
                Left = 5, 
                Top = y, 
                Width = rowWidth, 
                Height = 1, 
                BackColor = HolographicTheme.BorderCyan 
            };
            contentPanel.Controls.Add(separator);
            y += 14;

            // Action Buttons - holographic styled - side by side
            aiRegenSelectedBtn = new Button
            {
                Text = "AI Regen",
                Left = 5,
                Top = y,
                Width = 85,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = HolographicTheme.ButtonSuccess,
                ForeColor = HolographicTheme.TextPrimary,
                Font = new Font("Consolas", 8f, FontStyle.Bold)
            };
            aiRegenSelectedBtn.FlatAppearance.BorderColor = Color.FromArgb(0, 255, 200);
            aiRegenSelectedBtn.FlatAppearance.BorderSize = 1;
            aiRegenSelectedBtn.Click += (s, e) => AIRegenSelectedClicked?.Invoke(this, EventArgs.Empty);
            panelTooltip.SetToolTip(aiRegenSelectedBtn, "Regenerate selected objects using AI");
            contentPanel.Controls.Add(aiRegenSelectedBtn);

            aiRegenAsOneBtn = new Button
            {
                Text = "AI Regen as One",
                Left = 95,
                Top = y,
                Width = 110,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = HolographicTheme.ButtonAccent,
                ForeColor = HolographicTheme.TextPrimary,
                Font = new Font("Consolas", 8f, FontStyle.Bold)
            };
            aiRegenAsOneBtn.FlatAppearance.BorderColor = HolographicTheme.CyanAccent;
            aiRegenAsOneBtn.FlatAppearance.BorderSize = 1;
            aiRegenAsOneBtn.Click += (s, e) => AIRegenAsOneClicked?.Invoke(this, EventArgs.Empty);
            panelTooltip.SetToolTip(aiRegenAsOneBtn, "Combine selected objects and regenerate as single image");
            contentPanel.Controls.Add(aiRegenAsOneBtn);
            y += 38;

            // STOP button - cancels the in-flight AI generation (per item + server interrupt)
            aiStopBtn = new Button
            {
                Text = "STOP",
                Left = 5,
                Top = y,
                Width = ctrlWidth,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(140, 20, 30),
                ForeColor = HolographicTheme.TextPrimary,
                Font = new Font("Consolas", 9, FontStyle.Bold),
                Enabled = false
            };
            aiStopBtn.FlatAppearance.BorderColor = Color.FromArgb(255, 80, 90);
            aiStopBtn.FlatAppearance.BorderSize = 1;
            aiStopBtn.Click += (s, e) => StopAIGenerationClicked?.Invoke(this, EventArgs.Empty);
            panelTooltip.SetToolTip(aiStopBtn, "Stop the running AI generation (also works with ESC)");
            contentPanel.Controls.Add(aiStopBtn);
            y += 38;

            // Old/New toggle button
            oldNewBtn = new Button
            {
                Text = "Old/New",
                Left = 5,
                Top = y,
                Width = 90,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = HolographicTheme.ButtonBackground,
                ForeColor = HolographicTheme.TextPrimary,
                Font = new Font("Consolas", 9, FontStyle.Bold)
            };
            oldNewBtn.FlatAppearance.BorderColor = HolographicTheme.BorderCyan;
            oldNewBtn.FlatAppearance.BorderSize = 1;
            oldNewBtn.Click += (s, e) => OldNewClicked?.Invoke(this, EventArgs.Empty);
            panelTooltip.SetToolTip(oldNewBtn, "Toggle between original and AI-generated versions");
            contentPanel.Controls.Add(oldNewBtn);
            y += 42;

            revertAiBtn = new Button
            {
                Text = "Revert AI Changes",
                Left = 5,
                Top = y,
                Width = ctrlWidth,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = HolographicTheme.ButtonDanger,
                ForeColor = HolographicTheme.TextPrimary,
                Font = new Font("Consolas", 9, FontStyle.Bold)
            };
            revertAiBtn.FlatAppearance.BorderColor = Color.FromArgb(255, 100, 120);
            revertAiBtn.FlatAppearance.BorderSize = 1;
            revertAiBtn.Click += (s, e) => RevertAIChangesClicked?.Invoke(this, EventArgs.Empty);
            panelTooltip.SetToolTip(revertAiBtn, "Discard all AI changes and restore original images");
            contentPanel.Controls.Add(revertAiBtn);
            y += 42;

            revertOgBtn = new Button
            {
                Text = "Revert OG",
                Left = 5,
                Top = y,
                Width = ctrlWidth,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(120, 40, 40),
                ForeColor = HolographicTheme.TextPrimary,
                Font = new Font("Consolas", 9, FontStyle.Bold)
            };
            revertOgBtn.FlatAppearance.BorderColor = Color.FromArgb(255, 150, 100);
            revertOgBtn.FlatAppearance.BorderSize = 1;
            revertOgBtn.Click += (s, e) => RevertOGClicked?.Invoke(this, EventArgs.Empty);
            panelTooltip.SetToolTip(revertOgBtn, "Reload the ORIGINAL UO artwork from disk for the selection (or all objects if nothing is selected). Works even when Revert AI Changes has nothing left to restore.");
            contentPanel.Controls.Add(revertOgBtn);
            y += 42;

            unifyIdsBtn = new Button
            {
                Text = "Unify Same IDs",
                Left = 5,
                Top = y,
                Width = ctrlWidth,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = HolographicTheme.ButtonBackground,
                ForeColor = HolographicTheme.TextPrimary,
                Font = new Font("Consolas", 9, FontStyle.Bold)
            };
            unifyIdsBtn.FlatAppearance.BorderColor = HolographicTheme.BorderCyan;
            unifyIdsBtn.FlatAppearance.BorderSize = 1;
            unifyIdsBtn.Click += (s, e) => UnifyIdsClicked?.Invoke(this, EventArgs.Empty);
            panelTooltip.SetToolTip(unifyIdsBtn, "After Regen as One: make every repeated ItemID use one style (copies the largest slice to all same-ID objects, matching what Save writes per ID).");
            contentPanel.Controls.Add(unifyIdsBtn);
            y += 42;

            // Save to MUL section - separator
            var saveSeparator = new Label 
            { 
                Left = 5, 
                Top = y, 
                Width = rowWidth, 
                Height = 1, 
                BackColor = HolographicTheme.BorderCyan 
            };
            contentPanel.Controls.Add(saveSeparator);
            y += 14;

            // Pending changes label - HIDDEN FOR NOW
            pendingChangesLabel = new Label
            {
                Text = "No pending changes",
                Left = 5,
                Top = y,
                Width = rowWidth,
                Height = 18,
                ForeColor = HolographicTheme.TextMuted,
                BackColor = Color.Transparent,
                Font = new Font("Consolas", 8.5f)
            };
            contentPanel.Controls.Add(pendingChangesLabel);
            y += 22;

            // Save to MUL button (overwrite original)
            saveToMulBtn = new Button
            {
                Text = "Save Art",
                Left = 5,
                Top = y,
                Width = 125,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = HolographicTheme.ButtonSuccess,
                ForeColor = HolographicTheme.TextPrimary,
                Font = new Font("Consolas", 9, FontStyle.Bold),
                Enabled = false
            };
            saveToMulBtn.FlatAppearance.BorderColor = Color.FromArgb(0, 255, 150);
            saveToMulBtn.FlatAppearance.BorderSize = 1;
            saveToMulBtn.Click += (s, e) => SaveToMulClicked?.Invoke(this, EventArgs.Empty);
            panelTooltip.SetToolTip(saveToMulBtn, "Overwrite original art in MUL/UOP files");
            contentPanel.Controls.Add(saveToMulBtn);

            // Save as New Slot button
            saveAsNewBtn = new Button
            {
                Text = "Save as New",
                Left = 135,
                Top = y,
                Width = 125,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = HolographicTheme.ButtonAccent,
                ForeColor = HolographicTheme.TextPrimary,
                Font = new Font("Consolas", 9, FontStyle.Bold),
                Enabled = false
            };
            saveAsNewBtn.FlatAppearance.BorderColor = HolographicTheme.CyanAccent;
            saveAsNewBtn.FlatAppearance.BorderSize = 1;
            saveAsNewBtn.Click += (s, e) => SaveToNewSlotClicked?.Invoke(this, EventArgs.Empty);
            panelTooltip.SetToolTip(saveAsNewBtn, "Save to empty art slots (keeps originals intact)");
            contentPanel.Controls.Add(saveAsNewBtn);
            y += 36;

            // Save whole canvas as new entries button
            saveCanvasAsNewBtn = new Button
            {
                Text = "Save Canvas as New",
                Left = 5,
                Top = y,
                Width = ctrlWidth,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = HolographicTheme.ButtonAccent,
                ForeColor = HolographicTheme.TextPrimary,
                Font = new Font("Consolas", 9, FontStyle.Bold)
            };
            saveCanvasAsNewBtn.FlatAppearance.BorderColor = HolographicTheme.CyanAccent;
            saveCanvasAsNewBtn.FlatAppearance.BorderSize = 1;
            saveCanvasAsNewBtn.Click += (s, e) => SaveCanvasAsNewClicked?.Invoke(this, EventArgs.Empty);
            panelTooltip.SetToolTip(saveCanvasAsNewBtn, "Save EVERYTHING on the canvas as brand-new art entries in unused slots (originals untouched). Canvas items are re-pointed to the new IDs.");
            contentPanel.Controls.Add(saveCanvasAsNewBtn);
            y += 36;

            // Push to MeesaJarJar button
            pushToJarJarBtn = new Button
            {
                Text = "\u2601 Push to JarJar",
                Left = 5,
                Top = y,
                Width = ctrlWidth,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(80, 0, 140),
                ForeColor = HolographicTheme.TextPrimary,
                Font = new Font("Consolas", 9, FontStyle.Bold),
                Enabled = AppConfig.Instance.JarJarPushEnabled
            };
            pushToJarJarBtn.FlatAppearance.BorderColor = Color.FromArgb(160, 0, 255);
            pushToJarJarBtn.FlatAppearance.BorderSize = 1;
            pushToJarJarBtn.Click += (s, e) => PushToJarJarClicked?.Invoke(this, EventArgs.Empty);
            panelTooltip.SetToolTip(pushToJarJarBtn, AppConfig.Instance.JarJarPushEnabled
                ? "Upload modified art to MeesaJarJar.com (live on shard in ~30 seconds)"
                : "Push to MeesaJarJar is currently disabled (Settings)");
            contentPanel.Controls.Add(pushToJarJarBtn);

            // Auto-push checkbox
            autoPushJarJarCheckBox = new CheckBox
            {
                Text = "Auto-push after AI gen",
                Left = 5,
                Top = y + 34,
                Width = ctrlWidth,
                Height = 20,
                ForeColor = Color.FromArgb(180, 100, 255),
                BackColor = Color.Transparent,
                Font = new Font("Consolas", 8f),
                Checked = AppConfig.Instance.JarJarAutoPush && AppConfig.Instance.JarJarPushEnabled,
                Enabled = AppConfig.Instance.JarJarPushEnabled
            };
            autoPushJarJarCheckBox.CheckedChanged += (s, e) =>
            {
                AppConfig.Instance.JarJarAutoPush = autoPushJarJarCheckBox.Checked;
                AppConfig.Instance.Save();
            };
            panelTooltip.SetToolTip(autoPushJarJarCheckBox, "Automatically push to MeesaJarJar after each AI generation completes");
            contentPanel.Controls.Add(autoPushJarJarCheckBox);
            y += 58;

            // Status label - holographic styled (moved up, closer to Revert AI Changes button)
            statusLabel = new Label
            {
                Left = 5,
                Top = y - 20, // Move up by subtracting from current y position
                Width = ctrlWidth,
                Height = 40,
                Text = "Ready",
                ForeColor = HolographicTheme.TextPrimary,
                Font = new Font("Consolas", 9f),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = HolographicTheme.InputBackground,
                Padding = new Padding(5),
                TextAlign = ContentAlignment.MiddleCenter
            };
            contentPanel.Controls.Add(statusLabel);

            // Add panels to control
            this.Controls.Add(contentPanel);
            this.Controls.Add(headerPanel);

            // ComfyUI status indicator panel
            comfyStatusIndicator = new Panel
            {
                Size = new Size(20, 20),
                Location = new Point(this.Width - 30, 5),
                BackColor = Color.Transparent
            };
            this.Controls.Add(comfyStatusIndicator);

            // Timer for checking ComfyUI status
            comfyStatusTimer = new Timer
            {
                Interval = 5000 // Check every 5 seconds
            };
            comfyStatusTimer.Tick += ComfyStatusTimer_Tick;

            // Start the status check timer
            comfyStatusTimer.Start();
        }

        private async void ComfyStatusTimer_Tick(object sender, EventArgs e)
        {
            if (_isCheckingComfyStatus) return;
            _isCheckingComfyStatus = true;

            try
            {
                // Check if ComfyUI is reachable
                using (var cts = new System.Threading.CancellationTokenSource())
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(3)); // 3 seconds timeout
                    var response = await _httpClient.GetAsync(ComfyUrl, cts.Token);
                    SetComfyStatusIndicator(response.IsSuccessStatusCode);
                }
            }
            catch
            {
                SetComfyStatusIndicator(false);
            }

            _isCheckingComfyStatus = false;
        }

        private void SetComfyStatusIndicator(bool isWorking)
        {
            if (comfyStatusIndicator.InvokeRequired)
            {
                comfyStatusIndicator.Invoke(new Action(() => SetComfyStatusIndicator(isWorking)));
                return;
            }

            comfyStatusIndicator.BackColor = isWorking ? Color.LawnGreen : Color.Red;
        }

        // Helper methods for creating themed controls
        private Label CreateThemedLabel(string text, int left, int top, int width, bool isHeader = false)
        {
            return new Label
            {
                Text = text,
                Left = left,
                Top = top,
                Width = width,
                Height = 18,
                ForeColor = isHeader ? HolographicTheme.CyanAccent : HolographicTheme.TextSecondary,
                BackColor = Color.Transparent,
                Font = new Font("Consolas", 8.5f)
            };
        }

        private TextBox CreateThemedTextBox(int left, int top, int width, string text)
        {
            var tb = new TextBox
            {
                Left = left,
                Top = top,
                Width = width,
                Text = text,
                BackColor = HolographicTheme.InputBackground,
                ForeColor = HolographicTheme.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9)
            };
            return tb;
        }

        private NumericUpDown CreateThemedNumericUpDown(int left, int top, int width, decimal min, decimal max, decimal value, decimal increment = 1, int decimalPlaces = 0)
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
                DecimalPlaces = decimalPlaces,
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

        private Button CreateThemedButton(string text, int left, int top, int width, int height = 25)
        {
            var btn = new Button
            {
                Text = text,
                Left = left,
                Top = top,
                Width = width,
                Height = height,
                FlatStyle = FlatStyle.Flat,
                BackColor = HolographicTheme.ButtonBackground,
                ForeColor = HolographicTheme.TextPrimary,
                Font = new Font("Consolas", 9, FontStyle.Bold)
            };
            btn.FlatAppearance.BorderColor = HolographicTheme.BorderCyan;
            btn.FlatAppearance.BorderSize = 1;
            return btn;
        }

        private void CollapseButton_Click(object sender, EventArgs e)
        {
            _isCollapsed = !_isCollapsed;

            if (_isCollapsed)
            {
                _expandedWidth = this.Width;
                this.Width = 30;
                contentPanel.Visible = false;
                collapseButton.Text = "?";
            }
            else
            {
                this.Width = _expandedWidth;
                contentPanel.Visible = true;
                collapseButton.Text = "?";
            }
        }

        /// <summary>
        /// Handle AI backend selection change
        /// </summary>
        private async void AIBackendComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            var selectedBackend = SelectedBackend;
            
            // Apply UI state changes
            ApplyBackendUIState(selectedBackend);
            
            if (IsLocalBackend(selectedBackend))
            {
                // Stop ComfyUI status checking timer
                if (comfyStatusTimer != null)
                {
                    comfyStatusTimer.Stop();
                }
                
                // Check if local models are downloaded
                bool available = IsSelectedLocalBackendAvailable(selectedBackend);
                string backendName = ImageGeneratorFactory.GetBackendName(selectedBackend);
                
                if (available)
                {
                    aiBackendStatusLabel.Text = "? Ready";
                    aiBackendStatusLabel.ForeColor = Color.FromArgb(0, 255, 100);
                    SetStatus(backendName + " ready", Color.FromArgb(0, 255, 100));
                    SetComfyStatusIndicator(true, backendName + " ready");
                    SetUrlStatusIndicator(true, backendName + " ready");
                }
                else
                {
                    aiBackendStatusLabel.Text = "?? Models needed";
                    aiBackendStatusLabel.ForeColor = Color.Orange;
                    SetStatus("Local models needed", Color.Orange);
                    SetComfyStatusIndicator(false, "Models needed");
                    SetUrlStatusIndicator(false, "Models needed");
                    
                    bool success = await ImageGeneratorFactory.EnsureBackendAvailable(selectedBackend, this.FindForm());
                    if (success)
                    {
                        aiBackendStatusLabel.Text = "? Ready";
                        aiBackendStatusLabel.ForeColor = Color.FromArgb(0, 255, 100);
                        SetStatus(backendName + " ready", Color.FromArgb(0, 255, 100));
                        SetComfyStatusIndicator(true, backendName + " ready");
                        SetUrlStatusIndicator(true, backendName + " ready");
                    }
                    else
                    {
                        // Switch back to ComfyUI if setup failed
                        aiBackendComboBox.SelectedIndex = 0;
                        return;
                    }
                }
            }
            else // ComfyUI
            {
                // Start ComfyUI status checking timer
                if (comfyStatusTimer != null && !comfyStatusTimer.Enabled)
                {
                    comfyStatusTimer.Start();
                }
                
                // Check ComfyUI status
                await CheckComfyUIStatus();
            }
            
            // Save selected backend to config
            AppConfig.Instance.SelectedAIBackend = selectedBackend;
            AppConfig.Instance.Save();
        }

        /// <summary>
        /// Gets the current settings as a BatchSettings-compatible object
        /// </summary>
        public (string Url, string Prompt, string Negative, int Steps, double Cfg, double Denoise, long? Seed, string Sampler, string Scheduler) GetSettings()
        {
            return (ComfyUrl, Prompt, NegativePrompt, Steps, Cfg, Denoise, Seed, Sampler, Scheduler);
        }

        /// <summary>
        /// Set the Old/New toggle button label
        /// </summary>
        public void SetOldNewButtonText(string text)
        {
            if (oldNewBtn == null) return;
            if (oldNewBtn.InvokeRequired)
            {
                oldNewBtn.Invoke(new Action(() => SetOldNewButtonText(text)));
                return;
            }
            oldNewBtn.Text = text;
        }

        /// <summary>
        /// Set whether the Old/New button is enabled
        /// </summary>
        public void SetOldNewEnabled(bool enabled)
        {
            if (oldNewBtn == null) return;
            if (oldNewBtn.InvokeRequired)
            {
                oldNewBtn.Invoke(new Action(() => SetOldNewEnabled(enabled)));
                return;
            }
            oldNewBtn.Enabled = enabled;
        }

        /// <summary>
        /// Hide the "AI Regen as One" button (used when panel is embedded in GUMP Editor)
        /// </summary>
        public void HideRegenAsOneButton()
        {
            if (aiRegenAsOneBtn == null) return;
            if (aiRegenAsOneBtn.InvokeRequired)
            {
                aiRegenAsOneBtn.Invoke(new Action(() => HideRegenAsOneButton()));
                return;
            }
            aiRegenAsOneBtn.Visible = false;
            
            // Expand the "AI Regen Selected" button to fill the space
            if (aiRegenSelectedBtn != null)
            {
                aiRegenSelectedBtn.Width = 195; // Full width
            }
        }

        /// <summary>
        /// Enable/disable the STOP button (enabled only while a generation is running)
        /// </summary>
        public void SetStopEnabled(bool enabled)
        {
            if (aiStopBtn == null) return;
            if (aiStopBtn.InvokeRequired)
            {
                aiStopBtn.Invoke(new Action(() => SetStopEnabled(enabled)));
                return;
            }
            aiStopBtn.Enabled = enabled;
        }

        /// <summary>
        /// Hide the STOP button (used when panel is embedded in GUMP/Texture
        /// editors, which have no cancellable generation)
        /// </summary>
        public void HideStopButton()
        {
            if (aiStopBtn == null) return;
            if (aiStopBtn.InvokeRequired)
            {
                aiStopBtn.Invoke(new Action(() => HideStopButton()));
                return;
            }
            aiStopBtn.Visible = false;
        }

        /// <summary>
        /// Hide the "Save Canvas as New" button (used when panel is embedded in
        /// GUMP/Texture editors, which have no canvas to save from)
        /// </summary>
        public void HideSaveCanvasAsNewButton()
        {
            if (saveCanvasAsNewBtn == null) return;
            if (saveCanvasAsNewBtn.InvokeRequired)
            {
                saveCanvasAsNewBtn.Invoke(new Action(() => HideSaveCanvasAsNewButton()));
                return;
            }
            saveCanvasAsNewBtn.Visible = false;
        }

        /// <summary>
        /// Hide the "Unify Same IDs" button (used when panel is embedded in
        /// GUMP/Texture editors, which have no multi-object canvas)
        /// </summary>
        public void HideUnifyButton()
        {
            if (unifyIdsBtn == null) return;
            if (unifyIdsBtn.InvokeRequired)
            {
                unifyIdsBtn.Invoke(new Action(() => HideUnifyButton()));
                return;
            }
            unifyIdsBtn.Visible = false;
        }

        /// <summary>
        /// Hide the "Revert OG" button (used when panel is embedded in GUMP/Texture
        /// editors, where the main-canvas disk-art reload does not apply)
        /// </summary>
        public void HideRevertOGButton()
        {
            if (revertOgBtn == null) return;
            if (revertOgBtn.InvokeRequired)
            {
                revertOgBtn.Invoke(new Action(() => HideRevertOGButton()));
                return;
            }
            revertOgBtn.Visible = false;
        }

        /// <summary>
        /// Clean up resources when the control is disposed
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                comfyStatusTimer?.Stop();
                comfyStatusTimer?.Dispose();
                
                var tooltip = urlStatusIndicator?.Tag as ToolTip;
                tooltip?.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Get available checkpoints from the Text2Image workflow
        /// </summary>
        private object[] GetCheckpointOptions()
        {
            // Default checkpoints - will be populated when RefreshCheckpoints is called
            return new object[] { Text2ImageWorkflow.DEFAULT_CHECKPOINT };
        }

        /// <summary>
        /// Refresh the checkpoint list from ComfyUI server
        /// </summary>
        public async Task RefreshCheckpoints()
        {
            if (checkpointBox == null) return;

            try
            {
                var client = new ComfyUIClient(ComfyUrl);
                var checkpoints = await client.GetAvailableCheckpoints();
                
                if (checkpointBox.InvokeRequired)
                {
                    checkpointBox.Invoke(new Action(() => UpdateCheckpointList(checkpoints)));
                }
                else
                {
                    UpdateCheckpointList(checkpoints);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refreshing checkpoints: {ex.Message}");
            }
        }

        private void UpdateCheckpointList(List<string> checkpoints)
        {
            if (checkpoints == null || checkpoints.Count == 0) return;

            string currentSelection = checkpointBox.SelectedItem?.ToString();
            checkpointBox.Items.Clear();
            
            foreach (var cp in checkpoints)
            {
                checkpointBox.Items.Add(cp);
            }

            // Try to restore selection
            if (!string.IsNullOrEmpty(currentSelection))
            {
                int idx = checkpointBox.Items.IndexOf(currentSelection);
                if (idx >= 0)
                {
                    checkpointBox.SelectedIndex = idx;
                    return;
                }
            }

            // Default to dreamshaper if available, else first item
            int defaultIdx = checkpointBox.Items.IndexOf(Text2ImageWorkflow.DEFAULT_CHECKPOINT);
            checkpointBox.SelectedIndex = defaultIdx >= 0 ? defaultIdx : 0;
        }
    }
}
