using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    /// <summary>
    /// Holographic blue/teal theme for the application
    /// </summary>
    public static class HolographicTheme
    {
        // Main background colors
        public static readonly Color DarkBackground = Color.FromArgb(10, 15, 25);
        public static readonly Color PanelBackground = Color.FromArgb(15, 25, 40);
        public static readonly Color ControlBackground = Color.FromArgb(20, 35, 55);
        public static readonly Color InputBackground = Color.FromArgb(12, 20, 35);

        // Accent colors
        public static readonly Color CyanAccent = Color.FromArgb(0, 255, 255);
        public static readonly Color TealAccent = Color.FromArgb(0, 200, 200);
        public static readonly Color BlueAccent = Color.FromArgb(0, 150, 255);
        public static readonly Color GlowCyan = Color.FromArgb(100, 0, 255, 255);

        // Text colors
        public static readonly Color TextPrimary = Color.FromArgb(0, 255, 255);
        public static readonly Color TextSecondary = Color.FromArgb(150, 220, 230);
        public static readonly Color TextMuted = Color.FromArgb(100, 150, 170);

        // Border colors
        public static readonly Color BorderCyan = Color.FromArgb(0, 180, 200);
        public static readonly Color BorderDark = Color.FromArgb(30, 60, 80);
        public static readonly Color BorderGlow = Color.FromArgb(80, 0, 255, 255);

        // Button colors
        public static readonly Color ButtonBackground = Color.FromArgb(20, 50, 70);
        public static readonly Color ButtonHover = Color.FromArgb(30, 70, 100);
        public static readonly Color ButtonAccent = Color.FromArgb(0, 120, 150);
        public static readonly Color ButtonSuccess = Color.FromArgb(0, 150, 100);
        public static readonly Color ButtonWarning = Color.FromArgb(200, 150, 0);
        public static readonly Color ButtonDanger = Color.FromArgb(180, 50, 70);

        // Grid colors for canvas
        public static readonly Color GridLine = Color.FromArgb(40, 0, 200, 220);
        public static readonly Color GridLineBright = Color.FromArgb(60, 0, 255, 255);
        public static readonly Color CanvasBackground = Color.FromArgb(8, 12, 20);

        // Selection colors
        public static readonly Color SelectionBlue = Color.FromArgb(0, 150, 255);
        public static readonly Color SelectionCyan = Color.FromArgb(0, 255, 255);
        public static readonly Color MarqueeSelect = Color.FromArgb(40, 0, 200, 255);
        public static readonly Color MarqueeDeselect = Color.FromArgb(40, 255, 100, 100);

        /// <summary>
        /// Apply theme to a Form
        /// </summary>
        public static void ApplyToForm(Form form)
        {
            form.BackColor = DarkBackground;
            form.ForeColor = TextPrimary;
        }

        /// <summary>
        /// Apply theme to a Panel
        /// </summary>
        public static void ApplyToPanel(Panel panel, bool isHeader = false)
        {
            panel.BackColor = isHeader ? ControlBackground : PanelBackground;
            panel.ForeColor = TextPrimary;
        }

        /// <summary>
        /// Apply theme to a Button with optional accent style
        /// </summary>
        public static void ApplyToButton(Button button, ButtonStyle style = ButtonStyle.Default)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = BorderCyan;
            button.ForeColor = TextPrimary;
            button.Font = new Font(button.Font.FontFamily, button.Font.Size, FontStyle.Bold);

            switch (style)
            {
                case ButtonStyle.Success:
                    button.BackColor = ButtonSuccess;
                    button.FlatAppearance.BorderColor = Color.FromArgb(0, 200, 150);
                    break;
                case ButtonStyle.Warning:
                    button.BackColor = ButtonWarning;
                    button.FlatAppearance.BorderColor = Color.FromArgb(255, 200, 0);
                    button.ForeColor = DarkBackground;
                    break;
                case ButtonStyle.Danger:
                    button.BackColor = ButtonDanger;
                    button.FlatAppearance.BorderColor = Color.FromArgb(255, 80, 100);
                    break;
                case ButtonStyle.Accent:
                    button.BackColor = ButtonAccent;
                    button.FlatAppearance.BorderColor = CyanAccent;
                    break;
                default:
                    button.BackColor = ButtonBackground;
                    break;
            }
        }

        /// <summary>
        /// Apply theme to a TextBox
        /// </summary>
        public static void ApplyToTextBox(TextBox textBox)
        {
            textBox.BackColor = InputBackground;
            textBox.ForeColor = TextPrimary;
            textBox.BorderStyle = BorderStyle.FixedSingle;
        }

        /// <summary>
        /// Apply theme to a Label
        /// </summary>
        public static void ApplyToLabel(Label label, bool isHeader = false)
        {
            label.ForeColor = isHeader ? CyanAccent : TextSecondary;
            label.BackColor = Color.Transparent;
        }

        /// <summary>
        /// Apply theme to a ListBox
        /// </summary>
        public static void ApplyToListBox(ListBox listBox)
        {
            listBox.BackColor = InputBackground;
            listBox.ForeColor = TextPrimary;
            listBox.BorderStyle = BorderStyle.FixedSingle;
        }

        /// <summary>
        /// Apply theme to a ListView
        /// </summary>
        public static void ApplyToListView(ListView listView)
        {
            listView.BackColor = InputBackground;
            listView.ForeColor = TextPrimary;
            listView.BorderStyle = BorderStyle.FixedSingle;
        }

        /// <summary>
        /// Apply theme to a GroupBox
        /// </summary>
        public static void ApplyToGroupBox(GroupBox groupBox)
        {
            groupBox.BackColor = PanelBackground;
            groupBox.ForeColor = CyanAccent;
        }

        /// <summary>
        /// Apply theme to a NumericUpDown
        /// </summary>
        public static void ApplyToNumericUpDown(NumericUpDown numericUpDown)
        {
            numericUpDown.BackColor = InputBackground;
            numericUpDown.ForeColor = TextPrimary;
            numericUpDown.BorderStyle = BorderStyle.FixedSingle;
        }

        /// <summary>
        /// Apply theme to a ComboBox
        /// </summary>
        public static void ApplyToComboBox(ComboBox comboBox)
        {
            comboBox.BackColor = InputBackground;
            comboBox.ForeColor = TextPrimary;
            comboBox.FlatStyle = FlatStyle.Flat;
        }

        /// <summary>
        /// Apply theme to a CheckBox
        /// </summary>
        public static void ApplyToCheckBox(CheckBox checkBox)
        {
            checkBox.ForeColor = TextSecondary;
            checkBox.BackColor = Color.Transparent;
        }

        /// <summary>
        /// Apply theme to a RadioButton
        /// </summary>
        public static void ApplyToRadioButton(RadioButton radioButton)
        {
            radioButton.ForeColor = TextSecondary;
            radioButton.BackColor = Color.Transparent;
        }

        /// <summary>
        /// Apply theme to a TrackBar (limited styling available)
        /// </summary>
        public static void ApplyToTrackBar(TrackBar trackBar)
        {
            trackBar.BackColor = PanelBackground;
        }

        /// <summary>
        /// Apply theme to a SplitContainer
        /// </summary>
        public static void ApplyToSplitContainer(SplitContainer splitContainer)
        {
            splitContainer.BackColor = DarkBackground;
            splitContainer.Panel1.BackColor = PanelBackground;
            splitContainer.Panel2.BackColor = PanelBackground;
        }

        /// <summary>
        /// Apply theme to a PictureBox (canvas)
        /// </summary>
        public static void ApplyToCanvas(PictureBox pictureBox)
        {
            pictureBox.BackColor = CanvasBackground;
        }

        /// <summary>
        /// Apply theme to a DataGridView
        /// </summary>
        public static void ApplyToDataGridView(DataGridView dataGridView)
        {
            dataGridView.BackgroundColor = InputBackground;
            dataGridView.ForeColor = TextPrimary;
            dataGridView.GridColor = BorderDark;
            dataGridView.DefaultCellStyle.BackColor = InputBackground;
            dataGridView.DefaultCellStyle.ForeColor = TextPrimary;
            dataGridView.DefaultCellStyle.SelectionBackColor = ButtonAccent;
            dataGridView.DefaultCellStyle.SelectionForeColor = TextPrimary;
            dataGridView.ColumnHeadersDefaultCellStyle.BackColor = ControlBackground;
            dataGridView.ColumnHeadersDefaultCellStyle.ForeColor = CyanAccent;
            dataGridView.EnableHeadersVisualStyles = false;
            dataGridView.BorderStyle = BorderStyle.FixedSingle;
        }

        /// <summary>
        /// Recursively apply theme to all controls in a container
        /// </summary>
        public static void ApplyToAllControls(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                ApplyToControl(control);
                if (control.HasChildren)
                {
                    ApplyToAllControls(control);
                }
            }
        }

        /// <summary>
        /// Apply theme to a single control based on its type
        /// </summary>
        public static void ApplyToControl(Control control)
        {
            if (control is Button btn)
                ApplyToButton(btn);
            else if (control is TextBox txt)
                ApplyToTextBox(txt);
            else if (control is Label lbl)
                ApplyToLabel(lbl);
            else if (control is ListBox lst)
                ApplyToListBox(lst);
            else if (control is ListView lv)
                ApplyToListView(lv);
            else if (control is GroupBox grp)
                ApplyToGroupBox(grp);
            else if (control is NumericUpDown nud)
                ApplyToNumericUpDown(nud);
            else if (control is ComboBox cmb)
                ApplyToComboBox(cmb);
            else if (control is CheckBox chk)
                ApplyToCheckBox(chk);
            else if (control is TrackBar trk)
                ApplyToTrackBar(trk);
            else if (control is SplitContainer sc)
                ApplyToSplitContainer(sc);
            else if (control is PictureBox pb)
                ApplyToCanvas(pb);
            else if (control is DataGridView dgv)
                ApplyToDataGridView(dgv);
            else if (control is Panel pnl)
                ApplyToPanel(pnl);
        }

        /// <summary>
        /// Draw a glowing border around a rectangle
        /// </summary>
        public static void DrawGlowingBorder(Graphics g, Rectangle rect, Color glowColor, int glowSize = 3)
        {
            for (int i = glowSize; i > 0; i--)
            {
                int alpha = (int)(30 * ((float)i / glowSize));
                using (var pen = new Pen(Color.FromArgb(alpha, glowColor), i * 2))
                {
                    g.DrawRectangle(pen, rect);
                }
            }
            using (var pen = new Pen(glowColor))
            {
                g.DrawRectangle(pen, rect);
            }
        }

        /// <summary>
        /// Create a gradient brush for headers
        /// </summary>
        public static LinearGradientBrush CreateHeaderGradient(Rectangle rect)
        {
            return new LinearGradientBrush(rect, ControlBackground, PanelBackground, LinearGradientMode.Vertical);
        }
    }

    public enum ButtonStyle
    {
        Default,
        Success,
        Warning,
        Danger,
        Accent
    }
}
