using MeesaMultisMaker.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MeesaMultisMaker.Dialogs
{
    /// <summary>
    /// Dialog for splitting sprite sheets into individual sprites.
    /// Shows preview with detected sprites outlined.
    /// </summary>
    public class SpriteSheetSplitterDialog : Form
    {
        private Bitmap sourceImage;
        private List<SpriteSheetSplitter.DetectedSprite> detectedSprites;
        private SpriteSheetSplitter.SplitOptions options;

        private PictureBox previewPictureBox;
        private Label spriteCountLabel;
        private NumericUpDown toleranceNumeric;
        private NumericUpDown minWidthNumeric;
        private NumericUpDown minHeightNumeric;
        private NumericUpDown paddingNumeric;
        private CheckBox removeHaloCheckBox;
        private ComboBox sortOrderCombo;
        private Button detectButton;
        private Button okButton;
        private Button cancelButton;
        private Panel optionsPanel;

        public List<SpriteSheetSplitter.DetectedSprite> DetectedSprites => detectedSprites;

        public SpriteSheetSplitterDialog(Bitmap sourceImage)
        {
            this.sourceImage = sourceImage ?? throw new ArgumentNullException(nameof(sourceImage));
            
            options = new SpriteSheetSplitter.SplitOptions
            {
                ColorTolerance = 30,
                MinSpriteWidth = 5,
                MinSpriteHeight = 5,
                MinPixelCount = 25,
                Padding = 2,
                RemoveHalo = true,
                SortOrder = SpriteSheetSplitter.SpriteSortOrder.LeftToRightTopToBottom
            };

            InitializeUI();
            PerformDetection(); // Initial detection
        }

        private void InitializeUI()
        {
            Text = "Split Sprite Sheet";
            Width = 900;
            Height = 700;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;

            HolographicTheme.ApplyToForm(this);

            // Preview panel
            previewPictureBox = new PictureBox
            {
                Location = new Point(10, 10),
                Size = new Size(600, 600),
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(20, 20, 25)
            };
            Controls.Add(previewPictureBox);

            // Options panel
            optionsPanel = new Panel
            {
                Location = new Point(620, 10),
                Size = new Size(260, 600),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(30, 30, 35)
            };
            Controls.Add(optionsPanel);

            int y = 10;

            // Title
            var titleLabel = new Label
            {
                Text = "Detection Options",
                Location = new Point(10, y),
                Width = 240,
                Height = 25,
                Font = new Font(Font.FontFamily, 11f, FontStyle.Bold),
                ForeColor = HolographicTheme.TextPrimary
            };
            optionsPanel.Controls.Add(titleLabel);
            y += 35;

            // Sprite count
            spriteCountLabel = new Label
            {
                Text = "Detected: 0 sprites",
                Location = new Point(10, y),
                Width = 240,
                Height = 20,
                ForeColor = HolographicTheme.CyanAccent,
                Font = new Font(Font.FontFamily, 9f, FontStyle.Bold)
            };
            optionsPanel.Controls.Add(spriteCountLabel);
            y += 30;

            // Color tolerance
            AddLabel("Color Tolerance:", 10, y, optionsPanel);
            toleranceNumeric = new NumericUpDown
            {
                Location = new Point(10, y + 20),
                Width = 240,
                Minimum = 0,
                Maximum = 255,
                Value = 30
            };
            HolographicTheme.ApplyToNumericUpDown(toleranceNumeric);
            toleranceNumeric.ValueChanged += (s, e) => options.ColorTolerance = (int)toleranceNumeric.Value;
            optionsPanel.Controls.Add(toleranceNumeric);
            y += 55;

            // Min width
            AddLabel("Min Sprite Width:", 10, y, optionsPanel);
            minWidthNumeric = new NumericUpDown
            {
                Location = new Point(10, y + 20),
                Width = 115,
                Minimum = 1,
                Maximum = 500,
                Value = 5
            };
            HolographicTheme.ApplyToNumericUpDown(minWidthNumeric);
            minWidthNumeric.ValueChanged += (s, e) => options.MinSpriteWidth = (int)minWidthNumeric.Value;
            optionsPanel.Controls.Add(minWidthNumeric);

            // Min height
            AddLabel("Height:", 135, y, optionsPanel);
            minHeightNumeric = new NumericUpDown
            {
                Location = new Point(135, y + 20),
                Width = 115,
                Minimum = 1,
                Maximum = 500,
                Value = 5
            };
            HolographicTheme.ApplyToNumericUpDown(minHeightNumeric);
            minHeightNumeric.ValueChanged += (s, e) => options.MinSpriteHeight = (int)minHeightNumeric.Value;
            optionsPanel.Controls.Add(minHeightNumeric);
            y += 55;

            // Padding
            AddLabel("Padding (pixels):", 10, y, optionsPanel);
            paddingNumeric = new NumericUpDown
            {
                Location = new Point(10, y + 20),
                Width = 240,
                Minimum = 0,
                Maximum = 50,
                Value = 2
            };
            HolographicTheme.ApplyToNumericUpDown(paddingNumeric);
            paddingNumeric.ValueChanged += (s, e) => options.Padding = (int)paddingNumeric.Value;
            optionsPanel.Controls.Add(paddingNumeric);
            y += 55;

            // Remove halo
            removeHaloCheckBox = new CheckBox
            {
                Text = "Remove Anti-Aliasing Halo",
                Location = new Point(10, y),
                Width = 240,
                Checked = true
            };
            HolographicTheme.ApplyToCheckBox(removeHaloCheckBox);
            removeHaloCheckBox.CheckedChanged += (s, e) => options.RemoveHalo = removeHaloCheckBox.Checked;
            optionsPanel.Controls.Add(removeHaloCheckBox);
            y += 30;

            // Sort order
            AddLabel("Sort Order:", 10, y, optionsPanel);
            sortOrderCombo = new ComboBox
            {
                Location = new Point(10, y + 20),
                Width = 240,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            sortOrderCombo.Items.AddRange(new object[]
            {
                "Left to Right, Top to Bottom",
                "Top to Bottom, Left to Right",
                "None (Detection Order)"
            });
            sortOrderCombo.SelectedIndex = 0;
            HolographicTheme.ApplyToComboBox(sortOrderCombo);
            sortOrderCombo.SelectedIndexChanged += (s, e) =>
            {
                options.SortOrder = (SpriteSheetSplitter.SpriteSortOrder)sortOrderCombo.SelectedIndex;
            };
            optionsPanel.Controls.Add(sortOrderCombo);
            y += 55;

            // Detect button
            detectButton = new Button
            {
                Text = "Re-Detect Sprites",
                Location = new Point(10, y),
                Width = 240,
                Height = 35
            };
            HolographicTheme.ApplyToButton(detectButton, ButtonStyle.Accent);
            detectButton.Click += (s, e) => PerformDetection();
            optionsPanel.Controls.Add(detectButton);
            y += 45;

            // Info label
            var infoLabel = new Label
            {
                Text = "Tip: Adjust tolerance if sprites\naren't detected correctly.\n\nRed boxes show detected sprites.",
                Location = new Point(10, y),
                Width = 240,
                Height = 80,
                ForeColor = Color.Gray,
                Font = new Font(Font.FontFamily, 8f)
            };
            optionsPanel.Controls.Add(infoLabel);

            // Buttons at bottom
            okButton = new Button
            {
                Text = "Split Into Layers",
                Location = new Point(620, 620),
                Width = 140,
                Height = 35,
                DialogResult = DialogResult.OK
            };
            HolographicTheme.ApplyToButton(okButton, ButtonStyle.Accent);
            Controls.Add(okButton);

            cancelButton = new Button
            {
                Text = "Cancel",
                Location = new Point(770, 620),
                Width = 110,
                Height = 35,
                DialogResult = DialogResult.Cancel
            };
            HolographicTheme.ApplyToButton(cancelButton);
            Controls.Add(cancelButton);

            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        private void AddLabel(string text, int x, int y, Control parent)
        {
            var label = new Label
            {
                Text = text,
                Location = new Point(x, y),
                Width = 240,
                Height = 18,
                ForeColor = HolographicTheme.TextPrimary,
                Font = new Font(Font.FontFamily, 8.5f)
            };
            parent.Controls.Add(label);
        }

        private void PerformDetection()
        {
            try
            {
                detectButton.Enabled = false;
                detectButton.Text = "Detecting...";
                Application.DoEvents();

                // Detect sprites
                detectedSprites = SpriteSheetSplitter.SplitSpriteSheet(sourceImage, options);

                // Update count
                spriteCountLabel.Text = $"Detected: {detectedSprites.Count} sprites";
                spriteCountLabel.ForeColor = detectedSprites.Count > 0 
                    ? HolographicTheme.CyanAccent 
                    : Color.Orange;

                // Update preview
                UpdatePreview();

                okButton.Enabled = detectedSprites.Count > 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Detection failed:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                spriteCountLabel.Text = "Detection failed!";
                spriteCountLabel.ForeColor = Color.Red;
            }
            finally
            {
                detectButton.Enabled = true;
                detectButton.Text = "Re-Detect Sprites";
            }
        }

        private void UpdatePreview()
        {
            if (detectedSprites == null || detectedSprites.Count == 0)
            {
                previewPictureBox.Image = sourceImage;
                return;
            }

            // Create preview with bounding boxes
            Bitmap preview = new Bitmap(sourceImage.Width, sourceImage.Height);
            using (Graphics g = Graphics.FromImage(preview))
            {
                g.Clear(Color.FromArgb(20, 20, 25));
                g.DrawImage(sourceImage, 0, 0);

                // Draw bounding boxes
                using (Pen redPen = new Pen(Color.FromArgb(255, 0, 80), 2))
                using (Pen cyanPen = new Pen(HolographicTheme.CyanAccent, 1))
                using (Font font = new Font("Arial", 10, FontStyle.Bold))
                using (Brush textBrush = new SolidBrush(Color.White))
                using (Brush bgBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
                {
                    foreach (var sprite in detectedSprites)
                    {
                        // Draw red bounding box
                        g.DrawRectangle(redPen, sprite.Bounds);

                        // Draw cyan inner box
                        Rectangle innerBox = sprite.Bounds;
                        innerBox.Inflate(-3, -3);
                        g.DrawRectangle(cyanPen, innerBox);

                        // Draw sprite index
                        string indexText = (sprite.Index + 1).ToString();
                        SizeF textSize = g.MeasureString(indexText, font);
                        PointF textPos = new PointF(
                            sprite.Bounds.X + 5,
                            sprite.Bounds.Y + 5
                        );
                        RectangleF textBg = new RectangleF(
                            textPos.X - 2,
                            textPos.Y - 2,
                            textSize.Width + 4,
                            textSize.Height + 4
                        );
                        g.FillRectangle(bgBrush, textBg);
                        g.DrawString(indexText, font, textBrush, textPos);
                    }
                }
            }

            previewPictureBox.Image?.Dispose();
            previewPictureBox.Image = preview;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            previewPictureBox.Image?.Dispose();
        }
    }
}
