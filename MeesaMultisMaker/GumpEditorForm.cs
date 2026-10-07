using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MeesaMultisMaker.Utils;

namespace MeesaMultisMaker
{
    /// <summary>
    /// GUMP Editor form - allows viewing, editing, and creating GUMP graphics.
    /// Uses a simple grid-based canvas instead of isometric view.
    /// Supports layers, gump palette browser, and AI generation.
    /// 
    /// This is a partial class split across multiple files:
    /// - GumpEditorForm.cs (this file): Constructor, Dispose, keyboard handling
    /// - GumpEditorForm.Fields.cs: Field declarations and nested types
    /// - GumpEditorForm.UI.cs: InitializeComponent and UI building methods
    /// - GumpEditorForm.Palette.cs: Gump palette loading and list view handling
    /// - GumpEditorForm.Canvas.cs: Canvas painting, mouse interaction, import/export
    /// - GumpEditorForm.Layers.cs: Layer management and undo/redo
    /// - GumpEditorForm.Transform.cs: Transform, resize, rotate, and slice operations
    /// - GumpEditorForm.AI.cs: AI generation methods
    /// </summary>
    public partial class GumpEditorForm : Form
    {
        public GumpEditorForm()
        {
            InitializeComponent();

            // Start loading in background - form opens immediately
            BeginLoadMulFolder();
        }

