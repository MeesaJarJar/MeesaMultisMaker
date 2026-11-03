using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    /// <summary>
    /// One candidate artwork for a repeated ItemID.
    /// The Bitmap is owned by the caller, never by the dialog.
    /// </summary>
    public class UnifyVariant
    {
        public Bitmap Image;
        public bool IsRegen;
    }

    /// <summary>
    /// One repeated ItemID needing (or holding) a style choice.
    /// </summary>
    public class UnifyGroup
    {
        public string GraphicId;
        public int MemberCount;
        public List<UnifyVariant> Variants = new List<UnifyVariant>();
        public int DefaultIndex;
    }

    /// <summary>
    /// Lets the user pick the winning artwork per repeated ItemID.
    /// Returns the choice as GraphicId -> variant Bitmap (caller-owned ref).
    ///
    /// HANDLE-SAFETY: earlier revisions built 3 windowed controls
    /// (Panel + PictureBox + RadioButton) PER variant, so hundreds of
    /// variants exhausted the ~10k USER handle limit ("Error creating
    /// window handle"). This revision builds exactly ONE ListView per
    /// group (plus its header Label). Thumbnails are 96px clones owned
    /// and disposed by the dialog; caller-owned variant Bitmaps are
    /// NEVER disposed here.
    /// </summary>
    public class UnifyPickerDialog : Form
    {
        public readonly Dictionary<string, Bitmap> ChosenById =
            new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        private const int ThumbSize = 96;
        private const int VirtualThreshold = 500;

        private readonly List<UnifyGroup> groups;
        private readonly List<int> chosenIndex;
        private readonly List<ListView> groupViews = new List<ListView>();
        private readonly List<ImageList> imageLists = new List<ImageList>();
        private readonly List<List<Bitmap>> thumbStore = new List<List<Bitmap>>();
        private readonly List<List<string>> itemTags = new List<List<string>>();
        private bool disposedDialog;

        public UnifyPickerDialog(List<UnifyGroup> groups)
        {
            this.groups = groups;
            chosenIndex = groups.Select(g => Math.Max(0, Math.Min(g.DefaultIndex, g.Variants.Count - 1))).ToList();

            Text = "Unify Same IDs - pick the winning artwork";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(30, 30, 40);
            ForeColor = Color.White;
            int rowH = 178;
            int h = Math.Min(620, 100 + groups.Count * rowH);
            Size = new Size(580, Math.Max(240, h));

            var scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(30, 30, 40),
                Padding = new Padding(10)
            };
            Controls.Add(scroll);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 48, BackColor = Color.FromArgb(24, 24, 32) };
            Controls.Add(bottom);

            var okBtn = new Button
            {
                Text = "Apply",
                DialogResult = DialogResult.OK,
                Left = 380,
                Top = 8,
                Width = 80,
                Height = 30
            };
            okBtn.Click += (s, e) => CollectChoices();
            var cancelBtn = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Left = 470,
                Top = 8,
                Width = 80,
                Height = 30
            };
            bottom.Controls.Add(okBtn);
            bottom.Controls.Add(cancelBtn);
            AcceptButton = okBtn;
            CancelButton = cancelBtn;

            int y = 6;
            for (int gi = 0; gi < groups.Count; gi++)
            {
                var g = groups[gi];
                int capturedGi = gi;
                var header = new Label
                {
                    Text = string.Format("{0} - {1} tiles, {2} artworks (pick the winner)", g.GraphicId, g.MemberCount, g.Variants.Count),
                    Left = 6,
                    Top = y,
                    Width = 520,
                    Height = 20,
                    ForeColor = Color.Cyan,
                    Font = new Font(Font.FontFamily, 9f, FontStyle.Bold)
                };
                scroll.Controls.Add(header);
                y += 24;

                // Build 96px thumbnail clones (dialog-owned) + tags. Never hand
                // full-size (possibly 4096px) caller Bitmaps to the UI.
                var thumbs = new List<Bitmap>(g.Variants.Count);
                var tags = new List<string>(g.Variants.Count);
                for (int vi = 0; vi < g.Variants.Count; vi++)
                {
                    var v = g.Variants[vi];
                    int vw, vh;
                    bool usable;
                    Bitmap thumb = MakeThumb(v.Image, out vw, out vh, out usable);
                    thumbs.Add(thumb);
                    tags.Add(BuildTag(vi, v, vw, vh, usable));
                }
                thumbStore.Add(thumbs);
                itemTags.Add(tags);

                var imgList = new ImageList
                {
                    ImageSize = new Size(ThumbSize, ThumbSize),
                    ColorDepth = ColorDepth.Depth32Bit
                };
                foreach (var t in thumbs)
                {
                    try { imgList.Images.Add(t); }
                    catch { /* keep going; missing thumb shows blank */ }
                }
                imageLists.Add(imgList);

                bool useVirtual = g.Variants.Count > VirtualThreshold;
                var view = new ListView
                {
                    Left = 6,
                    Top = y,
                    Width = 520,
                    Height = 138,
                    View = View.LargeIcon,
                    LargeImageList = imgList,
                    MultiSelect = false,
                    HideSelection = false,
                    FullRowSelect = true,
                    LabelWrap = true,
                    Scrollable = true,
                    BorderStyle = BorderStyle.FixedSingle,
                    BackColor = Color.FromArgb(24, 24, 32),
                    ForeColor = Color.White,
                    UseCompatibleStateImageBehavior = false
                };

                if (useVirtual)
                {
                    // VirtualMode avoids materializing thousands of
                    // ListViewItem objects; the ImageList above is the
                    // thumbnail cache (pre-filled 96px clones).
                    view.VirtualMode = true;
                    view.VirtualListSize = g.Variants.Count;
                    view.RetrieveVirtualItem += (s, e) =>
                    {
                        try
                        {
                            string text = itemTags[capturedGi][e.ItemIndex];
                            e.Item = new ListViewItem(text, e.ItemIndex);
                            e.Item.Tag = e.ItemIndex;
                        }
                        catch
                        {
                            e.Item = new ListViewItem("#?", 0);
                        }
                    };
                    view.CacheVirtualItems += (s, e) => { /* thumbs pre-cached */ };
                    view.VirtualItemsSelectionRangeChanged += (s, e) =>
                    {
                        try
                        {
                            if (e.IsSelected)
                                chosenIndex[capturedGi] = Math.Max(0, Math.Min(e.StartIndex, groups[capturedGi].Variants.Count - 1));
                        }
                        catch { }
                    };
                    view.SelectedIndexChanged += (s, e) =>
                    {
                        try
                        {
                            if (view.SelectedIndices.Count > 0)
                                chosenIndex[capturedGi] = view.SelectedIndices[0];
                        }
                        catch { }
                    };
                }
                else
                {
                    for (int vi = 0; vi < g.Variants.Count; vi++)
                    {
                        var item = new ListViewItem(tags[vi], vi);
                        item.Tag = vi;
                        view.Items.Add(item);
                    }
                    view.SelectedIndexChanged += (s, e) =>
                    {
                        try
                        {
                            if (view.SelectedIndices.Count > 0)
                                chosenIndex[capturedGi] = view.SelectedIndices[0];
                        }
                        catch { }
                    };
                    view.ItemSelectionChanged += (s, e) =>
                    {
                        try
                        {
                            if (e.IsSelected && e.Item != null && e.Item.Tag is int)
                                chosenIndex[capturedGi] = (int)e.Item.Tag;
                        }
                        catch { }
                    };
                }

                scroll.Controls.Add(view);
                groupViews.Add(view);
                y += 144;

                // Default selection = DefaultIndex (single winner per ID).
                try
                {
                    int def = chosenIndex[gi];
                    if (def >= 0 && def < g.Variants.Count)
                    {
                        if (useVirtual)
                        {
                            view.SelectedIndices.Add(def);
                            try { view.EnsureVisible(def); } catch { }
                        }
                        else if (def < view.Items.Count)
                        {
                            view.Items[def].Selected = true;
                            view.Items[def].Focused = true;
                            try { view.EnsureVisible(def); } catch { }
                        }
                    }
                }
                catch { }
            }
        }

        private static string BuildTag(int vi, UnifyVariant v, int vw, int vh, bool usable)
        {
            if (!usable)
                return string.Format("#{0} (unusable)", vi + 1);
            return string.Format("#{0}{1} ({2}x{3})", vi + 1, v != null && v.IsRegen ? " AI" : "", vw, vh);
        }

        private static Bitmap MakeThumb(Image src, out int vw, out int vh, out bool usable)
        {
            vw = 0; vh = 0; usable = false;
            try
            {
                if (src == null) return MakePlaceholderThumb();
                vw = src.Width; vh = src.Height;
                usable = vw > 0 && vh > 0;
            }
            catch { usable = false; }
            if (!usable) return MakePlaceholderThumb();
            try
            {
                float scale = Math.Min((float)ThumbSize / vw, (float)ThumbSize / vh);
                int dw = Math.Max(1, (int)(vw * scale));
                int dh = Math.Max(1, (int)(vh * scale));
                var thumb = new Bitmap(ThumbSize, ThumbSize);
                using (var gr = Graphics.FromImage(thumb))
                {
                    gr.Clear(Color.FromArgb(24, 24, 32));
                    gr.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    gr.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    gr.SmoothingMode = SmoothingMode.HighQuality;
                    int dx = (ThumbSize - dw) / 2;
                    int dy = (ThumbSize - dh) / 2;
                    gr.DrawImage(src, new Rectangle(dx, dy, dw, dh), new Rectangle(0, 0, vw, vh), GraphicsUnit.Pixel);
                }
                return thumb;
            }
            catch
            {
                usable = false;
                try { return MakePlaceholderThumb(); }
                catch { return new Bitmap(ThumbSize, ThumbSize); }
            }
        }

        private static Bitmap MakePlaceholderThumb()
        {
            var bmp = new Bitmap(ThumbSize, ThumbSize);
            using (var gr = Graphics.FromImage(bmp))
            {
                gr.Clear(Color.FromArgb(40, 40, 50));
                using (var pen = new Pen(Color.Gray))
                {
                    gr.DrawRectangle(pen, 2, 2, ThumbSize - 5, ThumbSize - 5);
                    gr.DrawLine(pen, 2, 2, ThumbSize - 3, ThumbSize - 3);
                    gr.DrawLine(pen, ThumbSize - 3, 2, 2, ThumbSize - 3);
                }
            }
            return bmp;
        }

        private void CollectChoices()
        {
            ChosenById.Clear();
            for (int gi = 0; gi < groups.Count; gi++)
            {
                var g = groups[gi];
                if (g.Variants == null || g.Variants.Count == 0) continue;
                int idx = Math.Max(0, Math.Min(chosenIndex[gi], g.Variants.Count - 1));
                // The chosen artwork may have been disposed upstream; fall back
                // to the first usable variant. If none is usable, omit the key
                // so the caller keeps its own safe fallback (never a corpse).
                Bitmap pick = IsUsable(g.Variants[idx].Image) ? g.Variants[idx].Image : null;
                if (pick == null)
                    pick = g.Variants.Where(v => IsUsable(v.Image)).Select(v => v.Image).FirstOrDefault();
                if (pick != null)
                    ChosenById[g.GraphicId] = pick;
            }
        }

        private static bool IsUsable(Image img)
        {
            if (img == null) return false;
            try { return img.Width > 0 && img.Height > 0; }
            catch { return false; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposedDialog)
            {
                disposedDialog = true;
                // Dispose dialog-owned 96px thumbnails + ImageLists only.
                // Caller-owned variant Bitmaps are NEVER disposed here.
                try
                {
                    if (imageLists != null)
                        foreach (var il in imageLists)
                        { try { if (il != null) il.Dispose(); } catch { } }
                }
                catch { }
                try
                {
                    if (thumbStore != null)
                        foreach (var lst in thumbStore)
                        {
                            if (lst == null) continue;
                            foreach (var t in lst)
                            { try { if (t != null) t.Dispose(); } catch { } }
                        }
                }
                catch { }
            }
            base.Dispose(disposing);
        }
    }
}
