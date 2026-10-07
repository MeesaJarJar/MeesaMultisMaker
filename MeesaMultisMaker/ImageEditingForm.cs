using MeesaMultisMaker.Controls;
using System;
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

            // Create the editing panel
            _editingPanel = new ImageEditingPanel();
            _editingPanel.Dock = DockStyle.Fill;

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
                FillHoles = _editingPanel.FillHoles
            };
        }

        public void SetStatus(string message, Color? color = null)
        {
            _editingPanel?.SetStatus(message, color);
        }

        public void ResetControls()
        {
            _editingPanel?.ResetToDefaults();
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
    }
}