        private void GumpEditorForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Z) { Undo(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.Y) { Redo(); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.S) { ExportCanvas(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.V) { PasteFromClipboard(); e.Handled = true; }
        }

        private void SendSelectedToMap(bool asOverlay)
        {
            var source = selectedGump ?? selectedGumps.FirstOrDefault();
            if (source == null || source.Image == null)
            {
                MessageBox.Show(this, "Select a gump on the canvas first.", "Send to Map Editor",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Bitmap copy = new Bitmap(source.Image);

            try
            {
                if (_mapViewerBridge == null || _mapViewerBridge.IsDisposed)
                {
                    _mapViewerBridge = new MapViewerForm
                    {
                        StartPosition = FormStartPosition.CenterScreen,
                        ShowInTaskbar = true
                    };
                    _mapViewerBridge.FormClosed += (s, e) => { _mapViewerBridge = null; };
                    _mapViewerBridge.Show();
                }
                else
                {
                    if (!_mapViewerBridge.Visible) _mapViewerBridge.Show();
                    _mapViewerBridge.BringToFront();
                }

                _mapViewerBridge.AddPainterImageToMapLayer(copy, asOverlay);
                SetStatus($"Sent gump to Map Editor as {(asOverlay ? "overlay" : "underlay")}");
            }
            catch (Exception ex)
            {
                copy.Dispose();
                MessageBox.Show(this, $"Failed to send to Map Editor: {ex.Message}", "Send Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _loadingCts?.Cancel();
                _loadingCts?.Dispose();

                foreach (var gump in placedGumps)
                {
                    gump.Image?.Dispose();
                }

                foreach (var kvp in gumpCache)
                {
                    kvp.Value?.Dispose();
                }
                gumpCache.Clear();
                _gumpDimensionCache.Clear();

                gumpImageList?.Dispose();
                tooltip?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Represents a placed GUMP object on the canvas
    /// </summary>
    public class PlacedGump
    {
        public int GumpId { get; set; }
        public Bitmap Image { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int LayerIndex { get; set; }
        public float ScaleX { get; set; } = 1.0f;
        public float ScaleY { get; set; } = 1.0f;
        public float Rotation { get; set; } = 0f;
        public float SkewX { get; set; } = 0f;
        public float SkewY { get; set; } = 0f;

        /// <summary>
        /// Returns true if the image is valid and can be safely accessed
        /// </summary>
        public bool IsValid => ImageHelper.IsValidImage(Image);

        /// <summary>
        /// Gets the width of the gump after scaling. Returns 0 if image is invalid.
        /// </summary>
        public int Width
        {
            get
            {
                if (!ImageHelper.IsValidImage(Image)) return 0;
                try { return (int)(Image.Width * ScaleX); }
                catch { return 0; }
            }
        }

        /// <summary>
        /// Gets the height of the gump after scaling. Returns 0 if image is invalid.
        /// </summary>
        public int Height
        {
            get
            {
                if (!ImageHelper.IsValidImage(Image)) return 0;
                try { return (int)(Image.Height * ScaleY); }
                catch { return 0; }
            }
        }

        /// <summary>
        /// Gets the center point of the gump
        /// </summary>
        public PointF Center => new PointF(X + Width / 2f, Y + Height / 2f);

        public bool ContainsPoint(int px, int py)
        {
            if (!ImageHelper.IsValidImage(Image)) return false;

            // For rotated gumps, we need to check if the point is within the rotated bounds
            if (Math.Abs(Rotation) > 0.01f)
            {
                // Transform the point to the gump's local coordinate system
                var center = Center;
                float angleRad = -Rotation * (float)Math.PI / 180f;
                float cos = (float)Math.Cos(angleRad);
                float sin = (float)Math.Sin(angleRad);

                // Translate point to origin (center of gump)
                float dx = px - center.X;
                float dy = py - center.Y;

                // Rotate point
                float localX = dx * cos - dy * sin + center.X;
                float localY = dx * sin + dy * cos + center.Y;

                // Check against unrotated bounds
                return localX >= X && localX < X + Width && localY >= Y && localY < Y + Height;
            }

            int w = Width;
            int h = Height;
            return px >= X && px < X + w && py >= Y && py < Y + h;
        }

        /// <summary>
        /// Gets the bounding rectangle of the gump (axis-aligned, accounts for rotation)
        /// </summary>
        public Rectangle GetBounds()
        {
            if (!ImageHelper.IsValidImage(Image)) return Rectangle.Empty;

            if (Math.Abs(Rotation) < 0.01f && Math.Abs(SkewX) < 0.01f && Math.Abs(SkewY) < 0.01f)
            {
                return new Rectangle(X, Y, Width, Height);
            }

            // Calculate rotated/skewed corners
            var corners = GetTransformedCorners();
            if (corners == null || corners.Length == 0) return Rectangle.Empty;

            float minX = corners.Min(p => p.X);
            float maxX = corners.Max(p => p.X);
            float minY = corners.Min(p => p.Y);
            float maxY = corners.Max(p => p.Y);

            return Rectangle.FromLTRB((int)minX, (int)minY, (int)Math.Ceiling(maxX), (int)Math.Ceiling(maxY));
        }

        /// <summary>
        /// Gets the four corners of the gump after transformation
        /// </summary>
        public PointF[] GetTransformedCorners()
        {
            int w = Width;
            int h = Height;
            if (w == 0 || h == 0) return new PointF[0];

            var center = Center;

            // Original corners relative to center
            PointF[] corners = new PointF[]
            {
                new PointF(X - center.X, Y - center.Y),
                new PointF(X + w - center.X, Y - center.Y),
                new PointF(X + w - center.X, Y + h - center.Y),
                new PointF(X - center.X, Y + h - center.Y)
            };

            // Apply rotation
            float angleRad = Rotation * (float)Math.PI / 180f;
            float cos = (float)Math.Cos(angleRad);
            float sin = (float)Math.Sin(angleRad);

            for (int i = 0; i < 4; i++)
            {
                float x = corners[i].X;
                float y = corners[i].Y;

                // Apply skew
                x += y * SkewX;
                y += x * SkewY;

                // Apply rotation
                float rx = x * cos - y * sin;
                float ry = x * sin + y * cos;

                // Translate back
                corners[i] = new PointF(rx + center.X, ry + center.Y);
            }

            return corners;
        }

        public PlacedGump Clone()
        {
            return new PlacedGump
            {
                GumpId = GumpId,
                Image = ImageHelper.SafeCopy(Image),
                X = X,
                Y = Y,
                LayerIndex = LayerIndex,
                ScaleX = ScaleX,
                ScaleY = ScaleY,
                Rotation = Rotation,
                SkewX = SkewX,
                SkewY = SkewY
            };
        }

    }
}
