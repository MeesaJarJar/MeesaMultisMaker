using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    /// <summary>
    /// "Split Multi by Z-Bands + Category" dialog (Form1 main canvas).
    ///
    /// Shows the Z histogram (owner-drawn bars, red cut lines), editable band
    /// cuts (add/remove numeric + Merge/Split band buttons) and the resulting
    /// band x category parts with a per-part color key. Every change pushes a
    /// live canvas tint preview through setPreview (paint-time only, art is
    /// never mutated). Explode returns DialogResult.OK; Form1 then performs
    /// the actual PushUndo + displacement. Close/X cancels (Form1 clears the
    /// preview on return either way).
    ///
    /// Pattern follows SettingsForm / GenerationOptionsDialog: a plain Form
    /// with code-built controls + HolographicTheme styling, no .resx.
    /// </summary>
    public class ZBandSplitDialog : Form
    {
        private readonly List<PlacedObject> objects; // live canvas refs
        private readonly Func<PlacedObject, int, int, bool, ZPartCategory> classify;
        private readonly Action<Dictionary<PlacedObject, Color>> setPreview;
        private readonly Action<List<PlacedObject>> selectObjects;

        private readonly SortedDictionary<int, int> histogram;
        private readonly SortedSet<int> distinctZ;
        private readonly List<int> cuts = new List<int>();
        private List<ZBand> bands = new List<ZBand>();
        private List<ZPart> parts = new List<ZPart>();

        private HistogramPanel histPanel;
        private ListBox cutsListBox;
        private NumericUpDown cutNumeric;
        private ListBox bandsListBox;
        private ListBox partsListBox;
        private Label statusLabel;
        private Button explodeButton;
        private Button autoCutsButton;

        /// <summary>Final parts at Explode time (band x category, non-empty).</summary>
        public List<ZPart> ResultParts { get; private set; }
        /// <summary>Final cut Zs at Explode time (ascending).</summary>
        public List<int> ResultCuts { get; private set; }

        public ZBandSplitDialog(
            List<PlacedObject> liveObjects,
            Func<PlacedObject, int, int, bool, ZPartCategory> classifyFn,
            Action<Dictionary<PlacedObject, Color>> setPreviewFn,
            Action<List<PlacedObject>> selectObjectsFn)
        {
            objects = liveObjects ?? new List<PlacedObject>();
            classify = classifyFn;
            setPreview = setPreviewFn;
            selectObjects = selectObjectsFn;

            histogram = ZBandSplitter.BuildHistogram(objects);
            distinctZ = new SortedSet<int>(histogram.Keys);
            cuts.AddRange(ZBandSplitter.ProposeCuts(distinctZ));

            InitializeComponent();
            Recompute();
        }

        // ---------------- UI construction ----------------

        private void InitializeComponent()
        {
            Text = "Split Multi by Z-Bands + Category";
            Width = 780;
            Height = 640;
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = true;
            MinimizeBox = false;
            MinimumSize = new Size(640, 520);
            HolographicTheme.ApplyToForm(this);

            int margin = 10;
            int y = margin;

            statusLabel = new Label
            {
                Left = margin,
                Top = y,
                Width = ClientSize.Width - margin * 2,
                Height = 34,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            HolographicTheme.ApplyToLabel(statusLabel);
            Controls.Add(statusLabel);
            y += 40;

            histPanel = new HistogramPanel
            {
                Left = margin,
                Top = y,
                Width = ClientSize.Width - margin * 2,
                Height = 150,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(histPanel);
            y += 158;

            int colW = (ClientSize.Width - margin * 3) / 3;

            // ---- Cuts column ----
            var cutsHeader = MakeHeader("Band cuts (top Z of lower band)", margin, y, colW);
            Controls.Add(cutsHeader);
            cutsListBox = new ListBox
            {
                Left = margin,
                Top = y + 22,
                Width = colW,
                Height = 150,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            HolographicTheme.ApplyToListBox(cutsListBox);
            Controls.Add(cutsListBox);

            cutNumeric = new NumericUpDown
            {
                Left = margin,
                Top = y + 178,
                Width = 90,
                Minimum = -999,
                Maximum = 999
            };
            HolographicTheme.ApplyToNumericUpDown(cutNumeric);
            Controls.Add(cutNumeric);

            var addCutBtn = MakeButton("Add", margin + 96, y + 176, 70, (s, e) => AddCut((int)cutNumeric.Value));
            Controls.Add(addCutBtn);
            var delCutBtn = MakeButton("Remove", margin + 172, y + 176, 80, (s, e) => RemoveSelectedCut());
            Controls.Add(delCutBtn);

            autoCutsButton = MakeButton("Auto cuts", margin, y + 206, 110, (s, e) => AutoCuts(), ButtonStyle.Accent);
            Controls.Add(autoCutsButton);

            // ---- Bands column ----
            int bx = margin * 2 + colW;
            Controls.Add(MakeHeader("Bands (Merge / Split)", bx, y, colW));
            bandsListBox = new ListBox
            {
                Left = bx,
                Top = y + 22,
                Width = colW,
                Height = 150,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            HolographicTheme.ApplyToListBox(bandsListBox);
            Controls.Add(bandsListBox);
            Controls.Add(MakeButton("Merge v next", bx, y + 178, 110, (s, e) => MergeSelectedBand()));
            Controls.Add(MakeButton("Split at gap", bx + 116, y + 178, 110, (s, e) => SplitSelectedBand()));

            // ---- Parts column ----
            int px = margin * 3 + colW * 2;
            Controls.Add(MakeHeader("Parts (band x category)", px, y, colW));
            partsListBox = new ListBox
            {
                Left = px,
                Top = y + 22,
                Width = ClientSize.Width - px - margin,
                Height = 180,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 20
            };
            HolographicTheme.ApplyToListBox(partsListBox);
            partsListBox.DrawItem += PartsListBox_DrawItem;
            Controls.Add(partsListBox);
            Controls.Add(MakeButton("Select on canvas", px, y + 208, 130, (s, e) => SelectSelectedPart()));

            y += 244;

            var ruleLabel = new Label
            {
                Left = margin,
                Top = y,
                Width = ClientSize.Width - margin * 2,
                Height = 52,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Font = new Font(Font.FontFamily, 7.5f),
                Text = "Category rule: TileData flags first (Door/Stairs > Roof > Wall > Bridge/Surface > Impassable+h>0 Wall > "
                    + "Impassable+h0 top Roof), else Z-heuristic vs band base (<20 = Wall). "
                    + "Objects on a cut Z join the LOWER band. Preview tint is paint-time only; Explode moves live objects (undoable)."
            };
            HolographicTheme.ApplyToLabel(ruleLabel);
            Controls.Add(ruleLabel);
            y += 56;

            explodeButton = MakeButton("Explode", margin, y, 130, (s, e) => ConfirmExplode(), ButtonStyle.Success);
            explodeButton.Height = 32;
            Controls.Add(explodeButton);
            var closeBtn = MakeButton("Close", margin + 140, y, 110, (s, e) => { DialogResult = DialogResult.Cancel; }, ButtonStyle.Default);
            closeBtn.Height = 32;
            Controls.Add(closeBtn);
        }

        private Label MakeHeader(string text, int left, int top, int width)
        {
            var l = new Label { Text = text, Left = left, Top = top, Width = width, Height = 20 };
            HolographicTheme.ApplyToLabel(l, true);
            return l;
        }

        private Button MakeButton(string text, int left, int top, int width, EventHandler onClick, ButtonStyle style)
        {
            var b = new Button { Text = text, Left = left, Top = top, Width = width, Height = 25 };
            b.Click += onClick;
            HolographicTheme.ApplyToButton(b, style);
            return b;
        }

        private Button MakeButton(string text, int left, int top, int width, EventHandler onClick)
        {
            return MakeButton(text, left, top, width, onClick, ButtonStyle.Default);
        }

        // ---------------- recompute + preview ----------------

        private void Recompute()
        {
            cuts.Sort();
            for (int i = cuts.Count - 1; i >= 0; i--)
            {
                if (i > 0 && cuts[i] == cuts[i - 1]) cuts.RemoveAt(i);
            }
            if (objects.Count == 0)
            {
                bands = new List<ZBand>();
                parts = new List<ZPart>();
            }
            else
            {
                int lo = distinctZ.Min;
                int hi = distinctZ.Max;
                bands = ZBandSplitter.BuildBands(cuts, lo, hi);
                parts = ZBandSplitter.BuildParts(objects, bands, classify);
            }
            RefreshLists();
            PushPreview();
        }

        private void RefreshLists()
        {
            cutsListBox.BeginUpdate();
            cutsListBox.Items.Clear();
            foreach (int c in cuts) cutsListBox.Items.Add("Z <= " + c);
            cutsListBox.EndUpdate();

            bandsListBox.BeginUpdate();
            bandsListBox.Items.Clear();
            foreach (var b in bands) bandsListBox.Items.Add(b.ToString());
            bandsListBox.EndUpdate();

            partsListBox.BeginUpdate();
            partsListBox.Items.Clear();
            foreach (var p in parts) partsListBox.Items.Add(p);
            partsListBox.EndUpdate();

            histPanel.Data = histogram;
            histPanel.Cuts = new List<int>(cuts);
            histPanel.Bands = bands;
            histPanel.Invalidate();

            if (objects.Count == 0)
            {
                statusLabel.Text = "Canvas is empty - place objects first (Explode disabled).";
            }
            else if (distinctZ.Count == 1)
            {
                statusLabel.Text = string.Format(
                    "No gaps found: single Z ({0}) on {1} object(s) - category-only split into {2} part(s).",
                    distinctZ.Min, objects.Count, parts.Count);
            }
            else if (cuts.Count == 0)
            {
                statusLabel.Text = string.Format(
                    "No gaps >= {0} found ({1} distinct Z) - single band, {2} part(s) by category. Add cuts manually or Explode as-is.",
                    ZBandSplitter.MinGap, distinctZ.Count, parts.Count);
            }
            else
            {
                int hidden = objects.Count(o => o != null && o.Hidden);
                statusLabel.Text = string.Format(
                    "{0} object(s) ({1} hidden), {2} distinct Z, {3} band(s), {4} part(s). Cuts: {5}.",
                    objects.Count, hidden, distinctZ.Count, bands.Count, parts.Count,
                    string.Join(", ", cuts.Select(c => "Z<=" + c)));
            }
            explodeButton.Enabled = parts.Count > 0;
        }

        private void PushPreview()
        {
            try
            {
                if (setPreview == null) return;
                var map = new Dictionary<PlacedObject, Color>();
                foreach (var p in parts)
                    foreach (var o in p.Members)
                        if (o != null && !map.ContainsKey(o)) map[o] = p.Color;
                setPreview(map);
            }
            catch { }
        }

        // ---------------- cut editing ----------------

        private void AddCut(int z)
        {
            if (objects.Count == 0) return;
            if (!cuts.Contains(z))
            {
                cuts.Add(z);
                Recompute();
            }
        }

        private void RemoveSelectedCut()
        {
            if (cutsListBox.SelectedIndex >= 0 && cutsListBox.SelectedIndex < cuts.Count)
            {
                cuts.RemoveAt(cutsListBox.SelectedIndex);
                Recompute();
            }
        }

        private void AutoCuts()
        {
            cuts.Clear();
            cuts.AddRange(ZBandSplitter.ProposeCuts(distinctZ));
            Recompute();
        }

        private void MergeSelectedBand()
        {
            int i = bandsListBox.SelectedIndex;
            if (i < 0 || i >= bands.Count - 1) return; // last band has no upper cut
            int cut = bands[i].MaxZ;
            cuts.Remove(cut);
            Recompute();
        }

        private void SplitSelectedBand()
        {
            int i = bandsListBox.SelectedIndex;
            if (i < 0 || i >= bands.Count) return;
            var band = bands[i];
            // Largest internal empty gap between distinct Zs inside this band.
            var inside = distinctZ.Where(z => z >= band.MinZ && z <= band.MaxZ).ToList();
            if (inside.Count < 2)
            {
                MessageBox.Show("Band \"" + band.Name + "\" holds a single Z - nothing to split.",
                    "Split Band", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int bestLower = inside[0];
            int bestGap = 0;
            for (int k = 1; k < inside.Count; k++)
            {
                int gap = inside[k] - inside[k - 1];
                if (gap > bestGap) { bestGap = gap; bestLower = inside[k - 1]; }
            }
            if (bestGap < 2)
            {
                MessageBox.Show("Band \"" + band.Name + "\" has no empty Z gap - nothing to split.",
                    "Split Band", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!cuts.Contains(bestLower)) cuts.Add(bestLower);
            Recompute();
        }

        private void SelectSelectedPart()
        {
            try
            {
                var p = partsListBox.SelectedItem as ZPart;
                if (p == null || selectObjects == null) return;
                selectObjects(new List<PlacedObject>(p.Members));
            }
            catch { }
        }

        private void PartsListBox_DrawItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0 || e.Index >= parts.Count) return;
            var p = parts[e.Index];
            using (var sw = new SolidBrush(p.Color))
                e.Graphics.FillRectangle(sw, e.Bounds.Left + 2, e.Bounds.Top + 2, 16, e.Bounds.Height - 4);
            using (var pen = new Pen(Color.Black))
                e.Graphics.DrawRectangle(pen, e.Bounds.Left + 2, e.Bounds.Top + 2, 16, e.Bounds.Height - 4);
            bool sel = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using (var brush = new SolidBrush(sel ? SystemColors.HighlightText : partsListBox.ForeColor))
                e.Graphics.DrawString(p.Name + "  (" + p.Members.Count + ")",
                    e.Font, brush, e.Bounds.Left + 22, e.Bounds.Top + 2);
            e.DrawFocusRectangle();
        }

        private void ConfirmExplode()
        {
            if (parts.Count == 0) return;
            ResultParts = parts;
            ResultCuts = new List<int>(cuts);
            DialogResult = DialogResult.OK;
        }

        // ---------------- histogram (owner-drawn) ----------------

        private sealed class HistogramPanel : Panel
        {
            public SortedDictionary<int, int> Data = new SortedDictionary<int, int>();
            public List<int> Cuts = new List<int>();
            public List<ZBand> Bands = new List<ZBand>();

            public HistogramPanel()
            {
                DoubleBuffered = true;
                BackColor = Color.FromArgb(20, 26, 34);
                BorderStyle = BorderStyle.FixedSingle;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                if (Data == null || Data.Count == 0)
                {
                    using (var brush = new SolidBrush(Color.Gray))
                        g.DrawString("(no data)", Font, brush, 8, 8);
                    return;
                }
                int padL = 34, padR = 10, padT = 8, padB = 20;
                int plotW = Width - padL - padR;
                int plotH = Height - padT - padB;
                if (plotW <= 10 || plotH <= 10) return;

                var zs = Data.Keys.ToList();
                int lo = zs[0], hi = zs[zs.Count - 1];
                int span = Math.Max(1, hi - lo + 1);
                int max = Math.Max(1, Data.Values.Max());

                // Band background shading (alternating).
                if (Bands != null && Bands.Count > 1)
                {
                    for (int i = 0; i < Bands.Count; i++)
                    {
                        if (i % 2 == 1) continue;
                        float x0 = padL + (Bands[i].MinZ - lo) * (float)plotW / span;
                        float x1 = padL + (Bands[i].MaxZ - lo + 1) * (float)plotW / span;
                        using (var b = new SolidBrush(Color.FromArgb(24, 0, 255, 255)))
                            g.FillRectangle(b, x0, padT, x1 - x0, plotH);
                    }
                }

                // Bars: one per distinct Z, x centered in its Z slot.
                float slotW = (float)plotW / span;
                float barW = Math.Max(2f, Math.Min(slotW - 1f, 14f));
                using (var barBrush = new SolidBrush(Color.FromArgb(0, 200, 220)))
                using (var axisPen = new Pen(Color.Gray))
                using (var txtBrush = new SolidBrush(Color.LightGray))
                using (var small = new Font(Font.FontFamily, 7f))
                {
                    foreach (var kv in Data)
                    {
                        float cx = padL + (kv.Key - lo + 0.5f) * slotW;
                        float bh = (float)kv.Value / max * plotH;
                        g.FillRectangle(barBrush, cx - barW / 2f, padT + plotH - bh, barW, bh);
                    }
                    g.DrawLine(axisPen, padL, padT + plotH, padL + plotW, padT + plotH);
                    g.DrawString("Z " + lo, small, txtBrush, 2, padT + plotH - 6);
                    string hiTxt = "Z " + hi;
                    var sz = g.MeasureString(hiTxt, small);
                    g.DrawString(hiTxt, small, txtBrush, Width - padR - sz.Width, padT + plotH - 6);
                    string maxTxt = "x" + max;
                    g.DrawString(maxTxt, small, txtBrush, 2, padT);
                }

                // Cut lines (red) between the cut Z slot and the next slot.
                using (var cutPen = new Pen(Color.FromArgb(255, 90, 90), 2f))
                using (var cutBrush = new SolidBrush(Color.FromArgb(255, 150, 150)))
                using (var small = new Font(Font.FontFamily, 7f))
                {
                    foreach (int c in Cuts ?? new List<int>())
                    {
                        float x = padL + (c - lo + 1) * slotW;
                        g.DrawLine(cutPen, x, padT, x, padT + plotH);
                        g.DrawString("cut " + c, small, cutBrush, Math.Min(x + 2, Width - 46), padT + 2);
                    }
                }
            }
        }
    }
}
