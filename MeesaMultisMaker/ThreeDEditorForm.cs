using MeesaMultisMaker.ComfyUI;
using MeesaMultisMaker.ThreeD;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    /// <summary>
    /// 3D Editor: sends a text prompt to the MultisMaker1 ComfyUI workflow,
    /// downloads the resulting .ply gaussian splat, previews it in a UO-style
    /// isometric viewport (8 directions), and exports UO-ratio sprites.
    /// </summary>
    public partial class ThreeDEditorForm : Form
    {
        // Fired when the user sends the current view to the multi canvas.
        // Bitmap ownership transfers to the subscriber (do not dispose here).
        public event Action<Bitmap, string> SpriteReady;

        private TextBox promptTextBox;
        private CheckBox loopCheckBox;
        private CheckBox loopNewImageCheckBox;
        private CheckBox loopRandomPromptCheckBox;
        // True while a run is in its 2D half: a finished 2D stages itself
        // as init and chains into Generate 3D instead of ending. Set by
        // loop-with-new-image iterations and by Generate 3D kickoffs that
        // have no usable init image (a 3D from pure black would only crash
        // Tripo with "mask is empty"). Consumed by GenerateButton_Click:
        // a chained call skips the 2D routing and goes straight to 3D.
        private bool _chain3D;
        // When true, the pending chain only fires while Loop is still
        // checked (loop iterations). A manual 2D-first kickoff with Loop
        // off chains unconditionally.
        private bool _chainNeedsLoop;
        private TextBox seedTextBox;
        private NumericUpDown stepsNumeric;
        private NumericUpDown fluxStepsNumeric;
        private TextBox workflowPathTextBox;
        private TextBox comfyUrlTextBox;
        private TextBox initImageTextBox;
        private Button generateButton;
        private Button genInitButton;
        private PictureBox initPreviewBox;
        private Button cancelButton;
        private Button openPlyButton;
        private Button undoLassoButton;
        private Label statusLabel;
        private ProgressBar progressBar;
        private TextBox logTextBox;

        private PictureBox viewport;
        private Button[] dirButtons = new Button[8];
        private NumericUpDown pitchNumeric;
        private TrackBar zoomTrackBar;
        private TextBox dotSizeTextBox;
        private TextBox maxSplatsTextBox;
        private CheckBox gridCheckBox;
        private PictureBox refThumb;
        private string _shownRef = string.Empty;
        private TableLayoutPanel quadPanel;
        private PictureBox[] quadViews;
        private Button quadBtn;
        private bool _quad;

        private void ShowRefImage(Bitmap bmp)
        {
            try
            {
                if (refThumb == null) { bmp.Dispose(); return; }
                var old = refThumb.Image as Bitmap;
                refThumb.Image = bmp;
                refThumb.Visible = true;
                refThumb.BringToFront();
                if (old != null) old.Dispose();
                if (_shownRef != null && _shownRef.Length > 0 && !_refLogged)
                {
                    _refLogged = true;
                    Log("Showing Flux reference image.");
                }
            }
            catch { try { bmp.Dispose(); } catch { } }
        }

        private bool _refLogged;

        private void HideRef()
        {
            try
            {
                if (InvokeRequired) { BeginInvoke(new Action(HideRef)); return; }
                _shownRef = string.Empty;
                _refLogged = false;
                if (refThumb != null)
                {
                    var old = refThumb.Image as Bitmap;
                    refThumb.Image = null;
                    refThumb.Visible = false;
                    if (old != null) old.Dispose();
                }
            }
            catch { }
        }
        private CheckBox uoCheckBox;
        private NumericUpDown baseRotNumeric;
        private NumericUpDown rotXNumeric, rotYNumeric, rotZNumeric;
        private NumericUpDown scXNumeric, scYNumeric, scZNumeric;
        private CheckBox _scaleLockChk;
        private bool _scaleSync;
        private Button bgColorButton;
        private CheckBox transparentCheckBox;
        private CheckBox cropCheckBox;
        private ComboBox resolutionComboBox;
        private Label viewInfoLabel;
        private Button exportCurrentButton;
        private Button exportAllButton;
        private Button sendToCanvasButton;
        private Button useAsInputButton;
        private Label plyInfoLabel;

        private PlySplatModel _model;
        private string _plyPath = string.Empty;
        private Bitmap _viewBitmap;
        private float _yaw = 45f; // reset to FacingYaw(N) in constructor
        private int _direction = 0;
        private Color _bgColor = Color.FromArgb(24, 24, 32);
        private ComfyUIClient _comfy;
        private CancellationTokenSource _cts;
        private ThreeD.ComfyPreviewSocket _activePreviewSocket;
        private bool _renderQueued;
        private string _initImageServerName;

        private SplitContainer _split;

        public ThreeDEditorForm()
        {
            InitializeComponent();
            HolographicTheme.ApplyToForm(this);
            _comfy = new ComfyUIClient(comfyUrlTextBox.Text);
            _yaw = FacingYaw(_direction);
            pitchNumeric.Enabled = !UoMode();
            UpdateViewButtons();
            BuildEditPanel();
            BuildTileDock();
            RefreshInitPreview();
            KeyPreview = true;
            KeyDown += ThreeDEditorForm_KeyDown;
            Shown += ThreeDEditorForm_Shown;
        }

        private void ThreeDEditorForm_Shown(object sender, EventArgs e)
        {
            try
            {
                // Order matters: set the distance first while min sizes are
                // still defaults (always satisfiable at full width), then
                // raise the mins to match. Everything guarded.
                if (_split != null && _split.Width > 0)
                {
                    int avail = _split.Width - _split.SplitterWidth;
                    int want = 370;
                    if (want > avail - 25) want = avail - 25;
                    if (want < 25) want = 25;
                    _split.SplitterDistance = want;
                    // Only raise mins as far as the real width allows.
                    int min1 = Math.Min(330, Math.Max(25, want));
                    int min2 = Math.Min(400, Math.Max(25, avail - want));
                    _split.Panel1MinSize = min1;
                    _split.Panel2MinSize = min2;
                    _split.FixedPanel = FixedPanel.Panel1;
                }
                HolographicTheme.ApplyToAllControls(this);
            HolographicTheme.ApplyToButton(generateButton, ButtonStyle.Success);
            HolographicTheme.ApplyToButton(cancelButton, ButtonStyle.Danger);
            HolographicTheme.ApplyToButton(sendToCanvasButton, ButtonStyle.Accent);
            var tips = new ToolTip { ShowAlways = true };
            try
            {
                tips.SetToolTip(workflowPathTextBox, workflowPathTextBox.Text);
                tips.SetToolTip(initImageTextBox, initImageTextBox.Text);
                tips.SetToolTip(viewport, "Drag to orbit. Direction buttons snap to the 8 UO facings.");
            }
            catch { }
            UpdateViewButtons();
            }
            catch { }
        }

        #region UI construction

        private void InitializeComponent()
        {
            Text = "3D Editor - MultisMaker1 (Text to PLY to UO Sprite)";
            Width = 1220;
            Height = 800;
            StartPosition = FormStartPosition.CenterScreen;
            // Right panel content reaches x~790 (direction row + bottom
            // rows), so the minimum must keep Panel2 that wide: 370 left
            // + splitter + ~810 right + chrome. No more vanishing buttons.
            MinimumSize = new Size(1200, 700);

            // NOTE: do NOT set SplitterDistance / Panel mins / FixedPanel here:
            // the container is still default-sized (~150px) and the setters
            // throw InvalidOperationException when violated. All of that is
            // done in Shown, after layout, inside try/catch.
            _split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical
            };
            var main = _split;
            Controls.Add(main);

            // ---------- Left: generation controls ----------
            var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10), AutoScroll = true };
            main.Panel1.Controls.Add(left);
            int y = 8;

            left.Controls.Add(MakeLabel("Prompt (object on black background):", 10, y, 320, 16));
            y += 20;
            promptTextBox = new TextBox
            {
                Location = new Point(10, y), Width = 320, Height = 70,
                Multiline = true, ScrollBars = ScrollBars.Vertical,
                Text = "an armoire, medieval, single object on a black background"
            };
            left.Controls.Add(promptTextBox);
            y += 78;

            BuildLlmPromptUi(left, ref y);

            loopCheckBox = new CheckBox { Location = new Point(10, y), Width = 320, Height = 20, Text = "Loop (auto-resubmit Generate forever)" };
            left.Controls.Add(loopCheckBox);
            var loopTip = new ToolTip { ShowAlways = true };
            try { loopTip.SetToolTip(loopCheckBox, "After each finished run, immediately start the next one. Every .ply is timestamped so nothing overwrites. Uncheck or Cancel to stop. Stops on error."); } catch { }
            y += 26;

            loopNewImageCheckBox = new CheckBox { Location = new Point(10, y), Width = 320, Height = 20, Text = "Loop with new image" };
            left.Controls.Add(loopNewImageCheckBox);
            try { loopTip.SetToolTip(loopNewImageCheckBox, "Loop mode only: every iteration clears the init image and generates a fresh 2D first, then the 3D from it. Never reuses the previous init. Tip: leave Seed at -1 so each iteration differs."); } catch { }
            loopNewImageCheckBox.CheckedChanged += (s, e) =>
            {
                try { if (loopNewImageCheckBox.Checked && loopCheckBox != null) loopCheckBox.Checked = true; }
                catch { }
            };
            y += 26;

            loopRandomPromptCheckBox = new CheckBox { Location = new Point(10, y), Width = 320, Height = 20, Text = "Loop with random prompt" };
            left.Controls.Add(loopRandomPromptCheckBox);
            try { loopTip.SetToolTip(loopRandomPromptCheckBox, "Loop mode only: dream up a fresh prompt with the local LLM at the start of every iteration. Combines with Loop with new image (new prompt AND new image each run). A failed idea stops the loop."); } catch { }
            loopRandomPromptCheckBox.CheckedChanged += (s, e) =>
            {
                try { if (loopRandomPromptCheckBox.Checked && loopCheckBox != null) loopCheckBox.Checked = true; }
                catch { }
            };
            y += 26;

            left.Controls.Add(MakeLabel("Seed (-1 = random):", 10, y, 150, 20));
            seedTextBox = new TextBox { Location = new Point(160, y), Width = 170, Height = 20, Text = "-1" };
            left.Controls.Add(seedTextBox);
            y += 28;

            left.Controls.Add(MakeLabel("KSampler steps:", 10, y, 150, 20));
            stepsNumeric = new NumericUpDown
            {
                Location = new Point(160, y), Width = 170, Height = 20,
                Minimum = 1, Maximum = 100, Value = 20
            };
            left.Controls.Add(stepsNumeric);
            y += 28;

            left.Controls.Add(MakeLabel("Flux steps (2D btn):", 10, y, 150, 20));
            fluxStepsNumeric = new NumericUpDown
            {
                Location = new Point(160, y), Width = 170, Height = 20,
                Minimum = 1, Maximum = 12, Value = 4
            };
            left.Controls.Add(fluxStepsNumeric);
            y += 28;

            left.Controls.Add(MakeLabel("Model facing offset:", 10, y, 150, 20));
            baseRotNumeric = new NumericUpDown
            {
                Location = new Point(160, y), Width = 170, Height = 20,
                Minimum = -360, Maximum = 720, Increment = 5, Value = 225
            };
            baseRotNumeric.ValueChanged += BaseRot_Changed;
            left.Controls.Add(baseRotNumeric);
            y += 28;

            left.Controls.Add(MakeLabel("Rotate X Y Z:", 10, y, 90, 20));
            rotXNumeric = MakeRotBox(105, y);
            rotYNumeric = MakeRotBox(175, y);
            rotZNumeric = MakeRotBox(245, y);
            left.Controls.Add(rotXNumeric);
            left.Controls.Add(rotYNumeric);
            left.Controls.Add(rotZNumeric);
            var rotTip = new ToolTip { ShowAlways = true };
            try
            {
                rotTip.SetToolTip(rotXNumeric, "Rotate object about X (degrees, applied first). Independent of N..NW facings.");
                rotTip.SetToolTip(rotYNumeric, "Rotate object about Y (degrees). Independent of N..NW facings.");
                rotTip.SetToolTip(rotZNumeric, "Rotate object about Z (degrees, applied last). Independent of N..NW facings.");
            }
            catch { }
            y += 28;

            left.Controls.Add(MakeLabel("Scale X Y Z:", 10, y, 90, 20));
            scXNumeric = MakeScaleBox(105, y);
            scYNumeric = MakeScaleBox(175, y);
            scZNumeric = MakeScaleBox(245, y);
            left.Controls.Add(scXNumeric);
            left.Controls.Add(scYNumeric);
            left.Controls.Add(scZNumeric);
            try
            {
                rotTip.SetToolTip(scXNumeric, "Scale object along X (1 = unchanged). Independent of N..NW facings.");
                rotTip.SetToolTip(scYNumeric, "Scale object along Y (1 = unchanged). Independent of N..NW facings.");
                rotTip.SetToolTip(scZNumeric, "Scale object along Z (1 = unchanged). Independent of N..NW facings.");
            }
            catch { }
            y += 28;

            _scaleLockChk = new CheckBox { Location = new Point(10, y), Width = 320, Height = 20, Text = "Lock scale X/Y/Z together", Checked = true };
            try
            {
                var lockTip = new ToolTip { ShowAlways = true };
                lockTip.SetToolTip(_scaleLockChk, "ON: editing any Scale box sets all three (uniform resize). OFF: each axis independent.");
            }
            catch { }
            left.Controls.Add(_scaleLockChk);
            y += 26;

            uoCheckBox = new CheckBox { Location = new Point(10, y), Width = 320, Height = 20, Text = "UO projection (matches canvas grid)", Checked = true };
            uoCheckBox.CheckedChanged += UoCheckBox_CheckedChanged;
            left.Controls.Add(uoCheckBox);
            y += 28;
            var uoTip = new ToolTip { ShowAlways = true };
            try
            {
                uoTip.SetToolTip(uoCheckBox, "ON: true UO oblique view, 1:1 grid like the multi canvas. Exports always use this.\nOFF: free orbit camera for inspecting tops/bottoms.");
                uoTip.SetToolTip(baseRotNumeric, "Rotates the model for all 8 facings. Default 225 puts TripoSplat fronts on S.");
            }
            catch { }

            left.Controls.Add(MakeLabel("ComfyUI URL:", 10, y, 320, 16));
            y += 18;
            comfyUrlTextBox = new TextBox { Location = new Point(10, y), Width = 250, Height = 20 };
            try { comfyUrlTextBox.Text = AppConfig.Instance.ComfyUIUrl; }
            catch { comfyUrlTextBox.Text = "http://localhost:8188"; }
            left.Controls.Add(comfyUrlTextBox);
            var testBtn = new Button { Location = new Point(266, y - 1), Width = 64, Height = 23, Text = "Test" };
            testBtn.Click += TestBtn_Click;
            left.Controls.Add(testBtn);
            y += 30;

            left.Controls.Add(MakeLabel("Workflow (MultisMaker1.json):", 10, y, 320, 16));
            y += 18;
            workflowPathTextBox = new TextBox { Location = new Point(10, y), Width = 250, Height = 20, Text = MultisMaker1Workflow.DefaultWorkflowPath };
            left.Controls.Add(workflowPathTextBox);
            var browseWf = new Button { Location = new Point(266, y - 1), Width = 64, Height = 23, Text = "..." };
            browseWf.Click += BrowseWf_Click;
            left.Controls.Add(browseWf);
            y += 30;

            left.Controls.Add(MakeLabel("Init image (optional):", 10, y, 320, 16));
            y += 18;
            initImageTextBox = new TextBox { Location = new Point(10, y), Width = 206, Height = 20, ReadOnly = true };
            initImageTextBox.KeyDown += InitImageBox_KeyDown;
            try
            {
                var pasteTip = new ToolTip { ShowAlways = true };
                pasteTip.SetToolTip(initImageTextBox, "Click Browse (...), or focus here and press Ctrl+V to paste an image straight from the clipboard.");
            }
            catch { }
            left.Controls.Add(initImageTextBox);
            var browseImg = new Button { Location = new Point(222, y - 1), Width = 58, Height = 23, Text = "..." };
            browseImg.Click += BrowseImg_Click;
            left.Controls.Add(browseImg);
            var clearImg = new Button { Location = new Point(286, y - 1), Width = 44, Height = 23, Text = "X" };
            clearImg.Click += (s, ev) => { initImageTextBox.Text = string.Empty; _initImageServerName = null; RefreshInitPreview(); Log("Init image cleared (neutral black will be used)."); };
            left.Controls.Add(clearImg);
            var clearTip = new ToolTip { ShowAlways = true };
            try { clearTip.SetToolTip(clearImg, "Clear the init image (falls back to neutral black)."); } catch { }
            y += 30;

            initPreviewBox = new PictureBox
            {
                Location = new Point(10, y), Width = 320, Height = 160,
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.Black
            };
            left.Controls.Add(initPreviewBox);
            try { clearTip.SetToolTip(initPreviewBox, "Current init image (what the next Generate starts from). Black = neutral, full text-to-image."); } catch { }
            y += 168;

            generateButton = new Button { Location = new Point(10, y), Width = 200, Height = 32, Text = "Generate 3D (.ply)" };
            generateButton.Click += GenerateButton_Click;
            left.Controls.Add(generateButton);
            cancelButton = new Button { Location = new Point(216, y), Width = 114, Height = 32, Text = "Cancel", Enabled = false };
            cancelButton.Click += CancelButton_Click;
            left.Controls.Add(cancelButton);
            y += 40;

            genInitButton = new Button { Location = new Point(10, y), Width = 320, Height = 26, Text = "Generate Init Image (2D only)" };
            genInitButton.Click += GenInitButton_Click;
            left.Controls.Add(genInitButton);
            try
            {
                var giTip = new ToolTip { ShowAlways = true };
                giTip.SetToolTip(genInitButton, "Runs only the Flux image stage (same prompt, seed and init) and stages the result as the init image. Fast way to approve the 2D before a minutes-long 3D run.");
            }
            catch { }
            y += 34;

            openPlyButton = new Button { Location = new Point(10, y), Width = 320, Height = 26, Text = "Open local 3D model (skip generation)" };
            openPlyButton.Click += OpenPlyButton_Click;
            left.Controls.Add(openPlyButton);
            y += 34;

            undoLassoButton = new Button { Location = new Point(10, y), Width = 320, Height = 26, Text = "Undo edit", Enabled = false };
            undoLassoButton.Click += UndoLassoButton_Click;
            left.Controls.Add(undoLassoButton);
            y += 34;

            statusLabel = new Label { Location = new Point(10, y), Width = 320, Height = 20, Text = "Idle.", ForeColor = Color.Gray };
            left.Controls.Add(statusLabel);
            y += 24;
            progressBar = new ProgressBar { Location = new Point(10, y), Width = 320, Height = 16, Style = ProgressBarStyle.Blocks, Visible = false };
            left.Controls.Add(progressBar);
            y += 24;

            plyInfoLabel = new Label { Location = new Point(10, y), Width = 320, Height = 20, Text = "No model loaded.", ForeColor = Color.Gray };
            left.Controls.Add(plyInfoLabel);
            y += 26;

            left.Controls.Add(MakeLabel("Log:", 10, y, 320, 16));
            y += 18;
            logTextBox = new TextBox
            {
                Location = new Point(10, y), Width = 320, Height = 220,
                Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true
            };
            left.Controls.Add(logTextBox);

            // ---------- Right: viewport + export ----------
            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            main.Panel2.Controls.Add(right);

            var dirPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = HolographicTheme.PanelBackground
            };
            right.Controls.Add(dirPanel);
            for (int i = 0; i < 8; i++)
            {
                int d = i;
                var b = new Button { Width = 44, Height = 28, Text = SplatRenderer.DirectionNames[i], Tag = d };
                b.Click += DirButton_Click;
                dirButtons[i] = b;
                dirPanel.Controls.Add(b);
            }
            viewInfoLabel = new Label { Width = 220, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Text = "Dir N (45 deg) UO" };
            dirPanel.Controls.Add(viewInfoLabel);
            var setSBtn = new Button { Width = 64, Height = 28, Text = "Set S" };
            setSBtn.Click += SetSBtn_Click;
            dirPanel.Controls.Add(setSBtn);
            var setSTip = new ToolTip { ShowAlways = true };
            try { setSTip.SetToolTip(setSBtn, "Orbit to the model's front, then click: this view becomes South and all 8 facings follow."); } catch { }
            quadBtn = new Button { Width = 64, Height = 28, Text = "Quad" };
            quadBtn.Click += delegate { SetQuad(!_quad); };
            dirPanel.Controls.Add(quadBtn);
            try { setSTip.SetToolTip(quadBtn, "Toggle 4-panel view (Top / Front / Side / current). Double-click a panel to go back."); } catch { }
            var sliceTopBtn = new Button { Width = 88, Height = 28, Text = "Slice tiles" };
            sliceTopBtn.Click += SliceTopBtn_Click;
            dirPanel.Controls.Add(sliceTopBtn);
            try { setSTip.SetToolTip(sliceTopBtn, "Hard-cut the model into UO tiles (see TILE SLICES panel on the right)."); } catch { }

            viewport = new PictureBox
            {
                Dock = DockStyle.Fill, BackColor = Color.FromArgb(18, 18, 24),
                SizeMode = PictureBoxSizeMode.CenterImage, BorderStyle = BorderStyle.FixedSingle
            };
            viewport.MouseDown += Viewport_MouseDown;
            viewport.MouseMove += Viewport_MouseMove;
            viewport.MouseUp += Viewport_MouseUp;
            viewport.MouseWheel += ThreeDView_MouseWheel;
            viewport.Paint += Viewport_PaintOverlay;
            refThumb = new PictureBox
            {
                Location = new Point(8, 8),
                Size = new Size(168, 168),
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.Black,
                Visible = false
            };
            var refTip = new ToolTip { ShowAlways = true };
            try { refTip.SetToolTip(refThumb, "Flux reference image (what the 3D is being built from)."); } catch { }
            viewport.Controls.Add(refThumb);
            viewport.SizeChanged += Viewport_SizeChanged;
            right.Controls.Add(viewport);
            viewport.BringToFront();

            quadPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Visible = false
            };
            quadPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            quadPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            quadPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            quadPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            string[] quadNames = new string[] { "Top", "Front", "Side", "Persp" };
            quadViews = new PictureBox[4];
            for (int qi = 0; qi < 4; qi++)
            {
                var cell = new Panel { Dock = DockStyle.Fill, Padding = new Padding(2) };
                var lab = new Label
                {
                    Dock = DockStyle.Top, Height = 16, Text = quadNames[qi],
                    TextAlign = ContentAlignment.MiddleLeft
                };
                var box = new PictureBox
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.FromArgb(18, 18, 24),
                    SizeMode = PictureBoxSizeMode.CenterImage,
                    BorderStyle = BorderStyle.FixedSingle
                };
                box.DoubleClick += delegate { SetQuad(false); };
                box.MouseDown += QuadBox_MouseDown;
                box.MouseMove += QuadBox_MouseMove;
                box.MouseUp += QuadBox_MouseUp;
                cell.Controls.Add(box);
                cell.Controls.Add(lab);
                quadPanel.Controls.Add(cell, qi % 2, qi / 2);
                quadViews[qi] = box;
            }
            quadPanel.Resize += delegate { QueueRender(); };
            right.Controls.Add(quadPanel);

            // Backstop: if the window ever is narrower than the ~790px of
            // absolute-positioned controls below, scroll instead of clip.
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 124, AutoScroll = true };
            right.Controls.Add(bottom);

            int by = 6;
            bottom.Controls.Add(MakeLabel("Pitch (UO ~26.57):", 6, by + 3, 120, 16));
            pitchNumeric = new NumericUpDown
            {
                Location = new Point(126, by), Width = 70, Height = 20,
                Minimum = 5, Maximum = 65, DecimalPlaces = 2, Increment = 0.5M, Value = 26.57M
            };
            pitchNumeric.ValueChanged += ViewParam_Changed;
            bottom.Controls.Add(pitchNumeric);

            bottom.Controls.Add(MakeLabel("Zoom:", 210, by + 3, 44, 16));
            zoomTrackBar = new TrackBar { Location = new Point(250, by - 2), Width = 105, Height = 32, Minimum = 40, Maximum = 300, Value = 100, TickFrequency = 20 };
            zoomTrackBar.ValueChanged += ViewParam_Changed;
            bottom.Controls.Add(zoomTrackBar);

            bottom.Controls.Add(MakeLabel("Dot size:", 367, by + 3, 56, 16));
            dotSizeTextBox = new TextBox { Location = new Point(423, by), Width = 64, Height = 20, Text = "1.00" };
            dotSizeTextBox.TextChanged += ViewParam_Changed;
            bottom.Controls.Add(dotSizeTextBox);
            var dotTip = new ToolTip { ShowAlways = true };
            try { dotTip.SetToolTip(dotSizeTextBox, "Splat size multiplier (0.05 - 8). Lower = tighter dots, higher = softer fill."); } catch { }

            bgColorButton = new Button { Location = new Point(534, by - 2), Width = 60, Height = 26, Text = "BG..." };
            bgColorButton.Click += BgColorButton_Click;
            bottom.Controls.Add(bgColorButton);
            transparentCheckBox = new CheckBox { Location = new Point(600, by + 1), Width = 96, Height = 20, Text = "Transparent", Checked = true };
            transparentCheckBox.CheckedChanged += ViewParam_Changed;
            bottom.Controls.Add(transparentCheckBox);
            cropCheckBox = new CheckBox { Location = new Point(700, by + 1), Width = 56, Height = 20, Text = "Crop", Checked = true };
            bottom.Controls.Add(cropCheckBox);
            by += 46; // extra pad: the zoom TrackBar (32px + ticks) must clear row 2

            bottom.Controls.Add(MakeLabel("Export size:", 6, by + 4, 70, 16));
            resolutionComboBox = new ComboBox { Location = new Point(80, by), Width = 90, Height = 21, DropDownStyle = ComboBoxStyle.DropDownList };
            resolutionComboBox.Items.AddRange(new object[] { "256", "512", "1024", "2048" });
            resolutionComboBox.SelectedIndex = 1;
            bottom.Controls.Add(resolutionComboBox);

            exportCurrentButton = new Button { Location = new Point(182, by - 2), Width = 140, Height = 26, Text = "Export current view" };
            exportCurrentButton.Click += ExportCurrentButton_Click;
            bottom.Controls.Add(exportCurrentButton);
            exportAllButton = new Button { Location = new Point(328, by - 2), Width = 140, Height = 26, Text = "Export all 8 dirs" };
            exportAllButton.Click += ExportAllButton_Click;
            bottom.Controls.Add(exportAllButton);
            sendToCanvasButton = new Button { Location = new Point(474, by - 2), Width = 150, Height = 26, Text = "Send view to canvas" };
            sendToCanvasButton.Click += SendToCanvasButton_Click;
            bottom.Controls.Add(sendToCanvasButton);
            useAsInputButton = new Button { Location = new Point(630, by - 2), Width = 160, Height = 26, Text = "Use view as input" };
            useAsInputButton.Click += UseAsInputButton_Click;
            bottom.Controls.Add(useAsInputButton);
            var inputTip = new ToolTip { ShowAlways = true };
            try { inputTip.SetToolTip(useAsInputButton, "Render the current view and stage it as the init image for the next Generate (iterative refine loop)."); } catch { }
            by += 40;

            bottom.Controls.Add(MakeLabel("Drag to orbit. Middle-drag slides model on grid. Ctrl+drag lassos splats to delete.", 6, by, 500, 16));
            bottom.Controls.Add(MakeLabel("Max splats:", 512, by + 3, 64, 16));
            maxSplatsTextBox = new TextBox { Location = new Point(578, by), Width = 80, Height = 20, Text = "0" };
            maxSplatsTextBox.TextChanged += ViewParam_Changed;
            bottom.Controls.Add(maxSplatsTextBox);
            bottom.Controls.Add(MakeLabel("(0 = all)", 662, by + 3, 60, 16));
            gridCheckBox = new CheckBox { Location = new Point(726, by + 1), Width = 56, Height = 20, Text = "Grid", Checked = true };
            gridCheckBox.CheckedChanged += ViewParam_Changed;
            bottom.Controls.Add(gridCheckBox);
            var capTip = new ToolTip { ShowAlways = true };
            try { capTip.SetToolTip(maxSplatsTextBox, "Cap how many splats draw in the preview (0 = all). Lower = faster. Exports always use all splats."); } catch { }
        }

        private static Label MakeLabel(string text, int x, int y, int w, int h)
        {
            return new Label { Text = text, Location = new Point(x, y), Width = w, Height = h };
        }

        private NumericUpDown MakeRotBox(int x, int y)
        {
            var n = new NumericUpDown
            {
                Location = new Point(x, y), Width = 60, Height = 20,
                Minimum = -180, Maximum = 180, Increment = 5, DecimalPlaces = 1
            };
            n.ValueChanged += Rot_Changed;
            return n;
        }

        private void Rot_Changed(object sender, EventArgs e)
        {
            // Rot/Scale dials move baked positions, so bins re-cut once the
            // dials settle (debounced); export re-slices as backup.
            QueueRender();
            ScheduleSliceRefresh();
        }

        private void RotVals(out float rx, out float ry, out float rz)
        {
            rx = rotXNumeric != null ? (float)rotXNumeric.Value : 0f;
            ry = rotYNumeric != null ? (float)rotYNumeric.Value : 0f;
            rz = rotZNumeric != null ? (float)rotZNumeric.Value : 0f;
        }

        private NumericUpDown MakeScaleBox(int x, int y)
        {
            var n = new NumericUpDown
            {
                Location = new Point(x, y), Width = 60, Height = 20,
                Minimum = 0.1M, Maximum = 5M, Increment = 0.1M, DecimalPlaces = 2, Value = 1M
            };
            n.ValueChanged += ScaleBox_Changed;
            return n;
        }

        /// <summary>
        /// Scale dials: with the lock on, editing any one box mirrors its
        /// value into the other two (uniform resize). Re-entrancy guarded.
        /// </summary>
        private void ScaleBox_Changed(object sender, EventArgs e)
        {
            try
            {
                if (!_scaleSync)
                {
                    NumericUpDown src = sender as NumericUpDown;
                    if (src != null && _scaleLockChk != null && _scaleLockChk.Checked &&
                        scXNumeric != null && scYNumeric != null && scZNumeric != null)
                    {
                        _scaleSync = true;
                        try
                        {
                            if (scXNumeric != src) scXNumeric.Value = src.Value;
                            if (scYNumeric != src) scYNumeric.Value = src.Value;
                            if (scZNumeric != src) scZNumeric.Value = src.Value;
                        }
                        finally { _scaleSync = false; }
                    }
                }
            }
            catch { try { _scaleSync = false; } catch { } }
            QueueRender();
            ScheduleSliceRefresh();
        }

        private void ScaleVals(out float sx, out float sy, out float sz)
        {
            sx = scXNumeric != null ? (float)scXNumeric.Value : 1f;
            sy = scYNumeric != null ? (float)scYNumeric.Value : 1f;
            sz = scZNumeric != null ? (float)scZNumeric.Value : 1f;
            if (sx <= 0) sx = 1f;
            if (sy <= 0) sy = 1f;
            if (sz <= 0) sz = 1f;
        }

        #endregion

        #region Generation pipeline

        private void Log(string msg)
        {
            try
            {
                if (InvokeRequired) { BeginInvoke(new Action<string>(Log), msg); return; }
                logTextBox.AppendText(string.Format("[{0:HH:mm:ss}] {1}\r\n", DateTime.Now, msg));
            }
            catch { }
        }

        private void SetStatus(string msg, Color color)
        {
            try
            {
                if (InvokeRequired) { BeginInvoke(new Action<string, Color>(SetStatus), msg, color); return; }
                statusLabel.Text = msg;
                statusLabel.ForeColor = color;
            }
            catch { }
        }

        private void TestBtn_Click(object sender, EventArgs e)
        {
            try
            {
                _comfy = new ComfyUIClient(comfyUrlTextBox.Text.Trim());
                SetStatus("Testing connection...", Color.Gray);
                var t = _comfy.TestConnection();
                t.ContinueWith(task =>
                {
                    if (task.IsFaulted || !task.Result)
                        SetStatus("ComfyUI not reachable.", Color.Red);
                    else
                        SetStatus("Connected to ComfyUI.", Color.Green);
                });
            }
            catch (Exception ex) { SetStatus("Error: " + ex.Message, Color.Red); }
        }

        private void BrowseWf_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "ComfyUI workflow (*.json)|*.json";
                dlg.FileName = workflowPathTextBox.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    workflowPathTextBox.Text = dlg.FileName;
            }
        }

        /// <summary>
        /// Ctrl+V into the init box: image bitmaps (screenshots, browser
        /// copies, Painter) and copied image files both land as the init
        /// image via a temp file. Anything else falls through silently.
        /// </summary>
        private void InitImageBox_KeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (!e.Control || e.KeyCode != Keys.V) return;
                string path = null;
                try
                {
                    if (Clipboard.ContainsImage())
                    {
                        using (var img = Clipboard.GetImage())
                        {
                            if (img != null)
                            {
                                path = Path.Combine(Path.GetTempPath(), "mm_paste_init.png");
                                img.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                            }
                        }
                    }
                    else if (Clipboard.ContainsFileDropList())
                    {
                        var files = Clipboard.GetFileDropList();
                        foreach (string f in files)
                        {
                            string ext = Path.GetExtension(f).ToLowerInvariant();
                            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".webp" || ext == ".bmp")
                            {
                                if (File.Exists(f)) { path = f; break; }
                            }
                        }
                    }
                }
                catch { path = null; }
                if (string.IsNullOrEmpty(path)) return; // not an image: ignore
                e.SuppressKeyPress = true;
                e.Handled = true;
                initImageTextBox.Text = path;
                _initImageServerName = null;
                RefreshInitPreview();
                Log("Init image pasted from clipboard: " + path);
            }
            catch { }
        }

        private void BrowseImg_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "Images (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp";
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    initImageTextBox.Text = dlg.FileName;
                    _initImageServerName = null;
                    RefreshInitPreview();
                }
            }
        }

        private void OpenPlyButton_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "3D models (*.ply;*.obj;*.fbx;*.glb;*.gltf;*.stl;*.dae;*.blend;*.3ds;*.x;*.x3d;*.stp;*.step;*.ifc)|*.ply;*.obj;*.fbx;*.glb;*.gltf;*.stl;*.dae;*.blend;*.3ds;*.x;*.x3d;*.stp;*.step;*.ifc"
                    + "|Point cloud (*.ply)|*.ply"
                    + "|Wavefront (*.obj)|*.obj"
                    + "|Autodesk FBX (*.fbx)|*.fbx"
                    + "|glTF (*.glb;*.gltf)|*.glb;*.gltf"
                    + "|All files (*.*)|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    LoadModel(dlg.FileName);
                    Log("Loaded local 3D model: " + dlg.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Failed to load 3D model:\r\n" + ex.Message, "3D Editor",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (loopCheckBox != null && loopCheckBox.Checked)
                {
                    loopCheckBox.Checked = false;
                    Log("Loop stopped by user.");
                }
                if (_cts != null) _cts.Cancel();
                if (_comfy != null)
                {
                    var c = _comfy;
                    Task.Run(() => { try { c.Interrupt(); } catch { } });
                }
                SetStatus("Cancelling...", Color.Orange);
            }
            catch { }
        }

        private void GenerateButton_Click(object sender, EventArgs e)
        {
            if (_cts != null) return; // already running
            // A chained call (2D half just staged its result) goes straight
            // to the 3D — it must NOT re-enter the 2D routing below.
            bool chained = _chain3D;
            _chain3D = false;
            _chainNeedsLoop = false;
            string prompt = promptTextBox.Text.Trim();
            if (prompt.Length == 0) { MessageBox.Show(this, "Enter a prompt first.", "3D Editor"); return; }
            if (!File.Exists(workflowPathTextBox.Text))
            {
                MessageBox.Show(this, "Workflow file not found:\r\n" + workflowPathTextBox.Text, "3D Editor");
                return;
            }
            long seed;
            if (!long.TryParse(seedTextBox.Text.Trim(), out seed)) seed = -1;
            int steps = stepsNumeric != null ? Math.Max(1, (int)stepsNumeric.Value) : 20;
            int fluxSteps = fluxStepsNumeric != null ? Math.Max(1, Math.Min(12, (int)fluxStepsNumeric.Value)) : 4;

            string initLocal = initImageTextBox.Text.Trim();
            bool haveInit = !string.IsNullOrEmpty(initLocal) && File.Exists(initLocal);
            bool loopFresh = false;
            try { loopFresh = loopCheckBox != null && loopCheckBox.Checked && loopNewImageCheckBox != null && loopNewImageCheckBox.Checked; }
            catch { }
            // No usable init (or a fresh-image loop iteration): run the 2D
            // half first and chain into the 3D. A 3D straight from black
            // only dies in Tripo with "mask is empty", so never allow it.
            if (!chained && (!haveInit || loopFresh))
            {
                _chain3D = true;
                try { _chainNeedsLoop = loopCheckBox != null && loopCheckBox.Checked; }
                catch { _chainNeedsLoop = false; }
                if (!haveInit)
                    Log("No init image: generating a fresh 2D first, then 3D...");
                else
                {
                    initImageTextBox.Text = string.Empty;
                    _initImageServerName = null;
                    RefreshInitPreview();
                    Log("Loop with new image: init cleared, generating fresh 2D...");
                }
                GenInitButton_Click(genInitButton, EventArgs.Empty);
                return;
            }

            try { AppConfig.Instance.ComfyUIUrl = comfyUrlTextBox.Text.Trim(); } catch { }
            _comfy = new ComfyUIClient(comfyUrlTextBox.Text.Trim());
            _cts = new CancellationTokenSource();
            generateButton.Enabled = false;
            cancelButton.Enabled = true;
            progressBar.Style = ProgressBarStyle.Marquee;
            progressBar.Visible = true;
            HideRef();

            Task.Run(() => RunPipeline(prompt, seed, initLocal, steps, fluxSteps, _cts.Token));
        }

        /// <summary>
        /// Resolve the init image to a server-side filename: the user's file
        /// when set, else an uploaded neutral black square (never the
        /// template's stale PNG). Shared by the 3D and 2D-only flows.
        /// </summary>
        private async Task<string> UploadInitAsync(string initLocal, CancellationToken token)
        {
            SetStatus("Uploading init image...", HolographicTheme.BlueAccent);
            string initServer;
            if (!string.IsNullOrEmpty(initLocal) && File.Exists(initLocal))
            {
                byte[] bytes = File.ReadAllBytes(initLocal);
                string name = Path.GetFileName(initLocal);
                initServer = await _comfy.UploadImage(bytes, name);
                Log("Init image uploaded as: " + initServer + ".");
            }
            else
            {
                using (var black = new Bitmap(1024, 1024, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
                {
                    using (var g = Graphics.FromImage(black))
                        g.Clear(Color.Black);
                    byte[] bytes = BitmapToPngBytes(black);
                    initServer = await _comfy.UploadImage(bytes, "mm_neutral_black.png");
                }
                Log("No init image: using neutral black (mm_neutral_black.png).");
            }
            token.ThrowIfCancellationRequested();
            return initServer;
        }

        /// <summary>
        /// True when the image file is (near-)pure black. Tripo's
        /// remove-background finds no foreground in those, and the run dies
        /// with "mask is empty" — so callers reject them with a clear
        /// message instead of queueing a doomed run. Unreadable files
        /// return false (let the server decide).
        /// </summary>
        private static bool IsBlankImage(string path, out double mean)
        {
            mean = 0;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var bmp = new Bitmap(fs))
                {
                    if (bmp.Width < 1 || bmp.Height < 1) return true;
                    int stepX = Math.Max(1, bmp.Width / 64);
                    int stepY = Math.Max(1, bmp.Height / 64);
                    long sum = 0;
                    long n = 0;
                    for (int y = 0; y < bmp.Height; y += stepY)
                        for (int x = 0; x < bmp.Width; x += stepX)
                        {
                            Color c = bmp.GetPixel(x, y);
                            sum += (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
                            n++;
                        }
                    if (n == 0) return true;
                    mean = (double)sum / n;
                    return mean < 2.5;
                }
            }
            catch { return false; }
        }

        /// <summary>Sidebar thumbnail of whatever the next run starts from.</summary>
        private void RefreshInitPreview()
        {
            try
            {
                if (InvokeRequired) { BeginInvoke(new Action(RefreshInitPreview)); return; }
                if (initPreviewBox == null) return;
                Bitmap bmp = null;
                try
                {
                    string path = initImageTextBox != null ? initImageTextBox.Text.Trim() : string.Empty;
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var tmp = new Bitmap(fs))
                            bmp = new Bitmap(tmp);
                    }
                }
                catch { if (bmp != null) bmp.Dispose(); bmp = null; }
                if (bmp == null)
                    bmp = new Bitmap(64, 64); // stays black = neutral
                var old = initPreviewBox.Image as Bitmap;
                initPreviewBox.Image = bmp;
                if (old != null) old.Dispose();
            }
            catch { }
        }

        private void GenInitButton_Click(object sender, EventArgs e)
        {
            // A chained loop run that never starts must not leak the flag
            // into some later manual 2D run (which would then chain a 3D).
            if (_cts != null) { _chain3D = false; _chainNeedsLoop = false; return; } // already running
            string prompt = promptTextBox.Text.Trim();
            if (prompt.Length == 0) { _chain3D = false; _chainNeedsLoop = false; MessageBox.Show(this, "Enter a prompt first.", "3D Editor"); return; }
            if (!File.Exists(workflowPathTextBox.Text))
            {
                _chain3D = false;
                _chainNeedsLoop = false;
                MessageBox.Show(this, "Workflow file not found:\r\n" + workflowPathTextBox.Text, "3D Editor");
                return;
            }
            long seed;
            if (!long.TryParse(seedTextBox.Text.Trim(), out seed)) seed = -1;
            int fluxSteps = fluxStepsNumeric != null ? Math.Max(1, Math.Min(12, (int)fluxStepsNumeric.Value)) : 4;

            try { AppConfig.Instance.ComfyUIUrl = comfyUrlTextBox.Text.Trim(); } catch { }
            _comfy = new ComfyUIClient(comfyUrlTextBox.Text.Trim());
            _cts = new CancellationTokenSource();
            generateButton.Enabled = false;
            if (genInitButton != null) genInitButton.Enabled = false;
            cancelButton.Enabled = true;
            progressBar.Style = ProgressBarStyle.Marquee;
            progressBar.Visible = true;

            string initLocal = initImageTextBox.Text.Trim();
            Task.Run(() => RunFluxOnly(prompt, seed, initLocal, fluxSteps, _cts.Token));
        }

        /// <summary>
        /// 2D-only run: the same Flux image stage (prompt, seed, init) the
        /// 3D run opens with, saved as an image. The result is staged right
        /// back as the init image, so this is the approve-the-2D step.
        /// </summary>
        private async Task RunFluxOnly(string prompt, long seed, string initLocal, int fluxSteps, CancellationToken token)
        {
            try
            {
                string initServer = await UploadInitAsync(initLocal, token);

                SetStatus("Building flux-only workflow...", HolographicTheme.BlueAccent);
                MultisMaker1Workflow.BuildResult built;
                try
                {
                    built = MultisMaker1Workflow.Build(workflowPathTextBox.Text, prompt, seed, initServer, 20, fluxSteps, true);
                    Log("Flux-only graph (" + built.FormatNote + "), seed=" + built.Seed + ".");
                }
                catch
                {
                    Log("Workflow file: " + workflowPathTextBox.Text);
                    throw;
                }

                SetStatus("Queueing on ComfyUI...", HolographicTheme.BlueAccent);
                string promptId = await _comfy.QueuePrompt(built.PromptJson, Guid.NewGuid().ToString());
                if (string.IsNullOrEmpty(promptId)) throw new Exception("Server did not return a prompt_id.");
                Log("Queued 2D, prompt_id=" + promptId + ". (Same image the 3D run would start from.)");

                string[] imgExts = new string[] { ".png", ".jpg", ".jpeg", ".webp" };
                ComfyUIClient.OutputImage found = null;
                var start = DateTime.Now;
                int unseenStreak = 0;
                while (!token.IsCancellationRequested)
                {
                    var queue = await _comfy.GetQueue();
                    string hist = await _comfy.GetHistoryJson(promptId);
                    bool hasEntry = hist.Contains(promptId);

                    var files = ComfyUIClient.ScanHistoryForFiles(hist, imgExts);
                    if (files.Count > 0) { found = files[0]; break; }

                    if (hasEntry && (hist.Contains("\"completed\":false") || hist.Contains("\"completed\": false")))
                        throw new Exception("ComfyUI execution failed. See server log for node errors.");

                    bool inQueue = queue.Running.Contains(promptId) || queue.Pending.Contains(promptId);
                    var elapsed = DateTime.Now - start;
                    if (hasEntry || inQueue)
                    {
                        unseenStreak = 0;
                        SetStatus(string.Format("Rendering 2D... {0:mm\\:ss} elapsed", elapsed), HolographicTheme.BlueAccent);
                    }
                    else
                    {
                        unseenStreak++;
                        SetStatus(string.Format("Waiting for server... {0:mm\\:ss}", elapsed), HolographicTheme.BlueAccent);
                        if (elapsed.TotalSeconds > 20 && unseenStreak >= 3)
                            throw new Exception("Prompt vanished from the server queue and history (likely restarted).");
                    }
                    if (elapsed.TotalMinutes > 10) throw new Exception("Timed out waiting for the 2D image.");
                    await Task.Delay(2000, token);
                }
                token.ThrowIfCancellationRequested();
                if (found == null) { SetStatus("Cancelled.", Color.Orange); return; }

                SetStatus("Downloading 2D image...", HolographicTheme.BlueAccent);
                byte[] bytes = await _comfy.DownloadFile(found);
                if (bytes == null || bytes.Length == 0) throw new Exception("Downloaded image is empty.");
                string path = Path.Combine(Path.GetTempPath(), "mm_flux_init.png");
                File.WriteAllBytes(path, bytes);
                double mean;
                if (IsBlankImage(path, out mean))
                    throw new Exception("Flux returned a blank (black) image (mean brightness " +
                        mean.ToString("0.0") + "). Not staging it — Tripo would fail with 'mask is empty'. Try again or adjust the prompt.");
                SafeInvoke(() =>
                {
                    initImageTextBox.Text = path;
                    RefreshInitPreview();
                });
                Log("Init image ready: " + path + " (staged for the next Generate).");
                // Chain into Generate 3D (flags stay set; the chained call
                // consumes them). A loop-gated chain dies here if Loop was
                // unchecked mid-2D; a manual 2D-first kickoff always chains.
                bool chain = _chain3D;
                if (chain && _chainNeedsLoop)
                {
                    bool loop = false;
                    try { loop = loopCheckBox != null && loopCheckBox.Checked; }
                    catch { }
                    if (!loop)
                    {
                        chain = false;
                        _chain3D = false;
                        _chainNeedsLoop = false;
                    }
                }
                if (chain)
                {
                    Log("Fresh 2D staged, starting 3D...");
                    SetStatus("Done.", Color.Green);
                    SafeInvoke(() => GenerateButton_Click(generateButton, EventArgs.Empty));
                }
                else SetStatus("Done.", Color.Green);
            }
            catch (OperationCanceledException) { _chain3D = false; _chainNeedsLoop = false; SetStatus("Cancelled.", Color.Orange); Log("Cancelled by user."); }
            catch (Exception ex) { _chain3D = false; _chainNeedsLoop = false; SetStatus("Error: " + ex.Message, Color.Red); Log("ERROR: " + ex.Message); }
            finally
            {
                // NOTE: on a chained 3D the buttons flicker back on here and
                // GenerateButton_Click (already queued via BeginInvoke)
                // re-disables them with its own run state. Harmless.
                SafeInvoke(() =>
                {
                    generateButton.Enabled = true;
                    if (genInitButton != null) genInitButton.Enabled = true;
                    cancelButton.Enabled = false;
                    progressBar.Style = ProgressBarStyle.Blocks;
                    progressBar.Visible = false;
                });
                var cts = _cts;
                _cts = null;
                if (cts != null) cts.Dispose();
            }
        }

        private async Task RunPipeline(string prompt, long seed, string initLocal, int steps, int fluxSteps, CancellationToken token)
        {
            bool succeeded = false;
            bool cancelled = false;
            try
            {
                // Belt and suspenders (the button normally routes empty init
                // via the 2D half first): never queue a 3D the server is
                // guaranteed to fail with "mask is empty".
                if (string.IsNullOrEmpty(initLocal) || !File.Exists(initLocal))
                    throw new Exception("No init image staged. Generate a 2D image first — Generate 3D needs one to start from.");
                double initMean;
                if (IsBlankImage(initLocal, out initMean))
                    throw new Exception("Init image '" + Path.GetFileName(initLocal) + "' is blank (mean brightness " +
                        initMean.ToString("0.0") + "). Tripo needs foreground pixels — generate or pick a real 2D first.");
                string initServer = await UploadInitAsync(initLocal, token);

                SetStatus("Building workflow...", HolographicTheme.BlueAccent);
                MultisMaker1Workflow.BuildResult built;
                try
                {
                    // No-flux 3D: the staged init image feeds Tripo directly.
                    // The 2D stage lives on "Generate Init Image" now.
                    built = MultisMaker1Workflow.Build(workflowPathTextBox.Text, prompt, seed, initServer, steps, fluxSteps, false, true);
                    Log("KSampler steps=" + steps + " (Flux stage skipped; init feeds Tripo directly).");
                }
                catch
                {
                    Log("Workflow file: " + workflowPathTextBox.Text);
                    throw;
                }
                Log("Workflow " + built.FormatNote + ": prompt JSON " + built.PromptJson.Length + " chars, seed=" + built.Seed + ".");
                // Never overwrite the seed box: a "-1" there must stay "-1"
                // so the next run randomizes again. The used seed is in the
                // log above for anyone wanting to reproduce this run.

                SetStatus("Queueing on ComfyUI...", HolographicTheme.BlueAccent);
                // Live preview socket must be connected BEFORE queueing, with
                // the same client id, or the server sends "executed"
                // thumbnails nowhere. Best-effort: failures just mean no
                // live corner image (history fallback still applies).
                string clientId = Guid.NewGuid().ToString();
                ThreeD.ComfyPreviewSocket previewSocket = null;
                string[] runIdBox = new string[1]; // set once queue returns
                try
                {
                    previewSocket = new ThreeD.ComfyPreviewSocket();
                    previewSocket.PreviewReady += delegate (ThreeD.ComfyPreviewSocket.PreviewEvent ev)
                    {
                        try
                        {
                            string wantId = runIdBox[0];
                            if (ev == null || string.IsNullOrEmpty(wantId) ||
                                !string.Equals(ev.PromptId, wantId, StringComparison.Ordinal)) return;
                            if (string.IsNullOrEmpty(ev.Filename)) return;
                            if (string.Equals(ev.Filename, _shownRef, StringComparison.OrdinalIgnoreCase)) return;
                            _shownRef = ev.Filename;
                            var req = new ComfyUIClient.OutputImage
                            {
                                Filename = ev.Filename,
                                Subfolder = ev.Subfolder ?? string.Empty,
                                Type = string.IsNullOrEmpty(ev.Type) ? "output" : ev.Type
                            };
                            Task.Run(async () =>
                            {
                                try
                                {
                                    byte[] ibytes = await _comfy.DownloadFile(req);
                                    if (ibytes == null || ibytes.Length == 0) return;
                                    using (var ms = new MemoryStream(ibytes))
                                    {
                                        Bitmap refBmp = new Bitmap(ms);
                                        SafeInvoke(() => ShowRefImage(refBmp));
                                    }
                                }
                                catch { }
                            });
                        }
                        catch { }
                    };
                    await previewSocket.ConnectAsync(comfyUrlTextBox.Text.Trim(), clientId, token);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Preview socket unavailable: " + ex.Message);
                    try { if (previewSocket != null) previewSocket.Dispose(); } catch { }
                    previewSocket = null;
                }
                string promptId = await _comfy.QueuePrompt(built.PromptJson, clientId);
                runIdBox[0] = promptId;
                // Socket lives until the run ends; closed in the outer finally.
                _activePreviewSocket = previewSocket;
                previewSocket = null;
                if (string.IsNullOrEmpty(promptId)) throw new Exception("Server did not return a prompt_id.");
                Log("Queued, prompt_id=" + promptId + ". Waiting for .ply (this takes minutes)...");

                var start = DateTime.Now;
                string foundName = null;
                ComfyUIClient.OutputImage found = null;
                int unseenStreak = 0; // polls where id is in neither queue nor history
                string lastState = string.Empty;
                while (!token.IsCancellationRequested)
                {
                    var queue = await _comfy.GetQueue();
                    string hist = await _comfy.GetHistoryJson(promptId);
                    bool hasEntry = hist.Contains(promptId);

                    var files = ComfyUIClient.ScanHistoryForFiles(hist, new string[] { ".ply" });
                    if (files.Count > 0) { found = files[0]; break; }

                    // Show the Flux reference image (preview nodes) live in
                    // the viewport corner while the 3D bakes.
                    try
                    {
                        var imgs = ComfyUIClient.ScanHistoryForFiles(hist,
                            new string[] { ".png", ".jpg", ".jpeg", ".webp" });
                        if (imgs.Count > 0)
                        {
                            var latest = imgs[imgs.Count - 1];
                            if (!string.Equals(latest.Filename, _shownRef, StringComparison.OrdinalIgnoreCase))
                            {
                                _shownRef = latest.Filename;
                                byte[] ibytes = await _comfy.DownloadFile(latest);
                                if (ibytes != null && ibytes.Length > 0)
                                {
                                    using (var ms = new MemoryStream(ibytes))
                                    {
                                        Bitmap refBmp = new Bitmap(ms);
                                        SafeInvoke(() => ShowRefImage(refBmp));
                                    }
                                }
                            }
                        }
                    }
                    catch { }

                    // Failed server-side? History entry exists but completed=false.
                    if (hasEntry && (hist.Contains("\"completed\":false") || hist.Contains("\"completed\": false")))
                    {
                        string why = "execution failed";
                        int ei = hist.IndexOf("exception_message");
                        if (ei >= 0) why = hist.Substring(ei, Math.Min(300, hist.Length - ei));
                        throw new Exception("ComfyUI " + why + ". See server log for node errors.");
                    }

                    bool inQueue = queue.Running.Contains(promptId) || queue.Pending.Contains(promptId);
                    var elapsed = DateTime.Now - start;
                    if (hasEntry || inQueue)
                    {
                        unseenStreak = 0;
                        string state;
                        if (queue.Running.Contains(promptId)) state = "Running";
                        else if (queue.Pending.Contains(promptId))
                        {
                            int pos = queue.Pending.IndexOf(promptId) + 1;
                            state = "Queued (#" + pos + " of " + queue.Pending.Count + ")";
                        }
                        else state = "Finishing";
                        if (state != lastState) { Log("Server state: " + state + "."); lastState = state; }
                        SetStatus(string.Format("{0}... {1:mm\\:ss} elapsed", state, elapsed), HolographicTheme.BlueAccent);
                    }
                    else
                    {
                        // Accepted prompts live in queue or history. In neither
                        // (after a grace period) means the server dropped it,
                        // almost always a server restart wiping both stores.
                        unseenStreak++;
                        SetStatus(string.Format("Waiting for server to pick up... {0:mm\\:ss}", elapsed), HolographicTheme.BlueAccent);
                        if (elapsed.TotalSeconds > 20 && unseenStreak >= 3)
                            throw new Exception("Prompt " + promptId + " vanished from the server queue and history. " +
                                "The ComfyUI server most likely restarted (queue + history are in-memory). " +
                                "Re-queue with Generate; do not restart the server mid-run.");
                    }

                    if (elapsed.TotalMinutes > 30) throw new Exception("Timed out waiting for .ply after 30 min.");
                    await Task.Delay(2000, token);
                }
                token.ThrowIfCancellationRequested();
                if (found == null) { SetStatus("Cancelled.", Color.Orange); return; }
                foundName = found.Filename;
                Log("Got 3D output: " + foundName + " (subfolder='" + found.Subfolder + "', type='" + found.Type + "').");

                SetStatus("Downloading .ply...", HolographicTheme.BlueAccent);
                byte[] plyBytes = await _comfy.DownloadFile(found);
                if (plyBytes == null || plyBytes.Length == 0) throw new Exception("Downloaded .ply is empty.");
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MeesaMultisMaker", "3D");
                Directory.CreateDirectory(dir);
                string safe = string.Format("multismaker_{0:yyyyMMdd_HHmmss}.ply", DateTime.Now);
                string localPath = Path.Combine(dir, safe);
                File.WriteAllBytes(localPath, plyBytes);
                Log(string.Format("Saved {0:0.0} MB to {1}", plyBytes.Length / 1048576.0, localPath));
                // Verify before handing to the UI thread: AV quarantine,
                // OneDrive on-demand sync, or a full/locked disk can
                // otherwise surface later as a bare FileNotFound ("file is
                // introuvable") with no useful context.
                bool savedOk = false;
                try
                {
                    for (int retry = 0; retry < 3 && !savedOk; retry++)
                    {
                        if (retry > 0)
                        {
                            try { await Task.Delay(250, token); }
                            catch { }
                        }
                        try { savedOk = File.Exists(localPath) && new FileInfo(localPath).Length == plyBytes.Length; }
                        catch { savedOk = false; }
                    }
                }
                catch { savedOk = false; }
                if (!savedOk)
                    throw new Exception(string.Format(
                        "Wrote {0} bytes to '{1}' but the file is not readable there. " +
                        "Check antivirus quarantine, OneDrive sync, disk space and folder permissions.",
                        plyBytes.Length, localPath));

                SafeInvoke(() =>
                {
                    try { LoadModel(localPath); HideRef(); }
                    catch (Exception ex) { Log("Load failed: " + ex.Message); SetStatus("Load failed.", Color.Red); }
                });
                SetStatus("Done.", Color.Green);
                succeeded = true;
            }
            catch (OperationCanceledException) { cancelled = true; SetStatus("Cancelled.", Color.Orange); Log("Cancelled by user."); HideRef(); }
            catch (Exception ex) { SetStatus("Error: " + ex.Message, Color.Red); Log("ERROR: " + ex.Message); HideRef(); }
            finally
            {
                SafeInvoke(() =>
                {
                    generateButton.Enabled = true;
                    cancelButton.Enabled = false;
                    progressBar.Style = ProgressBarStyle.Blocks;
                    progressBar.Visible = false;
                });
                var cts = _cts;
                _cts = null;
                if (cts != null) cts.Dispose();
                var sock = _activePreviewSocket;
                _activePreviewSocket = null;
                if (sock != null)
                {
                    try { sock.Dispose(); }
                    catch { }
                }
            }

            // Loop mode: one clean run finished -> resubmit immediately.
            // Anything else (cancel, error, unchecked, closing) ends the loop.
            // NOTE: checks `cancelled`, never token (its source is disposed).
            if (succeeded && !cancelled)
            {
                bool loop = false;
                try { loop = loopCheckBox != null && loopCheckBox.Checked; }
                catch { }
                if (loop)
                {
                    bool newPrompt = false;
                    try { newPrompt = loopRandomPromptCheckBox != null && loopRandomPromptCheckBox.Checked; }
                    catch { }
                    // Random-prompt loop: dream up next iteration's prompt
                    // first (synchronously installed, so the queued run
                    // reads it). A failed idea stops the loop.
                    if (newPrompt)
                    {
                        Log("Loop with random prompt: dreaming up the next idea...");
                        SetStatus("Dreaming up a prompt…", HolographicTheme.BlueAccent);
                        string idea = null;
                        try
                        {
                            if (_llmCts != null) { try { _llmCts.Cancel(); } catch { } }
                            _llmCts = new CancellationTokenSource();
                            idea = await GenerateRandomPromptAsync(_llmCts.Token);
                        }
                        catch (OperationCanceledException) { idea = null; }
                        catch (Exception ex)
                        {
                            Log("Loop random prompt failed: " + ex.Message);
                            idea = null;
                        }
                        try { loop = loopCheckBox != null && loopCheckBox.Checked; }
                        catch { loop = false; }
                        if (!loop) return;
                        if (string.IsNullOrEmpty(idea))
                        {
                            Log("Loop with random prompt gave nothing usable; loop stopped.");
                            SetStatus("LLM gave nothing usable.", Color.Orange);
                            return;
                        }
                        try
                        {
                            Invoke(new Action(() =>
                            {
                                promptTextBox.Text = idea;
                                promptTextBox.SelectionStart = promptTextBox.Text.Length;
                                promptTextBox.SelectionLength = 0;
                            }));
                        }
                        catch { return; }
                        Log("Loop random prompt: " + idea);
                    }
                    // Resubmit through Generate: it routes fresh-image loop
                    // iterations (and empty-init kickoffs) via the 2D half
                    // first, everything else straight to 3D. No pause:
                    // ComfyUI queues the next run the moment this one
                    // releases the GPU.
                    Log("Loop: starting next run (uncheck Loop or Cancel to stop)...");
                    if (loop)
                        SafeInvoke(() => GenerateButton_Click(generateButton, EventArgs.Empty));
                }
            }
        }

        #endregion

        #region Model + viewport

        private void LoadModel(string path)
        {
            string ext = "";
            try { ext = Path.GetExtension(path).ToLowerInvariant(); }
            catch { }
            var model = ext == ".ply"
                ? PlySplatModel.Load(path)
                : MeshSplatModel.Load(path);
            if (_model != null && _model != model) { /* structs only; nothing to dispose */ }
            _model = model;
            _plyPath = path;
            _lassoPts.Clear();
            _lasso = false;
            DiscardCage();
            ResetSelection();
            ClearSlices(null);
            _isolateCache = null;
            _isolateFor = -1;
            plyInfoLabel.Text = string.Format("{0} splats - {1}", model.Count, Path.GetFileName(path));
            plyInfoLabel.ForeColor = Color.Green;
            Log(string.Format("Model: {0} splats, bounds X[{1:0.00},{2:0.00}] Y[{3:0.00},{4:0.00}] Z[{5:0.00},{6:0.00}]",
                model.Count, model.MinX, model.MaxX, model.MinY, model.MaxY, model.MinZ, model.MaxZ));
            if (ext != ".ply")
                Log("Mesh sampled: " + MeshSplatModel.LastTextureReport);
            QueueRender();
        }

        private void SafeInvoke(Action a)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(a);
            }
            catch { }
        }

        private void QueueRender()
        {
            if (_renderQueued || _model == null) return;
            _renderQueued = true;
            SafeInvoke(() =>
            {
                _renderQueued = false;
                try { RenderView(); }
                catch (Exception ex) { Log("Render failed: " + ex.Message); }
            });
        }

        private void SetQuad(bool on)
        {
            _quad = on;
            try
            {
                if (quadPanel != null) quadPanel.Visible = on;
                if (viewport != null) viewport.Visible = !on;
                if (quadBtn != null)
                {
                    if (on)
                    {
                        quadBtn.BackColor = HolographicTheme.ButtonAccent;
                        quadBtn.ForeColor = Color.White;
                        quadBtn.FlatStyle = FlatStyle.Flat;
                    }
                    else
                    {
                        HolographicTheme.ApplyToButton(quadBtn, ButtonStyle.Default);
                    }
                }
            }
            catch { }
            viewInfoLabel.Text = on ? "Quad: Top / Front / Side / Persp (double-click returns)" : viewInfoLabel.Text;
            QueueRender();
        }

        /// <summary>
        /// Traditional 4-panel inspection layout. Every quadrant renders
        /// through the exact same path as the main viewer (GPU exact splats
        /// at the user's dot size and splat cap), so what you see matches
        /// everywhere. Top/Front/Side are fixed orthographic views, Persp
        /// mirrors the current main view.
        /// </summary>
        private void RenderQuad()
        {
            if (_model == null || quadViews == null) return;
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                float pitch = (float)pitchNumeric.Value;
                float zoom = zoomTrackBar.Value / 100f;
                float dots = DotScale();
                bool transparent = transparentCheckBox.Checked;
                int qcap = MaxSplats(); // 0 = all, same as the main viewer
                float[] yaws = new float[] { 0f, _yaw, _yaw + 90f, _yaw };
                float[] pitches = new float[] { 90f, 0f, 0f, pitch };
                bool grid = gridCheckBox != null && gridCheckBox.Checked;
                Color gridColor = grid ? HolographicTheme.GridLine : Color.Empty;
                for (int i = 0; i < 4; i++)
                {
                    try
                    {
                        var box = quadViews[i];
                        if (box == null) continue;
                        int w = Math.Max(64, box.ClientSize.Width);
                        int h = Math.Max(64, box.ClientSize.Height);
                        if (w < 16 || h < 16) continue;
                        bool uo = (i == 3) && UoMode();
                        var bmp = RenderComposedModel(_model, w, h, yaws[i], pitches[i], zoom, dots,
                            transparent, qcap, true, gridColor, uo, yaws[i]);
                        if (_cage != null)
                        {
                            try { DrawCageOverlay(bmp, i); }
                            catch { }
                        }
                        var old = box.Image as Bitmap;
                        box.Image = bmp;
                        if (old != null) old.Dispose();
                    }
                    catch (Exception ex) { Log("Quad render failed: " + ex.Message); }
                }
            }
            finally { Cursor.Current = Cursors.Default; }
        }

        private void RenderView()
        {
            if (_model == null || viewport.Width <= 0 || viewport.Height <= 0) return;
            if (_quad) { RenderQuad(); return; }
            int w = Math.Max(64, viewport.Width);
            int h = Math.Max(64, viewport.Height);
            float pitch = (float)pitchNumeric.Value;
            float zoom = zoomTrackBar.Value / 100f;
            float dots = DotScale();
            bool transparent = transparentCheckBox.Checked;
            // Idle: every splat (or the user's cap), full quality.
            // Orbit-dragging: coarse stride for responsiveness, full
            // redraw lands on release.
            int cap = MaxSplats();
            if (_orbiting && (cap == 0 || cap > 30000)) cap = 30000;
            bool grid = gridCheckBox != null && gridCheckBox.Checked;
            bool uo = UoMode();
            // In UO mode the grid stays canvas-aligned (yaw 0) under the
            // rotated model, exactly like art sitting on the multi canvas.
            float gridYaw = uo ? 0f : _yaw;
            var bmp = RenderComposed(w, h, _yaw, pitch, zoom, dots, transparent, cap, !_orbiting, grid ? HolographicTheme.GridLine : Color.Empty, uo, gridYaw);
            var old = _viewBitmap;
            _viewBitmap = bmp;
            viewport.Image = _viewBitmap;
            if (old != null) old.Dispose();
            if (uo)
                viewInfoLabel.Text = string.Format("Dir {0} ({1:0} deg) UO",
                    SplatRenderer.DirectionNames[_direction], NormYaw(_yaw));
            else
                viewInfoLabel.Text = string.Format("Dir {0} ({1:0} deg)  pitch {2:0.0}",
                    SplatRenderer.DirectionNames[_direction], NormYaw(_yaw), pitch);
        }

        private float BaseRot()
        {
            return baseRotNumeric != null ? (float)baseRotNumeric.Value : 225f;
        }

        private bool UoMode()
        {
            return uoCheckBox != null && uoCheckBox.Checked;
        }

        private static float NormYaw(float yaw)
        {
            yaw %= 360f;
            if (yaw < 0) yaw += 360f;
            return yaw;
        }

        /// <summary>Total model yaw showing UO facing f, given the per-model
        /// base rotation (default 225: TripoSplat fronts face S there).</summary>
        private float FacingYaw(int f)
        {
            return NormYaw(BaseRot() - (f - 4) * 45f);
        }

        private int YawToFacing(float yaw)
        {
            int s = (int)Math.Round((BaseRot() - yaw) / 45.0);
            return ((((s + 4) % 8) + 8) % 8);
        }

        private void BaseRot_Changed(object sender, EventArgs e)
        {
            // Keep the current facing button; rotate the model under it.
            _yaw = FacingYaw(_direction);
            QueueRender();
            ScheduleSliceRefresh();
        }

        private void UoCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            pitchNumeric.Enabled = !UoMode();
            QueueRender();
        }

        private void DirButton_Click(object sender, EventArgs e)
        {
            var b = sender as Button;
            if (b == null) return;
            _direction = (int)b.Tag;
            _yaw = FacingYaw(_direction);
            UpdateViewButtons();
            QueueRender();
            ScheduleSliceRefresh();
        }

        /// <summary>
        /// Each generation can face a different way. Orbit to the front and
        /// click: the current view becomes South, all facings recomputed.
        /// </summary>
        private void SetSBtn_Click(object sender, EventArgs e)
        {
            baseRotNumeric.Value = (decimal)NormYaw(_yaw);
            _direction = 4;
            _yaw = FacingYaw(_direction);
            UpdateViewButtons();
            QueueRender();
            ScheduleSliceRefresh();
            Log("South set to current view (offset " + ((decimal)NormYaw(_yaw)).ToString("0") + ").");
        }

        private void UpdateViewButtons()
        {
            for (int i = 0; i < 8; i++)
            {
                if (dirButtons[i] == null) continue;
                if (i == _direction)
                {
                    dirButtons[i].BackColor = HolographicTheme.ButtonAccent;
                    dirButtons[i].ForeColor = Color.White;
                    dirButtons[i].FlatStyle = FlatStyle.Flat;
                    dirButtons[i].FlatAppearance.BorderColor = HolographicTheme.CyanAccent;
                    dirButtons[i].FlatAppearance.BorderSize = 1;
                    dirButtons[i].Font = new Font(dirButtons[i].Font.FontFamily, dirButtons[i].Font.Size, FontStyle.Bold);
                }
                else
                {
                    HolographicTheme.ApplyToButton(dirButtons[i], ButtonStyle.Default);
                }
            }
        }

        private void ViewParam_Changed(object sender, EventArgs e)
        {
            QueueRender();
        }

        /// <summary>Preview splat cap typed by the user (0 = all).</summary>
        private int MaxSplats()
        {
            int v = 0;
            try
            {
                if (maxSplatsTextBox != null &&
                    !int.TryParse(maxSplatsTextBox.Text,
                        System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out v))
                    v = 0;
            }
            catch { v = 0; }
            if (v < 0) v = 0;
            return v;
        }

        /// <summary>Splat size multiplier typed by the user (0.05 - 8).</summary>
        private float DotScale()
        {
            float v = 1f;
            try
            {
                if (dotSizeTextBox != null &&
                    !float.TryParse(dotSizeTextBox.Text,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out v))
                    v = 1f;
            }
            catch { v = 1f; }
            if (v < 0.05f) v = 0.05f;
            if (v > 8f) v = 8f;
            return v;
        }

        private void Viewport_SizeChanged(object sender, EventArgs e)
        {
            QueueRender();
        }

        private void BgColorButton_Click(object sender, EventArgs e)
        {
            using (var dlg = new ColorDialog())
            {
                dlg.Color = _bgColor;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _bgColor = dlg.Color;
                    QueueRender();
                }
            }
        }

        // Drag-to-orbit (button held only).
        private bool _orbiting;
        private int _orbitX, _orbitY;
        private float _orbitYaw, _orbitPitch;

        // Ctrl+drag lasso delete. Selection/undo machinery lives in the
        // Edit partial (ThreeDEditorForm.Edit.cs).
        private bool _lasso;
        private readonly List<Point> _lassoPts = new List<Point>();

        // Middle-drag slides the model on the ground plane (selection if
        // any, else whole model). One undo step on release.
        private bool _panning;
        private Point _panLast;
        private bool _panMoved;
        private List<int> _panIdx;
        private SplatPoint[] _panBefore;
        private double _panTDX, _panTDY, _panTDZ;

        private void Viewport_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle && _model != null && _model.Count > 0)
            {
                _panning = true;
                _panMoved = false;
                _panLast = e.Location;
                _panTDX = _panTDY = _panTDZ = 0;
                try
                {
                    _panIdx = ThreeD.SplatOps.Affected(_model, UseSel());
                    _panBefore = Snapshot(_panIdx);
                }
                catch { _panIdx = null; _panBefore = null; }
                try
                {
                    viewport.Capture = true;
                    viewport.Cursor = Cursors.SizeAll;
                }
                catch { }
                return;
            }
            if (e.Button == MouseButtons.Left && _model != null)
            {
                bool ctrl = (Control.ModifierKeys & Keys.Control) == Keys.Control;
                if (ctrl)
                {
                    _lasso = true;
                    _orbiting = false;
                    _lassoPts.Clear();
                    _lassoPts.Add(e.Location);
                    viewport.Capture = true;
                    viewport.Invalidate();
                }
                else if (HandleEditMouseDown(e))
                {
                    // Edit tool consumed the gesture (partial class).
                }
                else
                {
                    _orbiting = true;
                    _orbitX = e.X;
                    _orbitY = e.Y;
                    _orbitYaw = _yaw;
                    _orbitPitch = (float)pitchNumeric.Value;
                    viewport.Capture = true;
                }
            }
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (!e.Button.HasFlag(MouseButtons.Left)) { _orbiting = false; }
            if (_panning)
            {
                if (_model == null) return;
                int dx = e.X - _panLast.X, dy = e.Y - _panLast.Y;
                if (dx != 0 || dy != 0)
                {
                    _panLast = e.Location;
                    try { PanModelByPixels(dx, dy); }
                    catch { }
                }
                return;
            }
            if (_lasso)
            {
                if (_model == null) return;
                Point last = _lassoPts[_lassoPts.Count - 1];
                int dx = e.X - last.X, dy = e.Y - last.Y;
                if (dx * dx + dy * dy >= 9)
                {
                    _lassoPts.Add(e.Location);
                    viewport.Invalidate(); // repaint overlay only, no re-render
                }
                return;
            }
            if (HandleEditMouseMove(e)) return;
            if (!_orbiting || _model == null) return;
            _yaw = (_orbitYaw + (e.X - _orbitX) * 0.4f) % 360f;
            if (_yaw < 0) _yaw += 360f;
            // Snap highlight to nearest facing without snapping the view.
            _direction = YawToFacing(_yaw);
            if (!UoMode())
            {
                decimal pitch = (decimal)Math.Max(5.0, Math.Min(65.0, _orbitPitch + (e.Y - _orbitY) * 0.25));
                if (pitch != pitchNumeric.Value) pitchNumeric.Value = pitch;
            }
            UpdateViewButtons();
            QueueRender();
        }

        /// <summary>
        /// Slide the model on the ground plane following a screen drag.
        /// Screen pixels convert through the current fit/zoom, then from
        /// yaw-frame ground axes back to model frame (full inverse, so Rot /
        /// Scale dials stay exact). Height is untouched: the model stays on
        /// the grid.
        /// </summary>
        private void PanModelByPixels(int dxPx, int dyPx)
        {
            if (_model == null || _model.Count == 0) return;
            int w, h;
            double fit, cx, cy;
            ViewportCam(out w, out h, out fit, out cx, out cy);
            if (!(fit > 0)) return;
            bool uo = UoMode();
            double dx1, dz1;
            if (uo)
            {
                dx1 = (dxPx + dyPx) / (2.0 * fit);
                dz1 = (dyPx - dxPx) / (2.0 * fit);
            }
            else
            {
                float pitchDeg = pitchNumeric != null ? (float)pitchNumeric.Value : 26.57f;
                double sinP = Math.Sin(pitchDeg * Math.PI / 180.0);
                dx1 = dxPx / fit;
                dz1 = Math.Abs(sinP) < 0.08 ? dyPx / fit : dyPx / (sinP * fit);
            }
            // Model-frame delta = (Ry * M)^-1 * (dx1, 0, dz1).
            float rx, ry, rz;
            RotVals(out rx, out ry, out rz);
            float scx, scy, scz;
            ScaleVals(out scx, out scy, out scz);
            double[] mpos = ThreeD.SplatRenderer.ScaledMatrix(
                ThreeD.SplatRenderer.ObjectMatrix(rx, ry, rz), scx, scy, scz);
            double yaw = _yaw * Math.PI / 180.0;
            double c = Math.Cos(yaw), s = Math.Sin(yaw);
            // C = Ry * M (row-major).
            double[] cc = new double[9];
            cc[0] = c * mpos[0] + s * mpos[6];
            cc[1] = c * mpos[1] + s * mpos[7];
            cc[2] = c * mpos[2] + s * mpos[8];
            cc[3] = mpos[3]; cc[4] = mpos[4]; cc[5] = mpos[5];
            cc[6] = -s * mpos[0] + c * mpos[6];
            cc[7] = -s * mpos[1] + c * mpos[7];
            cc[8] = -s * mpos[2] + c * mpos[8];
            double[] inv = new double[9];
            ThreeD.SplatCage.Invert3x3(cc, inv);
            double mdx = inv[0] * dx1 + inv[1] * 0 + inv[2] * dz1;
            double mdy = inv[3] * dx1 + inv[4] * 0 + inv[5] * dz1;
            double mdz = inv[6] * dx1 + inv[7] * 0 + inv[8] * dz1;
            var pts = _model.Points;
            if (_panIdx != null && _panIdx.Count > 0)
            {
                foreach (int i in _panIdx)
                {
                    if (i < 0 || i >= pts.Count) continue;
                    var p = pts[i];
                    p.X = (float)(p.X + mdx);
                    p.Y = (float)(p.Y + mdy);
                    p.Z = (float)(p.Z + mdz);
                    pts[i] = p;
                }
            }
            else
            {
                for (int i = 0; i < pts.Count; i++)
                {
                    var p = pts[i];
                    p.X = (float)(p.X + mdx);
                    p.Y = (float)(p.Y + mdy);
                    p.Z = (float)(p.Z + mdz);
                    pts[i] = p;
                }
            }
            _panTDX += mdx; _panTDY += mdy; _panTDZ += mdz;
            _panMoved = true;
            QueueRender();
        }

        private void Viewport_MouseUp(object sender, MouseEventArgs e)
        {
            if (_panning)
            {
                _panning = false;
                try { viewport.Capture = false; }
                catch { }
                try { viewport.Cursor = Cursors.Default; }
                catch { }
                try
                {
                    if (_panMoved && _panIdx != null && _panBefore != null &&
                        _panIdx.Count > 0 && _panBefore.Length == _panIdx.Count &&
                        _model != null)
                    {
                        _editUndo.Add(new EditUndo
                        {
                            IsAdd = false,
                            IsDelete = false,
                            Idx = _panIdx.ToArray(),
                            Before = _panBefore
                        });
                        TrimUndo();
                        MarkUndoAvailable();
                        Log(string.Format("Moved {0} splats by ({1:0.000},{2:0.000},{3:0.000}) ({4}).",
                            _panIdx.Count, _panTDX, _panTDY, _panTDZ, Scope()));
                    }
                }
                catch (Exception ex) { Log("Pan undo failed: " + ex.Message); }
                _panIdx = null;
                _panBefore = null;
                _panMoved = false;
                RefreshSlicesKeepActive("pan");
                QueueRender();
                return;
            }
            if (HandleEditMouseUp(e)) return;
            if (_lasso)
            {
                _lasso = false;
                try { viewport.Capture = false; }
                catch { }
                try
                {
                    if (_lassoPts.Count >= 3) LassoDelete();
                }
                catch (Exception ex) { Log("Lasso delete failed: " + ex.Message); }
                _lassoPts.Clear();
                viewport.Invalidate();
                return;
            }
            bool was = _orbiting;
            _orbiting = false;
            try { viewport.Capture = false; }
            catch { }
            if (was)
            {
                try { AutoRefreshSlices(); }
                catch { }
                QueueRender(); // repaint at full splat quality
            }
        }

        private void Viewport_PaintOverlay(object sender, PaintEventArgs e)
        {
            if (PaintEditOverlay(e)) return;
            if (!_lasso || _lassoPts.Count < 2) return;
            try
            {
                using (var pen = new Pen(Color.Yellow, 2f))
                {
                    e.Graphics.DrawLines(pen, _lassoPts.ToArray());
                    e.Graphics.DrawLine(pen, _lassoPts[_lassoPts.Count - 1], _lassoPts[0]);
                }
            }
            catch { }
        }

        private static bool PointInLasso(double px, double py, List<Point> poly)
        {
            bool inside = false;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = poly[i].X, yi = poly[i].Y;
                double xj = poly[j].X, yj = poly[j].Y;
                if (((yi > py) != (yj > py)) &&
                    (px < (xj - xi) * (py - yi) / (yj - yi) + xi))
                    inside = !inside;
            }
            return inside;
        }

        private void LassoDelete()
        {
            if (_model == null || _model.Points.Count == 0) return;
            // Identical projection to the viewport (renderer helper).
            bool uo = UoMode();
            float pitch = (float)pitchNumeric.Value;
            int w = Math.Max(64, viewport.Width), h = Math.Max(64, viewport.Height);
            double fit = Math.Min(w, h) * 0.42 * (zoomTrackBar.Value / 100f);
            double cx = w * 0.5, cy = h * 0.52;

            float rx, ry, rz;
            RotVals(out rx, out ry, out rz);
            float scx, scy, scz;
            ScaleVals(out scx, out scy, out scz);
            double[] robj = SplatRenderer.ScaledMatrix(SplatRenderer.ObjectMatrix(rx, ry, rz), scx, scy, scz);

            var pts = _model.Points;
            var idx = new List<int>();
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                double qx, qy, qz;
                SplatRenderer.TransformPoint(robj, p.X, p.Y, p.Z, out qx, out qy, out qz);
                float sx, sy;
                SplatRenderer.ProjectPoint((float)qx, (float)qy, (float)qz, _yaw, pitch, uo, fit, cx, cy, out sx, out sy);
                if (sx < -40 || sy < -40 || sx > w + 40 || sy > h + 40) continue;
                if (PointInLasso(sx, sy, _lassoPts)) idx.Add(i);
            }

            if (idx.Count == 0)
            {
                Log("Lasso: no splats inside the loop.");
                return;
            }

            PushDeleteUndo(idx);
            SplatOps.DeleteIndices(_model, idx);
            RefreshSlicesKeepActive("lasso delete");

            RefreshModelLabel();
            Log(string.Format("Lasso: removed {0} splats ({1} left). Undo available.", idx.Count, _model.Count));
            QueueRender();
        }

        private void UndoLassoButton_Click(object sender, EventArgs e)
        {
            UndoEdit();
        }

        #endregion

        #region Export

        private int ExportSize()
        {
            int s;
            if (resolutionComboBox != null && int.TryParse(Convert.ToString(resolutionComboBox.SelectedItem), out s))
                return s;
            return 512;
        }

        /// <summary>
        /// Exports always bake the true UO projection (never the orbit
        /// camera), so sprites drop onto the multi canvas correctly.
        /// </summary>
        private bool _glLogged;

        /// <summary>
        /// Exact GPU splat layer over a GDI+ background/grid base, with full
        /// GDI+ fallback when the GPU path is unavailable. The GPU layer uses
        /// SuperSplat's per-pixel falloff, so output matches the reference.
        /// </summary>
        private Bitmap RenderComposed(int w, int h, float yaw, float pitch, float zoom,
            float dots, bool transparent, int cap, bool hq, Color gridColor, bool uo, float gridYaw)
        {
            return RenderComposedModel(ViewportModel() ?? _model, w, h, yaw, pitch, zoom,
                dots, transparent, cap, hq, gridColor, uo, gridYaw);
        }

        private Bitmap RenderComposedModel(PlySplatModel model, int w, int h, float yaw, float pitch, float zoom,
            float dots, bool transparent, int cap, bool hq, Color gridColor, bool uo, float gridYaw)
        {
            float rx, ry, rz;
            // Isolated tile models are pre-baked: render with identity.
            bool baked = false;
            try { baked = model != null && model == _isolateCache; }
            catch { }
            if (baked) { rx = ry = rz = 0f; }
            else RotVals(out rx, out ry, out rz);
            float sx, sy, sz;
            if (baked) { sx = sy = sz = 1f; }
            else ScaleVals(out sx, out sy, out sz);
            Bitmap layer = null;
            if (hq)
            {
                try { layer = GlSplatRenderer.RenderLayer(model, yaw, pitch, w, h, zoom, dots, cap, uo, rx, ry, rz, sx, sy, sz); }
                catch (Exception ex)
                {
                    Log("GPU render failed, CPU fallback: " + ex.Message);
                    layer = null;
                }
                if (layer != null && !_glLogged)
                {
                    _glLogged = true;
                    Log(GlSplatRenderer.StatusNote);
                }
            }
            Bitmap bmp = SplatRenderer.Render(model, yaw, pitch, w, h, zoom, dots, transparent,
                _bgColor, cap, hq, gridColor, uo, gridYaw, layer != null, rx, ry, rz, sx, sy, sz);
            if (layer == null) return bmp;
            try
            {
                using (var g = Graphics.FromImage(bmp))
                    g.DrawImage(layer, 0, 0, w, h);
            }
            finally { layer.Dispose(); }
            return bmp;
        }

        private Bitmap RenderExport(float yawDeg)
        {
            int s = ExportSize();
            float pitch = (float)pitchNumeric.Value;
            float zoom = zoomTrackBar.Value / 100f;
            float dots = DotScale();
            bool transparent = transparentCheckBox.Checked;
            Bitmap bmp = RenderComposedModel(_model, s, s, yawDeg, pitch, zoom, dots, transparent, 0, true, Color.Empty, true, 0f);
            if (cropCheckBox.Checked && transparent)
            {
                Bitmap cropped = SplatRenderer.CropTransparent(bmp, 4);
                if (cropped != bmp) bmp.Dispose();
                bmp = cropped;
            }
            return bmp;
        }

        private void ExportCurrentButton_Click(object sender, EventArgs e)
        {
            if (_model == null) { MessageBox.Show(this, "No model loaded.", "3D Editor"); return; }
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "PNG (*.png)|*.png";
                dlg.FileName = string.Format("uo_{0}_{1}.png",
                    Path.GetFileNameWithoutExtension(_plyPath.Length > 0 ? _plyPath : "model"),
                    SplatRenderer.DirectionNames[_direction]);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    // Snap to the nearest exact facing: sprite sets must be
                    // on-facing, not mid-drag angles.
                    int snap = YawToFacing(_yaw);
                    if (snap != _direction || NormYaw(_yaw) != FacingYaw(snap))
                    {
                        _direction = snap;
                        _yaw = FacingYaw(snap);
                        UpdateViewButtons();
                        QueueRender();
                        Log("Snapped to " + SplatRenderer.DirectionNames[snap] + " for export.");
                    }
                    using (var bmp = RenderExport(_yaw))
                        bmp.Save(dlg.FileName, ImageFormat.Png);
                    Log("Exported current view to " + dlg.FileName);
                    SetStatus("Exported.", Color.Green);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Export failed:\r\n" + ex.Message, "3D Editor");
                }
            }
        }

        private void ExportAllButton_Click(object sender, EventArgs e)
        {
            if (_model == null) { MessageBox.Show(this, "No model loaded.", "3D Editor"); return; }
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Folder for the 8 UO direction sprites";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string baseName = Path.GetFileNameWithoutExtension(_plyPath.Length > 0 ? _plyPath : "model");
                    var saved = new List<string>();
                    for (int d = 0; d < 8; d++)
                    {
                        float yaw = FacingYaw(d);
                        string path = Path.Combine(dlg.SelectedPath,
                            string.Format("{0}_{1}.png", baseName, SplatRenderer.DirectionNames[d]));
                        using (var bmp = RenderComposedModel(_model, ExportSize(), ExportSize(), yaw, (float)pitchNumeric.Value,
                            zoomTrackBar.Value / 100f, DotScale(), transparentCheckBox.Checked, 0, true, Color.Empty, true, 0f))
                        {
                            Bitmap final = bmp;
                            Bitmap cropped = null;
                            if (cropCheckBox.Checked && transparentCheckBox.Checked)
                            {
                                cropped = SplatRenderer.CropTransparent(bmp, 4);
                                final = cropped;
                            }
                            final.Save(path, ImageFormat.Png);
                            if (cropped != null) cropped.Dispose();
                        }
                        saved.Add(path);
                    }
                    Log("Exported 8 directions to " + dlg.SelectedPath);
                    SetStatus("Exported 8 sprites.", Color.Green);
                    MessageBox.Show(this, "Exported:\r\n" + string.Join("\r\n", saved), "3D Editor");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Export failed:\r\n" + ex.Message, "3D Editor");
                }
            }
        }

        private void SendToCanvasButton_Click(object sender, EventArgs e)
        {
            if (_model == null) { MessageBox.Show(this, "No model loaded.", "3D Editor"); return; }
            try
            {
                Bitmap bmp = RenderExport(_yaw); // ownership transfers to canvas
                string name = string.Format("3d_{0}_{1}",
                    Path.GetFileNameWithoutExtension(_plyPath.Length > 0 ? _plyPath : "model"),
                    SplatRenderer.DirectionNames[_direction]);
                if (SpriteReady != null) SpriteReady(bmp, name);
                Log("Sent current view to canvas (" + name + ").");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Send to canvas failed:\r\n" + ex.Message, "3D Editor");
            }
        }

        private void UseAsInputButton_Click(object sender, EventArgs e)
        {
            if (_model == null) { MessageBox.Show(this, "No model loaded.", "3D Editor"); return; }
            try
            {
                // Same WYSIWYG render as Send-to-canvas, staged as the next
                // run's init image: render -> view -> generate, iterative loop.
                using (var bmp = RenderExport(_yaw))
                {
                    string path = Path.Combine(Path.GetTempPath(), "mm_view_as_input.png");
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                    initImageTextBox.Text = path;
                    _initImageServerName = null;
                    RefreshInitPreview();
                }
                Log("Current view staged as init image for the next Generate.");
                SetStatus("View staged as input.", Color.Green);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Stage as input failed:\r\n" + ex.Message, "3D Editor");
            }
        }

        /// <summary>
        /// External entry point (Painter "Send to 3D"): stages the given
        /// bitmap as this editor's init image. Must be called on the UI
        /// thread. Takes its own copy; the caller keeps ownership.
        /// </summary>
        public void SetInitImage(Bitmap bmp)
        {
            try
            {
                if (bmp == null || IsDisposed) return;
                string path = Path.Combine(Path.GetTempPath(), "mm_painter_init.png");
                using (var copy = new Bitmap(bmp))
                    copy.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                initImageTextBox.Text = path;
                _initImageServerName = null;
                RefreshInitPreview();
                Log("Init image received (staged for the next Generate).");
                SetStatus("Init image set.", Color.Green);
            }
            catch (Exception ex)
            {
                Log("SetInitImage failed: " + ex.Message);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { if (_cts != null) _cts.Cancel(); } catch { }
            try { if (_llmCts != null) _llmCts.Cancel(); } catch { }
            try { if (_acCts != null) _acCts.Cancel(); } catch { }
            try { LLM.LlamaEngine.Unload(); } catch { }
            try { if (_sliceRefreshTimer != null) { _sliceRefreshTimer.Stop(); _sliceRefreshTimer.Dispose(); _sliceRefreshTimer = null; } } catch { }
            if (_viewBitmap != null) { _viewBitmap.Dispose(); _viewBitmap = null; }
            base.OnFormClosed(e);
        }

        #endregion
    }
}
