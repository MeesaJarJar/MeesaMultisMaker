using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MeesaMultisMaker.Controls
{
    /// <summary>
    /// A scrollable panel with a custom drawn holographic vertical scrollbar.
    /// Uses a child panel (Content) to host user controls. Supports vertical scrolling only.
    /// </summary>
    public class HoloScrollPanel : Panel
    {
        private readonly Panel contentPanel;
        private float thumbTop;
        private bool dragging;
        private float dragOffset;
        private const int ScrollbarWidth = 12;
        private const int ThumbMin = 24;

        public Control Content => contentPanel;

        public HoloScrollPanel()
        {
            DoubleBuffered = true;
            AutoScroll = true;
            BackColor = Color.Transparent;

            contentPanel = new Panel
            {
                Location = new Point(0, 0),
                Width = this.Width - ScrollbarWidth,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
            };
            Controls.Add(contentPanel);

            this.Resize += (s, e) => { contentPanel.Width = this.Width - ScrollbarWidth; Invalidate(); };
            this.MouseDown += OnMouseDown;
            this.MouseMove += OnMouseMove;
            this.MouseUp += (s, e) => { dragging = false; };
            this.MouseWheel += OnMouseWheel;
            contentPanel.ControlAdded += (s, e) => RefreshScroll();
            contentPanel.ControlRemoved += (s, e) => RefreshScroll();
        }

        private void OnMouseWheel(object sender, MouseEventArgs e)
        {
            ScrollBy(-e.Delta / 3f);
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (IsOverThumb(e.Location))
            {
                dragging = true;
                dragOffset = e.Y - thumbTop;
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (dragging)
            {
                var trackHeight = Height;
                var thumbHeight = GetThumbHeight();
                thumbTop = Math.Max(0, Math.Min(trackHeight - thumbHeight, e.Y - dragOffset));
                SyncContentToThumb();
                Invalidate();
            }
        }

        private bool IsOverThumb(Point p)
        {
            int thumbHeight = GetThumbHeight();
            var xStart = Width - ScrollbarWidth;
            return p.X >= xStart && p.Y >= thumbTop && p.Y <= thumbTop + thumbHeight;
        }

        private int GetThumbHeight()
        {
            var contentHeight = contentPanel.Height;
            var viewHeight = Height;
            if (contentHeight <= viewHeight) return viewHeight;
            var h = (int)Math.Max(ThumbMin, (float)viewHeight * viewHeight / contentHeight);
            return h;
        }

        private void SyncContentToThumb()
        {
            var contentHeight = contentPanel.Height;
            var viewHeight = Height;
            if (contentHeight <= viewHeight)
            {
                contentPanel.Top = 0;
                return;
            }
            var trackHeight = viewHeight - GetThumbHeight();
            var progress = thumbTop / (trackHeight <= 0 ? 1 : trackHeight);
            var maxOffset = contentHeight - viewHeight;
            contentPanel.Top = -(int)(progress * maxOffset);
        }

        private void ScrollBy(float delta)
        {
            var contentHeight = contentPanel.Height;
            var viewHeight = Height;
            if (contentHeight <= viewHeight) return;

            var maxOffset = contentHeight - viewHeight;
            var currentOffset = -contentPanel.Top;
            var newOffset = Math.Max(0, Math.Min(maxOffset, currentOffset + delta));

            var progress = newOffset / maxOffset;
            var trackHeight = viewHeight - GetThumbHeight();
            thumbTop = progress * trackHeight;
            contentPanel.Top = -(int)newOffset;
            Invalidate();
        }

        public void RefreshScroll()
        {
            var contentHeight = contentPanel.Height;
            var viewHeight = Height;
            if (contentHeight <= viewHeight)
            {
                thumbTop = 0;
                contentPanel.Top = 0;
            }
            else
            {
                var maxOffset = contentHeight - viewHeight;
                var currentOffset = Math.Max(0, Math.Min(maxOffset, -contentPanel.Top));
                var progress = currentOffset / maxOffset;
                var trackHeight = viewHeight - GetThumbHeight();
                thumbTop = progress * trackHeight;
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.Clear(BackColor);
            contentPanel.Width = Width - ScrollbarWidth;

            var trackRect = new Rectangle(Width - ScrollbarWidth, 0, ScrollbarWidth, Height);
            using (var trackBrush = new SolidBrush(Color.FromArgb(20, 120, 180)))
                g.FillRectangle(trackBrush, trackRect);

            int thumbHeight = GetThumbHeight();
            var thumbRect = new Rectangle(Width - ScrollbarWidth + 1, (int)thumbTop, ScrollbarWidth - 2, thumbHeight);
            using (var thumbBrush = new LinearGradientBrush(thumbRect, Color.FromArgb(40, 200, 255), Color.FromArgb(30, 140, 200), 90f))
                g.FillRectangle(thumbBrush, thumbRect);
            using (var borderPen = new Pen(Color.FromArgb(80, 220, 255)))
                g.DrawRectangle(borderPen, thumbRect);
        }
    }
}
