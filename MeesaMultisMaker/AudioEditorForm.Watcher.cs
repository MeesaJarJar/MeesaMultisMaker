using MeesaMultisMaker.Audio;
using MeesaMultisMaker.ComfyUI;
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
    /// Ambient watcher (experimental): watches a webcam feed, describes the
    /// scene with a local vision model every interval, and dynamically
    /// crafts foley ambience + background music through the existing SFX /
    /// music generators. Two dedicated playback layers (music + foley) sit
    /// beside the manual transport player, so auditioning never fights it.
    /// </summary>
    public partial class AudioEditorForm
    {
        private Panel _watchCamHost;
        private PictureBox _watchPreviewBox;
        private TextBox _watchFoleyAskBox, _watchMusicAskBox;
        private TextBox _watchDescribeBox;
        private WaveMiniView _watchMusicView, _watchFoleyView;
        private GroupBox _watchOnAirBox;
        private Label _watchMusicAirLabel, _watchFoleyAirLabel;
        private WaveData _watchMusicAir, _watchFoleyAir;
        private string _watchMusicAirName = "-", _watchFoleyAirName = "-";
        private DateTime _watchMusicStarted = DateTime.MinValue, _watchFoleyStarted = DateTime.MinValue;
        private ComboBox _watchCamCombo;
        private NumericUpDown _watchIntervalNum;
        private NumericUpDown _watchFoleySecNum, _watchMusicSecNum;
        private Button _watchPreviewBtn, _watchSnapBtn, _watchStartBtn;
        private CheckBox _watchFoleyChk, _watchMusicChk;
        private ListBox _watchSceneList;
        private Label _watchStatusLabel;

        private System.Windows.Forms.Timer _watchTimer;
        private System.Windows.Forms.Timer _watchPreviewTimer;
        private bool _watchPreviewBusy;
        private bool _watchPreviewOn;
        private CancellationTokenSource _watchCts;
        private bool _watchSenseBusy;
        private bool _watchOn;
        // Independent per-layer generation single-flights: sense keeps
        // firing every N sec while foley/music render in the background.
        private bool _foleyGenBusy;
        private bool _musicGenBusy;
        private DateTime _nextFoleyDue = DateTime.MinValue;
        private DateTime _nextMusicDue = DateTime.MinValue;
        // Music never cuts before this long on air (user expectation:
        // minimum 15s playback before a transition).
        private const double MusicMinPlaySec = 15.0;
        // Adaptive render estimates (measured: ~0.7s foley, ~7s music).
        // Next renders start at (swap - lastRender - buffer) so fast
        // models play full length gaplessly; slow ones catch up best-effort.
        private double _lastFoleyRenderSec = 0.7;
        private double _lastMusicRenderSec = 7.0;
        private DateTime _foleySwapDue = DateTime.MinValue;
        private DateTime _musicSwapDue = DateTime.MinValue;
        private WatchTimelineEntry _watchTimelineSelected;
        // Sense/scheduler split: the sense loop ONLY describes frames and
        // publishes the freshest prompts here. A 1s scheduler owns ALL
        // audio queueing, so prefetches fire on time with zero vision
        // latency on the critical path (prompt reuse, not re-describe).
        private string _latestFoleyPrompt;
        private string _latestMusicPrompt;
        private DateTime _latestSenseAt = DateTime.MinValue;
        private double _lastVisionSec = 4.0;
        private string _watchSceneSummary = "Watcher idle.";
        private System.Windows.Forms.Timer _watchSchedTimer;
        // DJ timeline: every clip that actually went on air, newest first.
        private class WatchTimelineEntry
        {
            public DateTime At;
            public DateTime? End;
            public bool Foley;
            public string ClipName = "-";
            public string Prompt = string.Empty;
            public string Scene = string.Empty;
            public long Seed;
            public double DurationSec;
            public string AudioPath = string.Empty;
            public string Caption = string.Empty;
        }
        private readonly List<WatchTimelineEntry> _watchTimeline = new List<WatchTimelineEntry>();
        private readonly object _watchTimelineLock = new object();
        private WatchTimelineCards _watchTimelineLane;
        private TextBox _watchTimelineDetail;
        private GroupBox _watchTimelineBox;
        private System.Windows.Forms.Timer _watchTimelineTimer;

        private readonly AudioPlayer _musicPlayer = new AudioPlayer("mm_audio_music");
        private readonly AudioPlayer _foleyPlayer = new AudioPlayer("mm_audio_foley");
        // Crossfade flip-flops: the incoming clip starts on the idle alias
        // while the outgoing ramps down, so swaps overlap instead of cut.
        private readonly AudioPlayer _musicPlayerB = new AudioPlayer("mm_audio_music_b");
        private readonly AudioPlayer _foleyPlayerB = new AudioPlayer("mm_audio_foley_b");
        private bool _musicFlip;
        private bool _foleyFlip;
        private const double FoleyXfadeSec = 1.0;
        private const double MusicXfadeSec = 2.0;
        // Change-gating: frame hash of the last sensed frame, per-layer
        // static flags from the latest sense, and the prompts behind the
        // last queued renders (prompt-sim gate). Static layers loop the
        // on-air file at swap time instead of burning GPU.
        private byte[] _lastFrameHash;
        private bool _foleySceneStatic;
        private bool _musicSceneStatic;
        private string _lastQueuedFoleyPrompt;
        private string _lastQueuedMusicPrompt;
        /// <summary>Frame-static threshold 0..1, mirrored from the Δ% box
        /// (background threads must not touch the control directly).</summary>
        private double _frameStaticBelow = 0.04;
        private const double PromptStaticAt = 0.85;
        private NumericUpDown _watchDeltaNum;
        private CheckBox _watchBounceChk, _watchClapChk;
        private string _foleyPlayPath = string.Empty;
        private string _musicPlayPath = string.Empty;
        private double _foleyClipLen;
        private double _musicClipLen;

        private Panel BuildWatcherPanel(Panel left, int y)
        {
            var p = new Panel { Location = new Point(0, y), Width = 340, Height = 764 };
            left.Controls.Add(p);
            int ly = 0;

            _watchCamHost = new Panel
            {
                Location = new Point(10, ly), Width = 320, Height = 120,
                BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black
            };
            _watchPreviewBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black
            };
            _watchCamHost.Controls.Add(_watchPreviewBox);
            p.Controls.Add(_watchCamHost);
            ly += 124;

            p.Controls.Add(MakeLabel("Cam:", 10, ly + 4, 36, 20));
            _watchCamCombo = new ComboBox
            {
                Location = new Point(48, ly), Width = 150, Height = 21,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            p.Controls.Add(_watchCamCombo);
            var refreshBtn = new Button { Location = new Point(204, ly - 1), Width = 52, Height = 23, Text = "Find" };
            refreshBtn.Click += delegate { RefreshCameraList(); };
            p.Controls.Add(refreshBtn);
            _watchPreviewBtn = new Button { Location = new Point(262, ly - 1), Width = 58, Height = 23, Text = "Preview" };
            _watchPreviewBtn.Click += WatchPreviewBtn_Click;
            p.Controls.Add(_watchPreviewBtn);
            ly += 28;

            _watchSnapBtn = new Button { Location = new Point(10, ly - 1), Width = 100, Height = 23, Text = "Snapshot now" };
            _watchSnapBtn.Click += WatchSnapBtn_Click;
            p.Controls.Add(_watchSnapBtn);
            p.Controls.Add(MakeLabel("Every (s):", 116, ly + 4, 60, 20));
            _watchIntervalNum = new NumericUpDown
            {
                Location = new Point(178, ly), Width = 48, Height = 20,
                Minimum = 5, Maximum = 120, Value = 20
            };
            _watchIntervalNum.ValueChanged += delegate
            {
                try
                {
                    if (_watchTimer != null)
                        _watchTimer.Interval = Math.Max(1000, (int)_watchIntervalNum.Value * 1000);
                }
                catch { }
            };
            p.Controls.Add(_watchIntervalNum);
            _watchStartBtn = new Button { Location = new Point(232, ly - 1), Width = 88, Height = 23, Text = "Start watcher" };
            _watchStartBtn.Click += WatchStartBtn_Click;
            p.Controls.Add(_watchStartBtn);
            ly += 28;

            _watchFoleyChk = new CheckBox { Location = new Point(10, ly), Width = 150, Height = 20, Text = "Foley ambience", Checked = true };
            p.Controls.Add(_watchFoleyChk);
            _watchMusicChk = new CheckBox { Location = new Point(170, ly), Width = 150, Height = 20, Text = "Background music", Checked = true };
            p.Controls.Add(_watchMusicChk);
            ly += 24;

            p.Controls.Add(MakeLabel("Foley s:", 10, ly + 4, 52, 20));
            _watchFoleySecNum = new NumericUpDown
            {
                Location = new Point(64, ly), Width = 52, Height = 20,
                Minimum = 2, Maximum = 60, Value = 10
            };
            p.Controls.Add(_watchFoleySecNum);
            p.Controls.Add(MakeLabel("Music s:", 124, ly + 4, 58, 20));
            _watchMusicSecNum = new NumericUpDown
            {
                Location = new Point(184, ly), Width = 52, Height = 20,
                Minimum = 10, Maximum = 300, Value = 45
            };
            p.Controls.Add(_watchMusicSecNum);
            p.Controls.Add(MakeLabel("Δ%:", 242, ly + 4, 30, 20));
            double deltaInit = 4.0;
            try
            {
                deltaInit = AppConfig.Instance.WatcherSceneDeltaPct;
                if (deltaInit < 0.5 || deltaInit > 20) deltaInit = 4.0;
            }
            catch { deltaInit = 4.0; }
            _watchDeltaNum = new NumericUpDown
            {
                Location = new Point(272, ly), Width = 48, Height = 20,
                Minimum = (decimal)0.5, Maximum = 20, Increment = (decimal)0.5,
                DecimalPlaces = 1, Value = (decimal)deltaInit
            };
            _frameStaticBelow = deltaInit / 100.0;
            _watchDeltaNum.ValueChanged += delegate
            {
                try
                {
                    double v = (double)_watchDeltaNum.Value;
                    _frameStaticBelow = v / 100.0;
                    AppConfig.Instance.WatcherSceneDeltaPct = v;
                    AppConfig.Instance.Save();
                }
                catch { }
            };
            p.Controls.Add(_watchDeltaNum);
            ly += 24;

            _watchBounceChk = new CheckBox { Location = new Point(10, ly), Width = 150, Height = 20, Text = "Bounce vocals", Checked = true };
            try { _watchBounceChk.Checked = AppConfig.Instance.WatcherBounceVocals; }
            catch { }
            _watchBounceChk.CheckedChanged += delegate
            {
                try { AppConfig.Instance.WatcherBounceVocals = _watchBounceChk.Checked; AppConfig.Instance.Save(); }
                catch { }
            };
            p.Controls.Add(_watchBounceChk);
            _watchClapChk = new CheckBox { Location = new Point(170, ly), Width = 150, Height = 20, Text = "CLAP gate", Checked = true };
            try { _watchClapChk.Checked = AppConfig.Instance.WatcherClapGate; }
            catch { }
            _watchClapChk.CheckedChanged += delegate
            {
                try { AppConfig.Instance.WatcherClapGate = _watchClapChk.Checked; AppConfig.Instance.Save(); }
                catch { }
            };
            p.Controls.Add(_watchClapChk);
            ly += 24;

            p.Controls.Add(MakeLabel("Ask for foley sounds:", 10, ly, 320, 16));
            ly += 18;
            _watchFoleyAskBox = new TextBox
            {
                Location = new Point(10, ly), Width = 320, Height = 40,
                Multiline = true, ScrollBars = ScrollBars.Vertical,
                Text = AppConfig.Instance.WatcherFoleyPrompt
            };
            _watchFoleyAskBox.Leave += delegate
            {
                try
                {
                    string t = _watchFoleyAskBox.Text.Trim();
                    if (t.Length > 0)
                    {
                        AppConfig.Instance.WatcherFoleyPrompt = _watchFoleyAskBox.Text;
                        AppConfig.Instance.Save();
                    }
                }
                catch { }
            };
            p.Controls.Add(_watchFoleyAskBox);
            ly += 46;
            p.Controls.Add(MakeLabel("Ask for background music:", 10, ly, 320, 16));
            ly += 18;
            _watchMusicAskBox = new TextBox
            {
                Location = new Point(10, ly), Width = 320, Height = 40,
                Multiline = true, ScrollBars = ScrollBars.Vertical,
                Text = AppConfig.Instance.WatcherMusicPrompt
            };
            _watchMusicAskBox.Leave += delegate
            {
                try
                {
                    string t = _watchMusicAskBox.Text.Trim();
                    if (t.Length > 0)
                    {
                        AppConfig.Instance.WatcherMusicPrompt = _watchMusicAskBox.Text;
                        AppConfig.Instance.Save();
                    }
                }
                catch { }
            };
            p.Controls.Add(_watchMusicAskBox);
            try
            {
                var dtip = new ToolTip { ShowAlways = true };
                dtip.SetToolTip(_watchFoleyAskBox, "Foley question sent to the vision model with every frame. Saved automatically.");
                dtip.SetToolTip(_watchMusicAskBox, "Music question sent to the vision model with every frame. Saved automatically.");
            }
            catch { }
            ly += 46;

            _watchSceneList = new ListBox { Location = new Point(10, ly), Width = 320, Height = 40 };
            p.Controls.Add(_watchSceneList);
            ly += 44;

            _watchOnAirBox = new GroupBox
            {
                Location = new Point(10, ly), Width = 320, Height = 104,
                Text = "ON AIR (live layers)"
            };
            _watchMusicAirLabel = MakeLabel("Music: -", 8, 18, 304, 16);
            _watchOnAirBox.Controls.Add(_watchMusicAirLabel);
            _watchFoleyAirLabel = MakeLabel("Foley: -", 8, 36, 304, 16);
            _watchOnAirBox.Controls.Add(_watchFoleyAirLabel);
            var musicNext = new Label
            {
                Location = new Point(8, 54), Width = 304, Height = 14,
                Text = "yellow line = layer playhead", ForeColor = Color.Gray
            };
            _watchOnAirBox.Controls.Add(musicNext);
            var airTip = new ToolTip { ShowAlways = true };
            try
            {
                airTip.SetToolTip(_watchOnAirBox, "Independent layers: the next finished song starts the instant its layer ends. One shows music, the other foley -- never the selection.");
            }
            catch { }
            var musicBox = new WaveMiniView { Location = new Point(8, 68), Width = 148, Height = 30 };
            musicBox.Tag = "music";
            musicBox.ParentForm = this;
            _watchOnAirBox.Controls.Add(musicBox);
            _watchMusicView = musicBox;
            var foleyBox = new WaveMiniView { Location = new Point(164, 68), Width = 148, Height = 30 };
            foleyBox.Tag = "foley";
            foleyBox.ParentForm = this;
            _watchOnAirBox.Controls.Add(foleyBox);
            _watchFoleyView = foleyBox;
            p.Controls.Add(_watchOnAirBox);
            ly += 108;

            _watchStatusLabel = MakeLabel("Watcher idle.", 10, ly, 320, 30);
            p.Controls.Add(_watchStatusLabel);
            ly += 34;

            _watchTimelineBox = new GroupBox
            {
                Location = new Point(10, ly), Width = 320, Height = 196,
                Text = "TIMELINE - DJ lineup (every play)"
            };
            _watchTimelineLane = new WatchTimelineCards { ParentForm = this };
            _watchTimelineLane.Location = new Point(8, 18);
            _watchTimelineLane.Width = 304;
            _watchTimelineLane.Height = 118;
            _watchTimelineBox.Controls.Add(_watchTimelineLane);
            _watchTimelineDetail = new TextBox
            {
                Location = new Point(8, 140), Width = 150, Height = 48,
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical
            };
            _watchTimelineBox.Controls.Add(_watchTimelineDetail);
            var describeBtn = new Button { Location = new Point(162, 140), Width = 64, Height = 48, Text = "Describe" };
            describeBtn.Click += WatchDescribeBtn_Click;
            try
            {
                var dtip = new ToolTip { ShowAlways = true };
                dtip.SetToolTip(describeBtn, "Caption the selected timeline card: what the clip actually contains. Saved back to the library.");
            }
            catch { }
            _watchTimelineBox.Controls.Add(describeBtn);
            var clearBtn = new Button { Location = new Point(230, 140), Width = 66, Height = 48, Text = "Clear" };
            clearBtn.Click += delegate
            {
                try
                {
                    lock (_watchTimelineLock) { _watchTimeline.Clear(); }
                    _watchTimelineSelected = null;
                    SafeInvoke(() =>
                    {
                        try
                        {
                            _watchTimelineDetail.Text = string.Empty;
                            if (_watchTimelineLane != null && !_watchTimelineLane.IsDisposed)
                                _watchTimelineLane.Invalidate();
                        }
                        catch { }
                    });
                }
                catch { }
            };
            _watchTimelineBox.Controls.Add(clearBtn);
            p.Controls.Add(_watchTimelineBox);
            if (_watchTimelineTimer == null)
            {
                _watchTimelineTimer = new System.Windows.Forms.Timer { Interval = 400 };
                _watchTimelineTimer.Tick += delegate
                {
                    try
                    {
                        if (modeWatcherPanel == null || !modeWatcherPanel.Visible) return;
                        if (_watchTimelineLane != null && !_watchTimelineLane.IsDisposed)
                            _watchTimelineLane.Invalidate();
                        if (_watchMusicView != null && !_watchMusicView.IsDisposed)
                            _watchMusicView.Invalidate();
                        if (_watchFoleyView != null && !_watchFoleyView.IsDisposed)
                            _watchFoleyView.Invalidate();
                    }
                    catch { }
                };
                _watchTimelineTimer.Start();
            }
            if (_watchSchedTimer == null)
            {
                _watchSchedTimer = new System.Windows.Forms.Timer { Interval = 1000 };
                _watchSchedTimer.Tick += WatchSched_Tick;
                _watchSchedTimer.Start();
            }

            try
            {
                var tip = new ToolTip { ShowAlways = true };
                tip.SetToolTip(_watchBounceChk, "Music that asked for no vocals gets transcribed; vocals heard = one hardened retry, air the cleaner take.");
                tip.SetToolTip(_watchClapChk, "Clips scoring under the CLAP match floor get one retry; the better-matching take airs.");
                tip.SetToolTip(_watchStartBtn, "Sense every N sec (camera + vision describe, logged). A 1s scheduler prefetches foley/music BEFORE the current clip ends using the freshest scene -- queueing never waits for the next sense tick.");
                tip.SetToolTip(_watchSnapBtn, "Run one sense cycle right now (uses the same Foley/Music settings).");
                tip.SetToolTip(_watchIntervalNum, "Sense cadence: how often the camera is described (sec). Foley/music use their own lengths below.");
                tip.SetToolTip(_watchFoleySecNum, "Foley clip length AND re-render period (sec). Fresh scene prompt each cycle.");
                tip.SetToolTip(_watchMusicSecNum, "Music clip length (sec). Next starts early for overlap; on-air holds 15s minimum.");
                tip.SetToolTip(_watchDeltaNum, "Scene-change threshold: frames differing by LESS than this % count as static (vision skipped, clips loop). Lower = re-renders on smaller changes.");
                tip.SetToolTip(_watchTimelineLane, "Newest on top. Click a card for the full prompt + scene; double-click to load it into Clips.");
            }
            catch { }
            try
            {
                Task.Run(() =>
                {
                    try { System.Threading.Thread.Sleep(1500); }
                    catch { }
                    SafeInvoke(() => RefreshCameraList());
                });
            }
            catch { }
            return p;
        }

        #region On-air display

        private class WaveMiniView : Panel
        {
            public string Tag;
            public AudioEditorForm ParentForm;

            public WaveMiniView()
            {
                DoubleBuffered = true;
                BackColor = Color.FromArgb(10, 10, 16);
                BorderStyle = BorderStyle.FixedSingle;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                try
                {
                    var form = ParentForm;
                    if (form == null) return;
                    WaveData w;
                    string name;
                    DateTime started;
                    AudioPlayer layer;
                    if (Tag == "music")
                    {
                        w = form._watchMusicAir;
                        name = form._watchMusicAirName;
                        started = form._watchMusicStarted;
                        layer = form._musicPlayer;
                    }
                    else
                    {
                        w = form._watchFoleyAir;
                        name = form._watchFoleyAirName;
                        started = form._watchFoleyStarted;
                        layer = form._foleyPlayer;
                    }
                    var g = e.Graphics;
                    int wid = Math.Max(1, ClientSize.Width), h = Math.Max(1, ClientSize.Height);
                    if (w == null || w.FrameCount == 0)
                    {
                        using (var f = new Font(FontFamily.GenericSansSerif, 8))
                        using (var br = new SolidBrush(Color.Gray))
                        {
                            var sf = new StringFormat { Alignment = StringAlignment.Center };
                            g.DrawString(Tag == "music" ? "music layer idle" : "foley layer idle",
                                f, br, new RectangleF(0, 0, wid, h), sf);
                        }
                        return;
                    }
                    var peaks = w.GetPeaks(Math.Max(64, wid));
                    int mid = h / 2;
                    float amp = mid - 2;
                    using (var pen = new Pen(Tag == "music" ? Color.Cyan : Color.Orange))
                    {
                        for (int x = 0; x < wid; x++)
                        {
                            int b = (int)((long)x * peaks.Length / wid);
                            if (b >= peaks.Length) b = peaks.Length - 1;
                            float p = peaks[b];
                            if (p < 0) p = 0;
                            if (p > 1) p = 1;
                            float dh = Math.Max(1, p * amp);
                            if (dh >= 1) g.DrawLine(pen, x, mid - dh, x, mid + dh);
                        }
                    }
                    double elapsed = (DateTime.Now - started).TotalSeconds;
                    double frac = w.DurationSec > 0 ? elapsed / w.DurationSec : -1;
                    if (frac >= 0 && frac <= 1.05 && layer != null && layer.IsPlaying)
                    {
                        int x = (int)(frac * wid);
                        using (var pen = new Pen(Color.Yellow, 1.5f))
                            g.DrawLine(pen, x, 0, x, h);
                    }
                    using (var f = new Font(FontFamily.GenericSansSerif, 7))
                    using (var br = new SolidBrush(Color.LightGray))
                        g.DrawString(name, f, br, new PointF(2, 1));
                }
                catch { }
            }
        }

        private void NoteOnAir(bool foley, string name, WaveData wave)
        {
            try
            {
                if (foley)
                {
                    _watchFoleyAir = wave;
                    _watchFoleyAirName = name;
                    _watchFoleyStarted = DateTime.Now;
                    if (_watchFoleyAirLabel != null)
                        _watchFoleyAirLabel.Text = "Foley: " + name +
                            (wave != null ? " (" + wave.DurationSec.ToString("0.0") + "s)" : string.Empty);
                }
                else
                {
                    _watchMusicAir = wave;
                    _watchMusicAirName = name;
                    _watchMusicStarted = DateTime.Now;
                    if (_watchMusicAirLabel != null)
                        _watchMusicAirLabel.Text = "Music: " + name +
                            (wave != null ? " (" + wave.DurationSec.ToString("0.0") + "s)" : string.Empty);
                }
            }
            catch { }
        }

        private void WatchDescribeBtn_Click(object sender, EventArgs e)
        {
            try
            {
                var entry = _watchTimelineSelected;
                if (entry == null || string.IsNullOrEmpty(entry.AudioPath) || !File.Exists(entry.AudioPath))
                {
                    Log("Describe: select a timeline card with audio first.");
                    return;
                }
                string path = entry.AudioPath;
                Log("Describing " + Path.GetFileName(path) + "...");
                Task.Run(async () =>
                {
                    try
                    {
                        using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)))
                        {
                            var cap = await AudioGates.CaptionAsync(path, cts.Token, m => Log(m));
                            if (!cap.Ok)
                            {
                                Log("Describe failed (gates unavailable).");
                                return;
                            }
                            entry.Caption = cap.Text;
                            try { AudioLibrary.SetCaptionByPath(path, cap.Text); }
                            catch { }
                            SafeInvoke(() =>
                            {
                                try
                                {
                                    ShowTimelineDetail(entry);
                                    if (_watchTimelineLane != null && !_watchTimelineLane.IsDisposed)
                                        _watchTimelineLane.Invalidate();
                                }
                                catch { }
                            });
                            Log("Heard: \"" + Shorten(cap.Text, 160) + "\"");
                        }
                    }
                    catch (Exception ex) { Log("Describe failed: " + ex.Message); }
                });
            }
            catch (Exception ex) { Log("Describe failed: " + ex.Message); }
        }

        private void ShowTimelineDetail(WatchTimelineEntry entry)
        {
            try
            {
                if (_watchTimelineDetail == null || _watchTimelineDetail.IsDisposed) return;
                if (entry == null) { _watchTimelineDetail.Text = string.Empty; return; }
                string layer = entry.Foley ? "FOLEY" : "MUSIC";
                _watchTimelineDetail.Text =
                    entry.At.ToString("HH:mm:ss") + " " + layer + " " + entry.ClipName +
                    "\r\nSeed " + entry.Seed + ", clip " + entry.DurationSec.ToString("0.0") + "s" +
                    (entry.End.HasValue
                        ? ", on air " + (entry.End.Value - entry.At).TotalSeconds.ToString("0") + "s"
                        : " (on air)") +
                    "\r\nPROMPT: " + entry.Prompt +
                    (string.IsNullOrEmpty(entry.Scene) ? string.Empty : "\r\nSCENE: " + entry.Scene) +
                    (string.IsNullOrEmpty(entry.Caption) ? string.Empty : "\r\nHEARD: " + entry.Caption);
            }
            catch { }
        }

        /// <summary>
        /// Record a play in the DJ timeline and close out the previous
        /// same-layer entry (its End = this entry's start). Newest on top.
        /// </summary>
        private void NoteTimeline(bool foley, string clipName, string prompt,
            string scene, long seed, double durationSec, string audioPath,
            string caption)
        {
            WatchTimelineEntry entry = null;
            try
            {
                entry = new WatchTimelineEntry
                {
                    At = DateTime.Now,
                    Foley = foley,
                    ClipName = string.IsNullOrEmpty(clipName) ? "-" : clipName,
                    Prompt = prompt ?? string.Empty,
                    Scene = scene ?? string.Empty,
                    Seed = seed,
                    DurationSec = durationSec,
                    AudioPath = audioPath ?? string.Empty,
                    Caption = caption ?? string.Empty
                };
                lock (_watchTimelineLock)
                {
                    for (int i = 0; i < _watchTimeline.Count; i++)
                    {
                        var prev = _watchTimeline[i];
                        if (prev != null && prev.Foley == foley && !prev.End.HasValue)
                        {
                            prev.End = entry.At;
                            break;
                        }
                    }
                    _watchTimeline.Insert(0, entry);
                    while (_watchTimeline.Count > 200)
                        _watchTimeline.RemoveAt(_watchTimeline.Count - 1);
                }
                _watchTimelineSelected = entry;
            }
            catch { return; }
            SafeInvoke(() =>
            {
                try
                {
                    ShowTimelineDetail(entry);
                    if (_watchTimelineLane != null && !_watchTimelineLane.IsDisposed)
                    {
                        _watchTimelineLane.ScrollToTop();
                        _watchTimelineLane.Invalidate();
                    }
                }
                catch { }
            });
        }

        /// <summary>
        /// DJ lineup deck: vertical cards, newest on top, live progress on
        /// the on-air cards. Single owner-drawn control (no child spam) so
        /// it stays smooth at 400ms repaints.
        /// </summary>
        private class WatchTimelineCards : Panel
        {
            public AudioEditorForm ParentForm;
            private readonly ToolTip _tip = new ToolTip { ShowAlways = true };
            private WatchTimelineEntry _hover;

            public WatchTimelineCards()
            {
                DoubleBuffered = true;
                AutoScroll = true;
                BackColor = Color.FromArgb(8, 8, 14);
                BorderStyle = BorderStyle.FixedSingle;
                try { _tip.InitialDelay = 400; _tip.ReshowDelay = 200; }
                catch { }
                MouseClick += Cards_MouseClick;
                MouseDoubleClick += Cards_MouseDoubleClick;
                MouseMove += Cards_MouseMove;
                MouseLeave += delegate { _hover = null; };
            }

            public void ScrollToTop()
            {
                try { AutoScrollPosition = new Point(0, 0); }
                catch { }
            }

            private List<WatchTimelineEntry> Snapshot()
            {
                try
                {
                    var form = ParentForm;
                    if (form == null) return new List<WatchTimelineEntry>();
                    lock (form._watchTimelineLock)
                        return new List<WatchTimelineEntry>(form._watchTimeline);
                }
                catch { return new List<WatchTimelineEntry>(); }
            }

            private Rectangle CardRect(int index)
            {
                int w = Math.Max(60, ClientSize.Width - 12);
                int h = 56;
                int y = 6 + index * (h + 6) - AutoScrollPosition.Y;
                return new Rectangle(6, y, w, h);
            }

            private WatchTimelineEntry HitTest(Point pt)
            {
                try
                {
                    var items = Snapshot();
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (CardRect(i).Contains(pt)) return items[i];
                    }
                }
                catch { }
                return null;
            }

            private void Cards_MouseMove(object sender, MouseEventArgs e)
            {
                try
                {
                    var hit = HitTest(e.Location);
                    if (hit != _hover)
                    {
                        _hover = hit;
                        if (hit != null)
                        {
                            string layer = hit.Foley ? "FOLEY" : "MUSIC";
                            _tip.SetToolTip(this, layer + " " + hit.ClipName + "\r\n" + hit.Prompt);
                        }
                    }
                }
                catch { }
            }

            private void Cards_MouseClick(object sender, MouseEventArgs e)
            {
                try
                {
                    var hit = HitTest(e.Location);
                    var form = ParentForm;
                    if (form == null) return;
                    form._watchTimelineSelected = hit;
                    form.SafeInvoke(() =>
                    {
                        try
                        {
                            form.ShowTimelineDetail(hit);
                            Invalidate();
                        }
                        catch { }
                    });
                }
                catch { }
            }

            private void Cards_MouseDoubleClick(object sender, MouseEventArgs e)
            {
                try
                {
                    var hit = HitTest(e.Location);
                    var form = ParentForm;
                    if (hit == null || form == null) return;
                    if (string.IsNullOrEmpty(hit.AudioPath) || !File.Exists(hit.AudioPath)) return;
                    form.SafeInvoke(() =>
                    {
                        try { form.AddClipFile(hit.AudioPath, true); }
                        catch { }
                    });
                }
                catch { }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                try
                {
                    var g = e.Graphics;
                    var items = Snapshot();
                    var form = ParentForm;
                    WatchTimelineEntry selected = form != null ? form._watchTimelineSelected : null;
                    if (items.Count == 0)
                    {
                        using (var f = new Font(FontFamily.GenericSansSerif, 8))
                        using (var br = new SolidBrush(Color.Gray))
                        {
                            var sf = new StringFormat { Alignment = StringAlignment.Center };
                            g.DrawString("plays land here — start the watcher",
                                f, br, new RectangleF(0, 0, ClientSize.Width,
                                    Math.Max(40, ClientSize.Height)), sf);
                        }
                        return;
                    }
                    for (int i = 0; i < items.Count; i++)
                    {
                        var en = items[i];
                        if (en == null) continue;
                        var rc = CardRect(i);
                        if (rc.Bottom < 0 || rc.Top > ClientSize.Height) continue;
                        bool onAir = !en.End.HasValue;
                        bool isSel = selected == en;
                        Color accent = en.Foley ? Color.Orange : Color.Cyan;
                        Color bg = onAir ? Color.FromArgb(22, 22, 32) : Color.FromArgb(13, 13, 20);
                        Color edge = isSel ? Color.White : onAir ? accent : Color.FromArgb(60, 60, 75);
                        using (var b = new SolidBrush(bg))
                            g.FillRectangle(b, rc);
                        using (var b = new SolidBrush(accent))
                            g.FillRectangle(b, new Rectangle(rc.Left, rc.Top, 4, rc.Height));
                        using (var pen = new Pen(edge, isSel || onAir ? 1.6f : 1f))
                            g.DrawRectangle(pen, rc);
                        string layer = en.Foley ? "FOLEY" : "MUSIC";
                        string header = string.Format("{0:HH:mm:ss}  {1}  {2:0}s  seed {3}",
                            en.At, layer, en.DurationSec, en.Seed);
                        using (var f = new Font(FontFamily.GenericSansSerif, 7, FontStyle.Bold))
                        using (var br = new SolidBrush(accent))
                            g.DrawString(header, f, br, new PointF(rc.Left + 8, rc.Top + 3));
                        if (onAir)
                        {
                            int dot = 6 + (int)((DateTime.Now.Millisecond / 400) % 2) * 2;
                            using (var b = new SolidBrush(Color.Lime))
                                g.FillEllipse(b, rc.Right - 14, rc.Top + 5, dot, dot);
                            using (var f = new Font(FontFamily.GenericSansSerif, 6.5f))
                            using (var br = new SolidBrush(Color.Lime))
                                g.DrawString("ON AIR", f, br, new PointF(rc.Right - 52, rc.Top + 4));
                        }
                        else if (en.End.HasValue)
                        {
                            double played = (en.End.Value - en.At).TotalSeconds;
                            using (var f = new Font(FontFamily.GenericSansSerif, 6.5f))
                            using (var br = new SolidBrush(Color.Gray))
                                g.DrawString("played " + played.ToString("0") + "s", f, br,
                                    new PointF(rc.Right - 62, rc.Top + 4));
                        }
                        using (var f = new Font(FontFamily.GenericSansSerif, 7.5f, FontStyle.Bold))
                        using (var br = new SolidBrush(Color.WhiteSmoke))
                        {
                            var rf = new RectangleF(rc.Left + 8, rc.Top + 17, rc.Width - 16, 14);
                            g.DrawString(TrimTo(g, en.ClipName ?? "-", f, rf.Width), f, br, rf);
                        }
                        using (var f = new Font(FontFamily.GenericSansSerif, 7))
                        using (var br = new SolidBrush(Color.LightGray))
                        {
                            var rf = new RectangleF(rc.Left + 8, rc.Top + 31, rc.Width - 16, 14);
                            g.DrawString(TrimTo(g, en.Prompt ?? string.Empty, f, rf.Width), f, br, rf);
                        }
                        // Progress: on-air fills with elapsed; done shows
                        // played-vs-clip (truncated = dim partial).
                        double frac = 0;
                        if (onAir)
                        {
                            double el = (DateTime.Now - en.At).TotalSeconds;
                            if (en.DurationSec > 0) frac = el / en.DurationSec;
                        }
                        else if (en.End.HasValue && en.DurationSec > 0)
                        {
                            frac = (en.End.Value - en.At).TotalSeconds / en.DurationSec;
                        }
                        if (frac < 0) frac = 0;
                        if (frac > 1) frac = 1;
                        int barW = rc.Width - 16;
                        int barX = rc.Left + 8;
                        int barY = rc.Bottom - 7;
                        using (var b = new SolidBrush(Color.FromArgb(40, 40, 55)))
                            g.FillRectangle(b, new Rectangle(barX, barY, barW, 3));
                        if (frac > 0)
                        {
                            using (var b = new SolidBrush(onAir ? accent : Color.FromArgb(90, 90, 110)))
                                g.FillRectangle(b, new Rectangle(barX, barY, (int)(barW * frac), 3));
                        }
                    }
                }
                catch { }
            }

            private static string TrimTo(System.Drawing.Graphics g, string s, Font f, float maxW)
            {
                try
                {
                    if (string.IsNullOrEmpty(s)) return string.Empty;
                    s = s.Replace("\r", " ").Replace("\n", " ").Trim();
                    if (g.MeasureString(s, f).Width <= maxW) return s;
                    while (s.Length > 4 && g.MeasureString(s + "...", f).Width > maxW)
                        s = s.Substring(0, s.Length - 1);
                    return s + "...";
                }
                catch { return s ?? string.Empty; }
            }
        }

        #endregion

        #region Shared audio-result waiter (manual runs + watcher)

        /// <summary>
        /// Poll queue + history until an audio file appears. Shared by the
        /// manual Generate flow and the watcher cycles. Throws on server
        /// errors; returns null only when cancelled.
        /// </summary>
        private async Task<ComfyUIClient.OutputImage> WaitAudioResult(
            string promptId, string kind, CancellationToken token)
        {
            string[] exts = AudioWorkflow.OutputExtensions();
            var start = DateTime.Now;
            DateTime finishingSince = DateTime.MaxValue;
            int unseenStreak = 0;
            string lastState = string.Empty;
            while (!token.IsCancellationRequested)
            {
                var queue = await _comfy.GetQueue();
                string hist = await _comfy.GetHistoryJson(promptId);
                var hstat = ComfyUIClient.ParseHistoryEntry(hist, promptId);
                bool hasEntry = hstat.HasEntry;

                var files = ComfyUIClient.ScanHistoryForFiles(hist, exts);
                ComfyUIClient.OutputImage wav = null;
                foreach (var f in files)
                {
                    if (f.Filename.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) { wav = f; break; }
                }
                if (wav != null) return wav;
                if (files.Count > 0) return files[0];

                // Terminal server failure: fail FAST (a run that errored
                // will never produce files; spinning in Finishing held the
                // foley layer hostage for minutes with zero retries).
                if (hasEntry && !string.IsNullOrEmpty(hstat.Error))
                    throw new Exception("ComfyUI execution failed" +
                        (!string.IsNullOrEmpty(hstat.ErrorNode) ? " at " + hstat.ErrorNode : "") +
                        ": " + hstat.Error);
                if (hasEntry && string.Equals(hstat.StatusStr, "error", StringComparison.OrdinalIgnoreCase))
                    throw new Exception("ComfyUI execution failed" +
                        (!string.IsNullOrEmpty(hstat.ErrorNode) ? " at " + hstat.ErrorNode : "") +
                        " (no output produced). Check the saver nodes in the workflow.");

                bool inRunning = queue.Running.Contains(promptId);
                int pendIdx = queue.Pending.IndexOf(promptId);
                bool inQueue = inRunning || pendIdx >= 0;
                var elapsed = DateTime.Now - start;
                if (hasEntry || inQueue)
                {
                    unseenStreak = 0;
                    string state;
                    if (inRunning) state = "Running";
                    else if (pendIdx >= 0)
                        state = "Queued (#" + (pendIdx + 1) + " of " + queue.Pending.Count + ")";
                    else state = "Finishing";
                    if (state == "Finishing" && finishingSince == DateTime.MaxValue)
                        finishingSince = DateTime.Now;
                    if (state != "Finishing") finishingSince = DateTime.MaxValue;
                    if (state != lastState)
                    {
                        Log(kind + ": " + state + "." +
                            (!string.IsNullOrEmpty(hstat.StatusStr) ? " (" + hstat.StatusStr + ")" : string.Empty));
                        lastState = state;
                    }
                    SetStatus(string.Format("{0}... {1:mm\\:ss} elapsed", state, elapsed), HolographicTheme.BlueAccent);
                    if (state == "Finishing" && (DateTime.Now - finishingSince).TotalMinutes > 3)
                        throw new Exception("Run finished without producing an audio file (outputs from: " +
                            (hstat.OutputNodeIds.Count > 0 ? string.Join(",", hstat.OutputNodeIds.ToArray()) : "none") +
                            "). Check the saver nodes in the workflow.");
                }
                else
                {
                    unseenStreak++;
                    finishingSince = DateTime.MaxValue;
                    SetStatus(string.Format("Waiting for server... {0:mm\\:ss}", elapsed), HolographicTheme.BlueAccent);
                    if (elapsed.TotalSeconds > 20 && unseenStreak >= 3)
                        throw new Exception("Prompt " + promptId + " vanished from the server queue and history. " +
                            "The server restarted, or the run was interrupted (Cancel/Interrupt here or from another client).");
                }
                if (elapsed.TotalMinutes > 20) throw new Exception("Timed out waiting for audio after 20 min.");
                await Task.Delay(2000, token);
            }
            token.ThrowIfCancellationRequested();
            return null;
        }

        private string AudioSaveDir()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "MeesaMultisMaker", "Audio");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private string UniqueAudioPath(string dir, string serverFilename)
        {
            string localPath = Path.Combine(dir, serverFilename);
            if (File.Exists(localPath))
                localPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(serverFilename) +
                    "_" + DateTime.Now.ToString("HHmmss") + Path.GetExtension(serverFilename));
            return localPath;
        }

        #endregion

        #region Watcher pipeline

        private string SelectedWatchCamera()
        {
            try
            {
                if (_watchCamCombo != null && _watchCamCombo.SelectedItem != null)
                    return _watchCamCombo.SelectedItem.ToString();
                if (_watchCamCombo != null && _watchCamCombo.Items.Count > 0)
                    return _watchCamCombo.Items[0].ToString();
            }
            catch { }
            return null;
        }

        private void RefreshCameraList()
        {
            try
            {
                SetWatchStatus("Looking for cameras...");
                Task.Run(() =>
                {
                    List<string> names = null;
                    string err = null;
                    try { names = CameraCapture.ListVideoDevices(); }
                    catch (Exception ex) { err = ex.Message; }
                    SafeInvoke(() =>
                    {
                        try
                        {
                            if (_watchCamCombo == null) return;
                            string keep = SelectedWatchCamera();
                            _watchCamCombo.BeginUpdate();
                            _watchCamCombo.Items.Clear();
                            if (names != null)
                            {
                                foreach (string n in names) _watchCamCombo.Items.Add(n);
                            }
                            if (_watchCamCombo.Items.Count > 0)
                            {
                                int back = !string.IsNullOrEmpty(keep)
                                    ? _watchCamCombo.Items.IndexOf(keep) : 0;
                                _watchCamCombo.SelectedIndex = Math.Max(0, back);
                                SetWatchStatus(_watchCamCombo.Items.Count + " camera(s) found.");
                            }
                            else
                            {
                                SetWatchStatus("No cameras found" +
                                    (string.IsNullOrEmpty(err) ? "." : ": " + err));
                            }
                        }
                        catch { }
                        finally
                        {
                            try { _watchCamCombo.EndUpdate(); }
                            catch { }
                        }
                    });
                });
            }
            catch { }
        }

        private void WatchPreviewBtn_Click(object sender, EventArgs e)
        {
            try
            {
                _watchPreviewOn = !_watchPreviewOn;
                if (_watchPreviewBtn != null)
                    _watchPreviewBtn.Text = _watchPreviewOn ? "No preview" : "Preview";
                if (_watchPreviewOn && (_watchCamCombo == null || _watchCamCombo.Items.Count == 0))
                    RefreshCameraList();
                if (_watchPreviewOn && _watchPreviewTimer == null)
                {
                    _watchPreviewTimer = new System.Windows.Forms.Timer { Interval = 1000 };
                    _watchPreviewTimer.Tick += WatchPreviewTimer_Tick;
                }
                if (_watchPreviewTimer != null)
                    _watchPreviewTimer.Enabled = _watchPreviewOn;
                if (!_watchPreviewOn && _watchPreviewBox != null)
                {
                    try
                    {
                        var old = _watchPreviewBox.Image as Bitmap;
                        _watchPreviewBox.Image = null;
                        if (old != null) old.Dispose();
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void WatchPreviewTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                if (!_watchPreviewOn || _watchPreviewBusy) return;
                string dev = SelectedWatchCamera();
                if (string.IsNullOrEmpty(dev)) return;
                _watchPreviewBusy = true;
                Task.Run(() =>
                {
                    try
                    {
                        byte[] jpg = CameraCapture.GrabFrameJpeg(dev, 480);
                        SafeInvoke(() =>
                        {
                            try
                            {
                                if (_watchPreviewBox == null || _watchPreviewBox.IsDisposed) return;
                                Bitmap next = null;
                                try
                                {
                                    using (var ms = new MemoryStream(jpg, false))
                                    using (var tmp = new Bitmap(ms))
                                        next = new Bitmap(tmp);
                                }
                                catch { return; }
                                var old = _watchPreviewBox.Image as Bitmap;
                                _watchPreviewBox.Image = next;
                                if (old != null) old.Dispose();
                            }
                            catch { }
                        });
                    }
                    catch (Exception ex)
                    {
                        Log("Preview frame failed: " + ex.Message);
                        SafeInvoke(() =>
                        {
                            _watchPreviewOn = false;
                            if (_watchPreviewTimer != null) _watchPreviewTimer.Enabled = false;
                            if (_watchPreviewBtn != null) _watchPreviewBtn.Text = "Preview";
                        });
                    }
                    finally { _watchPreviewBusy = false; }
                });
            }
            catch { _watchPreviewBusy = false; }
        }

        private void WatchSnapBtn_Click(object sender, EventArgs e)
        {
            try
            {
                // Snapshot always senses NOW (even while running) and force-
                // queues with the fresh prompt instead of waiting for dues.
                if (_watchSenseBusy)
                {
                    Log("Watcher: a sense cycle is already running (generation continues in the background).");
                    return;
                }
                if (_watchCts == null) _watchCts = new CancellationTokenSource();
                CancellationToken token;
                try { token = _watchCts.Token; }
                catch (ObjectDisposedException)
                {
                    _watchCts = new CancellationTokenSource();
                    token = _watchCts.Token;
                }
                Task.Run(() => WatchCycle(token, true));
            }
            catch (Exception ex) { Log("Watcher snapshot failed: " + ex.Message); }
        }

        private void WatchStartBtn_Click(object sender, EventArgs e)
        {
            try
            {
                if (_watchOn)
                {
                    StopWatcher("stopped by user");
                    return;
                }
                if (string.IsNullOrEmpty(SelectedWatchCamera()))
                {
                    MessageBox.Show(this, "Pick a camera first (Find).", "Watcher");
                    return;
                }
                _watchCts = new CancellationTokenSource();
                _watchOn = true;
                _watchSenseBusy = false;
                _foleyGenBusy = false;
                _musicGenBusy = false;
                _nextFoleyDue = DateTime.MinValue;
                _nextMusicDue = DateTime.MinValue;
                _foleySwapDue = DateTime.MinValue;
                _musicSwapDue = DateTime.MinValue;
                _lastFoleyRenderSec = 0.7;
                _lastMusicRenderSec = 7.0;
                _latestFoleyPrompt = null;
                _latestMusicPrompt = null;
                _latestSenseAt = DateTime.MinValue;
                _watchSceneSummary = "Starting...";
                _lastFrameHash = null;
                _foleySceneStatic = false;
                _musicSceneStatic = false;
                _lastQueuedFoleyPrompt = null;
                _lastQueuedMusicPrompt = null;
                _foleyPlayPath = string.Empty;
                _musicPlayPath = string.Empty;
                _foleyClipLen = 0;
                _musicClipLen = 0;
                _foleyFlip = false;
                _musicFlip = false;
                if (_watchTimer == null)
                {
                    _watchTimer = new System.Windows.Forms.Timer();
                    _watchTimer.Tick += WatchTimer_Tick;
                }
                _watchTimer.Interval = Math.Max(1000,
                    (_watchIntervalNum != null ? (int)_watchIntervalNum.Value : 20) * 1000);
                _watchTimer.Start();
                if (_watchStartBtn != null) _watchStartBtn.Text = "Stop watcher";
                SetWatchStatus("Watching every " + _watchTimer.Interval / 1000 + "s.");
                Log("Watcher on: sense every " + _watchTimer.Interval / 1000 +
                    "s; 1s scheduler prefetches before clips end, music holds 15s minimum.");
                try
                {
                    CancellationToken tok = _watchCts.Token;
                    Task.Run(() => WatchCycle(tok, false));
                }
                catch { }
            }
            catch (Exception ex) { MessageBox.Show(this, "Watcher failed:\r\n" + ex.Message, "Watcher"); }
        }

        private void StopWatcher(string why)
        {
            try
            {
                _watchOn = false;
                if (_watchTimer != null) _watchTimer.Stop();
                if (_watchCts != null)
                {
                    try { _watchCts.Cancel(); }
                    catch { }
                    _watchCts = null;
                }
                if (_watchStartBtn != null) _watchStartBtn.Text = "Start watcher";
                SetWatchStatus("Watcher idle (" + why + ").");
                Log("Watcher off (" + why + ").");
            }
            catch { }
        }

        private void WatchTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // Sense never blocks on generation and never queues: it
                // only publishes fresh prompts; the 1s scheduler owns ALL
                // queue timing so prefetches fire before clips end.
                if (!_watchOn || _watchSenseBusy || _watchCts == null) return;
                CancellationToken token;
                try { token = _watchCts.Token; }
                catch (ObjectDisposedException) { return; }
                Task.Run(() => WatchCycle(token, false));
            }
            catch { }
        }

        private void SetWatchStatus(string msg)
        {
            try
            {
                if (InvokeRequired) { BeginInvoke(new Action<string>(SetWatchStatus), msg); return; }
                if (_watchStatusLabel != null) _watchStatusLabel.Text = msg;
            }
            catch { }
        }

        private void AddSceneEntry(string description)
        {
            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action<string>(AddSceneEntry), description);
                    return;
                }
                if (_watchSceneList == null) return;
                string line = string.Format("[{0:HH:mm:ss}] {1}", DateTime.Now, description);
                if (line.Length > 120) line = line.Substring(0, 120) + "...";
                _watchSceneList.Items.Insert(0, line);
                while (_watchSceneList.Items.Count > 200)
                    _watchSceneList.Items.RemoveAt(_watchSceneList.Items.Count - 1);
            }
            catch { }
        }

        private bool WatchFoleyOn()
        {
            try { return _watchFoleyChk != null && _watchFoleyChk.Checked; }
            catch { return false; }
        }

        private bool WatchMusicOn()
        {
            try { return _watchMusicChk != null && _watchMusicChk.Checked; }
            catch { return false; }
        }

        private bool WatchBounceOn()
        {
            try { return _watchBounceChk != null && _watchBounceChk.Checked; }
            catch { return true; }
        }

        private bool WatchClapOn()
        {
            try { return _watchClapChk != null && _watchClapChk.Checked; }
            catch { return true; }
        }

        private async Task WatchCycle(CancellationToken token, bool forceQueue)
        {
            // Sense cycle: describes the frame, logs the scene, publishes
            // the freshest prompts. NEVER queues on cadence -- the 1s
            // scheduler owns all queue timing (prefetch before end). Only
            // a manual snapshot (forceQueue) fires renders directly.
            if (_watchSenseBusy) return;
            _watchSenseBusy = true;
            try
            {
                string dev = SelectedWatchCamera();
                if (string.IsNullOrEmpty(dev))
                {
                    SafeInvoke(() => SetWatchStatus("No camera selected."));
                    return;
                }
                SetWatchStatus("Watching: capturing frame...");
                byte[] jpg = null;
                try
                {
                    jpg = await Task.Run(() => CameraCapture.GrabFrameJpeg(dev, 512), token);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    Log("Watcher frame grab failed: " + ex.Message);
                    SafeInvoke(() => SetWatchStatus("Frame grab failed (see log)."));
                    return;
                }
                if (jpg == null || jpg.Length == 0)
                {
                    Log("Watcher frame grab returned nothing.");
                    return;
                }
                token.ThrowIfCancellationRequested();

                // Frame gate (cheap, before vision): pixel-identical scenes
                // skip both VL questions AND generation. Prompts stay
                // published; the scheduler loops on-air files at swaps.
                bool frameStatic = false;
                try
                {
                    byte[] hash = FrameHash16(jpg);
                    if (hash != null && _lastFrameHash != null)
                    {
                        double dist = SceneGate.HashDistance(_lastFrameHash, hash);
                        frameStatic = dist < _frameStaticBelow;
                        if (frameStatic)
                            Log("Watcher scene static (frame Δ" + (dist * 100).ToString("0.0") +
                                "%) -- holding clips, no vision call.");
                    }
                    if (hash != null) _lastFrameHash = hash;
                }
                catch { }
                if (frameStatic && _latestSenseAt != DateTime.MinValue)
                {
                    _latestSenseAt = DateTime.Now;
                    _foleySceneStatic = WatchFoleyOn();
                    _musicSceneStatic = WatchMusicOn();
                    _watchSceneSummary = _watchSceneSummary + " (static)";
                    SafeInvoke(() => SetWatchStatus(_watchSceneSummary));
                    return;
                }

                // Two separate vision questions, asked concurrently against
                // the same frame: foley asks for ambient SOUNDS, music asks
                // for background MUSIC. Either may be disabled.
                bool wantFoley = WatchFoleyOn();
                bool wantMusic = WatchMusicOn();
                if (!wantFoley && !wantMusic)
                {
                    Log("Watcher: both Foley and Music are off; nothing to do.");
                    return;
                }
                string foleyAsk = null, musicAsk = null;
                try
                {
                    var got = (object)Invoke(new Func<object>(() =>
                    {
                        return new string[]
                        {
                            WatchFoleyInstruction(),
                            WatchMusicInstruction()
                        };
                    }));
                    var pair = got as string[];
                    if (pair != null && pair.Length >= 2)
                    {
                        foleyAsk = pair[0];
                        musicAsk = pair[1];
                    }
                }
                catch { }
                if (string.IsNullOrWhiteSpace(foleyAsk))
                    foleyAsk = AppConfig.Instance.WatcherFoleyPrompt;
                if (string.IsNullOrWhiteSpace(musicAsk))
                    musicAsk = AppConfig.Instance.WatcherMusicPrompt;

                SetWatchStatus("Watching: asking the watcher (vision model)...");
                _comfy = new ComfyUIClient(comfyUrlTextBox.Text.Trim());
                DateTime visionStart = DateTime.Now;
                var askJobs = new List<Task<string>>();
                if (wantFoley) askJobs.Add(VisionEngine.DescribeAsync(jpg, foleyAsk, 140, token));
                else askJobs.Add(Task.FromResult<string>(null));
                if (wantMusic) askJobs.Add(VisionEngine.DescribeAsync(jpg, musicAsk, 140, token));
                else askJobs.Add(Task.FromResult<string>(null));
                string foleyPrompt = null, musicPrompt = null;
                try
                {
                    await Task.WhenAll(askJobs.ToArray());
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    Log("Watcher describe failed: " + ex.Message);
                    SafeInvoke(() => SetWatchStatus("Describe failed (see log)."));
                    return;
                }
                try
                {
                    if (wantFoley) foleyPrompt = askJobs[0].Result;
                    if (wantMusic) musicPrompt = askJobs[1].Result;
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    Log("Watcher describe failed: " + ex.Message);
                    SafeInvoke(() => SetWatchStatus("Describe failed (see log)."));
                    return;
                }
                token.ThrowIfCancellationRequested();
                foleyPrompt = wantFoley ? CleanPrompt(foleyPrompt, "quiet ambient room tone") : null;
                musicPrompt = wantMusic ? CleanPrompt(musicPrompt, "calm ambient loop") : null;
                string sceneLine = string.Empty;
                if (wantFoley) sceneLine = "SFX: " + Shorten(foleyPrompt, 100);
                if (wantMusic)
                {
                    if (sceneLine.Length > 0) sceneLine += " | ";
                    sceneLine += "MUS: " + Shorten(musicPrompt, 100);
                }
                AddSceneEntry(sceneLine);
                Log("Watcher prompts: " + sceneLine);

                try
                {
                    double vsec = (DateTime.Now - visionStart).TotalSeconds;
                    if (vsec > 0.2 && vsec < 300)
                        _lastVisionSec = _lastVisionSec <= 0
                            ? vsec : _lastVisionSec * 0.7 + vsec * 0.3;
                }
                catch { }
                // Publish for the scheduler: queueing reuses these, so the
                // vision latency is NEVER on the prefetch critical path.
                _latestSenseAt = DateTime.Now;
                if (wantFoley) _latestFoleyPrompt = foleyPrompt;
                if (wantMusic) _latestMusicPrompt = musicPrompt;
                // Prompt gate (semantic): pixels moved but meaning did not
                // (flicker, noise) -> loop instead of re-render, per layer.
                try
                {
                    _foleySceneStatic = wantFoley && !string.IsNullOrEmpty(foleyPrompt) &&
                        !string.IsNullOrEmpty(_lastQueuedFoleyPrompt) &&
                        SceneGate.PromptSimilarity(foleyPrompt, _lastQueuedFoleyPrompt) >= PromptStaticAt;
                    _musicSceneStatic = wantMusic && !string.IsNullOrEmpty(musicPrompt) &&
                        !string.IsNullOrEmpty(_lastQueuedMusicPrompt) &&
                        SceneGate.PromptSimilarity(musicPrompt, _lastQueuedMusicPrompt) >= PromptStaticAt;
                    if (_foleySceneStatic) Log("Watcher foley prompt unchanged -- will loop.");
                    if (_musicSceneStatic) Log("Watcher music prompt unchanged -- will loop.");
                }
                catch { }
                _watchSceneSummary = "Last scene: " + Shorten(foleyPrompt ?? musicPrompt ?? "-", 60);
                SafeInvoke(() => SetWatchStatus(_watchSceneSummary));
                // Manual snapshot only: fire now with the just-described
                // prompt instead of waiting for dues.
                if (forceQueue)
                {
                    if (wantFoley && !string.IsNullOrEmpty(foleyPrompt) && !_foleyGenBusy)
                    {
                        _foleyGenBusy = true;
                        _lastQueuedFoleyPrompt = foleyPrompt;
                        Log("Watcher snapshot foley start.");
                        var ignore = WatchGenerate(foleyPrompt, true, token);
                    }
                    if (wantMusic && !string.IsNullOrEmpty(musicPrompt) && !_musicGenBusy)
                    {
                        _musicGenBusy = true;
                        _lastQueuedMusicPrompt = musicPrompt;
                        Log("Watcher snapshot music start.");
                        var ignore2 = WatchGenerate(musicPrompt, false, token);
                    }
                    if ((_foleyGenBusy && wantFoley) || (_musicGenBusy && wantMusic))
                        SafeInvoke(() => SetWatchStatus(_watchSceneSummary + " | rendering snapshot..."));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log("Watcher cycle failed: " + ex.Message); }
            finally { _watchSenseBusy = false; }
        }

        private int WatchSenseIntervalSec()
        {
            try { return Math.Max(2, SafeNum(() => (int)_watchIntervalNum.Value, 20)); }
            catch { return 20; }
        }

        /// <summary>
        /// 1s prefetch scheduler: owns ALL watcher audio queueing. Fires
        /// each layer BEFORE its current clip ends (swap - render - buffer)
        /// reusing the latest sense prompt, so vision latency never delays
        /// a prefetch and slow GPUs still overlap. Sense ticks never queue.
        /// </summary>
        private void WatchSched_Tick(object sender, EventArgs e)
        {
            try
            {
                if (!_watchOn || _watchCts == null) return;
                CancellationToken token;
                try { token = _watchCts.Token; }
                catch { return; }
                if (token.IsCancellationRequested) return;
                bool wantFoley = WatchFoleyOn();
                bool wantMusic = WatchMusicOn();
                if (!wantFoley && !wantMusic) return;
                DateTime now = DateTime.Now;
                string status = _watchSceneSummary;
                if (string.IsNullOrEmpty(status)) status = "Watching...";
                // Foley prefetch with the freshest scene, or a zero-GPU
                // loop when the scene is static.
                if (wantFoley)
                {
                    if (_foleyGenBusy)
                    {
                        status += " | foley rendering...";
                    }
                    else if (string.IsNullOrEmpty(_latestFoleyPrompt) ||
                        _latestSenseAt == DateTime.MinValue)
                    {
                        status += " | foley waiting first scene...";
                    }
                    else if (_foleySceneStatic && _foleySwapDue != DateTime.MinValue &&
                        File.Exists(_foleyPlayPath) && _foleyClipLen > 0)
                    {
                        double toSwap = (_foleySwapDue - now).TotalSeconds;
                        if (toSwap > 1.0)
                        {
                            status += " | foley loops in " + toSwap.ToString("0") + "s (static)";
                            DateTime want = _foleySwapDue - TimeSpan.FromSeconds(1.0);
                            if (_nextFoleyDue > want) _nextFoleyDue = want;
                        }
                        else
                        {
                            _foleyGenBusy = true;
                            Log("Watcher foley static -- looping on-air file.");
                            var ignore = WatchReplay(true, token);
                            status += " | foley looping...";
                        }
                    }
                    else
                    {
                        double age = (now - _latestSenseAt).TotalSeconds;
                        double staleAfter = Math.Max(30.0, WatchSenseIntervalSec() * 3.0 + 10.0);
                        if (age > staleAfter)
                        {
                            status += " | foley scene stale";
                        }
                        else if (now >= _nextFoleyDue)
                        {
                            _foleyGenBusy = true;
                            string fp = _latestFoleyPrompt;
                            _lastQueuedFoleyPrompt = fp;
                            Log("Watcher foley prefetch (scene " + age.ToString("0") + "s old).");
                            var ignore = WatchGenerate(fp, true, token);
                            status += " | foley rendering...";
                        }
                        else
                        {
                            double waitF = (_nextFoleyDue - now).TotalSeconds;
                            if (waitF > 0.5) status += " | foley in " + waitF.ToString("0") + "s";
                        }
                    }
                }
                // Music prefetch with 15s minimum-play guard.
                if (wantMusic)
                {
                    if (_musicGenBusy)
                    {
                        status += " | music rendering...";
                    }
                    else if (string.IsNullOrEmpty(_latestMusicPrompt) ||
                        _latestSenseAt == DateTime.MinValue)
                    {
                        status += " | music waiting first scene...";
                    }
                    else
                    {
                        bool holding = false;
                        try
                        {
                            if (_musicPlayer != null && _musicPlayer.IsPlaying &&
                                _watchMusicStarted != DateTime.MinValue)
                            {
                                double elapsed = (now - _watchMusicStarted).TotalSeconds;
                                if (elapsed < MusicMinPlaySec)
                                {
                                    holding = true;
                                    DateTime holdUntil = _watchMusicStarted +
                                        TimeSpan.FromSeconds(MusicMinPlaySec);
                                    if (holdUntil > _nextMusicDue)
                                        _nextMusicDue = holdUntil;
                                    status += " | music holds " +
                                        (MusicMinPlaySec - elapsed).ToString("0") + "s (15s min)";
                                }
                            }
                        }
                        catch { holding = false; }
                        if (!holding)
                        {
                            if (_musicSceneStatic && _musicSwapDue != DateTime.MinValue &&
                                File.Exists(_musicPlayPath) && _musicClipLen > 0)
                            {
                                double toSwap = (_musicSwapDue - now).TotalSeconds;
                                if (toSwap > 1.0)
                                {
                                    status += " | music loops in " + toSwap.ToString("0") + "s (static)";
                                    DateTime want = _musicSwapDue - TimeSpan.FromSeconds(1.0);
                                    if (_nextMusicDue > want) _nextMusicDue = want;
                                }
                                else
                                {
                                    _musicGenBusy = true;
                                    Log("Watcher music static -- looping on-air file.");
                                    var ignore = WatchReplay(false, token);
                                    status += " | music looping...";
                                }
                            }
                            else
                            {
                                double age = (now - _latestSenseAt).TotalSeconds;
                                double staleAfter = Math.Max(30.0, WatchSenseIntervalSec() * 3.0 + 10.0);
                                if (age > staleAfter)
                                {
                                    status += " | music scene stale";
                                }
                                else if (now >= _nextMusicDue)
                                {
                                    _musicGenBusy = true;
                                    string mp = _latestMusicPrompt;
                                    _lastQueuedMusicPrompt = mp;
                                    Log("Watcher music prefetch (scene " + age.ToString("0") + "s old).");
                                    var ignore = WatchGenerate(mp, false, token);
                                    status += " | music rendering...";
                                }
                                else
                                {
                                    double waitM = (_nextMusicDue - now).TotalSeconds;
                                    if (waitM > 0.5) status += " | music in " + waitM.ToString("0") + "s";
                                }
                            }
                        }
                    }
                }
                SetWatchStatus(status);
            }
            catch { }
        }

        /// <summary>16x16 gray hash of a JPEG frame for change gating.</summary>
        private static byte[] FrameHash16(byte[] jpg)
        {
            try
            {
                using (var ms = new MemoryStream(jpg, false))
                using (var src = new Bitmap(ms))
                using (var tiny = new Bitmap(src, new Size(16, 16)))
                {
                    var h = new byte[256];
                    for (int y = 0; y < 16; y++)
                        for (int x = 0; x < 16; x++)
                        {
                            Color c = tiny.GetPixel(x, y);
                            h[y * 16 + x] = (byte)((c.R * 3 + c.G * 6 + c.B) / 10);
                        }
                    return h;
                }
            }
            catch { return null; }
        }

        private static string CleanPrompt(string raw, string fallback)
        {
            string s = (raw ?? string.Empty).Trim().Trim('"', '\'', ' ', '\t', '\r', '\n');
            if (s.Length == 0) return fallback;
            if (s.Length > 400) s = s.Substring(0, 400).Trim();
            return s.Length > 0 ? s : fallback;
        }

        private static string Shorten(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "-";
            s = s.Trim();
            return s.Length > max ? s.Substring(0, max) + "..." : s;
        }

        /// <summary>One ComfyUI render plus all local mastering (decode,
        /// trim, loudness match, loop-prep). Null on cancel/failure.</summary>
        private class WatchTake
        {
            public string Prompt = string.Empty;
            public string Negative = string.Empty;
            public long Seed;
            public string Workflow = string.Empty;
            public double RequestSec;
            public string LocalPath = string.Empty;
            public string PlayPath = string.Empty;
            public string AddPath = string.Empty;
            public WaveData Wave;
            public int Words = -1;
            public double Clap;
            public bool ClapOk;
        }

        private async Task<WatchTake> WatchRenderOnce(string kind, bool foley,
            string prompt, string negative, string workflow, double duration,
            int steps, string catOverride, CancellationToken token)
        {
            try
            {
                string audioId = AudioWorkflow.NewAudioId(kind);
                var built = AudioWorkflow.BuildTextToAudio(workflow, prompt,
                    duration, -1, steps, false, catOverride, negative);
                if (_comfy == null) _comfy = new ComfyUIClient(comfyUrlTextBox.Text.Trim());
                string promptId = await _comfy.QueuePrompt(built.PromptJson, Guid.NewGuid().ToString());
                if (string.IsNullOrEmpty(promptId)) throw new Exception("Server did not return a prompt_id.");
                Log("Watcher queued " + kind + " (" + promptId + ").");
                var found = await WaitAudioResult(promptId, kind, token);
                if (found == null) return null; // cancelled
                byte[] bytes = await _comfy.DownloadFile(found);
                if (bytes == null || bytes.Length == 0) throw new Exception("Downloaded audio is empty.");
                string dir = AudioSaveDir();
                string ext = ".wav";
                try
                {
                    string serverExt = Path.GetExtension(found.Filename);
                    if (!string.IsNullOrEmpty(serverExt)) ext = serverExt.ToLowerInvariant();
                }
                catch { }
                if (ext != ".wav" && ext != ".mp3" && ext != ".flac" && ext != ".ogg" && ext != ".opus") ext = ".wav";
                string localPath = Path.Combine(dir, audioId + ext);
                if (File.Exists(localPath)) localPath = UniqueAudioPath(dir, audioId + ext);
                File.WriteAllBytes(localPath, bytes);
                try { AudioPromptLedger.Append(audioId, kind, built.Seed, duration, localPath, prompt); }
                catch { }
                string addPath = localPath;
                if (!localPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        string wavPath = GetUniquePath(Path.ChangeExtension(localPath, ".wav"));
                        AudioConvert.DecodeToFile(localPath, wavPath);
                        addPath = wavPath;
                    }
                    catch (Exception ex)
                    {
                        Log("Watcher decode skipped (" + ex.Message + ").");
                    }
                }
                string playPath = addPath;
                try
                {
                    if (playPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(playPath))
                    {
                        double cut = WaveData.TrimFileEnds(playPath);
                        if (cut > 0.05)
                            Log("Watcher " + kind + " trimmed " + cut.ToString("0.0") + "s edge silence.");
                    }
                }
                catch { }
                // Loudness discipline: match the layer LUFS target so beds
                // hold a stable mix across renders.
                try
                {
                    if (playPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(playPath))
                    {
                        string lerr;
                        double target = foley ? AudioLoudness.FoleyTargetI : AudioLoudness.MusicTargetI;
                        if (AudioLoudness.TryNormalize(playPath, target, out lerr))
                            Log(kind + " loudness matched (" + target.ToString("0") + " LUFS).");
                        else if (!string.IsNullOrEmpty(lerr))
                            Log(kind + " loudness skipped (" + lerr + ").");
                    }
                }
                catch { }
                WaveData air = null;
                try
                {
                    if (playPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) && File.Exists(playPath))
                        air = WaveData.FromFile(playPath);
                }
                catch { air = null; }
                try
                {
                    if (air != null && air.DurationSec >= 4)
                    {
                        air.CrossfadeLoop(Math.Min(2.0, air.DurationSec / 6.0));
                        air.Save(playPath);
                    }
                }
                catch (Exception ex)
                {
                    Log("Watcher " + kind + " loop-prep skipped (" + ex.Message + ").");
                }
                return new WatchTake
                {
                    Prompt = prompt,
                    Negative = negative ?? string.Empty,
                    Seed = built.Seed,
                    Workflow = workflow,
                    RequestSec = duration,
                    LocalPath = localPath,
                    PlayPath = playPath,
                    AddPath = addPath,
                    Wave = air
                };
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex)
            {
                Log("Watcher " + kind + " render failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// One watcher-side generation: the prompt parameter is ALREADY a
        /// purpose-built foley/music prompt from the vision model (asked
        /// with the matching question). Only the Length line is appended
        /// here; the server-side filename is a short id (see the ledger),
        /// never prompt text.
        /// </summary>
        private async Task WatchGenerate(string vlPrompt, bool foley, CancellationToken token)
        {
            string kind = foley ? "WatcherFoley" : "WatcherMusic";
            try
            {
                string prompt, workflow;
                double duration;
                int steps;
                string cleanBase = null;
                if (foley)
                {
                    duration = SafeNum(() => (double)_watchFoleySecNum.Value, 10);
                    string clean = CleanPrompt(vlPrompt, "quiet ambient room tone");
                    cleanBase = clean;
                    prompt = clean + " Length: " + (int)duration + " seconds";
                    workflow = SafeText(() => workflowSfxBox.Text, AudioWorkflow.DefaultSfxWorkflowPath());
                    steps = SafeNum(() => (int)stepsSfxNum.Value, 8);
                }
                else
                {
                    // Music category kept EXPLICITLY in the outer widget:
                    // the Stable Audio subgraph reprompts Music inputs into
                    // full tracks.
                    duration = SafeNum(() => (double)_watchMusicSecNum.Value, 45);
                    string clean = CleanPrompt(vlPrompt, "calm ambient loop");
                    cleanBase = clean;
                    prompt = clean + " Length: " + (int)duration + " seconds";
                    workflow = SafeText(() => workflowMusicBox.Text, AudioWorkflow.DefaultMusicWorkflowPath());
                    steps = SafeNum(() => (int)stepsMusicNum.Value, 8);
                }
                if (string.IsNullOrEmpty(workflow) || !File.Exists(workflow))
                {
                    Log("Watcher " + kind + " skipped: workflow not found.");
                    try
                    {
                        DateTime retry = DateTime.Now + TimeSpan.FromSeconds(15);
                        if (foley) { if (retry > _nextFoleyDue) _nextFoleyDue = retry; }
                        else { if (retry > _nextMusicDue) _nextMusicDue = retry; }
                    }
                    catch { }
                    if (foley) _foleyGenBusy = false; else _musicGenBusy = false;
                    return;
                }
                string catOverride = foley ? "SFX" : "Music";
                // Watcher layers inherit the user's negative boxes for
                // their kind (same steering as manual/radio runs).
                string negative = SafeText(() => (foley ? negSfxBox : negMusicBox).Text, string.Empty);
                if (negative != null) negative = negative.Trim();
                int vol = foley ? 90 : 75;
                // Quality gates: vocal bouncer (music with instrumental
                // intent) + CLAP match floor. One retry max, air the best
                // take; losers are kept in Clips as evidence, never aired.
                bool bounceOn = !foley && WatchBounceOn();
                bool clapOn = WatchClapOn();
                DateTime renderStart = DateTime.Now;
                var takes = new List<WatchTake>();
                string attemptPrompt = prompt;
                string attemptNeg = negative ?? string.Empty;
                while (takes.Count < 2)
                {
                    var take = await WatchRenderOnce(kind, foley, attemptPrompt, attemptNeg,
                        workflow, duration, steps, catOverride, token);
                    if (take == null)
                    {
                        if (token.IsCancellationRequested) return;
                        break; // render failed (logged); air what we have, if anything
                    }
                    takes.Add(take);
                    // Gates run on the mastered file (post trim/loudnorm).
                    bool isWav = take.PlayPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                        && File.Exists(take.PlayPath);
                    if (!foley && bounceOn && isWav &&
                        AudioGates.LooksInstrumental(take.Prompt, take.Negative))
                    {
                        var tr = await AudioGates.TranscribeAsync(take.PlayPath, token, m => Log(m));
                        if (tr.Ok)
                        {
                            take.Words = tr.Words;
                            Log(kind + " heard " + tr.Words + " word(s)" +
                                (tr.Words > 0 ? ": \"" + Shorten(tr.Text, 80) + "\"" : string.Empty) + ".");
                        }
                    }
                    if (clapOn && isWav)
                    {
                        var cr = await AudioGates.ClapAsync(take.PlayPath, take.Prompt, token, m => Log(m));
                        if (cr.Ok)
                        {
                            take.Clap = cr.Score;
                            take.ClapOk = true;
                            Log(kind + " CLAP " + cr.Score.ToString("0.00") + ".");
                        }
                    }
                    bool bounceHit = !foley && bounceOn && take.Words >= AudioGates.WordsBounceAt;
                    bool clapHit = clapOn && take.ClapOk && take.Clap < AudioGates.ClapRejectBelow;
                    if ((bounceHit || clapHit) && takes.Count == 1)
                    {
                        if (bounceHit)
                        {
                            Log(kind + " bounced (vocals on an instrumental ask) -- one hardened retry.");
                            attemptPrompt = AudioGates.HardenMusicPrompt(cleanBase ?? "calm ambient loop", (int)duration);
                            attemptNeg = AudioGates.HardenMusicNegative(attemptNeg);
                        }
                        else
                        {
                            Log(kind + " under the CLAP floor (" + take.Clap.ToString("0.00") + ") -- one retry.");
                        }
                        continue;
                    }
                    break;
                }
                if (takes.Count == 0)
                {
                    try
                    {
                        DateTime retry = DateTime.Now + TimeSpan.FromSeconds(15);
                        if (foley) { if (retry > _nextFoleyDue) _nextFoleyDue = retry; }
                        else { if (retry > _nextMusicDue) _nextMusicDue = retry; }
                    }
                    catch { }
                    return;
                }
                int[] wordArr = new int[takes.Count];
                double[] clapArr = new double[takes.Count];
                for (int ti = 0; ti < takes.Count; ti++)
                {
                    wordArr[ti] = takes[ti].Words;
                    clapArr[ti] = takes[ti].ClapOk ? takes[ti].Clap : 0;
                }
                int best = AudioGates.BestAttemptIndex(wordArr, clapArr);
                for (int ti = 0; ti < takes.Count; ti++)
                {
                    if (ti == best) continue;
                    string loser = takes[ti].AddPath;
                    SafeInvoke(() =>
                    {
                        try { AddClipFile(loser, false); }
                        catch { }
                    });
                    Log(kind + " alternate kept (not aired): " + Path.GetFileName(loser) + ".");
                }
                var win = takes[best];
                if (takes.Count > 1)
                    Log(kind + " aired take " + (best + 1) + " of " + takes.Count + ".");
                // Prefetch estimate is queue-to-READY (render + retry +
                // gates), so slow quality passes self-correct the clock.
                try
                {
                    double totalSec = (DateTime.Now - renderStart).TotalSeconds;
                    if (totalSec < 0.2) totalSec = 0.2;
                    if (totalSec > 300) totalSec = 300;
                    if (foley)
                        _lastFoleyRenderSec = _lastFoleyRenderSec <= 0
                            ? totalSec : _lastFoleyRenderSec * 0.7 + totalSec * 0.3;
                    else
                        _lastMusicRenderSec = _lastMusicRenderSec <= 0
                            ? totalSec : _lastMusicRenderSec * 0.7 + totalSec * 0.3;
                }
                catch { }
                prompt = win.Prompt;
                workflow = win.Workflow;
                duration = win.RequestSec;
                string playPath = win.PlayPath;
                string addPath = win.AddPath;
                WaveData airWave2 = win.Wave;
                long winSeed = win.Seed;
                string gateTag = string.Empty;
                try
                {
                    if (win.ClapOk) gateTag += "clap " + win.Clap.ToString("0.00");
                    if (win.Words >= 0) gateTag += (gateTag.Length > 0 ? " | " : string.Empty) + "words " + win.Words;
                    if (gateTag.Length > 0) gateTag = " [" + gateTag + "]";
                }
                catch { gateTag = string.Empty; }
                string gateSuffix = gateTag;
                // Outgoing alias for the flip-flop (the one actually on air,
                // if any) plus this swap's crossfade width, clamped so at
                // least 60% of the incoming body survives the blend.
                AudioPlayer outgoing = foley
                    ? (_foleyFlip ? _foleyPlayerB : _foleyPlayer)
                    : (_musicFlip ? _musicPlayerB : _musicPlayer);
                double xfadeHere = foley ? FoleyXfadeSec : MusicXfadeSec;
                try
                {
                    double basis = airWave2 != null ? airWave2.DurationSec : duration;
                    if (basis > 0 && xfadeHere > basis * 0.4)
                        xfadeHere = Math.Max(0.3, basis * 0.4);
                }
                catch { }
                // Full-play hold with crossfade wake: the previous clip keeps
                // the layer until (swap - xfade), then the incoming overlaps
                // it. Fast renders wait here; slow ones arrive late and the
                // xfade degrades to a quick fade-in instead of truncating.
                try
                {
                    DateTime swapDue = foley ? _foleySwapDue : _musicSwapDue;
                    DateTime startedCopy = foley ? _watchFoleyStarted : _watchMusicStarted;
                    bool playingCopy = false;
                    try { playingCopy = outgoing != null && outgoing.IsPlaying; }
                    catch { playingCopy = false; }
                    DateTime holdUntil = swapDue != DateTime.MinValue && playingCopy
                        ? swapDue - TimeSpan.FromSeconds(xfadeHere) : DateTime.MinValue;
                    if (playingCopy && holdUntil != DateTime.MinValue && DateTime.Now < holdUntil)
                    {
                        // Wake at (swap - xfade) so the blend overlaps the
                        // outgoing tail instead of starting into silence.
                        double hold = (holdUntil - DateTime.Now).TotalSeconds;
                        if (hold > 0.1)
                        {
                            if (!foley)
                            {
                                double elapsed = startedCopy != DateTime.MinValue
                                    ? (DateTime.Now - startedCopy).TotalSeconds : 999;
                                if (elapsed < MusicMinPlaySec)
                                    Log("Watcher music holds " + hold.ToString("0") +
                                        "s more (full play, 15s min).");
                            }
                            await Task.Delay(TimeSpan.FromSeconds(Math.Min(hold, 120)), token);
                        }
                    }
                    else if (!foley && playingCopy && startedCopy != DateTime.MinValue)
                    {
                        // No swap booked (first clip edge): still honor 15s.
                        double elapsed = (DateTime.Now - startedCopy).TotalSeconds;
                        if (elapsed < MusicMinPlaySec)
                        {
                            double hold = MusicMinPlaySec - elapsed;
                            Log("Watcher music holds " + hold.ToString("0") +
                                "s more (15s minimum before cut).");
                            await Task.Delay(TimeSpan.FromSeconds(hold), token);
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
                token.ThrowIfCancellationRequested();
                // Layer disabled mid-render: keep the file in Clips, but do
                // NOT take the layer, book swaps, or touch dues -- the next
                // enable refires immediately via the scheduler.
                bool layerStillWanted = foley ? WatchFoleyOn() : WatchMusicOn();
                if (!layerStillWanted)
                {
                    Log("Watcher " + kind + " done but layer disabled -- kept in Clips, not aired.");
                    string keepPath = playPath;
                    SafeInvoke(() =>
                    {
                        try { AddClipFile(keepPath, false); }
                        catch (Exception ex) { Log("Watcher keep failed: " + ex.Message); }
                    });
                    return;
                }
                string sceneDesc = vlPrompt + gateSuffix;
                string watchPrompt = prompt;
                long watchSeed = winSeed;
                string watchWorkflow = workflow;
                double watchDur = airWave2 != null ? airWave2.DurationSec : duration;
                string watchName = Path.GetFileName(playPath);
                // Auto-caption the aired take: the library becomes
                // content-searchable and the timeline shows what it
                // actually contains (not just what was asked for).
                string heard = string.Empty;
                try
                {
                    if (playPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(playPath))
                    {
                        var cap = await AudioGates.CaptionAsync(playPath, token, m => Log(m));
                        if (cap.Ok)
                        {
                            heard = cap.Text;
                            Log(kind + " heard: \"" + Shorten(heard, 140) + "\"");
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
                DateTime onAirAt = DateTime.Now;
                // Flip-flop crossfade onto the layer (xfadeHere may have
                // degraded to a fade-in when the render arrived late).
                await WatchXfade(foley, playPath, vol, xfadeHere, token);
                if (foley)
                {
                    _foleyFlip = !_foleyFlip;
                    _foleyPlayPath = playPath;
                    _foleyClipLen = watchDur;
                }
                else
                {
                    _musicFlip = !_musicFlip;
                    _musicPlayPath = playPath;
                    _musicClipLen = watchDur;
                }
                SafeInvoke(() =>
                {
                    try
                    {
                        AddClipFile(addPath, false);
                        NoteOnAir(foley, Path.GetFileName(playPath), airWave2);
                        NoteTimeline(foley, Path.GetFileName(playPath),
                            watchPrompt, sceneDesc, watchSeed, watchDur, playPath, heard);
                        Log("Watcher " + (foley ? "foley" : "music") + " on air: " +
                            Path.GetFileName(playPath) + ".");
                        try
                        {
                            AudioLibrary.Append(new LibraryEntry
                            {
                                Kind = foley ? "WatcherFoley" : "WatcherMusic",
                                Prompt = watchPrompt,
                                Seed = watchSeed,
                                Workflow = watchWorkflow,
                                AudioPath = playPath,
                                DurationSec = airWave2 != null ? airWave2.DurationSec : 0,
                                Scene = sceneDesc,
                                Caption = heard
                            });
                        }
                        catch { }
                    }
                    catch (Exception ex) { Log("Watcher play failed: " + ex.Message); }
                });
                // Book the swap (full play) and the next prefetch from the
                // measured render: swap = onAir + clip; prefetch starts at
                // swap - render - buffer. At 0.7s/7s this is gapless; when
                // the GPU slows down the due lands in the past and the next
                // sense tick refires immediately (catch-up, no queue depth).
                try
                {
                    double clipLen = watchDur > 0 ? watchDur : duration;
                    if (clipLen < 1) clipLen = foley ? 5 : 30;
                    double renderEst = foley ? _lastFoleyRenderSec : _lastMusicRenderSec;
                    if (renderEst < 0.2) renderEst = foley ? 0.7 : 7.0;
                    double buffer = foley ? 1.0 : 3.0;
                    if (foley)
                    {
                        _foleySwapDue = onAirAt + TimeSpan.FromSeconds(clipLen);
                        double startIn = clipLen - renderEst - buffer;
                        if (startIn < 1.5) startIn = 1.5;
                        DateTime wanted = onAirAt + TimeSpan.FromSeconds(startIn);
                        if (wanted < _nextFoleyDue) { /* keep earlier sense due */ }
                        else _nextFoleyDue = wanted;
                    }
                    else
                    {
                        double effective = Math.Max(clipLen, MusicMinPlaySec);
                        _musicSwapDue = onAirAt + TimeSpan.FromSeconds(effective);
                        double startIn = effective - renderEst - buffer;
                        double floor = Math.Max(5.0, MusicMinPlaySec);
                        if (startIn < floor) startIn = floor;
                        DateTime wanted = onAirAt + TimeSpan.FromSeconds(startIn);
                        if (wanted < _nextMusicDue) { /* keep earlier sense due */ }
                        else _nextMusicDue = wanted;
                    }
                }
                catch { }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log("Watcher " + kind + " failed: " + ex.Message);
                try
                {
                    DateTime retry = DateTime.Now + TimeSpan.FromSeconds(15);
                    if (foley) { if (retry > _nextFoleyDue) _nextFoleyDue = retry; }
                    else { if (retry > _nextMusicDue) _nextMusicDue = retry; }
                }
                catch { }
            }
            finally
            {
                if (foley) _foleyGenBusy = false; else _musicGenBusy = false;
            }
        }

        /// <summary>
        /// Flip-flop crossfade onto a watcher layer: the incoming clip
        /// starts on the idle alias while the outgoing ramps down over
        /// xfadeSec (12 steps). Gap/first-clip arrivals degrade to a plain
        /// start. Never throws: cancel mid-blend finishes instantly.
        /// </summary>
        private async Task WatchXfade(bool foley, string newPath, int vol,
            double xfadeSec, CancellationToken token)
        {
            AudioPlayer incoming = foley
                ? (_foleyFlip ? _foleyPlayer : _foleyPlayerB)
                : (_musicFlip ? _musicPlayer : _musicPlayerB);
            AudioPlayer outgo = foley
                ? (_foleyFlip ? _foleyPlayerB : _foleyPlayer)
                : (_musicFlip ? _musicPlayerB : _musicPlayer);
            bool oldAlive = false;
            try { oldAlive = outgo != null && outgo.IsPlaying; }
            catch { oldAlive = false; }
            if (incoming == null) throw new Exception("No player alias.");
            if (!oldAlive || xfadeSec < 0.25)
            {
                SafeInvoke(() =>
                {
                    try { incoming.SetVolume(vol); incoming.Play(newPath); }
                    catch (Exception ex) { Log("Watcher play failed: " + ex.Message); }
                });
                return;
            }
            // Start muted (tiny blip possible between open and mute), ramp.
            SafeInvoke(() =>
            {
                try
                {
                    incoming.Play(newPath);
                    try { incoming.SetVolume(0); }
                    catch { }
                }
                catch (Exception ex) { Log("Watcher play failed: " + ex.Message); }
            });
            const int steps = 12;
            int stepMs = Math.Max(30, (int)(xfadeSec * 1000 / steps));
            try
            {
                for (int i = 1; i <= steps; i++)
                {
                    await Task.Delay(stepMs, token);
                    int ov = vol * (steps - i) / steps;
                    int nv = vol * i / steps;
                    try { outgo.SetVolume(ov); }
                    catch { }
                    try { incoming.SetVolume(nv); }
                    catch { }
                }
            }
            catch (OperationCanceledException)
            {
                // Finish instantly rather than leaving split volumes.
            }
            try { outgo.Stop(); }
            catch { }
            try { incoming.SetVolume(vol); }
            catch { }
        }

        /// <summary>
        /// Zero-GPU static-scene loop: restart the on-air file at its swap
        /// so a still scene holds audio without re-rendering. No timeline
        /// card (the ON AIR card keeps glowing); busy flag is owned by the
        /// scheduler caller.
        /// </summary>
        private async Task WatchReplay(bool foley, CancellationToken token)
        {
            string kind = foley ? "WatcherFoley" : "WatcherMusic";
            try
            {
                string path = foley ? _foleyPlayPath : _musicPlayPath;
                double clipLen = foley ? _foleyClipLen : _musicClipLen;
                DateTime swapDue = foley ? _foleySwapDue : _musicSwapDue;
                if (string.IsNullOrEmpty(path) || !File.Exists(path) || clipLen <= 0)
                {
                    Log("Watcher " + kind + " loop skipped (no on-air file).");
                    try
                    {
                        DateTime r = DateTime.Now + TimeSpan.FromSeconds(5);
                        if (foley) _nextFoleyDue = r; else _nextMusicDue = r;
                    }
                    catch { }
                    return;
                }
                try
                {
                    if (swapDue != DateTime.MinValue && DateTime.Now < swapDue)
                    {
                        double hold = (swapDue - DateTime.Now).TotalSeconds;
                        if (hold > 0.1)
                            await Task.Delay(TimeSpan.FromSeconds(Math.Min(hold, 120)), token);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
                token.ThrowIfCancellationRequested();
                if (foley ? !WatchFoleyOn() : !WatchMusicOn())
                {
                    Log("Watcher " + kind + " loop skipped (layer disabled).");
                    return;
                }
                // Restart on the ACTIVE alias (same loop-prepped file whose
                // head already contains its tail: the junction is smooth).
                AudioPlayer p = foley
                    ? (_foleyFlip ? _foleyPlayerB : _foleyPlayer)
                    : (_musicFlip ? _musicPlayerB : _musicPlayer);
                int vol = foley ? 90 : 75;
                DateTime started = DateTime.Now;
                string nm = Path.GetFileName(path);
                SafeInvoke(() =>
                {
                    try
                    {
                        if (p != null) { p.SetVolume(vol); p.Play(path); }
                        if (foley)
                        {
                            _watchFoleyStarted = started;
                            if (_watchFoleyAirLabel != null)
                                _watchFoleyAirLabel.Text = "Foley: " + nm + " (loop)";
                        }
                        else
                        {
                            _watchMusicStarted = started;
                            if (_watchMusicAirLabel != null)
                                _watchMusicAirLabel.Text = "Music: " + nm + " (loop)";
                        }
                        if (_watchTimelineLane != null && !_watchTimelineLane.IsDisposed)
                            _watchTimelineLane.Invalidate();
                    }
                    catch (Exception ex) { Log("Watcher loop play failed: " + ex.Message); }
                });
                Log("Watcher " + kind + " looping (scene static).");
                // Next check-in rides near the following swap but peeks
                // early so a dynamic scene preempts with render lead time.
                try
                {
                    DateTime swap = started + TimeSpan.FromSeconds(clipLen);
                    DateTime near = swap - TimeSpan.FromSeconds(1.0);
                    DateTime peek = DateTime.Now +
                        TimeSpan.FromSeconds(Math.Max(2, WatchSenseIntervalSec()));
                    DateTime due = near < peek ? near : peek;
                    if (foley) { _foleySwapDue = swap; _nextFoleyDue = due; }
                    else { _musicSwapDue = swap; _nextMusicDue = due; }
                }
                catch { }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log("Watcher " + kind + " loop failed: " + ex.Message); }
            finally
            {
                if (foley) _foleyGenBusy = false; else _musicGenBusy = false;
            }
        }

        private string WatchFoleyInstruction()
        {
            try
            {
                if (_watchFoleyAskBox != null && !string.IsNullOrWhiteSpace(_watchFoleyAskBox.Text))
                    return _watchFoleyAskBox.Text.Trim();
                return AppConfig.Instance.WatcherFoleyPrompt;
            }
            catch { return null; }
        }

        private string WatchMusicInstruction()
        {
            try
            {
                if (_watchMusicAskBox != null && !string.IsNullOrWhiteSpace(_watchMusicAskBox.Text))
                    return _watchMusicAskBox.Text.Trim();
                return AppConfig.Instance.WatcherMusicPrompt;
            }
            catch { return null; }
        }

        private string SafeText(Func<string> get, string fallback)
        {
            try
            {
                string v = null;
                try
                {
                    if (IsDisposed || !IsHandleCreated) return fallback;
                    v = (string)Invoke(get);
                }
                catch { v = null; }
                if (!string.IsNullOrEmpty(v)) return v;
            }
            catch { }
            return fallback;
        }

        private double SafeNum(Func<double> get, double fallback)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return fallback;
                object v = Invoke(get);
                return Convert.ToDouble(v);
            }
            catch { return fallback; }
        }

        private int SafeNum(Func<int> get, int fallback)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return fallback;
                object v = Invoke(get);
                return Convert.ToInt32(v);
            }
            catch { return fallback; }
        }

        #endregion
    }
}

