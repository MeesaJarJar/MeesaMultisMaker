using MeesaMultisMaker.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    /// <summary>
    /// Floating window for image editing operations
    /// </summary>
    public class ImageEditingForm : Form
    {
        private ImageEditingPanel _editingPanel;

        // Events to communicate back to main form
        public event EventHandler<ImageEffectEventArgs> ApplyEffects;
        public event EventHandler<ImageEffectEventArgs> ApplyEffectsToAll;
        public event EventHandler ResetRequested;
        public event EventHandler<ImageEffectEventArgs> RealTimePreview; // New event for real-time preview

        /// <summary>Forwarded desaturate eyedropper arming from the inner panel (carries Targets vs Protected).</summary>
        public event EventHandler<EyedropperRequestedEventArgs> EyedropperRequested;
        /// <summary>Forwarded eyedropper cancel from the inner panel.</summary>
        public event EventHandler EyedropperCancelled;
        /// <summary>Forwarded SAM arming from the inner panel.</summary>
        public event EventHandler SamSelectRequested;
        /// <summary>Forwarded SAM disarm from the inner panel (Esc / re-click). Keeps mask.</summary>
        public event EventHandler SamSelectCancelled;
        /// <summary>Forwarded SAM clear from the inner panel. Hosts drop mask + points.</summary>
        public event EventHandler SamClearRequested;
        /// <summary>Forwarded Show SAM toggle.</summary>
        public event EventHandler SamShowChanged;

        public ImageEditingForm()
        {
            InitializeComponent();
            HolographicTheme.ApplyToForm(this);
        }

        private void InitializeComponent()
        {
            this.Text = "Image Editing";
            this.Width = 320;
            this.Height = 700;
            this.FormBorderStyle = FormBorderStyle.SizableToolWindow;
            this.StartPosition = FormStartPosition.Manual;
            this.ShowInTaskbar = false;
            this.MinimumSize = new Size(320, 500);
            this.KeyPreview = true;
            this.KeyDown += ImageEditingForm_KeyDown;

            // Create the editing panel
            _editingPanel = new ImageEditingPanel();
            _editingPanel.Dock = DockStyle.Fill;
            _editingPanel.EyedropperRequested += (s, e) => EyedropperRequested?.Invoke(this, e);
            _editingPanel.EyedropperCancelled += (s, e) => EyedropperCancelled?.Invoke(this, EventArgs.Empty);
            _editingPanel.SamSelectRequested += (s, e) => SamSelectRequested?.Invoke(this, e);
            _editingPanel.SamSelectCancelled += (s, e) => SamSelectCancelled?.Invoke(this, e);
            _editingPanel.SamClearRequested += (s, e) => SamClearRequested?.Invoke(this, e);
            _editingPanel.SamShowChanged += (s, e) => SamShowChanged?.Invoke(this, e);

            // Wire up events
            _editingPanel.ApplyClicked += (s, e) =>
            {
                var args = GetCurrentEffectArgs();
                ApplyEffects?.Invoke(this, args);
            };

            _editingPanel.ApplyToAllSelectedClicked += (s, e) =>
            {
                var args = GetCurrentEffectArgs();
                ApplyEffectsToAll?.Invoke(this, args);
            };

            _editingPanel.ResetClicked += (s, e) =>
            {
                ResetRequested?.Invoke(this, EventArgs.Empty);
            };

            // Wire up real-time preview
            _editingPanel.EffectsChanged += (s, e) =>
            {
                var args = GetCurrentEffectArgs();
                RealTimePreview?.Invoke(this, args);
            };

            this.Controls.Add(_editingPanel);
        }

        private ImageEffectEventArgs GetCurrentEffectArgs()
        {
            return new ImageEffectEventArgs
            {
                Brightness = _editingPanel.Brightness,
                Contrast = _editingPanel.Contrast,
                Hue = _editingPanel.Hue,
                Saturation = _editingPanel.Saturation,
                PixelSize = _editingPanel.PixelSize,
                PixelizeEnabled = _editingPanel.PixelizeEnabled,
                PaletteColors = _editingPanel.PaletteColors,
                NoiseIntensity = _editingPanel.NoiseIntensity,
                DitherLevels = _editingPanel.DitherLevels,
                EdgeDarkening = _editingPanel.EdgeDarkening,
                ColorBands = _editingPanel.ColorBands,
                FillHoles = _editingPanel.FillHoles,
                RemoveBackground = _editingPanel.RemoveBackgroundEnabled,
                BackgroundMode = _editingPanel.BackgroundMode,
                BackgroundColor = _editingPanel.BackgroundColor,
                BackgroundThreshold = _editingPanel.BackgroundThreshold,
                DesaturateEnabled = _editingPanel.DesaturateEnabled,
                DesaturateTargets = new List<Color>(_editingPanel.DesaturateTargets),
                DesaturateProtected = new List<Color>(_editingPanel.DesaturateProtected),
                DesaturateColor = _editingPanel.DesaturateColor,
                DesaturateTolerance = _editingPanel.DesaturateTolerance,
                DespeckleMinSize = _editingPanel.DespeckleMinSize,
                SnapNearGray = _editingPanel.SnapNearGrayEnabled,
                SnapTolerance = _editingPanel.SnapTolerance
            };
        }

        public void SetStatus(string message, Color? color = null)
        {
            _editingPanel?.SetStatus(message, color);
        }

        public string StatusText => _editingPanel?.StatusText ?? string.Empty;

        public void ResetControls()
        {
            _editingPanel?.ResetToDefaults();
        }

        public ImageEditingPanel EditingPanel => _editingPanel;

        public bool IsEyedropperArmed => _editingPanel?.IsEyedropperArmed ?? false;

        public DesaturatePickKind ArmedPickKind => _editingPanel?.ArmedPickKind ?? DesaturatePickKind.Targets;

        public void ConfirmEyedropper(Color picked)
        {
            _editingPanel?.ConfirmEyedropper(picked);
        }

        public void ConfirmEyedropper(Color picked, DesaturatePickKind kind)
        {
            _editingPanel?.ConfirmEyedropper(picked, kind);
        }

        public void SetMatchCount(int count)
        {
            _editingPanel?.SetMatchCount(count);
        }

        public void CancelEyedropper()
        {
            _editingPanel?.CancelEyedropper();
        }

        public bool IsSamArmed => _editingPanel?.IsSamArmed ?? false;

        public bool ShowSam => _editingPanel?.ShowSam ?? true;

        public void CancelSamArming()
        {
            _editingPanel?.CancelSamArming();
        }

        public void ClearSam()
        {
            _editingPanel?.ClearSam();
        }

        public void SetSamInfo(int pointCount, bool hasMask, bool busy)
        {
            _editingPanel?.SetSamInfo(pointCount, hasMask, busy);
        }

        private void ImageEditingForm_KeyDown(object sender, KeyEventArgs e)
        {
            // Esc cancels an armed eyedropper / SAM arming when this floating window has focus.
            // Form1's own KeyDown covers the main canvas focus case.
            // SAM Esc keeps the last mask; Clear SAM drops it.
            if (e.KeyCode == Keys.Escape && IsSamArmed)
            {
                CancelSamArming();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Escape && IsEyedropperArmed)
            {
                CancelEyedropper();
                e.Handled = true;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return false; }
        }
    }

    /// <summary>
    /// Event args for image effect application
    /// </summary>
    public class ImageEffectEventArgs : EventArgs
    {
        public float Brightness { get; set; }
        public float Contrast { get; set; }
        public float Hue { get; set; }
        public float Saturation { get; set; }
        public int PixelSize { get; set; }
        public bool PixelizeEnabled { get; set; }
        public int PaletteColors { get; set; }

        // New UO-style effects
        public int NoiseIntensity { get; set; }
        public int DitherLevels { get; set; }
        public int EdgeDarkening { get; set; }
        public int ColorBands { get; set; }

        public bool FillHoles { get; set; }

        // Background removal
        public bool RemoveBackground { get; set; }
        public string BackgroundMode { get; set; }
        public Color BackgroundColor { get; set; }
        public int BackgroundThreshold { get; set; }

        // Partial-hue prep (exact-gray authoring)
        public bool DesaturateEnabled { get; set; }
        public List<Color> DesaturateTargets { get; set; } = new List<Color>();
        /// <summary>Protected colors: never desaturated, even within tolerance of a target.</summary>
        public List<Color> DesaturateProtected { get; set; } = new List<Color>();
        /// <summary>First-target convenience for the single-color case (DesaturateTargets[0] or Red when empty).</summary>
        public Color DesaturateColor
        {
            get { return (DesaturateTargets != null && DesaturateTargets.Count > 0) ? DesaturateTargets[0] : Color.Red; }
            set
            {
                if (DesaturateTargets == null) DesaturateTargets = new List<Color>();
                if (DesaturateTargets.Count == 0) DesaturateTargets.Add(value);
                else DesaturateTargets[0] = value;
            }
        }
        public int DesaturateTolerance { get; set; }
        /// <summary>Despeckle min blob size 0-16 (0 = off).</summary>
        public int DespeckleMinSize { get; set; }
        public bool SnapNearGray { get; set; }
        public int SnapTolerance { get; set; }
    }
}
