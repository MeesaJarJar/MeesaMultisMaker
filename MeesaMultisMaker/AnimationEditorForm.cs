using MeesaMultisMaker.Mul;
using MeesaMultisMaker.Utils;
using MeesaMultisMaker.Dialogs;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    /// <summary>
    /// Animation Editor for viewing, playing, and editing UO mobile animations
    /// </summary>
    public partial class AnimationEditorForm : Form
    {
        // UI Controls
        private Panel topPanel;
        private Panel leftPanel;
        private Panel centerPanel;
        private PictureBox previewPictureBox;
        private ListBox bodyListBox;
        private ListBox actionListBox;
        private ListBox directionListBox;
        private ListBox frameListBox;
        private ComboBox animTypeComboBox;
        private Button playButton;
        private Button stopButton;
        private Button exportFramesButton;
        private Button exportSpriteSheetButton;
        private Button exportAllFramesButton;
        private Button exportFullBodySheetButton;
        private Button importFramesButton;
        private Button replaceSingleFrameButton;
        private Button saveAnimButton;
        private TrackBar fpsTrackBar;
        private Label fpsLabel;
        private Label statusLabel;
        private CheckBox loopCheckBox;
        private NumericUpDown bodyIdNumeric;
        private Button searchBodyButton;
        private Label infoLabel;

        // Animation data
        private string mulFolderPath;
        private AnimReader.AnimationInfo currentAnimation;
        private Timer playbackTimer;
        private int currentFrameIndex = 0;
        private bool isPlaying = false;
        private Dictionary<int, string> bodyNames;

        public AnimationEditorForm()
        {
            InitializeComponent();
            InitializeCustomControls();
            LoadConfiguration();
            LoadBodyNames();
            LoadAnimationTypes();
            
            // Add Load event handler
            this.Load += AnimationEditorForm_Load;
        }

        private void AnimationEditorForm_Load(object sender, EventArgs e)
        {
            // Load the body list for the default animation type
            if (animTypeComboBox.SelectedIndex >= 0)
            {
                LoadBodyList();
            }
        }

        private void InitializeComponent()
        {
            this.Text = "Animation Editor - Meesa Multis Maker";
            this.Size = new Size(1200, 800);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = HolographicTheme.DarkBackground;
            
            // Apply holographic theme
            HolographicTheme.ApplyToForm(this);

            // Initialize playback timer
            playbackTimer = new Timer();
            playbackTimer.Interval = 100; // 10 FPS default
            playbackTimer.Tick += PlaybackTimer_Tick;
        }

        private void InitializeCustomControls()
        {
            // Top panel for controls
            topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 120,
                BackColor = HolographicTheme.PanelBackground
            };
            HolographicTheme.ApplyToPanel(topPanel);

            // Animation Type selector
            var typeLabel = new Label
            {
                Text = "Animation Type:",
                Location = new Point(10, 10),
                AutoSize = true
            };
            HolographicTheme.ApplyToLabel(typeLabel);
            topPanel.Controls.Add(typeLabel);

            animTypeComboBox = new ComboBox
            {
                Location = new Point(10, 35),
                Width = 150,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            HolographicTheme.ApplyToComboBox(animTypeComboBox);
            animTypeComboBox.SelectedIndexChanged += AnimTypeComboBox_SelectedIndexChanged;
            topPanel.Controls.Add(animTypeComboBox);

            // Body ID search
            var bodySearchLabel = new Label
            {
                Text = "Body ID:",
                Location = new Point(180, 10),
                AutoSize = true
            };
            HolographicTheme.ApplyToLabel(bodySearchLabel);
            topPanel.Controls.Add(bodySearchLabel);

            bodyIdNumeric = new NumericUpDown
            {
                Location = new Point(180, 35),
                Width = 80,
                Minimum = 0,
                Maximum = 9999
            };
            HolographicTheme.ApplyToNumericUpDown(bodyIdNumeric);
            topPanel.Controls.Add(bodyIdNumeric);

            searchBodyButton = CreateThemedButton("Search", 270, 35, 70);
            searchBodyButton.Click += SearchBodyButton_Click;
            topPanel.Controls.Add(searchBodyButton);

            var browseMulButton = CreateThemedButton("Browse MUL...", 350, 35, 100, ButtonStyle.Warning);
            browseMulButton.Click += BrowseMulButton_Click;
            topPanel.Controls.Add(browseMulButton);

            // Playback controls
            playButton = CreateThemedButton("? Play", 460, 35, 70, ButtonStyle.Success);
            playButton.Click += PlayButton_Click;
            topPanel.Controls.Add(playButton);

            stopButton = CreateThemedButton("? Stop", 540, 35, 70, ButtonStyle.Danger);
            stopButton.Click += StopButton_Click;
            stopButton.Enabled = false;
            topPanel.Controls.Add(stopButton);

            loopCheckBox = new CheckBox
            {
                Text = "Loop",
                Location = new Point(620, 37),
                Checked = true,
                AutoSize = true
            };
            HolographicTheme.ApplyToCheckBox(loopCheckBox);
            topPanel.Controls.Add(loopCheckBox);

            // FPS control
            var fpsControlLabel = new Label
            {
                Text = "FPS:",
                Location = new Point(700, 10),
                AutoSize = true
            };
            HolographicTheme.ApplyToLabel(fpsControlLabel);
            topPanel.Controls.Add(fpsControlLabel);

            fpsTrackBar = new TrackBar
            {
                Location = new Point(700, 35),
                Width = 150,
                Minimum = 1,
                Maximum = 60,
                Value = 10,
                TickFrequency = 5
            };
            HolographicTheme.ApplyToTrackBar(fpsTrackBar);
            fpsTrackBar.ValueChanged += FpsTrackBar_ValueChanged;
            topPanel.Controls.Add(fpsTrackBar);

            fpsLabel = new Label
            {
                Text = "10 FPS",
                Location = new Point(860, 37),
                AutoSize = true
            };
            HolographicTheme.ApplyToLabel(fpsLabel);
            topPanel.Controls.Add(fpsLabel);

            // Export/Import buttons
            exportFramesButton = CreateThemedButton("Export Frames", 10, 75, 120, ButtonStyle.Accent);
            exportFramesButton.Click += ExportFramesButton_Click;
            topPanel.Controls.Add(exportFramesButton);

            exportSpriteSheetButton = CreateThemedButton("Export Sprite Sheet", 140, 75, 140, ButtonStyle.Accent);
            exportSpriteSheetButton.Click += ExportSpriteSheetButton_Click;
            topPanel.Controls.Add(exportSpriteSheetButton);

            // Add new button for full body sprite sheet export
            exportAllFramesButton = CreateThemedButton("Export All Frames", 290, 75, 140, ButtonStyle.Accent);
            exportAllFramesButton.Click += ExportAllFramesButton_Click;
            topPanel.Controls.Add(exportAllFramesButton);

            var exportFullBodySheetButton = CreateThemedButton("Export Full Body Sheet", 440, 75, 155, ButtonStyle.Success);
            exportFullBodySheetButton.Click += ExportFullBodySpriteSheetButton_Click;
            topPanel.Controls.Add(exportFullBodySheetButton);

            importFramesButton = CreateThemedButton("Import / Split", 605, 75, 110, ButtonStyle.Warning);
            importFramesButton.Click += ImportFramesButton_Click;
            topPanel.Controls.Add(importFramesButton);

            replaceSingleFrameButton = CreateThemedButton("Replace Frame", 725, 75, 110, ButtonStyle.Warning);
            replaceSingleFrameButton.Click += ReplaceSingleFrameButton_Click;
            topPanel.Controls.Add(replaceSingleFrameButton);

            var saveAnimButton = CreateThemedButton("Save to MUL", 845, 75, 110, ButtonStyle.Success);
            saveAnimButton.Click += SaveAnimButton_Click;
            topPanel.Controls.Add(saveAnimButton);

            this.Controls.Add(topPanel);

            // Left panel for selection lists
            leftPanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = 300,
                BackColor = HolographicTheme.PanelBackground
            };
            HolographicTheme.ApplyToPanel(leftPanel);

            int listTop = 10;
            
            // Body list
            var bodyLabel = new Label
            {
                Text = "Body IDs:",
                Location = new Point(10, listTop),
                AutoSize = true
            };
            HolographicTheme.ApplyToLabel(bodyLabel, true);
            leftPanel.Controls.Add(bodyLabel);

            bodyListBox = new ListBox
            {
                Location = new Point(10, listTop + 25),
                Size = new Size(280, 120)
            };
            HolographicTheme.ApplyToListBox(bodyListBox);
            bodyListBox.SelectedIndexChanged += BodyListBox_SelectedIndexChanged;
            leftPanel.Controls.Add(bodyListBox);

            listTop += 155;

            // Action list
            var actionLabel = new Label
            {
                Text = "Actions:",
                Location = new Point(10, listTop),
                AutoSize = true
            };
            HolographicTheme.ApplyToLabel(actionLabel, true);
            leftPanel.Controls.Add(actionLabel);

            actionListBox = new ListBox
            {
                Location = new Point(10, listTop + 25),
                Size = new Size(280, 120)
            };
            HolographicTheme.ApplyToListBox(actionListBox);
            actionListBox.SelectedIndexChanged += ActionListBox_SelectedIndexChanged;
            leftPanel.Controls.Add(actionListBox);

            listTop += 155;

            // Direction list
            var dirLabel = new Label
            {
                Text = "Directions:",
                Location = new Point(10, listTop),
                AutoSize = true
            };
            HolographicTheme.ApplyToLabel(dirLabel, true);
            leftPanel.Controls.Add(dirLabel);

            directionListBox = new ListBox
            {
                Location = new Point(10, listTop + 25),
                Size = new Size(280, 100)
            };
            HolographicTheme.ApplyToListBox(directionListBox);
            directionListBox.SelectedIndexChanged += DirectionListBox_SelectedIndexChanged;
            leftPanel.Controls.Add(directionListBox);

            listTop += 135;

            // Frame list
            var frameLabel = new Label
            {
                Text = "Frames:",
                Location = new Point(10, listTop),
                AutoSize = true
            };
            HolographicTheme.ApplyToLabel(frameLabel, true);
            leftPanel.Controls.Add(frameLabel);

            frameListBox = new ListBox
            {
                Location = new Point(10, listTop + 25),
                Size = new Size(280, 150)
            };
            HolographicTheme.ApplyToListBox(frameListBox);
            frameListBox.SelectedIndexChanged += FrameListBox_SelectedIndexChanged;
            leftPanel.Controls.Add(frameListBox);

            this.Controls.Add(leftPanel);

            // Center panel for preview
            centerPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = HolographicTheme.DarkBackground,
                AutoScroll = true
            };
            HolographicTheme.ApplyToPanel(centerPanel);

            previewPictureBox = new PictureBox
            {
                Location = new Point(0, 0),
                Size = new Size(800, 600),
                BackColor = Color.FromArgb(20, 20, 25),
                SizeMode = PictureBoxSizeMode.CenterImage
            };
            previewPictureBox.Paint += PreviewPictureBox_Paint;
            centerPanel.Controls.Add(previewPictureBox);

            this.Controls.Add(centerPanel);

            // Bottom status bar
            statusLabel = new Label
            {
                Text = "Ready. Load an animation to begin.",
                Dock = DockStyle.Bottom,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0)
            };
            HolographicTheme.ApplyToLabel(statusLabel);
            this.Controls.Add(statusLabel);

            // Info label on top of preview
            infoLabel = new Label
            {
                Text = "",
                Location = new Point(10, 10),
                AutoSize = true,
                BackColor = Color.FromArgb(180, 0, 0, 0),
                ForeColor = Color.White,
                Padding = new Padding(5)
            };
            previewPictureBox.Controls.Add(infoLabel);
            infoLabel.BringToFront();
        }

        private Button CreateThemedButton(string text, int left, int top, int width, ButtonStyle style = ButtonStyle.Default)
        {
            var btn = new Button
            {
                Text = text,
                Left = left,
                Top = top,
                Width = width,
                Height = 28
            };
            HolographicTheme.ApplyToButton(btn, style);
            return btn;
        }

        private void LoadConfiguration()
        {
            var config = AppConfig.Instance;
            mulFolderPath = config.FindMulFolder();

            System.Diagnostics.Debug.WriteLine($"AnimationEditor: MUL folder from config: {mulFolderPath}");

            if (string.IsNullOrEmpty(mulFolderPath) || !Directory.Exists(mulFolderPath))
            {
                statusLabel.Text = "MUL folder not configured. Please configure in Settings.";
                System.Diagnostics.Debug.WriteLine("AnimationEditor: MUL folder not valid");
                
                // Try to find common UO installation paths
                var commonPaths = new[]
                {
                    @"C:\Program Files (x86)\Electronic Arts\Ultima Online Classic",
                    @"C:\Program Files (x86)\UOForever\UO",
                    @"C:\Program Files\Electronic Arts\Ultima Online Classic",
                    @"C:\Ultima Online",
                    @"C:\UO"
                };

                foreach (var path in commonPaths)
                {
                    if (Directory.Exists(path))
                    {
                        // Check if anim files exist
                        if (File.Exists(Path.Combine(path, "anim.mul")) && 
                            File.Exists(Path.Combine(path, "anim.idx")))
                        {
                            System.Diagnostics.Debug.WriteLine($"AnimationEditor: Found UO folder at {path}");
                            MessageBox.Show(
                                $"UO folder detected at:\n{path}\n\n" +
                                $"Please configure this path in Settings to use the Animation Editor.",
                                "UO Folder Detected",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                            break;
                        }
                    }
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"AnimationEditor: MUL folder configured: {mulFolderPath}");
                statusLabel.Text = $"MUL folder: {mulFolderPath}";
            }
        }

        private void LoadBodyNames()
        {
            bodyNames = new Dictionary<int, string>
            {
                { 0, "Human Male" },
                { 1, "Human Female" },
                { 400, "Orc" },
                { 401, "Ettin" },
                { 50, "Ogre" },
                // Add more body names as needed
            };
        }

        private void LoadAnimationTypes()
        {
            animTypeComboBox.Items.Clear();
            animTypeComboBox.Items.Add("Monster (anim.mul)");
            animTypeComboBox.Items.Add("Monster 2 (anim2.mul)");
            animTypeComboBox.Items.Add("Monster 3 (anim3.mul)");
            animTypeComboBox.Items.Add("People (anim4.mul - 5 dir)");
            animTypeComboBox.Items.Add("People 2 (anim5.mul - 8 dir)");
            animTypeComboBox.SelectedIndex = 0;
        }

        private void AnimTypeComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            LoadBodyList();
        }

        private void LoadBodyList()
        {
            if (string.IsNullOrEmpty(mulFolderPath))
            {
                statusLabel.Text = "MUL folder not configured. Please set it in Settings.";
                System.Diagnostics.Debug.WriteLine("AnimationEditor: MUL folder path is empty");
                return;
            }

            if (!Directory.Exists(mulFolderPath))
            {
                statusLabel.Text = $"MUL folder not found: {mulFolderPath}";
                System.Diagnostics.Debug.WriteLine($"AnimationEditor: MUL folder does not exist: {mulFolderPath}");
                return;
            }

            bodyListBox.Items.Clear();
            
            var type = (AnimReader.AnimationType)animTypeComboBox.SelectedIndex;
            
            System.Diagnostics.Debug.WriteLine($"AnimationEditor: Loading body IDs for type {type} from {mulFolderPath}");
            
            var bodyIds = AnimReader.GetAvailableBodyIds(mulFolderPath, type);

            System.Diagnostics.Debug.WriteLine($"AnimationEditor: Found {bodyIds.Count} body IDs");

            if (bodyIds.Count == 0)
            {
                statusLabel.Text = $"No animation files found for {type}. Check that anim*.mul and anim*.idx files exist.";
                MessageBox.Show(
                    $"No animation files found for {type}.\n\n" +
                    $"Expected files in: {mulFolderPath}\n" +
                    $"- {GetMulFileName(type)}\n" +
                    $"- {GetIdxFileName(type)}\n\n" +
                    $"Please ensure these files exist in your UO folder.",
                    "Animation Files Not Found",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            foreach (var bodyId in bodyIds)
            {
                string displayName = bodyId.ToString();
                if (bodyNames.ContainsKey(bodyId))
                {
                    displayName += $" - {bodyNames[bodyId]}";
                }
                bodyListBox.Items.Add(new BodyListItem { BodyId = bodyId, DisplayName = displayName });
            }

            statusLabel.Text = $"Loaded {bodyIds.Count} body IDs for {type}";
        }

        private string GetMulFileName(AnimReader.AnimationType type)
        {
            switch (type)
            {
                case AnimReader.AnimationType.Monster: return "anim.mul";
                case AnimReader.AnimationType.Monster2: return "anim2.mul";
                case AnimReader.AnimationType.Monster3: return "anim3.mul";
                case AnimReader.AnimationType.People: return "anim4.mul";
                case AnimReader.AnimationType.People2: return "anim5.mul";
                default: return "anim.mul";
            }
        }

        private string GetIdxFileName(AnimReader.AnimationType type)
        {
            switch (type)
            {
                case AnimReader.AnimationType.Monster: return "anim.idx";
                case AnimReader.AnimationType.Monster2: return "anim2.idx";
                case AnimReader.AnimationType.Monster3: return "anim3.idx";
                case AnimReader.AnimationType.People: return "anim4.idx";
                case AnimReader.AnimationType.People2: return "anim5.idx";
                default: return "anim.idx";
            }
        }

        private void SearchBodyButton_Click(object sender, EventArgs e)
        {
            int searchId = (int)bodyIdNumeric.Value;
            
            for (int i = 0; i < bodyListBox.Items.Count; i++)
            {
                var item = bodyListBox.Items[i] as BodyListItem;
                if (item != null && item.BodyId == searchId)
                {
                    bodyListBox.SelectedIndex = i;
                    bodyListBox.TopIndex = Math.Max(0, i - 3);
                    return;
                }
            }

            MessageBox.Show($"Body ID {searchId} not found in current animation type.",
                "Not Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void BrowseMulButton_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select your Ultima Online installation folder (containing anim.mul files)";
                if (!string.IsNullOrEmpty(mulFolderPath))
                {
                    fbd.SelectedPath = mulFolderPath;
                }

                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    // Check if animation files exist in selected folder
                    bool hasAnimFiles = File.Exists(Path.Combine(fbd.SelectedPath, "anim.mul")) &&
                                       File.Exists(Path.Combine(fbd.SelectedPath, "anim.idx"));

                    if (!hasAnimFiles)
                    {
                        var result = MessageBox.Show(
                            $"Warning: No animation files (anim.mul/anim.idx) found in:\n{fbd.SelectedPath}\n\n" +
                            $"Do you want to use this folder anyway?",
                            "Animation Files Not Found",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning);

                        if (result != DialogResult.Yes)
                            return;
                    }

                    mulFolderPath = fbd.SelectedPath;
                    
                    // Save to config
                    var config = AppConfig.Instance;
                    config.MulFolderPath = mulFolderPath;
                    config.Save();

                    statusLabel.Text = $"MUL folder set to: {mulFolderPath}";
                    
                    // Reload animation data
                    AnimReader.ClearCache();
                    LoadBodyList();
                    
                    MessageBox.Show(
                        $"MUL folder updated to:\n{mulFolderPath}\n\n" +
                        $"Animation data reloaded.",
                        "Folder Updated",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
        }

        private void BodyListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (bodyListBox.SelectedItem == null)
                return;

            var item = bodyListBox.SelectedItem as BodyListItem;
            if (item == null)
                return;

            LoadActionsForBody(item.BodyId);
        }

        private void LoadActionsForBody(int bodyId)
        {
            actionListBox.Items.Clear();
            directionListBox.Items.Clear();
            frameListBox.Items.Clear();

            var type = (AnimReader.AnimationType)animTypeComboBox.SelectedIndex;
            var animations = AnimReader.GetAvailableAnimations(mulFolderPath, bodyId);

            var actions = animations
                .Where(a => a.Type == type)
                .Select(a => a.Action)
                .Distinct()
                .OrderBy(a => a)
                .ToList();

            foreach (var action in actions)
            {
                string actionName = GetActionName(action);
                actionListBox.Items.Add(new ActionListItem { Action = action, DisplayName = actionName });
            }

            if (actionListBox.Items.Count > 0)
            {
                actionListBox.SelectedIndex = 0;
            }
        }

        private void ActionListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (actionListBox.SelectedItem == null || bodyListBox.SelectedItem == null)
                return;

            var actionItem = actionListBox.SelectedItem as ActionListItem;
            var bodyItem = bodyListBox.SelectedItem as BodyListItem;
            
            if (actionItem == null || bodyItem == null)
                return;

            LoadDirectionsForAction(bodyItem.BodyId, actionItem.Action);
        }

        private void LoadDirectionsForAction(int bodyId, int action)
        {
            directionListBox.Items.Clear();
            frameListBox.Items.Clear();

            var type = (AnimReader.AnimationType)animTypeComboBox.SelectedIndex;
            var animations = AnimReader.GetAvailableAnimations(mulFolderPath, bodyId);

            var directions = animations
                .Where(a => a.Type == type && a.Action == action)
                .Select(a => a.Direction)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            foreach (var dir in directions)
            {
                string dirName = GetDirectionName(dir);
                directionListBox.Items.Add(new DirectionListItem { Direction = dir, DisplayName = dirName });
            }

            if (directionListBox.Items.Count > 0)
            {
                directionListBox.SelectedIndex = 0;
            }
        }

        private void DirectionListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (directionListBox.SelectedItem == null || actionListBox.SelectedItem == null || bodyListBox.SelectedItem == null)
                return;

            var dirItem = directionListBox.SelectedItem as DirectionListItem;
            var actionItem = actionListBox.SelectedItem as ActionListItem;
            var bodyItem = bodyListBox.SelectedItem as BodyListItem;

            if (dirItem == null || actionItem == null || bodyItem == null)
                return;

            LoadAnimation(bodyItem.BodyId, actionItem.Action, dirItem.Direction);
        }

        private void LoadAnimation(int bodyId, int action, int direction)
        {
            StopPlayback();

            System.Diagnostics.Debug.WriteLine($"AnimationEditor: LoadAnimation called - Body={bodyId}, Action={action}, Dir={direction}");

            var type = (AnimReader.AnimationType)animTypeComboBox.SelectedIndex;
            
            System.Diagnostics.Debug.WriteLine($"AnimationEditor: Calling AnimReader.LoadAnimation with type={type}");
            
            currentAnimation = AnimReader.LoadAnimation(mulFolderPath, bodyId, action, direction, type);

            System.Diagnostics.Debug.WriteLine($"AnimationEditor: AnimReader returned - IsNull={currentAnimation == null}");
            
            if (currentAnimation != null)
            {
                System.Diagnostics.Debug.WriteLine($"AnimationEditor: Frame count = {currentAnimation.Frames?.Count ?? 0}");
            }

            if (currentAnimation == null || currentAnimation.Frames == null || currentAnimation.Frames.Count == 0)
            {
                statusLabel.Text = "Failed to load animation or no frames found.";
                System.Diagnostics.Debug.WriteLine("AnimationEditor: No frames loaded");
                frameListBox.Items.Clear();
                previewPictureBox.Image = null;
                previewPictureBox.Invalidate();
                return;
            }

            frameListBox.Items.Clear();
            for (int i = 0; i < currentAnimation.Frames.Count; i++)
            {
                frameListBox.Items.Add($"Frame {i}");
            }

            currentFrameIndex = 0;
            if (frameListBox.Items.Count > 0)
            {
                frameListBox.SelectedIndex = 0;
            }

            statusLabel.Text = $"Loaded {currentAnimation.Frames.Count} frames - Body:{bodyId} Action:{action} Dir:{direction}";
            System.Diagnostics.Debug.WriteLine($"AnimationEditor: Successfully loaded {currentAnimation.Frames.Count} frames");
            UpdatePreview();
        }

        private void FrameListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (frameListBox.SelectedIndex >= 0 && !isPlaying)
            {
                currentFrameIndex = frameListBox.SelectedIndex;
                UpdatePreview();
            }
        }

        private void UpdatePreview()
        {
            if (currentAnimation == null || currentAnimation.Frames == null || currentAnimation.Frames.Count == 0)
            {
                previewPictureBox.Image = null;
                infoLabel.Text = "";
                previewPictureBox.Invalidate();
                return;
            }

            if (currentFrameIndex < 0 || currentFrameIndex >= currentAnimation.Frames.Count)
                currentFrameIndex = 0;

            var frame = currentAnimation.Frames[currentFrameIndex];
            
            // Update info label
            infoLabel.Text = $"Frame {currentFrameIndex + 1}/{currentAnimation.Frames.Count}\n" +
                           $"Size: {frame.Image.Width}x{frame.Image.Height}\n" +
                           $"Center: {frame.CenterX}, {frame.CenterY}";

            previewPictureBox.Invalidate();
        }

        private void PreviewPictureBox_Paint(object sender, PaintEventArgs e)
        {
            if (currentAnimation == null || currentAnimation.Frames == null || currentAnimation.Frames.Count == 0)
                return;

            if (currentFrameIndex < 0 || currentFrameIndex >= currentAnimation.Frames.Count)
                return;

            var frame = currentAnimation.Frames[currentFrameIndex];
            if (frame.Image == null)
                return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            // Draw crosshair at center
            int centerX = previewPictureBox.Width / 2;
            int centerY = previewPictureBox.Height / 2;

            using (var pen = new Pen(Color.FromArgb(100, 0, 255, 255), 1))
            {
                g.DrawLine(pen, centerX - 20, centerY, centerX + 20, centerY);
                g.DrawLine(pen, centerX, centerY - 20, centerX, centerY + 20);
            }

            // Draw frame centered at the center point, offset by frame's center values
            int drawX = centerX - frame.CenterX;
            int drawY = centerY - frame.CenterY;

            g.DrawImage(frame.Image, drawX, drawY);

            // Draw bounding box
            using (var pen = new Pen(Color.FromArgb(150, 255, 255, 0), 1))
            {
                g.DrawRectangle(pen, drawX, drawY, frame.Image.Width, frame.Image.Height);
            }
        }

        private void PlayButton_Click(object sender, EventArgs e)
        {
            if (currentAnimation == null || currentAnimation.Frames == null || currentAnimation.Frames.Count == 0)
            {
                MessageBox.Show("No animation loaded to play.", "Animation Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            isPlaying = true;
            playButton.Enabled = false;
            stopButton.Enabled = true;
            playbackTimer.Start();
            statusLabel.Text = "Playing...";
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            StopPlayback();
        }

        private void StopPlayback()
        {
            isPlaying = false;
            playbackTimer.Stop();
            playButton.Enabled = true;
            stopButton.Enabled = false;
            statusLabel.Text = "Stopped";
        }

        private void PlaybackTimer_Tick(object sender, EventArgs e)
        {
            if (currentAnimation == null || currentAnimation.Frames == null || currentAnimation.Frames.Count == 0)
            {
                StopPlayback();
                return;
            }

            currentFrameIndex++;
            
            if (currentFrameIndex >= currentAnimation.Frames.Count)
            {
                if (loopCheckBox.Checked)
                {
                    currentFrameIndex = 0;
                }
                else
                {
                    StopPlayback();
                    return;
                }
            }

            frameListBox.SelectedIndex = currentFrameIndex;
            UpdatePreview();
        }

        private void FpsTrackBar_ValueChanged(object sender, EventArgs e)
        {
            int fps = fpsTrackBar.Value;
            playbackTimer.Interval = 1000 / fps;
            fpsLabel.Text = $"{fps} FPS";
        }

        private void ExportFramesButton_Click(object sender, EventArgs e)
        {
            if (currentAnimation == null || currentAnimation.Frames == null || currentAnimation.Frames.Count == 0)
            {
                MessageBox.Show("No animation loaded to export.", "Animation Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var fbd = new FolderBrowserDialog())
            using (var sfd = new SaveFileDialog())
            {
                fbd.Description = "Select folder to export animation frames";
                if (fbd.ShowDialog() != DialogResult.OK)
                    return;

                string baseName = $"body{currentAnimation.BodyId}_action{currentAnimation.Action}_dir{currentAnimation.Direction}";
                string exportFolder = Path.Combine(fbd.SelectedPath, baseName);
                Directory.CreateDirectory(exportFolder);

                for (int i = 0; i < currentAnimation.Frames.Count; i++)
                {
                    var frame = currentAnimation.Frames[i];
                    string framePath = Path.Combine(exportFolder, $"frame_{i:D3}.png");
                    frame.Image.Save(framePath, ImageFormat.Png);
                }

                // Export metadata
                var metadataPath = Path.Combine(exportFolder, "metadata.txt");
                var metadata = new System.Text.StringBuilder();
                metadata.AppendLine($"BodyID: {currentAnimation.BodyId}");
                metadata.AppendLine($"Action: {currentAnimation.Action}");
                metadata.AppendLine($"Direction: {currentAnimation.Direction}");
                metadata.AppendLine($"FrameCount: {currentAnimation.Frames.Count}");
                metadata.AppendLine("");
                metadata.AppendLine("Frame Data:");
                for (int i = 0; i < currentAnimation.Frames.Count; i++)
                {
                    var frame = currentAnimation.Frames[i];
                    metadata.AppendLine($"Frame {i}: Size={frame.Image.Width}x{frame.Image.Height}, Center=({frame.CenterX},{frame.CenterY})");
                }
                File.WriteAllText(metadataPath, metadata.ToString());

                MessageBox.Show($"Exported {currentAnimation.Frames.Count} frames to:\n{exportFolder}",
                    "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ExportSpriteSheetButton_Click(object sender, EventArgs e)
        {
            if (currentAnimation == null || currentAnimation.Frames == null || currentAnimation.Frames.Count == 0)
            {
                MessageBox.Show("No animation loaded to export.", "Animation Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = "PNG Image|*.png|All Files|*.*";
                sfd.DefaultExt = "png";
                sfd.FileName = $"spritesheet_body{currentAnimation.BodyId}_action{currentAnimation.Action}_dir{currentAnimation.Direction}.png";

                if (sfd.ShowDialog() != DialogResult.OK)
                    return;

                // Calculate sprite sheet dimensions
                int maxWidth = currentAnimation.Frames.Max(f => f.Image.Width);
                int maxHeight = currentAnimation.Frames.Max(f => f.Image.Height);
                int columns = (int)Math.Ceiling(Math.Sqrt(currentAnimation.Frames.Count));
                int rows = (int)Math.Ceiling((double)currentAnimation.Frames.Count / columns);

                int sheetWidth = maxWidth * columns;
                int sheetHeight = maxHeight * rows;

                using (var spriteSheet = new Bitmap(sheetWidth, sheetHeight, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(spriteSheet))
                {
                    g.Clear(Color.Transparent);
                    
                    // Set to pixel-perfect rendering - no interpolation or smoothing
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.SmoothingMode = SmoothingMode.None;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.CompositingQuality = CompositingQuality.HighSpeed;

                    // Draw all frames (no grid lines)
                    for (int i = 0; i < currentAnimation.Frames.Count; i++)
                    {
                        int col = i % columns;
                        int row = i / columns;
                        
                        int cellX = col * maxWidth;
                        int cellY = row * maxHeight;

                        var frame = currentAnimation.Frames[i];
                        // Center frame in cell
                        int offsetX = (maxWidth - frame.Image.Width) / 2;
                        int offsetY = (maxHeight - frame.Image.Height) / 2;
                        g.DrawImage(frame.Image, cellX + offsetX, cellY + offsetY, frame.Image.Width, frame.Image.Height);

                        // Update progress
                        if (i % 10 == 0)
                        {
                            // detailLabel.Text = $"Drawing frame {i + 1}/{currentAnimation.Frames.Count} at position ({col}, {row})";
                            // progressBar.Value = i;
                            // Application.DoEvents();
                        }
                    }

                    // progressBar.Value = currentAnimation.Frames.Count;
                    // detailLabel.Text = "Saving sprite sheet...";
                    // Application.DoEvents();

                    // Save with maximum quality - no compression
                    using (var stream = new FileStream(sfd.FileName, FileMode.Create))
                    {
                        spriteSheet.Save(stream, ImageFormat.Png);
                    }
                }

                MessageBox.Show($"Exported sprite sheet ({sheetWidth}x{sheetHeight}) with {currentAnimation.Frames.Count} frames.",
                    "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ExportAllFramesButton_Click(object sender, EventArgs e)
        {
            if (bodyListBox.SelectedItem == null)
            {
                MessageBox.Show("Please select a body ID first.", "Animation Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var bodyItem = bodyListBox.SelectedItem as BodyListItem;
            if (bodyItem == null)
                return;

            int bodyId = bodyItem.BodyId;
            var type = (AnimReader.AnimationType)animTypeComboBox.SelectedIndex;

            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select folder to export all animation frames";
                if (fbd.ShowDialog() != DialogResult.OK)
                    return;

                var animations = AnimReader.GetAvailableAnimations(mulFolderPath, bodyId)
                    .Where(a => a.Type == type)
                    .OrderBy(a => a.Action)
                    .ThenBy(a => a.Direction)
                    .ToList();

                if (animations.Count == 0)
                {
                    MessageBox.Show($"No animations found for Body ID {bodyId}.",
                        "No Data", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                int totalFrames = 0;

                foreach (var animInfo in animations)
                {
                    var anim = AnimReader.LoadAnimation(mulFolderPath, animInfo.BodyId, animInfo.Action, animInfo.Direction, type);

                    if (anim?.Frames == null || anim.Frames.Count == 0)
                        continue;

                    for (int i = 0; i < anim.Frames.Count; i++)
                    {
                        string fileName = $"{animInfo.BodyId}_{animInfo.Action}_{animInfo.Direction}_{i:D3}.png";
                        string framePath = Path.Combine(fbd.SelectedPath, fileName);
                        anim.Frames[i].Image.Save(framePath, ImageFormat.Png);
                    }

                    totalFrames += anim.Frames.Count;
                }

                MessageBox.Show(
                    $"Exported {totalFrames} frames to:\n{fbd.SelectedPath}",
                    "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        /// <summary>
        /// Export ALL frames from ALL actions and ALL directions for the selected body into a single massive sprite sheet
        /// Perfect for neural network training
        /// </summary>
        private void ExportFullBodySpriteSheetButton_Click(object sender, EventArgs e)
        {
            if (bodyListBox.SelectedItem == null)
            {
                MessageBox.Show("Please select a body ID first.", "Animation Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var bodyItem = bodyListBox.SelectedItem as BodyListItem;
            if (bodyItem == null)
                return;

            int bodyId = bodyItem.BodyId;
            var type = (AnimReader.AnimationType)animTypeComboBox.SelectedIndex;

            // Show progress form
            using (var progressForm = new Form())
            {
                progressForm.Text = "Exporting Full Body Sprite Sheet";
                progressForm.Size = new Size(500, 200);
                progressForm.StartPosition = FormStartPosition.CenterParent;
                progressForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                progressForm.MaximizeBox = false;
                progressForm.MinimizeBox = false;
                HolographicTheme.ApplyToForm(progressForm);

                var progressLabel = new Label
                {
                    Text = "Scanning animations...",
                    Location = new Point(20, 20),
                    AutoSize = true
                };
                HolographicTheme.ApplyToLabel(progressLabel);
                progressForm.Controls.Add(progressLabel);

                var progressBar = new ProgressBar
                {
                    Location = new Point(20, 50),
                    Size = new Size(440, 25)
                };
                progressForm.Controls.Add(progressBar);

                var detailLabel = new Label
                {
                    Text = "",
                    Location = new Point(20, 85),
                    Size = new Size(440, 60),
                    AutoSize = false
                };
                HolographicTheme.ApplyToLabel(detailLabel);
                progressForm.Controls.Add(detailLabel);

                progressForm.Show();
                Application.DoEvents();

                try
                {
                    // First pass: collect all animations and calculate max dimensions
                    progressLabel.Text = "Phase 1/3: Scanning all animations...";
                    Application.DoEvents();

                    var allFrames = new List<AnimReader.AnimationFrame>();
                    var frameMetadata = new List<string>();
                    int maxFrameWidth = 0;
                    int maxFrameHeight = 0;

                    var animations = AnimReader.GetAvailableAnimations(mulFolderPath, bodyId)
                        .Where(a => a.Type == type)
                        .OrderBy(a => a.Action)
                        .ThenBy(a => a.Direction)
                        .ToList();

                    if (animations.Count == 0)
                    {
                        MessageBox.Show($"No animations found for Body ID {bodyId}.", 
                            "No Data", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    progressBar.Maximum = animations.Count;
                    progressBar.Value = 0;

                    // Load all animations
                    foreach (var animInfo in animations)
                    {
                        detailLabel.Text = $"Loading: Body {animInfo.BodyId}, Action {animInfo.Action}, Direction {animInfo.Direction}";
                        Application.DoEvents();

                        var anim = AnimReader.LoadAnimation(mulFolderPath, animInfo.BodyId, animInfo.Action, animInfo.Direction, type);
                        
                        if (anim?.Frames != null && anim.Frames.Count > 0)
                        {
                            foreach (var frame in anim.Frames)
                            {
                                allFrames.Add(frame);
                                frameMetadata.Add($"Body:{animInfo.BodyId} Action:{animInfo.Action} Dir:{animInfo.Direction} Frame:{frame.FrameIndex} Size:{frame.Image.Width}x{frame.Image.Height} Center:{frame.CenterX},{frame.CenterY}");
                                
                                if (frame.Image.Width > maxFrameWidth)
                                    maxFrameWidth = frame.Image.Width;
                                if (frame.Image.Height > maxFrameHeight)
                                    maxFrameHeight = frame.Image.Height;
                            }
                        }

                        progressBar.Value++;
                    }

                    if (allFrames.Count == 0)
                    {
                        MessageBox.Show($"No frames found for Body ID {bodyId}.", 
                            "No Data", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    // Phase 2: Calculate optimal grid layout
                    progressLabel.Text = $"Phase 2/3: Calculating layout for {allFrames.Count} frames...";
                    detailLabel.Text = $"Max frame size: {maxFrameWidth}x{maxFrameHeight}";
                    Application.DoEvents();

                    // Add padding for better visibility
                    int cellWidth = maxFrameWidth + 4;   // 2px padding on each side
                    int cellHeight = maxFrameHeight + 4; // 2px padding top/bottom

                    // Calculate grid dimensions - aim for roughly square layout
                    int columns = (int)Math.Ceiling(Math.Sqrt(allFrames.Count));
                    int rows = (int)Math.Ceiling((double)allFrames.Count / columns);

                    int sheetWidth = cellWidth * columns;
                    int sheetHeight = cellHeight * rows;

                    // Ask user for save location
                    progressForm.Hide();
                    
                    using (var sfd = new SaveFileDialog())
                    {
                        sfd.Filter = "PNG Image|*.png|All Files|*.*";
                        sfd.DefaultExt = "png";
                        sfd.FileName = $"fullbody_spritesheet_body{bodyId}_{type}.png";
                        sfd.Title = $"Save Full Body Sprite Sheet ({allFrames.Count} frames)";

                        if (sfd.ShowDialog() != DialogResult.OK)
                            return;

                        progressForm.Show();
                        Application.DoEvents();

                        // Phase 3: Generate sprite sheet
                        progressLabel.Text = $"Phase 3/3: Generating sprite sheet ({sheetWidth}x{sheetHeight})...";
                        progressBar.Maximum = allFrames.Count;
                        progressBar.Value = 0;

                        using (var spriteSheet = new Bitmap(sheetWidth, sheetHeight, PixelFormat.Format32bppArgb))
                        using (var g = Graphics.FromImage(spriteSheet))
                        {
                            // Clear to transparent background
                            g.Clear(Color.Transparent);
                            
                            // Set to pixel-perfect rendering - no interpolation or smoothing
                            g.InterpolationMode = InterpolationMode.NearestNeighbor;
                            g.SmoothingMode = SmoothingMode.None;
                            g.PixelOffsetMode = PixelOffsetMode.Half;
                            g.CompositingMode = CompositingMode.SourceCopy;
                            g.CompositingQuality = CompositingQuality.HighSpeed;

                            // Draw all frames (no grid lines)
                            for (int i = 0; i < allFrames.Count; i++)
                            {
                                int col = i % columns;
                                int row = i / columns;
                                
                                int cellX = col * cellWidth;
                                int cellY = row * cellHeight;

                                var frame = allFrames[i];
                                
                                // Center frame in cell (with padding offset)
                                int offsetX = (cellWidth - frame.Image.Width) / 2;
                                int offsetY = (cellHeight - frame.Image.Height) / 2;
                                
                                g.DrawImage(frame.Image, cellX + offsetX, cellY + offsetY, frame.Image.Width, frame.Image.Height);

                                // Update progress
                                if (i % 10 == 0)
                                {
                                    detailLabel.Text = $"Drawing frame {i + 1}/{allFrames.Count} at position ({col}, {row})";
                                    progressBar.Value = i;
                                    Application.DoEvents();
                                }
                            }

                            progressBar.Value = allFrames.Count;
                            detailLabel.Text = "Saving sprite sheet...";
                            Application.DoEvents();

                            // Save with maximum quality - no compression
                            using (var stream = new FileStream(sfd.FileName, FileMode.Create))
                            {
                                spriteSheet.Save(stream, ImageFormat.Png);
                            }
                        }

                        // Save metadata
                        string metadataPath = Path.ChangeExtension(sfd.FileName, ".txt");
                        var metadata = new System.Text.StringBuilder();
                        metadata.AppendLine("=== FULL BODY SPRITE SHEET METADATA ===");
                        metadata.AppendLine($"Body ID: {bodyId}");
                        metadata.AppendLine($"Animation Type: {type}");
                        metadata.AppendLine($"Total Frames: {allFrames.Count}");
                        metadata.AppendLine($"Sheet Dimensions: {sheetWidth}x{sheetHeight}");
                        metadata.AppendLine($"Grid Layout: {columns} columns x {rows} rows");
                        metadata.AppendLine($"Cell Size: {cellWidth}x{cellHeight} (includes 2px padding)");
                        metadata.AppendLine($"Max Frame Size: {maxFrameWidth}x{maxFrameHeight}");
                        metadata.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                        metadata.AppendLine();
                        metadata.AppendLine("=== FRAME INDEX ===");
                        metadata.AppendLine("Format: Index | Body | Action | Direction | Frame | Size | Center | Grid Position");
                        metadata.AppendLine();
                        
                        for (int i = 0; i < allFrames.Count; i++)
                        {
                            int col = i % columns;
                            int row = i / columns;
                            metadata.AppendLine($"{i:D4} | {frameMetadata[i]} | Grid:({col},{row})");
                        }

                        File.WriteAllText(metadataPath, metadata.ToString());

                        detailLabel.Text = "Complete!";
                        Application.DoEvents();

                        progressForm.Close();

                        MessageBox.Show(
                            $"Full body sprite sheet exported successfully!\n\n" +
                            $"Total Frames: {allFrames.Count}\n" +
                            $"Sheet Size: {sheetWidth}x{sheetHeight}\n" +
                            $"Grid: {columns}x{rows}\n" +
                            $"Cell Size: {cellWidth}x{cellHeight}\n\n" +
                            $"Saved to:\n{sfd.FileName}\n\n" +
                            $"Metadata saved to:\n{metadataPath}",
                            "Export Complete",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                }
                catch (Exception ex)
                {
                    progressForm.Close();
                    MessageBox.Show($"Error exporting sprite sheet: {ex.Message}\n\n{ex.StackTrace}",
                        "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ImportFramesButton_Click(object sender, EventArgs e)
        {
            Bitmap sourceImage = GetBitmapFromClipboardPreserveAlpha();

            if (sourceImage == null)
            {
                using (var ofd = new OpenFileDialog())
                {
                    ofd.Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif";
                    ofd.Title = "Select sprite sheet or frame image";
                    if (ofd.ShowDialog() != DialogResult.OK)
                        return;

                    sourceImage = new Bitmap(ofd.FileName);
                }
            }

            if (sourceImage == null)
                return;

            try
            {
                using (var dialog = new SpriteSheetSplitterDialog(sourceImage))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                        return;

                    var sprites = dialog.DetectedSprites;
                    if (sprites == null || sprites.Count == 0)
                    {
                        MessageBox.Show("No sprites detected.", "Import / Split", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    StopPlayback();

                    // Dispose old imported frame images to avoid leaks.
                    if (currentAnimation?.Frames != null)
                    {
                        foreach (var f in currentAnimation.Frames)
                        {
                            f.Image?.Dispose();
                        }
                    }

                    if (currentAnimation == null)
                    {
                        currentAnimation = new AnimReader.AnimationInfo();
                    }

                    currentAnimation.BodyId = currentAnimation.BodyId;
                    currentAnimation.Action = currentAnimation.Action;
                    currentAnimation.Direction = currentAnimation.Direction;
                    currentAnimation.Type = (AnimReader.AnimationType)Math.Max(0, animTypeComboBox.SelectedIndex);
                    currentAnimation.Frames = new List<AnimReader.AnimationFrame>();

                    for (int i = 0; i < sprites.Count; i++)
                    {
                        var sp = sprites[i];
                        var frameImg = new Bitmap(sp.Image);
                        currentAnimation.Frames.Add(new AnimReader.AnimationFrame
                        {
                            FrameIndex = i,
                            Image = frameImg,
                            CenterX = frameImg.Width / 2,
                            CenterY = frameImg.Height / 2
                        });
                    }

                    currentAnimation.FrameCount = currentAnimation.Frames.Count;

                    frameListBox.Items.Clear();
                    for (int i = 0; i < currentAnimation.Frames.Count; i++)
                    {
                        frameListBox.Items.Add($"Frame {i}");
                    }

                    currentFrameIndex = 0;
                    if (frameListBox.Items.Count > 0)
                        frameListBox.SelectedIndex = 0;

                    statusLabel.Text = $"Imported {currentAnimation.Frames.Count} frame(s) from split sprite sheet.";
                    UpdatePreview();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to import/split image: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                sourceImage.Dispose();
            }
        }

        private void ReplaceSingleFrameButton_Click(object sender, EventArgs e)
        {
            if (currentAnimation == null || currentAnimation.Frames == null || currentAnimation.Frames.Count == 0)
            {
                MessageBox.Show("No animation loaded.", "Animation Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (currentFrameIndex < 0 || currentFrameIndex >= currentAnimation.Frames.Count)
            {
                MessageBox.Show("Please select a frame to replace.", "Animation Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "Image Files|*.png;*.bmp;*.jpg;*.jpeg|All Files|*.*";
                ofd.Title = "Select replacement image";

                if (ofd.ShowDialog() != DialogResult.OK)
                    return;

                try
                {
                    var newImage = new Bitmap(ofd.FileName);
                    var oldFrame = currentAnimation.Frames[currentFrameIndex];
                    
                    // Dispose old image and replace
                    oldFrame.Image?.Dispose();
                    oldFrame.Image = newImage;

                    UpdatePreview();
                    statusLabel.Text = $"Replaced frame {currentFrameIndex} with {Path.GetFileName(ofd.FileName)}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to load image: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void SaveAnimButton_Click(object sender, EventArgs e)
        {
            if (currentAnimation == null || currentAnimation.Frames == null || currentAnimation.Frames.Count == 0)
            {
                MessageBox.Show("No animation loaded to save.", "Animation Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (string.IsNullOrEmpty(mulFolderPath) || !Directory.Exists(mulFolderPath))
            {
                MessageBox.Show("MUL folder path is not configured. Please set it in Settings.",
                    "Configuration Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Save animation to MUL files?\n\n" +
                $"Body: {currentAnimation.BodyId}\n" +
                $"Action: {currentAnimation.Action}\n" +
                $"Direction: {currentAnimation.Direction}\n" +
                $"Frames: {currentAnimation.Frames.Count}\n\n" +
                $"Original files will be backed up.",
                "Save Animation",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
                return;

            try
            {
                statusLabel.Text = "Saving animation...";
                Application.DoEvents();

                var type = (AnimReader.AnimationType)animTypeComboBox.SelectedIndex;
                bool success = AnimWriter.WriteAnimation(mulFolderPath, currentAnimation, type);

                if (success)
                {
                    statusLabel.Text = "Animation saved successfully!";
                    MessageBox.Show("Animation saved to MUL files successfully!\n\nOriginal files have been backed up.",
                        "Save Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    statusLabel.Text = "Failed to save animation.";
                    MessageBox.Show("Failed to save animation. Check debug output for details.",
                        "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Error saving animation.";
                MessageBox.Show($"Error saving animation: {ex.Message}",
                    "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private byte[] CompressImageData(Bitmap image)
        {
            using (var ms = new MemoryStream())
            {
                image.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        private Bitmap GetBitmapFromClipboardPreserveAlpha()
        {
            try
            {
                IDataObject dataObject = Clipboard.GetDataObject();
                if (dataObject != null && dataObject.GetDataPresent("PNG"))
                {
                    var pngData = dataObject.GetData("PNG");
                    Stream stream = pngData as Stream;
                    if (stream == null && pngData is byte[] bytes)
                    {
                        stream = new MemoryStream(bytes);
                    }

                    if (stream != null)
                    {
                        using (stream)
                        using (var img = Image.FromStream(stream, true, true))
                        {
                            var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
                            using (var g = Graphics.FromImage(bmp))
                            {
                                g.Clear(Color.Transparent);
                                g.CompositingMode = CompositingMode.SourceCopy;
                                g.DrawImage(img, 0, 0, img.Width, img.Height);
                            }
                            return bmp;
                        }
                    }
                }
            }
            catch
            {
            }

            if (Clipboard.ContainsImage())
            {
                var img = Clipboard.GetImage();
                if (img != null)
                {
                    var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.DrawImage(img, 0, 0, img.Width, img.Height);
                    }
                    return bmp;
                }
            }

            return null;
        }

        private string GetActionName(int action)
        {
            // Standard UO action names
            var actionNames = new Dictionary<int, string>
            {
                { 0, "Walk" },
                { 1, "Walk (with weapon)" },
                { 2, "Run" },
                { 3, "Run (with weapon)" },
                { 4, "Stand" },
                { 5, "Fidget 1" },
                { 6, "Fidget 2" },
                { 7, "Stand (one-handed attack)" },
                { 8, "Stand (two-handed attack)" },
                { 9, "Attack 1" },
                { 10, "Attack 2" },
                { 11, "Attack 3" },
                { 12, "Attack (bow)" },
                { 13, "Attack (crossbow)" },
                { 14, "Get hit" },
                { 15, "Die 1" },
                { 16, "Die 2" },
                { 17, "On horse" },
                { 18, "Get hit (on horse)" },
                { 19, "Die (on horse)" },
                { 20, "Attack (on horse)" },
                { 21, "Bow" },
                { 22, "Salute" },
                { 23, "Eat" }
            };

            return actionNames.ContainsKey(action) ? $"{action} - {actionNames[action]}" : action.ToString();
        }

        private string GetDirectionName(int direction)
        {
            var dirNames = new[] { "South", "Southeast", "East", "Northeast", "North", "Northwest", "West", "Southwest" };
            return direction >= 0 && direction < dirNames.Length ? $"{direction} - {dirNames[direction]}" : direction.ToString();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopPlayback();
            playbackTimer?.Dispose();
            
            // Dispose current animation frames
            if (currentAnimation?.Frames != null)
            {
                foreach (var frame in currentAnimation.Frames)
                {
                    frame.Image?.Dispose();
                }
            }

            base.OnFormClosing(e);
        }

        // Helper classes for list items
        private class BodyListItem
        {
            public int BodyId { get; set; }
            public string DisplayName { get; set; }
            public override string ToString() => DisplayName;
        }

        private class ActionListItem
        {
            public int Action { get; set; }
            public string DisplayName { get; set; }
            public override string ToString() => DisplayName;
        }

        private class DirectionListItem
        {
            public int Direction { get; set; }
            public string DisplayName { get; set; }
            public override string ToString() => DisplayName;
        }
    }
}
