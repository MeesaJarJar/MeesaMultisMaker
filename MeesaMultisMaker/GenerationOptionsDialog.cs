using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    public class GenerationOptionsDialog : Form
    {
        private TrackBar randomnessTrackBar;
        private Label randomnessValueLabel;
        private CheckBox useSourceSizeCheckBox;
        private NumericUpDown widthNumeric;
        private NumericUpDown heightNumeric;
        private Button generateButton;
        private Button cancelButton;
        private Label infoLabel;

        public float Randomness { get; private set; }
        public bool UseSourceSize { get; private set; }
        public int GridWidth { get; private set; }
        public int GridHeight { get; private set; }

        // Event to trigger generation without closing the dialog
        public event Action GenerateRequested;

        public GenerationOptionsDialog(List<List<PlacedObject>> trainingData)
        {
            InitializeComponent(trainingData);
        }

        private void InitializeComponent(List<List<PlacedObject>> trainingData)
        {
            this.Text = "Generate Variant Structure";
            this.Width = 450;
            this.Height = 350;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = true;

            int y = 10;

            // Info label
            infoLabel = new Label
            {
                Left = 10,
                Top = y,
                Width = 420,
                Height = 60,
                Text = $"Training Examples: {trainingData.Count}\n" +
            $"Average Size: {GetAverageSize(trainingData)}\n" +
                        $"Unique Tiles: {CountUniqueTiles(trainingData)}",
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.LightYellow,
                Padding = new Padding(5)
            };
            this.Controls.Add(infoLabel);
            y += 70;

            // Randomness slider
            var randomnessLabel = new Label
            {
                Left = 10,
                Top = y,
                Width = 150,
                Text = "Randomness:"
            };
            this.Controls.Add(randomnessLabel);

            randomnessTrackBar = new TrackBar
            {
                Left = 160,
                Top = y,
                Width = 200,
                Minimum = 0,
                Maximum = 100,
                Value = 15,
                TickFrequency = 10
            };
            randomnessTrackBar.ValueChanged += (s, e) =>
        {
            float val = randomnessTrackBar.Value / 100f;
            randomnessValueLabel.Text = $"{val:F2} ({GetRandomnessDescription(val)})";
        };
            this.Controls.Add(randomnessTrackBar);

            randomnessValueLabel = new Label
            {
                Left = 370,
                Top = y,
                Width = 70,
                Text = "0.15 (Low)"
            };
            this.Controls.Add(randomnessValueLabel);
            y += 50;

            var randomnessHint = new Label
            {
                Left = 160,
                Top = y,
                Width = 260,
                Height = 30,
                Text = "0 = Exact copy, 1 = Maximum variation",
                ForeColor = Color.Gray,
                Font = new Font(this.Font, FontStyle.Italic)
            };
            this.Controls.Add(randomnessHint);
            y += 40;

            // Size options
            useSourceSizeCheckBox = new CheckBox
            {
                Left = 10,
                Top = y,
                Width = 420,
                Text = "Use source structure size (recommended for preserving macro features)",
                Checked = true
            };
            useSourceSizeCheckBox.CheckedChanged += (s, e) =>
                 {
                     widthNumeric.Enabled = !useSourceSizeCheckBox.Checked;
                     heightNumeric.Enabled = !useSourceSizeCheckBox.Checked;
                 };
            this.Controls.Add(useSourceSizeCheckBox);
            y += 30;

            var sizeLabel = new Label
            {
                Left = 10,
                Top = y,
                Width = 150,
                Text = "Custom Grid Size:"
            };
            this.Controls.Add(sizeLabel);

            var avgSize = GetAverageSize(trainingData);
            var defaultSize = ParseSize(avgSize);

            widthNumeric = new NumericUpDown
            {
                Left = 160,
                Top = y,
                Width = 60,
                Minimum = 1,
                Maximum = 50,
                Value = defaultSize.Width,
                Enabled = false
            };
            this.Controls.Add(widthNumeric);

            var xLabel = new Label { Left = 225, Top = y + 3, Width = 15, Text = "x" };
            this.Controls.Add(xLabel);

            heightNumeric = new NumericUpDown
            {
                Left = 245,
                Top = y,
                Width = 60,
                Minimum = 1,
                Maximum = 50,
                Value = defaultSize.Height,
                Enabled = false
            };
            this.Controls.Add(heightNumeric);
            y += 50;

            // Buttons
            generateButton = new Button
            {
                Text = "Generate",
                Left = 200,
                Top = y,
                Width = 100,
                Height = 35,
                BackColor = Color.LightGreen
            };
            generateButton.Click += (s, e) =>
              {
                  Randomness = randomnessTrackBar.Value / 100f;
                  UseSourceSize = useSourceSizeCheckBox.Checked;
                  GridWidth = (int)widthNumeric.Value;
                  GridHeight = (int)heightNumeric.Value;

                  // Trigger generation event instead of closing
                  GenerateRequested?.Invoke();
              };
            this.Controls.Add(generateButton);

            cancelButton = new Button
            {
                Text = "Close",
                Left = 310,
                Top = y,
                Width = 100,
                Height = 35,
                DialogResult = DialogResult.Cancel
            };
            this.Controls.Add(cancelButton);

            this.CancelButton = cancelButton;
        }

        private string GetAverageSize(List<List<PlacedObject>> trainingData)
        {
            if (trainingData.Count == 0) return "0x0";

            int totalWidth = 0;
            int totalHeight = 0;

            foreach (var structure in trainingData)
            {
                if (structure.Count == 0) continue;
                int minX = structure.Min(o => o.GridX);
                int maxX = structure.Max(o => o.GridX);
                int minY = structure.Min(o => o.GridY);
                int maxY = structure.Max(o => o.GridY);

                totalWidth += (maxX - minX + 1);
                totalHeight += (maxY - minY + 1);
            }

            int avgW = totalWidth / trainingData.Count;
            int avgH = totalHeight / trainingData.Count;
            return $"{avgW}x{avgH}";
        }

        private Size ParseSize(string sizeStr)
        {
            var parts = sizeStr.Split('x');
            if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
            {
                return new Size(w, h);
            }
            return new Size(10, 10);
        }

        private int CountUniqueTiles(List<List<PlacedObject>> trainingData)
        {
            var uniqueTiles = new HashSet<string>();
            foreach (var structure in trainingData)
            {
                foreach (var obj in structure)
                {
                    uniqueTiles.Add(obj.GraphicId);
                }
            }
            return uniqueTiles.Count;
        }

        private string GetRandomnessDescription(float value)
        {
            if (value < 0.2f) return "Very Low";
            if (value < 0.4f) return "Low";
            if (value < 0.6f) return "Medium";
            if (value < 0.8f) return "High";
            return "Very High";
        }
    }
}
