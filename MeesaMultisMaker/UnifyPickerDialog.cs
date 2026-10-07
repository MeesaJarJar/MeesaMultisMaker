using System;
using System.Collections.Generic;
using System.Drawing;
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
    /// </summary>
    public class UnifyPickerDialog : Form
    {
        public readonly Dictionary<string, Bitmap> ChosenById =
            new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        private readonly List<UnifyGroup> groups;
        private readonly List<int> chosenIndex;

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

                var flow = new FlowLayoutPanel
                {
                    Left = 6,
                    Top = y,
                    Width = 520,
                    Height = 138,
                    AutoScroll = true,
                    WrapContents = true,
                    BackColor = Color.FromArgb(24, 24, 32),
                    BorderStyle = BorderStyle.FixedSingle
                };
                scroll.Controls.Add(flow);
                y += 144;

                for (int vi = 0; vi < g.Variants.Count; vi++)
                {
                    var v = g.Variants[vi];
                    int capturedGi = gi;
                    int capturedVi = vi;

                    var cell = new Panel { Width = 116, Height = 128, BackColor = Color.Transparent };
                    var pic = new PictureBox
                    {
                        Image = v.Image,
                        SizeMode = PictureBoxSizeMode.Zoom,
                        Left = 8,
                        Top = 2,
                        Width = 100,
                        Height = 96,
                        BorderStyle = BorderStyle.FixedSingle
                    };
                    string tag = string.Format("#{0}{1} ({2}x{3})", vi + 1, v.IsRegen ? " AI" : "", v.Image.Width, v.Image.Height);
                    var radio = new RadioButton
                    {
                        Text = tag,
                        Left = 4,
                        Top = 100,
                        Width = 108,
                        Height = 22,
                        ForeColor = Color.White,
                        Checked = (vi == chosenIndex[gi]),
                        Tag = capturedVi
                    };
                    radio.CheckedChanged += (s, e) =>
                    {
                        var rb = s as RadioButton;
                        if (rb != null && rb.Checked)
                            chosenIndex[capturedGi] = (int)rb.Tag;
                    };
                    pic.Click += (s, e) => { radio.Checked = true; };
                    cell.Controls.Add(pic);
                    cell.Controls.Add(radio);
                    flow.Controls.Add(cell);
                }
            }
        }

        private void CollectChoices()
        {
            ChosenById.Clear();
            for (int gi = 0; gi < groups.Count; gi++)
            {
                var g = groups[gi];
                int idx = Math.Max(0, Math.Min(chosenIndex[gi], g.Variants.Count - 1));
                ChosenById[g.GraphicId] = g.Variants[idx].Image;
            }
        }
    }
}
