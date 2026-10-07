using MeesaMultisMaker.Audio;
using MeesaMultisMaker.ComfyUI;
using MeesaMultisMaker.Mul;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    /// <summary>
    /// Audio Editor / Generator: ComfyUI text-to-SFX, text-to-music and
    /// voice-clone TTS generation, WAV waveform editing (trim/fade/
    /// normalize/gain/reverse/resample/loop), file import/export, mic
    /// capture, and UO sound.mul slot audition + replacement.
    ///
    /// No new NuGet: playback/record via winmm MCI, DSP hand-rolled in
    /// WaveFile. MP3 outputs play fine but only WAV is editable.
    /// </summary>
    public partial class AudioEditorForm : Form
    {
        private class AudioClip
        {
            public string Name;
            public string Path; // may be temp; null if memory-only
            public WaveData Wave; // null when not WAV-decodable
            public bool IsWav;
            public string Caption = string.Empty;
            public string Label
            {
                get
                {
                    string d = Wave != null ? Wave.DurationSec.ToString("0.0") + "s" : "audio";
                    return Name + " (" + d + ")";
                }
            }
        }

        #region Waveform view

        private class WaveformBox : Panel
        {
            public float[] Peaks = new float[0];
            public double DurationSec;
            public double SelA = -1, SelB = -1; // seconds, -1 = none
            public double PlayheadSec = -1;
            private bool _drag;
            private Point _downPt;

            public WaveformBox()
            {
                DoubleBuffered = true;
                BackColor = Color.FromArgb(14, 14, 20);
                BorderStyle = BorderStyle.FixedSingle;
                Cursor = Cursors.Cross;
            }

            public bool HasSelection()
            {
                return SelA >= 0 && SelB > SelA + 1e-3;
            }

            public void ClearSelection()
            {
                SelA = SelB = -1;
                Invalidate();
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left || DurationSec <= 0) return;
                _drag = true;
                _downPt = e.Location;
                double t = XToSec(e.X);
                SelA = SelB = t;
                Capture = true;
                Invalidate();
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (!_drag || DurationSec <= 0) return;
                if (Math.Abs(e.X - _downPt.X) < 3 && Math.Abs(e.Y - _downPt.Y) < 3) return;
                SelB = XToSec(e.X);
                if (SelB < SelA) { double t = SelA; SelA = SelB; SelB = t; }
                Invalidate();
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                _drag = false;
                try { Capture = false; }
                catch { }
                if (SelA >= 0 && SelB <= SelA + 1e-3) { SelA = SelB = -1; }
                Invalidate();
            }

            private double XToSec(int x)
            {
                double t = (double)x / Math.Max(1, ClientSize.Width) * DurationSec;
                if (t < 0) t = 0;
                if (t > DurationSec) t = DurationSec;
                return t;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                int w = Math.Max(1, ClientSize.Width), h = Math.Max(1, ClientSize.Height);
                int mid = h / 2;
                using (var pen = new Pen(Color.FromArgb(40, 40, 60)))
                    g.DrawLine(pen, 0, mid, w, mid);
                if (HasSelection())
                {
                    int x0 = (int)(SelA / Math.Max(1e-9, DurationSec) * w);
                    int x1 = (int)(SelB / Math.Max(1e-9, DurationSec) * w);
                    using (var br = new SolidBrush(Color.FromArgb(60, 0, 200, 220)))
                        g.FillRectangle(br, x0, 0, Math.Max(1, x1 - x0), h);
                }
                if (Peaks != null && Peaks.Length > 0 && DurationSec > 0)
                {
                    int n = Peaks.Length;
                    float amp = (h / 2f) - 3;
                    using (var pen = new Pen(Color.FromArgb(0, 220, 220)))
                    {
                        for (int x = 0; x < w; x++)
                        {
                            int b = (int)((long)x * n / w);
                            if (b >= n) b = n - 1;
                            float p = Peaks[b];
                            if (p < 0) p = 0;
                            if (p > 1) p = 1;
                            float dh = p * amp;
                            if (dh < 1 && p > 0.003f) dh = 1;
                            if (dh >= 1) g.DrawLine(pen, x, mid - dh, x, mid + dh);
                        }
                    }
                }
                else
                {
                    using (var f = new Font(FontFamily.GenericSansSerif, 10))
                    using (var br = new SolidBrush(Color.Gray))
                    {
                        var sf = new StringFormat { Alignment = StringAlignment.Center };
                        g.DrawString("No clip loaded. Generate, record, or open a WAV.", f, br,
                            new RectangleF(0, mid - 12, w, 24), sf);
                    }
                }
                if (PlayheadSec >= 0 && DurationSec > 0)
                {
                    int x = (int)(PlayheadSec / DurationSec * w);
                    using (var pen = new Pen(Color.Yellow, 2f))
                        g.DrawLine(pen, x, 0, x, h);
                }
            }
        }

        #endregion

        #region Fields + construction

        private TextBox promptSfxBox, promptMusicBox, promptVoiceBox;
        private TextBox negSfxBox, negMusicBox;
        private NumericUpDown durSfxNum, durMusicNum, seedSfxNum, seedMusicNum, seedVoiceNum, stepsSfxNum, stepsMusicNum;
        private CheckBox repromptSfxChk, repromptMusicChk;
        private CheckBox autoQueueSfxChk, autoQueueMusicChk;
        private TextBox langVoiceBox;
        private TextBox workflowSfxBox, workflowMusicBox, workflowVoiceBox;
        private TextBox comfyUrlTextBox;
        private Button generateButton, cancelButton;
        private Button[] modeBtns = new Button[5];
        private Panel modeSfxPanel, modeMusicPanel, modeVoicePanel, modeWatcherPanel, modeLibraryPanel;
        // Everything below the mode tabs is re-stacked under the ACTIVE
        // tab's real content height (Watcher is ~764px tall, Voice ~230px:
        // a fixed reservation left a dead gap on every short tab).
        private Dictionary<Control, int> _belowModes;
        private int _mode; // 0 sfx, 1 music, 2 voice
        private Label statusLabel;
        private ProgressBar progressBar;
        private TextBox logTextBox;

        private Label refVoiceLabel;
        private string _refVoicePath; // local file used as clone reference
        private Button recordButton;

        private WaveformBox waveform;
        private Button playButton, stopButton, pauseButton;
        private TrackBar posTrackBar, volTrackBar;
        private Label timeLabel;
        private NumericUpDown fadeSecNum, gainDbNum, loopFadeNum;
        private ListBox clipListBox;
        private readonly List<AudioClip> _clips = new List<AudioClip>();

        private WaveData _wave; // working clip (reference into a list clip)
        private AudioClip _clip;
        private WaveData _undoWave;
        private string _playPath = string.Empty;

        private TextBox mulFolderBox;
        private ListBox mulSlotList;
        private NumericUpDown mulSlotNum;
        private Label mulInfoLabel;
        private Label mulSourceLabel;
        private Button _saveMulBtn;
        private TextBox musicFolderBox;
        private ListBox musicListBox;
        private Label musicInfoLabel;
        private readonly SoundMul _sounds = new SoundMul();
        private bool _mulDirty;

        private enum MulBackend { None, LegacyPair, Uop }
        private MulBackend _mulBackend = MulBackend.None;

        private class MulSlotView
        {
            public int Id;
            public string Name = string.Empty;
            public byte[] Pcm; // raw 22050/16/mono, null when empty
            public string Label
            {
                get
                {
                    double s = Pcm != null ? (double)Pcm.Length / 2 / 22050 : 0;
                    string n = string.IsNullOrEmpty(Name) ? "(no name)" : Name;
                    return string.Format("#{0} {1} ({2:0.0}s)", Id, n, s);
                }
            }
        }

        private readonly List<MulSlotView> _slotViews = new List<MulSlotView>();
        private readonly Dictionary<string, string> _musicMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // mp3 base -> "ids + loop"
        private readonly List<string> _musicFiles = new List<string>();

        private ComfyUIClient _comfy;
        private CancellationTokenSource _cts;
        private readonly AudioPlayer _player = new AudioPlayer();
        private System.Windows.Forms.Timer _posTimer;
        private bool _scrubbing;
        private SplitContainer _split;

        public AudioEditorForm()
        {
            InitializeComponent();
            HolographicTheme.ApplyToForm(this);
            try { _comfy = new ComfyUIClient(comfyUrlTextBox.Text); }
            catch { _comfy = new ComfyUIClient("http://localhost:8188"); }
            SetMode(0);
            RefreshClipList();
            _posTimer = new System.Windows.Forms.Timer { Interval = 120 };
            _posTimer.Tick += PosTimer_Tick;
            _posTimer.Start();
        }

        private void SetMode(int m)
        {
            _mode = m;
            try
            {
                if (modeSfxPanel != null) modeSfxPanel.Visible = m == 0;
                if (modeMusicPanel != null) modeMusicPanel.Visible = m == 1;
                if (modeVoicePanel != null) modeVoicePanel.Visible = m == 2;
                if (modeWatcherPanel != null) modeWatcherPanel.Visible = m == 3;
                if (modeLibraryPanel != null)
                {
                    modeLibraryPanel.Visible = m == 4;
                    if (m == 4) RefreshLibraryList();
                }
                for (int i = 0; i < modeBtns.Length; i++)
                {
                    if (modeBtns[i] == null) continue;
                    if (i == m)
                    {
                        modeBtns[i].BackColor = HolographicTheme.ButtonAccent;
                        modeBtns[i].ForeColor = Color.White;
                        modeBtns[i].FlatStyle = FlatStyle.Flat;
                    }
                    else HolographicTheme.ApplyToButton(modeBtns[i], ButtonStyle.Default);
                }
                LayoutBelowModes();
            }
            catch { }
        }

        /// <summary>
        /// Re-stack everything under the tabs directly beneath the ACTIVE
        /// tab's auto-sized Bottom, keeping the stacked controls on top in
        /// z-order. Called on tab switches and clone/design toggles.
        /// </summary>
        private void LayoutBelowModes()
        {
            try
            {
                if (_belowModes == null || _belowModes.Count == 0) return;
                Panel active = _mode == 0 ? modeSfxPanel : _mode == 1 ? modeMusicPanel :
                    _mode == 2 ? modeVoicePanel : _mode == 3 ? modeWatcherPanel : modeLibraryPanel;
                if (active == null) return;
                // Deterministic synchronous measure straight from child
                // bounds: no layout passes, no visibility reads, no theme
                // timing involved. Visibility is only trustworthy once the
                // form is actually shown (pre-show it reads false for
                // everything), so hidden controls are skipped only then --
                // counting them pre-show can only add gap, never overlap.
                bool trustVisible = false;
                try { trustVisible = IsHandleCreated && Visible; }
                catch { trustVisible = false; }
                int content = 0;
                foreach (Control c in active.Controls)
                {
                    try
                    {
                        if (trustVisible && !c.Visible) continue;
                        if (c.Bottom > content) content = c.Bottom;
                    }
                    catch { }
                }
                int ny = active.Top + content + 10;
                foreach (var kv in _belowModes)
                {
                    try
                    {
                        if (kv.Key != null && !kv.Key.IsDisposed)
                        {
                            kv.Key.Top = ny + kv.Value;
                            kv.Key.BringToFront();
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void RefreshOnAirViews()
        {
            try
            {
                if (_watchMusicView != null && _watchMusicView.Visible) _watchMusicView.Invalidate();
                if (_watchFoleyView != null && _watchFoleyView.Visible) _watchFoleyView.Invalidate();
            }
            catch { }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { if (_posTimer != null) { _posTimer.Stop(); _posTimer.Dispose(); } }
            catch { }
            try { if (_cts != null) _cts.Cancel(); }
            catch { }
            try { if (_player != null) _player.Dispose(); }
            catch { }
            try { StopWatcher("form closed"); }
            catch { }
            try { if (_watchTimer != null) { _watchTimer.Stop(); _watchTimer.Dispose(); } }
            catch { }
            try
            {
                _watchPreviewOn = false;
                if (_watchPreviewTimer != null) { _watchPreviewTimer.Stop(); _watchPreviewTimer.Dispose(); }
            }
            catch { }
            try { if (_musicPlayer != null) _musicPlayer.Dispose(); }
            catch { }
            try { if (_foleyPlayer != null) _foleyPlayer.Dispose(); }
            catch { }
            try { if (_musicPlayerB != null) _musicPlayerB.Dispose(); }
            catch { }
            try { if (_foleyPlayerB != null) _foleyPlayerB.Dispose(); }
            catch { }
            try { VisionEngine.Unload(); }
            catch { }
            try { MeesaMultisMaker.Audio.AudioGates.Unload(); }
            catch { }
            base.OnFormClosed(e);
        }

        #endregion

        #region UI construction

        private void InitializeComponent()
        {
            Text = "Audio Editor - SFX / Music / Voice (ComfyUI + UO sound.mul)";
            Width = 1220;
            Height = 800;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1020, 660);

            _split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
            Controls.Add(_split);

            // ---------- Left: generation + clips + log ----------
            var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10), AutoScroll = true };
            _split.Panel1.Controls.Add(left);
            int y = 8;

            var title = MakeLabel("GENERATE (ComfyUI)", 10, y, 330, 18);
            title.Font = new Font(title.Font.FontFamily, title.Font.Size, FontStyle.Bold);
            left.Controls.Add(title);
            y += 22;

            string[] modes = new string[] { "SFX", "Music", "Voice", "Watcher", "Library" };
            for (int i = 0; i < 5; i++)
            {
                int m = i;
                var b = new Button { Location = new Point(10 + i * 64, y), Width = 60, Height = 28, Text = modes[i] };
                b.Click += delegate { SetMode(m); };
                left.Controls.Add(b);
                modeBtns[i] = b;
            }
            y += 34;

            modeSfxPanel = BuildGenPanel(left, y, "Sound effect prompt:",
                "sword swing whoosh, clean metallic tone. Length: 2 seconds",
                out promptSfxBox, out negSfxBox, "muffled, distorted, clipping, echo, low quality",
                out durSfxNum, out seedSfxNum, out stepsSfxNum, out workflowSfxBox,
                out repromptSfxChk, out autoQueueSfxChk, AudioWorkflow.DefaultSfxWorkflowPath(), 5, true);
            modeMusicPanel = BuildGenPanel(left, y, "Music prompt:",
                "dark ambient dungeon loop, low drones, sparse percussion. Length: 30 seconds",
                out promptMusicBox, out negMusicBox, "vocals, lyrics, talking, abrupt ending, low quality",
                out durMusicNum, out seedMusicNum, out stepsMusicNum, out workflowMusicBox,
                out repromptMusicChk, out autoQueueMusicChk, AudioWorkflow.DefaultMusicWorkflowPath(), 30, true);
            modeVoicePanel = BuildVoicePanel(left, y);
            modeWatcherPanel = BuildWatcherPanel(left, y);
            modeLibraryPanel = BuildLibraryPanel(left, y);
            // Auto-size every tab to its content: the layout engine
            // measures correctly (visibility, fonts, toggles included),
            // so tabs can never overlap or cover the Generate row, and
            // the row below simply follows the active tab's Bottom.
            foreach (var mp in new Panel[] { modeSfxPanel, modeMusicPanel, modeVoicePanel, modeWatcherPanel, modeLibraryPanel })
            {
                try
                {
                    if (mp != null)
                    {
                        mp.AutoSize = true;
                        mp.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                    }
                }
                catch { }
            }
            y += 784;

            generateButton = new Button { Location = new Point(10, y), Width = 200, Height = 32, Text = "Generate" };
            generateButton.Click += GenerateButton_Click;
            left.Controls.Add(generateButton);
            cancelButton = new Button { Location = new Point(216, y), Width = 114, Height = 32, Text = "Cancel", Enabled = false };
            cancelButton.Click += CancelButton_Click;
            left.Controls.Add(cancelButton);
            y += 40;

            // Anchor on the buttons' own build position, NOT the running
            // y cursor (which has already advanced past them): a wrong
            // anchor shifts every tracked control by the difference.
            int belowBase = generateButton.Top;
            _belowModes = new Dictionary<Control, int>();
            Action<Control> track = c =>
            {
                try { _belowModes[c] = c.Top - belowBase; }
                catch { }
            };
            track(generateButton);
            track(cancelButton);
            y += 40;
            var comfyUrlLabel = MakeLabel("ComfyUI URL:", 10, y, 330, 16);
            left.Controls.Add(comfyUrlLabel);
            track(comfyUrlLabel);
            y += 18;
            comfyUrlTextBox = new TextBox { Location = new Point(10, y), Width = 250, Height = 20 };
            try { comfyUrlTextBox.Text = AppConfig.Instance.ComfyUIUrl; }
            catch { comfyUrlTextBox.Text = "http://localhost:8188"; }
            left.Controls.Add(comfyUrlTextBox);
            track(comfyUrlTextBox);
            var testBtn = new Button { Location = new Point(266, y - 1), Width = 64, Height = 23, Text = "Test" };
            testBtn.Click += TestBtn_Click;
            left.Controls.Add(testBtn);
            track(testBtn);
            y += 30;

            var clipTitle = MakeLabel("CLIPS", 10, y, 330, 18);
            clipTitle.Font = new Font(clipTitle.Font.FontFamily, clipTitle.Font.Size, FontStyle.Bold);
            left.Controls.Add(clipTitle);
            track(clipTitle);
            y += 22;
            clipListBox = new ListBox { Location = new Point(10, y), Width = 320, Height = 130 };
            clipListBox.SelectedIndexChanged += ClipList_SelectedIndexChanged;
            left.Controls.Add(clipListBox);
            track(clipListBox);
            y += 136;
            var bOpen = MkBtn(left, "Open file…", 10, y, 100);
            bOpen.Click += OpenClip_Click;
            track(bOpen);
            var bSave = MkBtn(left, "Save clip as…", 116, y, 104);
            bSave.Click += SaveClip_Click;
            track(bSave);
            var bDrop = MkBtn(left, "Remove", 226, y, 104);
            bDrop.Click += DropClip_Click;
            track(bDrop);
            y += 32;

            statusLabel = new Label { Location = new Point(10, y), Width = 320, Height = 20, Text = "Idle.", ForeColor = Color.Gray };
            left.Controls.Add(statusLabel);
            track(statusLabel);
            y += 24;
            progressBar = new ProgressBar { Location = new Point(10, y), Width = 320, Height = 16, Style = ProgressBarStyle.Blocks, Visible = false };
            left.Controls.Add(progressBar);
            track(progressBar);
            y += 24;
            var logLabel = MakeLabel("Log:", 10, y, 330, 16);
            left.Controls.Add(logLabel);
            track(logLabel);
            y += 18;
            logTextBox = new TextBox
            {
                Location = new Point(10, y), Width = 320, Height = 200,
                Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true
            };
            left.Controls.Add(logTextBox);
            track(logTextBox);

            // ---------- Right: waveform + transport + edits + MUL ----------
            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
            _split.Panel2.Controls.Add(right);

            waveform = new WaveformBox { Dock = DockStyle.Fill };
            right.Controls.Add(waveform);

            var transport = new Panel { Dock = DockStyle.Bottom, Height = 398 };
            right.Controls.Add(transport);
            int ty = 6;

            playButton = new Button { Location = new Point(6, ty), Width = 80, Height = 28, Text = "Play" };
            playButton.Click += PlayButton_Click;
            transport.Controls.Add(playButton);
            pauseButton = new Button { Location = new Point(92, ty), Width = 80, Height = 28, Text = "Pause" };
            pauseButton.Click += PauseButton_Click;
            transport.Controls.Add(pauseButton);
            stopButton = new Button { Location = new Point(178, ty), Width = 80, Height = 28, Text = "Stop" };
            stopButton.Click += StopButton_Click;
            transport.Controls.Add(stopButton);
            timeLabel = new Label { Location = new Point(264, ty + 5), Width = 200, Height = 20, Text = "0.0 / 0.0 s" };
            transport.Controls.Add(timeLabel);
            transport.Controls.Add(MakeLabel("Vol:", 470, ty + 5, 32, 20));
            volTrackBar = new TrackBar { Location = new Point(502, ty - 2), Width = 110, Height = 32, Minimum = 0, Maximum = 100, Value = 90, TickFrequency = 10 };
            volTrackBar.ValueChanged += delegate { try { _player.SetVolume(volTrackBar.Value); } catch { } };
            transport.Controls.Add(volTrackBar);
            ty += 36;

            posTrackBar = new TrackBar { Location = new Point(6, ty), Width = 606, Height = 32, Minimum = 0, Maximum = 1000, Value = 0, TickFrequency = 50 };
            posTrackBar.MouseDown += delegate { _scrubbing = true; };
            posTrackBar.MouseUp += PosTrackBar_MouseUp;
            transport.Controls.Add(posTrackBar);
            ty += 38;

            var editTitle = MakeLabel("EDIT (applies to loaded clip, Undo edit restores)", 6, ty, 400, 16);
            transport.Controls.Add(editTitle);
            ty += 20;
            var bTrim = MkBtnT(transport, "Trim to sel", 6, ty, 92);
            bTrim.Click += delegate { EditOp("trim"); };
            var bFadeIn = MkBtnT(transport, "Fade in", 104, ty, 80);
            bFadeIn.Click += delegate { EditOp("fadein"); };
            var bFadeOut = MkBtnT(transport, "Fade out", 190, ty, 80);
            bFadeOut.Click += delegate { EditOp("fadeout"); };
            var bNorm = MkBtnT(transport, "Normalize", 276, ty, 90);
            bNorm.Click += delegate { EditOp("norm"); };
            var bRev = MkBtnT(transport, "Reverse", 372, ty, 80);
            bRev.Click += delegate { EditOp("reverse"); };
            var bSil = MkBtnT(transport, "Crop silence", 458, ty, 96);
            bSil.Click += delegate { EditOp("silence"); };
            var bUndo = MkBtnT(transport, "Undo edit", 560, ty, 80);
            bUndo.Click += UndoEdit_Click;
            var bDescribe = MkBtnT(transport, "Describe", 646, ty, 80);
            bDescribe.Click += DescribeClip_Click;
            ty += 28;
            transport.Controls.Add(MakeLabel("Fade s:", 6, ty + 4, 52, 20));
            fadeSecNum = MkNumT(transport, 60, ty, 0, 30, 0.1M, 0.5M, 1);
            var bLoop = MkBtnT(transport, "Loopify", 130, ty, 70);
            bLoop.Click += delegate { EditOp("loop"); };
            loopFadeNum = MkNumT(transport, 202, ty, 0, 30, 0.5M, 2M, 1);
            transport.Controls.Add(MakeLabel("loop fade s (end->start blend)", 270, ty + 4, 220, 20));
            ty += 26;
            transport.Controls.Add(MakeLabel("Gain dB:", 6, ty + 4, 56, 20));
            gainDbNum = MkNumT(transport, 64, ty, -24, 24, 1, 0, 0);
            var bGain = MkBtnT(transport, "Apply gain", 132, ty, 90);
            bGain.Click += delegate { EditOp("gain"); };
            var b22050 = MkBtnT(transport, "To 22050 mono", 228, ty, 110);
            b22050.Click += delegate { EditOp("22050"); };
            try
            {
                var tip = new ToolTip { ShowAlways = true };
                tip.SetToolTip(bTrim, "Keep only the selected region (drag on the waveform).");
                tip.SetToolTip(bLoop, "Crossfade the tail into the head for a seamless music loop.");
                tip.SetToolTip(b22050, "Resample + mixdown for UO sound.mul (22050 Hz mono).");
            }
            catch { }
            ty += 28;

            var mulTitle = MakeLabel("UO SOUND + MUSIC", 6, ty, 300, 18);
            mulTitle.Font = new Font(mulTitle.Font.FontFamily, mulTitle.Font.Size, FontStyle.Bold);
            transport.Controls.Add(mulTitle);
            mulSourceLabel = MakeLabel("source: -", 312, ty + 1, 300, 16);
            transport.Controls.Add(mulSourceLabel);
            ty += 22;
            mulFolderBox = new TextBox { Location = new Point(6, ty), Width = 420, Height = 20, ReadOnly = true };
            try { mulFolderBox.Text = AppConfig.Instance.FindMulFolder(); }
            catch { }
            transport.Controls.Add(mulFolderBox);
            var bMulBrowse = MkBtnT(transport, "...", 432, ty, 40);
            bMulBrowse.Click += MulBrowse_Click;
            var bMulReload = MkBtnT(transport, "Reload", 478, ty, 70);
            bMulReload.Click += MulReload_Click;
            transport.Controls.Add(MakeLabel("Slot:", 554, ty + 4, 34, 20));
            mulSlotNum = MkNumT(transport, 590, ty, 0, 4094, 1, 0, 0);
            mulSlotNum.ValueChanged += MulSlotNum_Changed;
            ty += 26;
            mulSlotList = new ListBox { Location = new Point(6, ty), Width = 330, Height = 56 };
            mulSlotList.SelectedIndexChanged += MulSlotList_SelectedIndexChanged;
            transport.Controls.Add(mulSlotList);
            mulInfoLabel = MakeLabel("No sounds loaded.", 342, ty + 4, 270, 40);
            transport.Controls.Add(mulInfoLabel);
            ty += 60;
            var bSlotPlay = MkBtnT(transport, "Play slot", 6, ty, 80);
            bSlotPlay.Click += SlotPlay_Click;
            var bToSlot = MkBtnT(transport, "Clip -> slot", 92, ty, 90);
            bToSlot.Click += ClipToSlot_Click;
            _saveMulBtn = MkBtnT(transport, "Save MULs", 188, ty, 90);
            _saveMulBtn.Click += SaveMul_Click;
            var bFitSlot = MkBtnT(transport, "Fit clip to slot", 284, ty, 110);
            bFitSlot.Click += FitClipToSlot_Click;
            try
            {
                var tip2 = new ToolTip { ShowAlways = true };
                tip2.SetToolTip(bToSlot, "Write the loaded clip into the slot. Legacy pair: staged in memory until Save MULs. UOP: writes immediately (with .bak backup).");
                tip2.SetToolTip(bFitSlot, "Resample to 22050 mono and pad/trim to the slot's exact byte length first (UOP slots are fixed-size).");
            }
            catch { }
            ty += 28;
            var musTitle = MakeLabel("MUSIC FOLDER (mp3)", 6, ty, 300, 16);
            musTitle.Font = new Font(musTitle.Font.FontFamily, musTitle.Font.Size, FontStyle.Bold);
            transport.Controls.Add(musTitle);
            ty += 20;
            musicFolderBox = new TextBox { Location = new Point(6, ty), Width = 420, Height = 20, ReadOnly = true };
            transport.Controls.Add(musicFolderBox);
            var bMusBrowse = MkBtnT(transport, "...", 432, ty, 40);
            bMusBrowse.Click += MusicBrowse_Click;
            var bMusReload = MkBtnT(transport, "Reload", 478, ty, 70);
            bMusReload.Click += MusicReload_Click;
            ty += 26;
            musicListBox = new ListBox { Location = new Point(6, ty), Width = 420, Height = 52 };
            musicListBox.SelectedIndexChanged += MusicList_SelectedIndexChanged;
            transport.Controls.Add(musicListBox);
            musicInfoLabel = MakeLabel("No music folder.", 432, ty + 4, 180, 40);
            transport.Controls.Add(musicInfoLabel);
            ty += 56;
            var bMusPlay = MkBtnT(transport, "Play", 6, ty, 70);
            bMusPlay.Click += MusicPlay_Click;
            var bMusSave = MkBtnT(transport, "Save clip WAV here", 82, ty, 140);
            bMusSave.Click += MusicSaveClip_Click;
            var bMusOpen = MkBtnT(transport, "Open folder", 228, ty, 90);
            bMusOpen.Click += MusicOpenFolder_Click;

            HolographicTheme.ApplyToButton(generateButton, ButtonStyle.Success);
            HolographicTheme.ApplyToButton(cancelButton, ButtonStyle.Danger);
            HolographicTheme.ApplyToButton(playButton, ButtonStyle.Accent);
        }

        private Panel BuildGenPanel(Panel left, int y, string promptTitle, string promptDefault,
            out TextBox promptBox, out TextBox negBox, string negDefault,
            out NumericUpDown durNum, out NumericUpDown seedNum,
            out NumericUpDown stepsNum, out TextBox workflowBox, out CheckBox repromptChk,
            out CheckBox autoQueueChk, string workflowDefault, int durDefault, bool showSteps)
        {
            var p = new Panel { Location = new Point(0, y), Width = 340, Height = 322 };
            left.Controls.Add(p);
            int ly = 0;
            p.Controls.Add(MakeLabel(promptTitle, 10, ly, 320, 16));
            ly += 18;
            promptBox = new TextBox
            {
                Location = new Point(10, ly), Width = 320, Height = 48,
                Multiline = true, ScrollBars = ScrollBars.Vertical, Text = promptDefault
            };
            p.Controls.Add(promptBox);
            ly += 54;
            p.Controls.Add(MakeLabel("Negative (steer away from):", 10, ly, 320, 16));
            ly += 18;
            negBox = new TextBox
            {
                Location = new Point(10, ly), Width = 320, Height = 34,
                Multiline = true, ScrollBars = ScrollBars.Vertical, Text = negDefault
            };
            p.Controls.Add(negBox);
            try
            {
                var negTip = new ToolTip { ShowAlways = true };
                negTip.SetToolTip(negBox, "Written into the template's negative text encoder (empty = off). Same box feeds manual, radio, and watcher runs for this kind.");
            }
            catch { }
            ly += 40;
            p.Controls.Add(MakeLabel("Duration (s):", 10, ly + 4, 90, 20));
            durNum = new NumericUpDown
            {
                Location = new Point(102, ly), Width = 70, Height = 20,
                Minimum = 1, Maximum = 600, Value = durDefault
            };
            p.Controls.Add(durNum);
            p.Controls.Add(MakeLabel("Seed (-1):", 180, ly + 4, 62, 20));
            seedNum = new NumericUpDown
            {
                Location = new Point(244, ly), Width = 86, Height = 20,
                Minimum = -1, Maximum = 999999999, Value = -1
            };
            p.Controls.Add(seedNum);
            ly += 28;
            if (showSteps)
            {
                p.Controls.Add(MakeLabel("Steps:", 10, ly + 4, 90, 20));
                stepsNum = new NumericUpDown
                {
                    Location = new Point(102, ly), Width = 70, Height = 20,
                    Minimum = 1, Maximum = 200, Value = 8
                };
                p.Controls.Add(stepsNum);
                ly += 28;
                repromptChk = new CheckBox
                {
                    Location = new Point(10, ly), Width = 320, Height = 20,
                    Text = "LLM reprompt (needs working reprompt LLM)", Checked = false
                };
                p.Controls.Add(repromptChk);
                try
                {
                    var tip = new ToolTip { ShowAlways = true };
                    tip.SetToolTip(repromptChk, "OFF (default): your prompt is used directly; the reprompt LLM branch is cut so it cannot fail the run. ON: the workflow's LLM rewrites the prompt first (requires its Qwen weights to load cleanly).");
                }
                catch { }
                ly += 26;
                autoQueueChk = new CheckBox
                {
                    Location = new Point(10, ly), Width = 320, Height = 20,
                    Text = "AutoQueue radio (keep 1 song ahead)", Checked = false
                };
                autoQueueChk.CheckedChanged += RadioCheck_Changed;
                p.Controls.Add(autoQueueChk);
                try
                {
                    var tip2 = new ToolTip { ShowAlways = true };
                    tip2.SetToolTip(autoQueueChk, "Radio mode: while playing, the next song generates in the background; when a song ends the next starts and another queues. Uncheck to stop after the current song.");
                }
                catch { }
                ly += 26;
            }
            else
            {
                stepsNum = new NumericUpDown { Minimum = 1, Maximum = 200, Value = 8, Visible = false };
                repromptChk = null;
                autoQueueChk = null;
            }
            p.Controls.Add(MakeLabel("Workflow:", 10, ly, 320, 16));
            ly += 18;
            TextBox wfBox = new TextBox { Location = new Point(10, ly), Width = 256, Height = 20, Text = workflowDefault };
            workflowBox = wfBox;
            p.Controls.Add(wfBox);
            var browse = new Button { Location = new Point(272, ly - 1), Width = 58, Height = 23, Text = "..." };
            browse.Click += delegate
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Filter = "ComfyUI workflow (*.json)|*.json";
                    dlg.FileName = wfBox.Text;
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                        wfBox.Text = dlg.FileName;
                }
            };
            p.Controls.Add(browse);
            return p;
        }

        private Button _voiceCloneBtn, _voiceDesignBtn;
        private Panel _voiceCloneBox, _voiceDesignBox;
        private TextBox _voiceInstructBox;
        private Label _voiceWfLabel;
        private Button _voiceWfBrowse;
        private int _voiceMode; // 0 clone, 1 design

        private void SetVoiceMode(int m)
        {
            _voiceMode = m;
            try
            {
                if (_voiceCloneBox != null) _voiceCloneBox.Visible = m == 0;
                if (_voiceDesignBox != null) _voiceDesignBox.Visible = m == 1;
                if (workflowVoiceBox != null) workflowVoiceBox.Visible = m == 0;
                if (_voiceWfLabel != null) _voiceWfLabel.Visible = m == 0;
                if (_voiceWfBrowse != null) _voiceWfBrowse.Visible = m == 0;
                foreach (var pair in new Button[] { _voiceCloneBtn, _voiceDesignBtn })
                {
                    if (pair == null) continue;
                }
                if (_voiceCloneBtn != null)
                {
                    if (m == 0)
                    {
                        _voiceCloneBtn.BackColor = HolographicTheme.ButtonAccent;
                        _voiceCloneBtn.ForeColor = Color.White;
                        _voiceCloneBtn.FlatStyle = FlatStyle.Flat;
                    }
                    else HolographicTheme.ApplyToButton(_voiceCloneBtn, ButtonStyle.Default);
                }
                if (_voiceDesignBtn != null)
                {
                    if (m == 1)
                    {
                        _voiceDesignBtn.BackColor = HolographicTheme.ButtonAccent;
                        _voiceDesignBtn.ForeColor = Color.White;
                        _voiceDesignBtn.FlatStyle = FlatStyle.Flat;
                    }
                    else HolographicTheme.ApplyToButton(_voiceDesignBtn, ButtonStyle.Default);
                }
                // Clone/design swap the visible rows: re-stack below.
                try { LayoutBelowModes(); }
                catch { }
            }
            catch { }
        }

        private Panel BuildVoicePanel(Panel left, int y)
        {
            var p = new Panel { Location = new Point(0, y), Width = 340, Height = 322 };
            left.Controls.Add(p);
            int ly = 0;
            _voiceCloneBtn = new Button { Location = new Point(10, ly), Width = 158, Height = 24, Text = "Clone a voice" };
            _voiceCloneBtn.Click += delegate { SetVoiceMode(0); };
            p.Controls.Add(_voiceCloneBtn);
            _voiceDesignBtn = new Button { Location = new Point(172, ly), Width = 158, Height = 24, Text = "Design a voice" };
            _voiceDesignBtn.Click += delegate { SetVoiceMode(1); };
            p.Controls.Add(_voiceDesignBtn);
            try
            {
                var tip = new ToolTip { ShowAlways = true };
                tip.SetToolTip(_voiceDesignBtn, "No reference audio needed: describe the voice and Qwen invents it (needs the VoiceDesign model on the server).");
            }
            catch { }
            ly += 28;
            p.Controls.Add(MakeLabel("Text to speak:", 10, ly, 320, 16));
            ly += 18;
            promptVoiceBox = new TextBox
            {
                Location = new Point(10, ly), Width = 320, Height = 48,
                Multiline = true, ScrollBars = ScrollBars.Vertical,
                Text = "Step forward, traveler. The guards are watching."
            };
            p.Controls.Add(promptVoiceBox);
            ly += 54;

            _voiceDesignBox = new Panel { Location = new Point(0, ly), Width = 340, Height = 92 };
            p.Controls.Add(_voiceDesignBox);
            _voiceDesignBox.Controls.Add(MakeLabel("Voice description:", 10, 0, 320, 16));
            _voiceInstructBox = new TextBox
            {
                Location = new Point(10, 18), Width = 320, Height = 68,
                Multiline = true, ScrollBars = ScrollBars.Vertical,
                Text = "gruff old orc guard, deep gravelly voice, slow deliberate speech"
            };
            _voiceDesignBox.Controls.Add(_voiceInstructBox);

            _voiceCloneBox = new Panel { Location = new Point(0, ly), Width = 340, Height = 92 };
            p.Controls.Add(_voiceCloneBox);
            _voiceCloneBox.Controls.Add(MakeLabel("Reference voice:", 10, 0, 320, 16));
            refVoiceLabel = MakeLabel("(none - template default)", 10, 22, 170, 20);
            _voiceCloneBox.Controls.Add(refVoiceLabel);
            var bRefBrowse = new Button { Location = new Point(184, 18), Width = 70, Height = 23, Text = "..." };
            bRefBrowse.Click += RefBrowse_Click;
            _voiceCloneBox.Controls.Add(bRefBrowse);
            recordButton = new Button { Location = new Point(260, 18), Width = 70, Height = 23, Text = "Record" };
            recordButton.Click += RecordButton_Click;
            _voiceCloneBox.Controls.Add(recordButton);
            var bUseClip = new Button { Location = new Point(10, 48), Width = 320, Height = 23, Text = "Use selected clip as voice" };
            bUseClip.Click += UseClipAsRef_Click;
            _voiceCloneBox.Controls.Add(bUseClip);
            ly += 96;

            p.Controls.Add(MakeLabel("Seed (-1):", 10, ly + 4, 62, 20));
            seedVoiceNum = new NumericUpDown
            {
                Location = new Point(76, ly), Width = 86, Height = 20,
                Minimum = -1, Maximum = 999999999, Value = -1
            };
            p.Controls.Add(seedVoiceNum);
            p.Controls.Add(MakeLabel("Lang:", 170, ly + 4, 40, 20));
            langVoiceBox = new TextBox { Location = new Point(212, ly), Width = 118, Height = 20, Text = "Auto" };
            p.Controls.Add(langVoiceBox);
            ly += 28;
            SetVoiceMode(0);
            _voiceWfLabel = MakeLabel("Workflow (clone only):", 10, ly, 320, 16);
            p.Controls.Add(_voiceWfLabel);
            ly += 18;
            workflowVoiceBox = new TextBox { Location = new Point(10, ly), Width = 256, Height = 20, Text = AudioWorkflow.DefaultTtsWorkflowPath() };
            p.Controls.Add(workflowVoiceBox);
            var browse = new Button { Location = new Point(272, ly - 1), Width = 58, Height = 23, Text = "..." };
            _voiceWfBrowse = browse;
            browse.Click += delegate
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Filter = "ComfyUI workflow (*.json)|*.json";
                    dlg.FileName = workflowVoiceBox.Text;
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                        workflowVoiceBox.Text = dlg.FileName;
                }
            };
            p.Controls.Add(browse);
            return p;
        }

        private static Label MakeLabel(string text, int x, int y, int w, int h)
        {
            return new Label { Text = text, Location = new Point(x, y), Width = w, Height = h };
        }

        private static Button MkBtn(Control parent, string text, int x, int y, int w)
        {
            var b = new Button { Location = new Point(x, y), Width = w, Height = 24, Text = text };
            parent.Controls.Add(b);
            return b;
        }

        private static Button MkBtnT(Control parent, string text, int x, int y, int w)
        {
            var b = new Button { Location = new Point(x, y), Width = w, Height = 24, Text = text };
            parent.Controls.Add(b);
            return b;
        }

        private static NumericUpDown MkNumT(Control parent, int x, int y, decimal mn, decimal mx, decimal inc, decimal val, int dec)
        {
            var n = new NumericUpDown
            {
                Location = new Point(x, y), Width = 62, Height = 20,
                Minimum = mn, Maximum = mx, Increment = inc, Value = val, DecimalPlaces = dec
            };
            parent.Controls.Add(n);
            return n;
        }

        #endregion

        #region Library tab

        private TextBox _libSearchBox;
        private ListBox _libListBox;
        private Label _libDetailLabel;
        private List<LibraryEntry> _libShown = new List<LibraryEntry>();

        private Panel BuildLibraryPanel(Panel left, int y)
        {
            var p = new Panel { Location = new Point(0, y), Width = 340, Height = 322 };
            left.Controls.Add(p);
            int ly = 0;
            p.Controls.Add(MakeLabel("Every generation, with its prompt:", 10, ly, 320, 16));
            ly += 18;
            _libSearchBox = new TextBox { Location = new Point(10, ly), Width = 256, Height = 20 };
            _libSearchBox.TextChanged += delegate { RefreshLibraryList(); };
            p.Controls.Add(_libSearchBox);
            var bSearchClear = new Button { Location = new Point(272, ly - 1), Width = 58, Height = 23, Text = "X" };
            bSearchClear.Click += delegate
            {
                if (_libSearchBox != null) _libSearchBox.Text = string.Empty;
                RefreshLibraryList();
            };
            p.Controls.Add(bSearchClear);
            ly += 28;
            _libListBox = new ListBox { Location = new Point(10, ly), Width = 320, Height = 150 };
            _libListBox.SelectedIndexChanged += LibraryList_SelectedIndexChanged;
            p.Controls.Add(_libListBox);
            ly += 156;
            _libDetailLabel = MakeLabel("Pick an entry to see its prompt.", 10, ly, 320, 32);
            p.Controls.Add(_libDetailLabel);
            ly += 36;
            var bUse = new Button { Location = new Point(10, ly), Width = 100, Height = 23, Text = "Reuse prompt" };
            bUse.Click += LibraryReuse_Click;
            p.Controls.Add(bUse);
            var bPlay = new Button { Location = new Point(116, ly), Width = 70, Height = 23, Text = "Play" };
            bPlay.Click += LibraryPlay_Click;
            p.Controls.Add(bPlay);
            var bPrune = new Button { Location = new Point(192, ly), Width = 62, Height = 23, Text = "Prune" };
            bPrune.Click += LibraryPrune_Click;
            p.Controls.Add(bPrune);
            var bReload = new Button { Location = new Point(260, ly), Width = 70, Height = 23, Text = "Reload" };
            bReload.Click += delegate { RefreshLibraryList(); };
            p.Controls.Add(bReload);
            try
            {
                var tip = new ToolTip { ShowAlways = true };
                tip.SetToolTip(bUse, "Copy the entry's prompt into its tab and regenerate (new variation).");
                tip.SetToolTip(bPlay, "Audition the entry's audio file.");
                tip.SetToolTip(bPrune, "Drop entries whose audio files are gone.");
            }
            catch { }
            return p;
        }

        private void RefreshLibraryList()
        {
            try
            {
                if (_libListBox == null) return;
                string q = _libSearchBox != null ? _libSearchBox.Text : string.Empty;
                _libShown = AudioLibrary.Search(q);
                _libListBox.BeginUpdate();
                _libListBox.Items.Clear();
                foreach (var e in _libShown)
                    _libListBox.Items.Add((e.FileAlive ? string.Empty : "[missing] ") + e.Label);
                _libListBox.EndUpdate();
                LibraryList_SelectedIndexChanged(null, EventArgs.Empty);
            }
            catch { }
        }

        private LibraryEntry SelectedLibraryEntry()
        {
            try
            {
                if (_libListBox == null || _libListBox.SelectedIndex < 0 ||
                    _libListBox.SelectedIndex >= _libShown.Count) return null;
                return _libShown[_libListBox.SelectedIndex];
            }
            catch { return null; }
        }

        private void LibraryList_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                var en = SelectedLibraryEntry();
                if (_libDetailLabel == null) return;
                if (en == null)
                {
                    _libDetailLabel.Text = _libShown.Count == 0
                        ? "Library is empty. Generate something first."
                        : "Pick an entry to see its prompt.";
                    return;
                }
                string p = string.IsNullOrEmpty(en.Prompt) ? "(no prompt)" : en.Prompt;
                if (p.Length > 140) p = p.Substring(0, 140) + "...";
                _libDetailLabel.Text = string.Format("{0} | seed {1} | {2:0.0}s\r\n{3}",
                    en.Kind, en.Seed, en.DurationSec, p) +
                    (en.FileAlive ? string.Empty : "\r\nFile is missing.");
            }
            catch { }
        }

        private void LibraryPlay_Click(object sender, EventArgs e)
        {
            try
            {
                var en = SelectedLibraryEntry();
                if (en == null)
                {
                    MessageBox.Show(this, "Pick a library entry first.", "Library");
                    return;
                }
                if (!en.FileAlive)
                {
                    MessageBox.Show(this, "That entry's audio file is gone.", "Library");
                    return;
                }
                _player.SetVolume(volTrackBar != null ? volTrackBar.Value : 90);
                _player.Play(en.AudioPath);
                SetStatus("Playing library entry...", Color.Green);
            }
            catch (Exception ex) { MessageBox.Show(this, "Play failed:\r\n" + ex.Message, "Library"); }
        }

        private void LibraryPrune_Click(object sender, EventArgs e)
        {
            try
            {
                int n = AudioLibrary.PruneMissing();
                RefreshLibraryList();
                Log("Library prune: dropped " + n + " missing entr" + (n == 1 ? "y." : "ies."));
            }
            catch (Exception ex) { MessageBox.Show(this, "Prune failed:\r\n" + ex.Message, "Library"); }
        }

        /// <summary>
        /// Reuse: copy the entry's prompt (and seed) into its tab and
        /// switch there, so the user regenerates a variation. Radio chains
        /// are left alone to avoid hijacking playback.
        /// </summary>
        private void LibraryReuse_Click(object sender, EventArgs e)
        {
            try
            {
                var en = SelectedLibraryEntry();
                if (en == null)
                {
                    if (_mode == 4)
                        MessageBox.Show(this, "Pick a library entry first.", "Library");
                    return;
                }
                string kind = (en.Kind ?? string.Empty).ToLowerInvariant();
                if (kind.IndexOf("music") >= 0 || kind.IndexOf("watchermusic") >= 0)
                {
                    if (promptMusicBox != null) promptMusicBox.Text = en.Prompt;
                    if (seedMusicNum != null && en.Seed > 0)
                        seedMusicNum.Value = Math.Min(seedMusicNum.Maximum, en.Seed % 1000000000);
                    SetMode(1);
                }
                else if (kind.IndexOf("voice") >= 0)
                {
                    if (promptVoiceBox != null) promptVoiceBox.Text = en.Prompt;
                    SetMode(2);
                }
                else
                {
                    if (promptSfxBox != null) promptSfxBox.Text = en.Prompt;
                    if (seedSfxNum != null && en.Seed > 0)
                        seedSfxNum.Value = Math.Min(seedSfxNum.Maximum, en.Seed % 1000000000);
                    SetMode(0);
                }
                Log("Library: prompt restored to its tab (" + en.Kind + ").");
            }
            catch (Exception ex) { MessageBox.Show(this, "Reuse failed:\r\n" + ex.Message, "Library"); }
        }

        #endregion

        #region Transport + clips + edits + MUL

        private string TempWavPath(string tag)
        {
            return Path.Combine(Path.GetTempPath(),
                string.Format("mm_audio_{0}_{1:HHmmss}.wav", tag, DateTime.Now));
        }

        /// <summary>Best-effort duration for a library row (WAV decode).</summary>
        private static double clipDuration(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                    && File.Exists(path))
                    return WaveData.FromFile(path).DurationSec;
            }
            catch { }
            return 0;
        }

        private static string GetUniquePath(string path)
        {
            try
            {
                if (!File.Exists(path)) return path;
                string dir = Path.GetDirectoryName(path);
                string stem = Path.GetFileNameWithoutExtension(path);
                string ext = Path.GetExtension(path);
                for (int i = 1; i < 1000; i++)
                {
                    string cand = Path.Combine(dir, stem + "_" + i + ext);
                    if (!File.Exists(cand)) return cand;
                }
            }
            catch { }
            return path;
        }

        private void PosTimer_Tick(object sender, EventArgs e)
        {
            try { RadioTick(); }
            catch { }
            try { RefreshOnAirViews(); }
            catch { }
            try
            {
                if (_player != null && _player.IsRecording)
                {
                    SetStatus("Recording... " + (_player.RecordPositionMs() / 1000.0).ToString("0.0") + "s (Record again to stop)", Color.Red);
                    return;
                }
                if (_player == null || !_player.IsPlaying)
                {
                    if (waveform != null && waveform.PlayheadSec >= 0)
                    {
                        waveform.PlayheadSec = -1;
                        waveform.Invalidate();
                    }
                    return;
                }
                int ms = _player.PositionMs();
                if (ms < 0)
                {
                    if (waveform != null)
                    {
                        waveform.PlayheadSec = -1;
                        waveform.Invalidate();
                    }
                    SetStatus("Stopped.", Color.Gray);
                    return;
                }
                double sec = ms / 1000.0;
                if (waveform != null)
                {
                    waveform.PlayheadSec = sec;
                    waveform.Invalidate();
                }
                if (_wave != null)
                    timeLabel.Text = sec.ToString("0.0") + " / " + _wave.DurationSec.ToString("0.0") + " s";
                else if (_clip != null)
                    timeLabel.Text = sec.ToString("0.0") + " s (" + _clip.Name + ")";
                if (!_scrubbing && posTrackBar != null)
                {
                    int total = -1;
                    if (_wave != null && _wave.DurationSec > 0)
                        total = (int)(_wave.DurationSec * 1000);
                    else if (_player != null)
                        total = _player.LengthMs();
                    if (total > 0)
                    {
                        int v = (int)((long)ms * posTrackBar.Maximum / total);
                        if (v < 0) v = 0;
                        if (v > posTrackBar.Maximum) v = posTrackBar.Maximum;
                        posTrackBar.Value = v;
                    }
                }
            }
            catch { }
        }

        private string _playFilePath;

        private void PosTrackBar_MouseUp(object sender, MouseEventArgs e)
        {
            _scrubbing = false;
            _radioHold = false;
            try
            {
                if (posTrackBar == null) return;
                if (_wave != null)
                {
                    double sec = (double)posTrackBar.Value / posTrackBar.Maximum * _wave.DurationSec;
                    PlayFrom(sec);
                }
                else if (!string.IsNullOrEmpty(_playFilePath) && _player != null && _player.IsPlaying)
                {
                    int len = _player.LengthMs();
                    if (len > 0)
                    {
                        int ms = (int)((double)posTrackBar.Value / posTrackBar.Maximum * len);
                        _player.SetVolume(volTrackBar != null ? volTrackBar.Value : 90);
                        _player.Play(_playFilePath, ms);
                    }
                }
            }
            catch { }
        }

        private void PlayFrom(double sec)
        {
            try
            {
                if (_wave != null)
                {
                    _playPath = TempWavPath("play");
                    _wave.Save(_playPath);
                    _playFilePath = _playPath;
                    double end = _wave.DurationSec;
                    if (waveform != null && waveform.HasSelection()) end = waveform.SelB;
                    int fromMs = Math.Max(0, (int)(sec * 1000));
                    int toMs = (int)(Math.Min(end, _wave.DurationSec) * 1000);
                    _player.SetVolume(volTrackBar != null ? volTrackBar.Value : 90);
                    _player.Play(_playPath, fromMs, toMs > fromMs ? toMs : -1);
                    SetStatus("Playing...", Color.Green);
                    return;
                }
                // Non-WAV clips (mp3 and friends): play the file directly.
                // No waveform or editing, but fully audible.
                if (_clip != null && !string.IsNullOrEmpty(_clip.Path) && File.Exists(_clip.Path))
                {
                    _playFilePath = _clip.Path;
                    _player.SetVolume(volTrackBar != null ? volTrackBar.Value : 90);
                    _player.Play(_clip.Path, Math.Max(0, (int)(sec * 1000)));
                    SetStatus("Playing file...", Color.Green);
                    return;
                }
                MessageBox.Show(this, "Load a clip first.", "Audio Editor");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Playback failed:\r\n" + ex.Message, "Audio Editor");
            }
        }

        private void PlayButton_Click(object sender, EventArgs e)
        {
            try
            {
                _radioHold = false;
                if (_player != null && _player.IsPaused) { _player.Resume(); SetStatus("Playing...", Color.Green); return; }
                double from = 0;
                if (waveform != null && waveform.HasSelection()) from = waveform.SelA;
                else if (posTrackBar != null && _wave != null && _wave.DurationSec > 0)
                    from = (double)posTrackBar.Value / posTrackBar.Maximum * _wave.DurationSec;
                PlayFrom(from);
            }
            catch { }
        }

        private void PauseButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (_player == null) return;
                if (_player.IsPaused) { _player.Resume(); _radioHold = false; SetStatus("Playing...", Color.Green); }
                else if (_player.IsPlaying) { _player.Pause(); _radioHold = true; SetStatus("Paused.", Color.Orange); }
            }
            catch { }
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            try
            {
                _radioHold = true;
                if (_player != null) _player.Stop();
                if (waveform != null)
                {
                    waveform.PlayheadSec = -1;
                    waveform.Invalidate();
                }
                SetStatus("Stopped.", Color.Gray);
            }
            catch { }
        }

        #region Clips

        private bool _suppressClipSelect;

        private void RefreshClipList()
        {
            // Rebuilding the items resets + restores SelectedIndex, which
            // re-fires SelectedIndexChanged; without the guard that handler
            // reloads the clip, which rebuilds the list again: infinite
            // recursion ending in StackOverflow (a hard process death that
            // no catch block or crash log can record).
            _suppressClipSelect = true;
            try
            {
                if (clipListBox == null) return;
                int sel = clipListBox.SelectedIndex;
                clipListBox.BeginUpdate();
                clipListBox.Items.Clear();
                foreach (var c in _clips) clipListBox.Items.Add(c.Label);
                clipListBox.EndUpdate();
                if (sel >= 0 && sel < clipListBox.Items.Count)
                    clipListBox.SelectedIndex = sel;
            }
            catch { }
            finally { _suppressClipSelect = false; }
        }

        private void AddClipFile(string path, bool select)
        {
            string ext = "";
            try { ext = Path.GetExtension(path).ToLowerInvariant(); }
            catch { }
            WaveData wave = null;
            bool isWav = ext == ".wav";
            if (isWav)
            {
                try { wave = WaveData.FromFile(path); }
                catch (Exception ex) { throw new Exception("Could not decode WAV: " + ex.Message); }
            }
            else
            {
                // Compressed formats auto-convert to editable audio so
                // generated mp3/flac land ready to trim, loop and stage.
                try
                {
                    wave = AudioConvert.ToWave(path);
                    isWav = true;
                    Log(Path.GetFileName(path) + " decoded to editable audio.");
                }
                catch (Exception ex)
                {
                    Log(Path.GetFileName(path) + ": " + ex.Message);
                }
            }
            var clip = new AudioClip
            {
                Name = Path.GetFileName(path),
                Path = path,
                Wave = wave,
                IsWav = isWav
            };
            _clips.Add(clip);
            RefreshClipList();
            if (select && clipListBox != null)
                clipListBox.SelectedIndex = clipListBox.Items.Count - 1;
            if (!isWav)
                Log(Path.GetFileName(path) + " plays as-is (only WAV is editable).");
        }

        private void LoadClipToWaveform(AudioClip clip)
        {
            _clip = clip;
            _undoWave = null;
            _playFilePath = null;
            try { if (_player != null) _player.Stop(); }
            catch { }
            try
            {
                if (waveform != null) waveform.ClearSelection();
                if (posTrackBar != null) posTrackBar.Value = 0;
            }
            catch { }
            if (clip == null || clip.Wave == null)
            {
                _wave = null;
                if (waveform != null)
                {
                    waveform.Peaks = new float[0];
                    waveform.DurationSec = 0;
                    waveform.ClearSelection();
                    waveform.PlayheadSec = -1;
                    waveform.Invalidate();
                }
                timeLabel.Text = "0.0 / 0.0 s";
                if (clip != null && !clip.IsWav)
                    SetStatus("Non-WAV: playback only.", Color.Orange);
                return;
            }
            _wave = clip.Wave;
            RefreshWaveform();
            SetStatus("Loaded: " + clip.Name, Color.Green);
        }

        private void RefreshWaveform()
        {
            try
            {
                if (waveform == null) return;
                if (_wave == null)
                {
                    waveform.Peaks = new float[0];
                    waveform.DurationSec = 0;
                }
                else
                {
                    waveform.Peaks = _wave.GetPeaks(Math.Max(256, waveform.ClientSize.Width));
                    waveform.DurationSec = _wave.DurationSec;
                }
                waveform.PlayheadSec = -1;
                waveform.Invalidate();
                if (_wave != null && timeLabel != null)
                    timeLabel.Text = "0.0 / " + _wave.DurationSec.ToString("0.0") + " s";
                RefreshClipList();
            }
            catch { }
        }

        private void ClipList_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (_suppressClipSelect) return;
                if (clipListBox == null || clipListBox.SelectedIndex < 0 ||
                    clipListBox.SelectedIndex >= _clips.Count) return;
                LoadClipToWaveform(_clips[clipListBox.SelectedIndex]);
            }
            catch { }
        }

        private void OpenClip_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "Audio (*.wav;*.mp3;*.ogg;*.flac)|*.wav;*.mp3;*.ogg;*.flac|WAV (*.wav)|*.wav|All files (*.*)|*.*";
                dlg.Multiselect = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                foreach (string f in dlg.FileNames)
                {
                    try { AddClipFile(f, true); }
                    catch (Exception ex) { Log("Open failed (" + Path.GetFileName(f) + "): " + ex.Message); }
                }
            }
        }

        private void SaveClip_Click(object sender, EventArgs e)
        {
            if (_wave == null)
            {
                MessageBox.Show(this, "No WAV clip loaded.", "Audio Editor");
                return;
            }
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "WAV (*.wav)|*.wav";
                dlg.FileName = _clip != null ? Path.GetFileNameWithoutExtension(_clip.Name) + ".wav" : "clip.wav";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    _wave.Save(dlg.FileName);
                    Log("Saved clip to " + dlg.FileName + ".");
                    SetStatus("Saved.", Color.Green);
                }
                catch (Exception ex) { MessageBox.Show(this, "Save failed:\r\n" + ex.Message, "Audio Editor"); }
            }
        }

        private void DropClip_Click(object sender, EventArgs e)
        {
            try
            {
                if (clipListBox == null || clipListBox.SelectedIndex < 0) return;
                int idx = clipListBox.SelectedIndex;
                bool wasCurrent = _clips[idx] == _clip;
                try { _radioBuffer.Remove(_clips[idx]); }
                catch { }
                _clips.RemoveAt(idx);
                if (wasCurrent) LoadClipToWaveform(null);
                RefreshClipList();
            }
            catch { }
        }

        #endregion

        #region Edits

        private bool RequireWave()
        {
            if (_wave == null)
            {
                MessageBox.Show(this, "Load a WAV clip first (MP3 and friends are playback-only).", "Audio Editor");
                return false;
            }
            return true;
        }

        private void SnapshotUndo()
        {
            try
            {
                if (_wave == null) return;
                long bytes = (long)_wave.Samples.Length * 4L;
                if (bytes > 64L * 1024 * 1024)
                {
                    _undoWave = null;
                    Log("Undo skipped (clip over 64 MB).");
                    return;
                }
                _undoWave = _wave.Clone();
            }
            catch { _undoWave = null; }
        }

        private void UndoEdit_Click(object sender, EventArgs e)
        {
            if (_undoWave == null || _wave == null)
            {
                Log("Nothing to undo.");
                return;
            }
            try
            {
                _wave.Samples = _undoWave.Samples;
                _wave.SampleRate = _undoWave.SampleRate;
                _wave.Channels = _undoWave.Channels;
                _undoWave = null;
                RefreshWaveform();
                Log("Edit undone.");
            }
            catch (Exception ex) { Log("Undo failed: " + ex.Message); }
        }

        private void DescribeClip_Click(object sender, EventArgs e)
        {
            try
            {
                var clip = _clip;
                if (clip == null || string.IsNullOrEmpty(clip.Path) || !File.Exists(clip.Path))
                {
                    Log("Describe: load a clip first.");
                    return;
                }
                if (!clip.Path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    Log("Describe needs the decoded WAV (open the file so it decodes first).");
                    return;
                }
                string path = clip.Path;
                Log("Describing " + clip.Name + "...");
                SetStatus("Captioning...", HolographicTheme.BlueAccent);
                Task.Run(async () =>
                {
                    try
                    {
                        using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)))
                        {
                            var cap = await MeesaMultisMaker.Audio.AudioGates.CaptionAsync(
                                path, cts.Token, m => Log(m));
                            if (!cap.Ok)
                            {
                                SafeInvoke(() => SetStatus("Describe failed.", Color.Orange));
                                Log("Describe failed (gates unavailable).");
                                return;
                            }
                            clip.Caption = cap.Text;
                            try { MeesaMultisMaker.Audio.AudioLibrary.SetCaptionByPath(path, cap.Text); }
                            catch { }
                            SafeInvoke(() => SetStatus("Described.", Color.Green));
                            Log("Heard in " + clip.Name + ": \"" + cap.Text + "\"");
                        }
                    }
                    catch (Exception ex) { Log("Describe failed: " + ex.Message); }
                });
            }
            catch (Exception ex) { Log("Describe failed: " + ex.Message); }
        }

        private void EditOp(string which)
        {
            if (!RequireWave()) return;
            try
            {
                SnapshotUndo();
                string msg = string.Empty;
                if (which == "trim")
                {
                    if (waveform == null || !waveform.HasSelection())
                    {
                        MessageBox.Show(this, "Drag a region on the waveform first.", "Audio Editor");
                        _undoWave = null;
                        return;
                    }
                    var cut = _wave.Trim(waveform.SelA, waveform.SelB);
                    _wave.Samples = cut.Samples;
                    _wave.Channels = cut.Channels;
                    _wave.SampleRate = cut.SampleRate;
                    waveform.ClearSelection();
                    msg = "Trimmed to selection.";
                }
                else if (which == "fadein")
                {
                    double s = fadeSecNum != null ? (double)fadeSecNum.Value : 0.5;
                    _wave.FadeIn(s);
                    msg = "Fade in (" + s + "s).";
                }
                else if (which == "fadeout")
                {
                    double s = fadeSecNum != null ? (double)fadeSecNum.Value : 0.5;
                    _wave.FadeOut(s);
                    msg = "Fade out (" + s + "s).";
                }
                else if (which == "norm")
                {
                    _wave.Normalize();
                    msg = "Normalized.";
                }
                else if (which == "gain")
                {
                    double db = gainDbNum != null ? (double)gainDbNum.Value : 0;
                    _wave.GainDb(db);
                    msg = "Gain " + db + " dB.";
                }
                else if (which == "reverse")
                {
                    _wave.Reverse();
                    msg = "Reversed.";
                }
                else if (which == "silence")
                {
                    var cut = _wave.CropSilence();
                    _wave.Samples = cut.Samples;
                    msg = "Cropped silence.";
                }
                else if (which == "22050")
                {
                    _wave.ToMono();
                    _wave.Resample(22050);
                    msg = "Converted to 22050 Hz mono (MUL-ready).";
                }
                else if (which == "loop")
                {
                    double s = loopFadeNum != null ? (double)loopFadeNum.Value : 2;
                    _wave.CrossfadeLoop(s);
                    msg = "Loopified (tail blended into head over " + s + "s).";
                }
                if (_clip != null && !string.IsNullOrEmpty(_clip.Path) && _clip.Path.StartsWith(Path.GetTempPath()))
                    _clip.Path = null; // edited temp no longer matches file
                RefreshWaveform();
                if (!string.IsNullOrEmpty(msg)) Log(msg);
            }
            catch (Exception ex) { MessageBox.Show(this, "Edit failed:\r\n" + ex.Message, "Audio Editor"); }
        }

        #endregion

        #region Voice reference (browse / record / clip)

        private void RefBrowse_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "Audio (*.wav;*.mp3;*.ogg;*.flac)|*.wav;*.mp3;*.ogg;*.flac";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _refVoicePath = dlg.FileName;
                refVoiceLabel.Text = Path.GetFileName(_refVoicePath);
                Log("Voice reference: " + _refVoicePath + ".");
            }
        }

        private void UseClipAsRef_Click(object sender, EventArgs e)
        {
            try
            {
                if (_clip == null)
                {
                    MessageBox.Show(this, "Select a clip first.", "Audio Editor");
                    return;
                }
                string path = _clip.Path;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    if (_clip.Wave == null)
                    {
                        MessageBox.Show(this, "Clip has no audio data.", "Audio Editor");
                        return;
                    }
                    path = TempWavPath("refvoice");
                    _clip.Wave.Save(path);
                }
                _refVoicePath = path;
                refVoiceLabel.Text = Path.GetFileName(path) + " (clip)";
                Log("Voice reference set from clip: " + path + ".");
            }
            catch (Exception ex) { MessageBox.Show(this, "Ref voice failed:\r\n" + ex.Message, "Audio Editor"); }
        }

        private void RecordButton_Click(object sender, EventArgs e)
        {
            try
            {
                if (_player != null && _player.IsRecording)
                {
                    string path = TempWavPath("take");
                    _player.RecordStop(path);
                    if (recordButton != null) recordButton.Text = "Record";
                    SetStatus("Take saved.", Color.Green);
                    try { AddClipFile(path, true); }
                    catch (Exception ex) { Log("Take load failed: " + ex.Message); return; }
                    _refVoicePath = path;
                    if (refVoiceLabel != null) refVoiceLabel.Text = Path.GetFileName(path) + " (take)";
                    Log("Mic take saved (" + path + ") and set as voice reference.");
                    return;
                }
                _player.RecordStart(22050);
                if (recordButton != null) recordButton.Text = "Stop";
                SetStatus("Recording... press Record again to stop.", Color.Red);
            }
            catch (Exception ex) { MessageBox.Show(this, "Record failed:\r\n" + ex.Message, "Audio Editor"); }
        }

        #endregion

        #region UO sound + music panel

        private MulSlotView FindSlotView(int id)
        {
            foreach (var v in _slotViews)
            {
                if (v.Id == id) return v;
            }
            return null;
        }

        private void MulBrowse_Click(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "UO folder (soundLegacyMUL.uop or legacy sound.mul pair)";
                dlg.SelectedPath = mulFolderBox.Text;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                mulFolderBox.Text = dlg.SelectedPath;
                MulReload_Click(sender, e);
            }
        }

        private void MulReload_Click(object sender, EventArgs e)
        {
            try
            {
                string folder = mulFolderBox.Text.Trim();
                _slotViews.Clear();
                _mulDirty = false;
                _mulBackend = MulBackend.None;
                string uopPath = Path.Combine(folder, SoundUopReader.SOUND_UOP_FILENAME);
                string idxPath = Path.Combine(folder, "soundidx.mul");
                string mulPath = Path.Combine(folder, "sound.mul");
                if (File.Exists(uopPath))
                {
                    var entries = SoundUopReader.Load(folder);
                    foreach (var en in entries)
                    {
                        if (en.Pcm == null) continue;
                        _slotViews.Add(new MulSlotView { Id = en.Id, Name = en.Name, Pcm = en.Pcm });
                    }
                    _mulBackend = MulBackend.Uop;
                    mulSourceLabel.Text = "source: " + SoundUopReader.SOUND_UOP_FILENAME +
                        " (" + entries.Count + " entries, in-place replace)";
                    Log("Loaded " + _slotViews.Count + " sound slots from " + SoundUopReader.SOUND_UOP_FILENAME + ".");
                }
                else if (File.Exists(idxPath) && File.Exists(mulPath))
                {
                    _sounds.Load(folder);
                    for (int i = 0; i < _sounds.SlotCount; i++)
                    {
                        var s = _sounds.GetSlot(i);
                        if (s == null) continue;
                        _slotViews.Add(new MulSlotView { Id = s.Id, Name = s.Name, Pcm = s.Pcm });
                    }
                    _mulBackend = MulBackend.LegacyPair;
                    mulSourceLabel.Text = "source: legacy sound.mul pair (stage + Save)";
                    Log("Loaded " + _slotViews.Count + " sound slots from legacy pair.");
                }
                else
                {
                    mulSourceLabel.Text = "source: none found";
                    throw new Exception("Neither " + SoundUopReader.SOUND_UOP_FILENAME +
                        " nor the legacy sound.mul pair found in:\r\n" + folder);
                }
                if (_saveMulBtn != null)
                {
                    _saveMulBtn.Enabled = _mulBackend == MulBackend.LegacyPair;
                    try
                    {
                        var tip = new ToolTip { ShowAlways = true };
                        tip.SetToolTip(_saveMulBtn, _mulBackend == MulBackend.LegacyPair
                            ? "Rewrite sound.mul + soundidx.mul (.bak backup, first save only)."
                            : "UOP entries write immediately on Clip -> slot; nothing to save.");
                    }
                    catch { }
                }
                RefreshMulSlotList(-1);
                RefreshMulInfo();
                AutoMusicFolder(folder);
            }
            catch (Exception ex)
            {
                mulSourceLabel.Text = "source: load failed";
                mulInfoLabel.Text = "Load failed.";
                MessageBox.Show(this, "Sound load failed:\r\n" + ex.Message, "Audio Editor");
            }
        }

        private void MulSlotList_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (mulSlotList == null || mulSlotList.SelectedIndex < 0) return;
                string item = mulSlotList.SelectedItem as string;
                if (string.IsNullOrEmpty(item) || !item.StartsWith("#")) return;
                int sp = item.IndexOf(' ');
                int id;
                if (int.TryParse(item.Substring(1, sp - 1), out id) && mulSlotNum != null)
                {
                    if (id >= mulSlotNum.Minimum && id <= mulSlotNum.Maximum)
                        mulSlotNum.Value = id;
                }
                MulSlotNum_Changed(sender, e);
            }
            catch { }
        }

        private void RefreshMulInfo()
        {
            try
            {
                if (mulSlotNum == null || mulInfoLabel == null) return;
                int id = (int)mulSlotNum.Value;
                var s = FindSlotView(id);
                string info;
                if (s == null)
                    info = string.Format("Slot {0}: (empty).", id);
                else
                {
                    double d = s.Pcm != null ? (double)s.Pcm.Length / 2 / 22050 : 0;
                    info = string.Format("Slot {0}: {1} ({2:0.00}s).", id,
                        string.IsNullOrEmpty(s.Name) ? "(no name)" : s.Name, d);
                }
                if (_mulDirty) info += " (unsaved changes)";
                mulInfoLabel.Text = info;
            }
            catch { }
        }

        private void MulSlotNum_Changed(object sender, EventArgs e)
        {
            RefreshMulInfo();
        }

        private static byte[] WrapSlotWav(MulSlotView s)
        {
            if (s == null || s.Pcm == null)
                throw new Exception("Empty sound slot.");
            var entry = new SoundEntry { Id = s.Id, Name = s.Name, Pcm = s.Pcm };
            return SoundMul.WrapWav(entry);
        }

        private void SlotPlay_Click(object sender, EventArgs e)
        {
            try
            {
                if (mulSlotNum == null) return;
                var s = FindSlotView((int)mulSlotNum.Value);
                if (s == null)
                {
                    MessageBox.Show(this, "Slot is empty.", "Audio Editor");
                    return;
                }
                string path = TempWavPath("slot" + s.Id);
                File.WriteAllBytes(path, WrapSlotWav(s));
                _player.SetVolume(volTrackBar != null ? volTrackBar.Value : 90);
                _player.Play(path);
                SetStatus("Playing slot " + s.Id + "...", Color.Green);
            }
            catch (Exception ex) { MessageBox.Show(this, "Slot play failed:\r\n" + ex.Message, "Audio Editor"); }
        }

        private void ClipToSlot_Click(object sender, EventArgs e)
        {
            if (!RequireWave()) return;
            try
            {
                if (_slotViews.Count == 0 && _sounds.SlotCount == 0)
                {
                    MessageBox.Show(this, "Load the sounds first (Reload).", "Audio Editor");
                    return;
                }
                int id = (int)mulSlotNum.Value;
                string folder = mulFolderBox.Text.Trim();
                if (_mulBackend == MulBackend.Uop)
                {
                    var target = FindSlotView(id);
                    if (target == null || target.Pcm == null)
                    {
                        MessageBox.Show(this, "UOP slots are fixed-size: pick an existing slot.", "Audio Editor");
                        return;
                    }
                    int slotFrames = target.Pcm.Length / 2;
                    var work = _wave.Clone();
                    work.ToMono();
                    work.Resample(22050);
                    work.Normalize(0.98f);
                    bool padded = work.FitToFrames(slotFrames);
                    if (padded) Log("Clip padded with silence to fit slot " + id + ".");
                    var exact = new byte[slotFrames * 2];
                    for (int i = 0; i < work.Samples.Length && i < slotFrames; i++)
                    {
                        double v = work.Samples[i];
                        if (v > 1) v = 1;
                        else if (v < -1) v = -1;
                        short sh = (short)Math.Round(v * 32767.0);
                        exact[i * 2] = (byte)(sh & 0xFF);
                        exact[i * 2 + 1] = (byte)((sh >> 8) & 0xFF);
                    }
                    if (MessageBox.Show(this,
                        "Overwrite slot " + id + " in " + SoundUopReader.SOUND_UOP_FILENAME +
                        " now? The file is backed up to .bak (first write only).",
                        "Audio Editor", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                        return;
                    SoundUopReader.ReplacePcm(folder, id, exact);
                    target.Pcm = (byte[])exact.Clone();
                    RefreshMulInfo();
                    RefreshMulSlotList(id);
                    Log("Wrote clip into UOP slot " + id + ".");
                    SetStatus("Slot " + id + " replaced.", Color.Green);
                    return;
                }
                string name = _clip != null ? Path.GetFileNameWithoutExtension(_clip.Name) : "custom";
                _sounds.Replace(id, _wave, name);
                _mulDirty = true;
                // Mirror into the browser views.
                var staged = _sounds.GetSlot(id);
                var view = FindSlotView(id);
                if (staged != null)
                {
                    if (view == null) { view = new MulSlotView { Id = id }; _slotViews.Add(view); }
                    view.Name = staged.Name;
                    view.Pcm = staged.Pcm;
                    RefreshMulSlotList(id);
                    RefreshMulInfo();
                }
                Log("Staged clip into slot " + id + " (memory). Save MULs to write.");
                SetStatus("Staged to slot " + id + ".", Color.Green);
            }
            catch (Exception ex) { MessageBox.Show(this, "Stage failed:\r\n" + ex.Message, "Audio Editor"); }
        }

        private void FitClipToSlot_Click(object sender, EventArgs e)
        {
            if (!RequireWave()) return;
            try
            {
                var target = FindSlotView((int)mulSlotNum.Value);
                if (target == null || target.Pcm == null)
                {
                    MessageBox.Show(this, "Pick an existing slot first.", "Audio Editor");
                    return;
                }
                SnapshotUndo();
                _wave.ToMono();
                _wave.Resample(22050);
                _wave.Normalize(0.98f);
                bool padded = _wave.FitToFrames(target.Pcm.Length / 2);
                RefreshWaveform();
                Log("Clip fitted to slot " + target.Id + " (" +
                    (padded ? "padded" : "trimmed/same") + "). Preview it, then Clip -> slot.");
            }
            catch (Exception ex) { MessageBox.Show(this, "Fit failed:\r\n" + ex.Message, "Audio Editor"); }
        }

        private void RefreshMulSlotList(int selectId)
        {
            try
            {
                if (mulSlotList == null) return;
                var sorted = new List<MulSlotView>(_slotViews);
                sorted.Sort((a, b) => a.Id.CompareTo(b.Id));
                mulSlotList.BeginUpdate();
                mulSlotList.Items.Clear();
                int sel = -1;
                for (int k = 0; k < sorted.Count; k++)
                {
                    mulSlotList.Items.Add(sorted[k].Label);
                    if (sorted[k].Id == selectId) sel = k;
                }
                mulSlotList.EndUpdate();
                if (sel >= 0) mulSlotList.SelectedIndex = sel;
            }
            catch { }
        }

        private void SaveMul_Click(object sender, EventArgs e)
        {
            try
            {
                if (_mulBackend != MulBackend.LegacyPair || _sounds.SlotCount == 0)
                {
                    MessageBox.Show(this, "Nothing staged: UOP entries write immediately on Clip -> slot.", "Audio Editor");
                    return;
                }
                string folder = mulFolderBox.Text.Trim();
                if (MessageBox.Show(this,
                    "Rewrite sound.mul + soundidx.mul in:\r\n" + folder +
                    "\r\nOriginals are backed up to .bak (first save only).\r\nContinue?",
                    "Audio Editor", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
                _sounds.Save(folder);
                _mulDirty = false;
                RefreshMulInfo();
                Log("Saved sound.mul + soundidx.mul to " + folder + " (.bak kept).");
                SetStatus("MULs saved.", Color.Green);
            }
            catch (Exception ex) { MessageBox.Show(this, "MUL save failed:\r\n" + ex.Message, "Audio Editor"); }
        }

        #endregion

        #region Music folder (mp3)

        private void AutoMusicFolder(string uoFolder)
        {
            try
            {
                string digital = Path.Combine(uoFolder, "Music", "Digital");
                if (Directory.Exists(digital) && musicFolderBox != null)
                {
                    musicFolderBox.Text = digital;
                    MusicReload_Click(null, EventArgs.Empty);
                }
            }
            catch { }
        }

        private void MusicBrowse_Click(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Music folder (mp3 files + Config.txt)";
                dlg.SelectedPath = musicFolderBox.Text;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                musicFolderBox.Text = dlg.SelectedPath;
                MusicReload_Click(sender, e);
            }
        }

        private void MusicReload_Click(object sender, EventArgs e)
        {
            try
            {
                _musicMap.Clear();
                _musicFiles.Clear();
                if (musicListBox != null) musicListBox.Items.Clear();
                string folder = musicFolderBox.Text.Trim();
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                {
                    musicInfoLabel.Text = "No music folder.";
                    return;
                }
                string cfg = Path.Combine(folder, "Config.txt");
                if (File.Exists(cfg))
                {
                    foreach (string raw in File.ReadAllLines(cfg))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith("#")) continue;
                        int sp = line.IndexOfAny(new char[] { ' ', '\t' });
                        if (sp <= 0) continue;
                        int id;
                        if (!int.TryParse(line.Substring(0, sp), out id)) continue;
                        string rest = line.Substring(sp + 1).Trim();
                        string[] parts = rest.Split(',');
                        string name = parts[0].Trim();
                        bool loop = rest.IndexOf("loop", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (name.Length == 0) continue;
                        string tag = "music " + id + (loop ? " [loop]" : string.Empty);
                        string prev;
                        if (_musicMap.TryGetValue(name, out prev)) _musicMap[name] = prev + ", " + tag;
                        else _musicMap[name] = tag;
                    }
                }
                string[] files = Directory.GetFiles(folder, "*.mp3");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                int shown = 0;
                foreach (string f in files)
                {
                    string baseName = Path.GetFileNameWithoutExtension(f);
                    _musicFiles.Add(f);
                    string tag;
                    string line = baseName;
                    if (_musicMap.TryGetValue(baseName, out tag)) line += "  (" + tag + ")";
                    musicListBox.Items.Add(line);
                    shown++;
                    if (shown >= 500) break;
                }
                musicInfoLabel.Text = shown + " tracks" +
                    (_musicMap.Count > 0 ? ", " + _musicMap.Count + " mapped." : ".");
                Log("Music folder: " + shown + " mp3s.");
            }
            catch (Exception ex) { MessageBox.Show(this, "Music load failed:\r\n" + ex.Message, "Audio Editor"); }
        }

        private void MusicList_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (musicListBox == null || musicListBox.SelectedIndex < 0 ||
                    musicListBox.SelectedIndex >= _musicFiles.Count) return;
                string f = _musicFiles[musicListBox.SelectedIndex];
                var info = new FileInfo(f);
                musicInfoLabel.Text = info.Name + " (" + (info.Length / 1024) + " KB).";
            }
            catch { }
        }

        private void MusicPlay_Click(object sender, EventArgs e)
        {
            try
            {
                if (musicListBox == null || musicListBox.SelectedIndex < 0 ||
                    musicListBox.SelectedIndex >= _musicFiles.Count)
                {
                    MessageBox.Show(this, "Pick a track first.", "Audio Editor");
                    return;
                }
                string f = _musicFiles[musicListBox.SelectedIndex];
                _player.SetVolume(volTrackBar != null ? volTrackBar.Value : 90);
                _player.Play(f);
                SetStatus("Playing music...", Color.Green);
            }
            catch (Exception ex) { MessageBox.Show(this, "Music play failed:\r\n" + ex.Message, "Audio Editor"); }
        }

        private void MusicSaveClip_Click(object sender, EventArgs e)
        {
            if (!RequireWave()) return;
            try
            {
                string folder = musicFolderBox.Text.Trim();
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                {
                    MessageBox.Show(this, "Set a music folder first.", "Audio Editor");
                    return;
                }
                using (var dlg = new SaveFileDialog())
                {
                    dlg.InitialDirectory = folder;
                    dlg.Filter = "WAV (*.wav)|*.wav";
                    dlg.FileName = _clip != null ? Path.GetFileNameWithoutExtension(_clip.Name) + ".wav" : "custom.wav";
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    _wave.Save(dlg.FileName);
                    Log("Saved clip to music folder: " + dlg.FileName + ".");
                    MusicReload_Click(sender, e);
                }
            }
            catch (Exception ex) { MessageBox.Show(this, "Save failed:\r\n" + ex.Message, "Audio Editor"); }
        }

        private void MusicOpenFolder_Click(object sender, EventArgs e)
        {
            try
            {
                string folder = musicFolderBox.Text.Trim();
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    System.Diagnostics.Process.Start(folder);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Audio Editor"); }
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

        private void SafeInvoke(Action a)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(a);
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

        private void CancelButton_Click(object sender, EventArgs e)
        {
            try
            {
                _radioHold = true; // radio rests until Play/Generate resumes
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

        private int _runMode;
        private string _runPrompt, _runWorkflow, _runLang, _runRefLocal, _runInstruct, _runNegative;
        private int _runVoiceMode;

        // Radio (AutoQueue) state: while the mode checkbox is on, finished
        // songs auto-play and the next one generates in the background, so
        // exactly one song stays ahead of playback. _radioHold pauses the
        // machine on manual Stop/Pause/Cancel until Play/Generate resumes.
        private int _radioMode;
        private bool _radioHold;
        private bool _radioRun;
        private readonly List<AudioClip> _radioBuffer = new List<AudioClip>();
        private double _runDuration;
        private long _runSeed;
        private int _runSteps;
        private bool _runReprompt;

        private void GenerateButton_Click(object sender, EventArgs e)
        {
            if (_mode == 3)
            {
                // Watcher tab: the shared button runs one watcher cycle.
                WatchSnapBtn_Click(sender, e);
                return;
            }
            if (_mode == 4)
            {
                // Library tab: replay the selected entry's prompt (radio off).
                LibraryReuse_Click(sender, e);
                return;
            }
            if (_cts != null) return;
            if (!SnapshotRunParams(_mode, true)) return;
            _radioMode = _mode;
            _radioHold = false;
            _radioRun = false;
            LaunchRun();
        }

        /// <summary>
        /// Snapshot the run parameters from the given mode tab into the
        /// _run* fields (also sets _runMode). False when invalid.
        /// </summary>
        private bool SnapshotRunParams(int mode, bool showErrors)
        {
            try
            {
                _runMode = mode;
                if (mode == 0)
                {
                    _runPrompt = promptSfxBox.Text.Trim();
                    if (_runPrompt.Length == 0) throw new Exception("Enter a prompt first.");
                    _runNegative = negSfxBox != null ? negSfxBox.Text.Trim() : string.Empty;
                    _runWorkflow = workflowSfxBox.Text;
                    _runDuration = (double)durSfxNum.Value;
                    _runSeed = (long)seedSfxNum.Value;
                    _runSteps = (int)stepsSfxNum.Value;
                    _runReprompt = repromptSfxChk != null && repromptSfxChk.Checked;
                }
                else if (mode == 1)
                {
                    _runPrompt = promptMusicBox.Text.Trim();
                    if (_runPrompt.Length == 0) throw new Exception("Enter a prompt first.");
                    _runNegative = negMusicBox != null ? negMusicBox.Text.Trim() : string.Empty;
                    _runWorkflow = workflowMusicBox.Text;
                    _runDuration = (double)durMusicNum.Value;
                    _runSeed = (long)seedMusicNum.Value;
                    _runSteps = (int)stepsMusicNum.Value;
                    _runReprompt = repromptMusicChk != null && repromptMusicChk.Checked;
                }
                else
                {
                    _runPrompt = promptVoiceBox.Text.Trim();
                    if (_runPrompt.Length == 0) throw new Exception("Enter text to speak first.");
                    _runNegative = string.Empty;
                    _runVoiceMode = _voiceMode;
                    if (_voiceMode == 1)
                    {
                        _runInstruct = _voiceInstructBox != null ? _voiceInstructBox.Text.Trim() : string.Empty;
                        if (_runInstruct.Length == 0) throw new Exception("Describe the voice first.");
                        _runWorkflow = string.Empty;
                        _runRefLocal = null;
                    }
                    else
                    {
                        _runWorkflow = workflowVoiceBox.Text;
                        _runInstruct = string.Empty;
                        _runRefLocal = !string.IsNullOrEmpty(_refVoicePath) && File.Exists(_refVoicePath)
                            ? _refVoicePath : null;
                    }
                    _runSeed = (long)seedVoiceNum.Value;
                    _runLang = langVoiceBox.Text.Trim();
                    _runSteps = 0;
                    _runDuration = 0;
                }
                if (_runMode != 2 || _runVoiceMode == 0)
                {
                    if (string.IsNullOrEmpty(_runWorkflow) || !File.Exists(_runWorkflow))
                        throw new Exception("Workflow file not found:\r\n" + _runWorkflow);
                }
                try { AppConfig.Instance.ComfyUIUrl = comfyUrlTextBox.Text.Trim(); }
                catch { }
                return true;
            }
            catch (Exception ex)
            {
                if (showErrors)
                    MessageBox.Show(this, ex.Message, "Audio Editor");
                else
                    Log("Radio snapshot skipped: " + ex.Message);
                return false;
            }
        }

        private void LaunchRun()
        {
            string kind = _runMode == 0 ? "SFX" : _runMode == 1 ? "Music" : "Voice";
            _comfy = new ComfyUIClient(comfyUrlTextBox.Text.Trim());
            _cts = new CancellationTokenSource();
            generateButton.Enabled = false;
            cancelButton.Enabled = true;
            progressBar.Style = ProgressBarStyle.Marquee;
            progressBar.Visible = true;
            Task.Run(() => RunAudio(kind, _cts.Token));
        }

        private CheckBox RadioBoxForMode(int mode)
        {
            return mode == 1 ? autoQueueMusicChk : autoQueueSfxChk;
        }

        private void RadioCheck_Changed(object sender, EventArgs e)
        {
            try
            {
                var cb = sender as CheckBox;
                if (cb == null) return;
                if (cb.Checked)
                {
                    // Arming a tab arms its radio chain; the tick bootstraps
                    // from the current prompt on that tab.
                    _radioMode = (cb == autoQueueMusicChk) ? 1 : 0;
                    _radioHold = false;
                    Log("Radio on: keeping one song ahead.");
                }
                else if (_radioBuffer.Count > 0)
                {
                    _radioBuffer.Clear();
                    Log("Radio off: buffered songs released to the clip list.");
                }
            }
            catch { }
        }

        /// <summary>
        /// Radio tick (UI thread, from the position timer): keep exactly one
        /// finished song ahead of playback, and auto-play finished songs.
        /// </summary>
        private void RadioTick()
        {
            try
            {
                if (_radioBuffer == null) return;
                CheckBox cb = RadioBoxForMode(_radioMode);
                if (cb == null || !cb.Checked) return;
                if (_radioHold) return;
                if (_cts != null) return; // run already in flight
                _radioBuffer.RemoveAll(c => c == null || !_clips.Contains(c)
                    || string.IsNullOrEmpty(c.Path) || !File.Exists(c.Path)
                    || c.Wave == null || c.Wave.FrameCount == 0);
                // Tight end-poll: within the last 750 ms of the current song,
                // swap immediately instead of waiting for MCI to report done.
                int posMs = -1, lenMs = -1;
                bool playing = _player != null && _player.PollPlaying(out posMs, out lenMs);
                if (playing && lenMs > 0 && posMs >= 0 && lenMs - posMs <= 750)
                    playing = TryRadioAdvance();
                if (!playing)
                {
                    if (_radioBuffer.Count > 0)
                    {
                        TryRadioAdvance();
                    }
                    else
                    {
                        StartRadioRun();
                    }
                    return;
                }
                if (_radioBuffer.Count < 1)
                    StartRadioRun();
            }
            catch { }
        }

        /// <summary>
        /// Dequeue the next radio song and start it with no gap. Returns
        /// false when nothing playable is buffered.
        /// </summary>
        private bool TryRadioAdvance()
        {
            try
            {
                if (_radioBuffer == null || _radioBuffer.Count == 0) return false;
                var clip = _radioBuffer[0];
                _radioBuffer.RemoveAt(0);
                int idx = _clips.IndexOf(clip);
                if (idx < 0) return TryRadioAdvance();
                if (string.IsNullOrEmpty(clip.Path) || !File.Exists(clip.Path)) return TryRadioAdvance();
                if (clipListBox != null) clipListBox.SelectedIndex = idx;
                if (waveform != null) waveform.ClearSelection();
                Log("Radio: now playing " + clip.Name + ".");
                _radioHold = false;
                PlayFrom(0);
                return _player != null && _player.IsPlaying;
            }
            catch { return false; }
        }

        private void StartRadioRun()
        {
            try
            {
                if (_cts != null) return;
                if (!SnapshotRunParams(_radioMode, false)) return;
                _radioRun = true;
                Log("Radio: queueing next song...");
                LaunchRun();
            }
            catch { _radioRun = false; }
        }

        private async Task RunAudio(string kind, CancellationToken token)
        {
            try
            {
                AudioWorkflow.BuildResult built;
                if (_runMode == 2 && _runVoiceMode == 1)
                {
                    built = AudioWorkflow.BuildVoiceDesign(_runPrompt, _runInstruct,
                        _runSeed, _runLang);
                }
                else if (_runMode == 2)
                {
                    // Voice ref clip uploads first so LoadAudio points at it.
                    string refServer = null;
                    if (!string.IsNullOrEmpty(_runRefLocal) && File.Exists(_runRefLocal))
                    {
                        SetStatus("Uploading reference voice...", HolographicTheme.BlueAccent);
                        byte[] rb = File.ReadAllBytes(_runRefLocal);
                        refServer = await _comfy.UploadAudio(rb, Path.GetFileName(_runRefLocal));
                        Log("Reference voice uploaded as: " + refServer + ".");
                    }
                    else
                    {
                        Log("No reference voice set: using the template default.");
                    }
                    built = AudioWorkflow.BuildVoiceClone(_runWorkflow, _runPrompt,
                        _runSeed, refServer, _runLang);
                }
                else
                {
                    built = AudioWorkflow.BuildTextToAudio(_runWorkflow, _runPrompt,
                        _runDuration, _runSeed, _runSteps, _runReprompt, null, _runNegative);
                }
                Log(kind + " graph (" + built.FormatNote + "), seed=" + built.Seed + ".");
                string promptJson = built.PromptJson;

                SetStatus("Queueing on ComfyUI...", HolographicTheme.BlueAccent);
                string clientId = Guid.NewGuid().ToString();
                string promptId = await _comfy.QueuePrompt(promptJson, clientId);
                if (string.IsNullOrEmpty(promptId)) throw new Exception("Server did not return a prompt_id.");
                Log("Queued " + kind + ", prompt_id=" + promptId + ". Rendering audio...");

                ComfyUIClient.OutputImage found = await WaitAudioResult(promptId, kind, token);
                if (found == null) { SetStatus("Cancelled.", Color.Orange); return; }
                Log("Got audio: " + found.Filename + ".");

                SetStatus("Downloading audio...", HolographicTheme.BlueAccent);
                byte[] bytes2 = await _comfy.DownloadFile(found);
                if (bytes2 == null || bytes2.Length == 0) throw new Exception("Downloaded audio is empty.");
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MeesaMultisMaker", "Audio");
                Directory.CreateDirectory(dir);
                // Short id filenames always: the server's own filename may
                // embed prompt text (long) and collide across runs.
                string ext = ".wav";
                try
                {
                    string serverExt = Path.GetExtension(found.Filename);
                    if (!string.IsNullOrEmpty(serverExt)) ext = serverExt.ToLowerInvariant();
                }
                catch { }
                if (ext != ".wav" && ext != ".mp3" && ext != ".flac" && ext != ".ogg" && ext != ".opus") ext = ".wav";
                string runId = AudioWorkflow.NewAudioId(kind);
                string localPath = Path.Combine(dir, runId + ext);
                if (File.Exists(localPath)) localPath = GetUniquePath(localPath);
                File.WriteAllBytes(localPath, bytes2);
                Log(string.Format("Saved {0:0.0} KB to {1}", bytes2.Length / 1024.0, localPath));
                try { AudioPromptLedger.Append(runId, kind, built.Seed, _runDuration, localPath, _runPrompt); }
                catch { }
                // Decode off the UI thread: non-WAV arrivals become editable
                // WAVs next to the original instead of freezing the form in
                // AddClipFile (or worse, dying there with no evidence).
                string addPath = localPath;
                if (!localPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        SetStatus("Decoding audio...", HolographicTheme.BlueAccent);
                        string baseName = Path.GetFileNameWithoutExtension(localPath);
                        string wavPath = Path.Combine(Path.GetDirectoryName(localPath), baseName + ".wav");
                        if (File.Exists(wavPath)) wavPath = GetUniquePath(wavPath);
                        AudioConvert.DecodeToFile(localPath, wavPath);
                        Log("Decoded to editable WAV: " + wavPath + ".");
                        addPath = wavPath;
                    }
                    catch (Exception ex)
                    {
                        Log("Auto-decode skipped (" + ex.Message + "). File stays playback-only.");
                    }
                }
                // Same edge-silence trim as the watcher: generated clips
                // often start/end with empty whitespace.
                try
                {
                    if (addPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(addPath))
                    {
                        double cut = MeesaMultisMaker.Audio.WaveData.TrimFileEnds(addPath);
                        if (cut > 0.05)
                            Log("Trimmed " + cut.ToString("0.0") + "s edge silence.");
                    }
                }
                catch { }
                // Loudness discipline (same layer targets as the watcher).
                // Manual runs only measure the gates: scores go in the log
                // for the user to judge, no auto-retry here.
                try
                {
                    bool isWav = addPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(addPath);
                    if (isWav && _runMode != 2)
                    {
                        string lerr;
                        double target = _runMode == 0
                            ? MeesaMultisMaker.Audio.AudioLoudness.FoleyTargetI
                            : MeesaMultisMaker.Audio.AudioLoudness.MusicTargetI;
                        if (MeesaMultisMaker.Audio.AudioLoudness.TryNormalize(addPath, target, out lerr))
                            Log("Loudness matched (" + target.ToString("0") + " LUFS).");
                        else if (!string.IsNullOrEmpty(lerr))
                            Log("Loudness skipped (" + lerr + ").");
                    }
                }
                catch { }
                try
                {
                    bool isWav = addPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(addPath);
                    if (isWav && (_runMode == 0 || _runMode == 1))
                    {
                        var tr = MeesaMultisMaker.Audio.AudioGates.TranscribeAsync(
                            addPath, token, m => Log(m)).Result;
                        if (tr.Ok && tr.Words > 0)
                            Log("Heard " + tr.Words + " word(s): \"" +
                                tr.Text.Substring(0, Math.Min(100, tr.Text.Length)) + "\"");
                        var cr = MeesaMultisMaker.Audio.AudioGates.ClapAsync(
                            addPath, _runPrompt, token, m => Log(m)).Result;
                        if (cr.Ok)
                        {
                            Log("CLAP " + cr.Score.ToString("0.00") +
                                (cr.Score < MeesaMultisMaker.Audio.AudioGates.ClapRejectBelow
                                    ? " (under the match floor -- consider regenerating)." : "."));
                            SetStatus("CLAP " + cr.Score.ToString("0.00"),
                                cr.Score < MeesaMultisMaker.Audio.AudioGates.ClapRejectBelow
                                    ? Color.Orange : Color.Green);
                        }
                    }
                }
                catch { }
                // Caption the finished clip for the library (content
                // search); failures just leave the entry uncaptioned.
                string heardManual = string.Empty;
                try
                {
                    bool isWav = addPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(addPath);
                    if (isWav && (_runMode == 0 || _runMode == 1))
                    {
                        var cap = MeesaMultisMaker.Audio.AudioGates.CaptionAsync(
                            addPath, token, m => Log(m)).Result;
                        if (cap.Ok)
                        {
                            heardManual = cap.Text;
                            Log("Heard: \"" + heardManual + "\"");
                        }
                    }
                }
                catch { }
                string finalAdd = addPath;
                string finalHeard = heardManual;
                bool wasRadio = _radioRun;
                SafeInvoke(() =>
                {
                    try
                    {
                        int before = _clips.Count;
                        AddClipFile(finalAdd, !wasRadio);
                        if (wasRadio && _clips.Count > before)
                        {
                            var clip = _clips[_clips.Count - 1];
                            CheckBox cb = RadioBoxForMode(_radioMode);
                            if (cb != null && cb.Checked)
                            {
                                _radioBuffer.Add(clip);
                                Log("Radio: " + clip.Name + " ready (" + _radioBuffer.Count + " ahead).");
                            }
                            else if (clipListBox != null && _clips.Count > 0)
                            {
                                clipListBox.SelectedIndex = _clips.Count - 1;
                            }
                        }
                        // Library: every finished generation, with its
                        // prompt, seed, workflow and file -- silent by
                        // design, the Library tab reads it back.
                        try
                        {
                            AudioLibrary.Append(new LibraryEntry
                            {
                                Kind = kind,
                                Prompt = _runPrompt,
                                Seed = built.Seed,
                                Workflow = _runWorkflow,
                                AudioPath = finalAdd,
                                DurationSec = clipDuration(finalAdd),
                                Scene = string.Empty,
                                Caption = finalHeard
                            });
                        }
                        catch { }
                    }
                    catch (Exception ex) { Log("Load failed: " + ex.Message); SetStatus("Load failed.", Color.Red); }
                });
                SetStatus("Done.", Color.Green);
            }
            catch (OperationCanceledException) { SetStatus("Cancelled.", Color.Orange); Log("Cancelled by user."); }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message, Color.Red);
                Log("ERROR: " + ex.Message);
                if (_radioRun)
                {
                    _radioRun = false;
                    SafeInvoke(() =>
                    {
                        try
                        {
                            CheckBox cb = RadioBoxForMode(_radioMode);
                            if (cb != null) cb.Checked = false;
                        }
                        catch { }
                    });
                    Log("Radio stopped on error (no retry loop).");
                }
            }
            finally
            {
                _radioRun = false;
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
            }
        }

        #endregion

        #endregion
    }
}


