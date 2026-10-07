using System;
using System.Drawing;
using System.Windows.Forms;
using MeesaMultisMaker.Utils;

namespace MeesaMultisMaker
{
    public partial class PainterForm
    {
        private void InitializeComponent()
        {
            this.Text = "Painter - MeesaMultisMaker";
            this.Width = 1400;
            this.Height = 850;
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.FromArgb(45, 45, 48);

            // Create shared tooltip
            var painterTooltip = new ToolTip
            {
                AutoPopDelay = 5000,
                InitialDelay = 500,
                ReshowDelay = 200,
                ShowAlways = true
            };

            // Fixed height toolbar with two rows
            toolbar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 80,  // Fixed height for two rows
                BackColor = Color.FromArgb(37, 37, 38),
                Padding = new Padding(4)
            };

            // Row 1: Canvas settings, colors, brush size
            var row1 = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 36,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(2)
            };

            var label1 = new Label { Text = "Canvas:", AutoSize = true, ForeColor = Color.White, Padding = new Padding(2, 8, 2, 0) };
            row1.Controls.Add(label1);

            sizeCombo = new ComboBox { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
            sizeCombo.Items.AddRange(new object[] { "512x512", "768x768", "1024x1024", "1536x1536" });
            sizeCombo.SelectedIndex = 0;
            sizeCombo.SelectedIndexChanged += (s, e) => CreateCanvas();
            painterTooltip.SetToolTip(sizeCombo, "Select canvas resolution");
            row1.Controls.Add(sizeCombo);

            colorBtn = new Button { Text = "Color", Width = 60, Height = 28, FlatStyle = FlatStyle.Flat };
            colorBtn.BackColor = currentColor;
            colorBtn.ForeColor = Color.White;
            colorBtn.Click += ColorBtn_Click;
            painterTooltip.SetToolTip(colorBtn, "Choose drawing color");
            row1.Controls.Add(colorBtn);

            bgColorBtn = new Button { Text = "BG", Width = 40, Height = 28, FlatStyle = FlatStyle.Flat };
            bgColorBtn.BackColor = backgroundColor;
            bgColorBtn.ForeColor = Color.Black;
            bgColorBtn.Click += BgColorBtn_Click;
            painterTooltip.SetToolTip(bgColorBtn, "Choose background color");
            row1.Controls.Add(bgColorBtn);

            maskToggleBtn = new Button { Text = "Mask: Off", Width = 75, Height = 28, FlatStyle = FlatStyle.Flat };
            maskToggleBtn.BackColor = Color.FromArgb(60, 60, 60);
            maskToggleBtn.ForeColor = Color.White;
            maskToggleBtn.Click += (s, e) =>
            {
                drawMask = !drawMask;
                maskToggleBtn.Text = drawMask ? "Mask: On" : "Mask: Off";
                maskToggleBtn.BackColor = drawMask ? Color.FromArgb(0, 122, 204) : Color.FromArgb(60, 60, 60);
            };
            painterTooltip.SetToolTip(maskToggleBtn, "Toggle mask drawing mode (for AI inpainting)");
            row1.Controls.Add(maskToggleBtn);

            sizeLabel = new Label { Text = $"Size:{brushSize}", AutoSize = false, Width = 60, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.White, Padding = new Padding(4, 8, 0, 0) };
            row1.Controls.Add(sizeLabel);

            sizeTrack = new TrackBar { Minimum = 1, Maximum = 200, Value = brushSize, Width = 140, TickStyle = TickStyle.None, Height = 28 };
            sizeTrack.Scroll += (s, e) => { brushSize = sizeTrack.Value; sizeLabel.Text = $"Size:{brushSize}"; };
            painterTooltip.SetToolTip(sizeTrack, "Adjust brush/tool size");
            row1.Controls.Add(sizeTrack);

            clearBtn = new Button { Text = "Clear", Width = 55, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(200, 60, 60), ForeColor = Color.White };
            clearBtn.Click += ClearBtn_Click;
            painterTooltip.SetToolTip(clearBtn, "Clear active layer to background color");
            row1.Controls.Add(clearBtn);

            undoBtn = new Button { Text = "Undo", Width = 55, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White };
            undoBtn.Click += UndoBtn_Click;
            painterTooltip.SetToolTip(undoBtn, "Undo last action (Ctrl+Z)");
            row1.Controls.Add(undoBtn);

            // Row 2: Tools and file operations
            var row2 = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 36,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(2)
            };

            var toolLabel = new Label { Text = "Tools:", AutoSize = true, ForeColor = Color.White, Padding = new Padding(2, 8, 2, 0) };
            row2.Controls.Add(toolLabel);

            brushBtn = CreateToolButton("Brush", ToolMode.Brush);
            painterTooltip.SetToolTip(brushBtn, "Soft round brush with anti-aliasing");
            row2.Controls.Add(brushBtn);

            pencilBtn = CreateToolButton("Pencil", ToolMode.Pencil);
            painterTooltip.SetToolTip(pencilBtn, "Hard pixel-perfect pencil (1px alias)");
            row2.Controls.Add(pencilBtn);

            eraserBtn = CreateToolButton("Eraser", ToolMode.Eraser);
            painterTooltip.SetToolTip(eraserBtn, "Erase to transparent");
            row2.Controls.Add(eraserBtn);

            fillBtn = CreateToolButton("Fill", ToolMode.Fill);
            painterTooltip.SetToolTip(fillBtn, "Flood fill area with color");
            row2.Controls.Add(fillBtn);

            lineBtn = CreateToolButton("Line", ToolMode.Line);
            painterTooltip.SetToolTip(lineBtn, "Draw straight line (click and drag)");
            row2.Controls.Add(lineBtn);

            rectBtn = CreateToolButton("Rect", ToolMode.Rectangle);
            painterTooltip.SetToolTip(rectBtn, "Draw rectangle (click and drag)");
            row2.Controls.Add(rectBtn);

            circleBtn = CreateToolButton("Circle", ToolMode.Circle);
            painterTooltip.SetToolTip(circleBtn, "Draw circle/ellipse (click and drag)");
            row2.Controls.Add(circleBtn);

            moveBtn = CreateToolButton("Move", ToolMode.Move);
            painterTooltip.SetToolTip(moveBtn, "Move active layer content by dragging. Use [ and ] to scale content.");
            row2.Controls.Add(moveBtn);

            selectBtn = CreateToolButton("Select", ToolMode.Select);
            painterTooltip.SetToolTip(selectBtn, "Drag a rectangle selection. Use Copy/Cut, then Ctrl+V to paste as a new layer.");
            row2.Controls.Add(selectBtn);

            fillShapeCheck = new CheckBox { Text = "Fill", AutoSize = true, ForeColor = Color.White, Padding = new Padding(4, 6, 4, 0), Checked = false };
            painterTooltip.SetToolTip(fillShapeCheck, "Fill shapes instead of outline only");
            row2.Controls.Add(fillShapeCheck);

            // Separator
            row2.Controls.Add(new Label { Text = "|", ForeColor = Color.Gray, AutoSize = true, Padding = new Padding(4, 8, 4, 0) });

            copyBtn = new Button { Text = "Copy", Width = 50, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White };
            copyBtn.Click += (s, e) => CopySelectionToClipboard(cut: false);
            painterTooltip.SetToolTip(copyBtn, "Copy current selection (Ctrl+C)");
            row2.Controls.Add(copyBtn);

            cutBtn = new Button { Text = "Cut", Width = 50, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White };
            cutBtn.Click += (s, e) => CopySelectionToClipboard(cut: true);
            painterTooltip.SetToolTip(cutBtn, "Cut current selection (Ctrl+X)");
            row2.Controls.Add(cutBtn);

            removeBgBtn = new Button { Text = "Remove BG", Width = 84, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(80, 60, 60), ForeColor = Color.White };
            removeBgBtn.Click += (s, e) => RemoveBackgroundFromActiveLayer();
            painterTooltip.SetToolTip(removeBgBtn, "Auto-remove edge-connected background pixels based on corner colors");
            row2.Controls.Add(removeBgBtn);

            // Separator
            row2.Controls.Add(new Label { Text = "|", ForeColor = Color.Gray, AutoSize = true, Padding = new Padding(4, 8, 4, 0) });

            saveBtn = new Button { Text = "Save", Width = 50, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White };
            saveBtn.Click += SaveBtn_Click;
            painterTooltip.SetToolTip(saveBtn, "Save canvas as PNG file");
            row2.Controls.Add(saveBtn);

            loadBtn = new Button { Text = "Load", Width = 50, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White };
            loadBtn.Click += LoadBtn_Click;
            painterTooltip.SetToolTip(loadBtn, "Load image file into new layer");
            row2.Controls.Add(loadBtn);

            // Add rows to toolbar (in reverse order due to Dock.Top stacking)
            toolbar.Controls.Add(row2);
            toolbar.Controls.Add(row1);

            // Build layers panel with tooltips
            BuildLayersPanel(painterTooltip);

            // Image editing panel on the left
            imageEditPanel = new Controls.ImageEditingPanel()
            {
                Dock = DockStyle.Left,
                Width = 320
            };

            // Wire image editing panel events
            imageEditPanel.EffectsChanged += (s, e) =>
            {
                if (activeLayerIndex < 0 || activeLayerIndex >= layers.Count) return;
                if (layers[activeLayerIndex].Locked) return;
                if (!imageEditPanel.RealTimePreview) return;
                ApplyEffectsToLayer(true);
            };

            imageEditPanel.ApplyClicked += (s, e) =>
            {
                if (activeLayerIndex < 0 || activeLayerIndex >= layers.Count) return;
                if (layers[activeLayerIndex].Locked) return;
                SaveLayerUndo(activeLayerIndex);
                ApplyEffectsToLayer(false);
            };

            imageEditPanel.ResetClicked += (s, e) =>
            {
                if (activeLayerIndex < 0 || activeLayerIndex >= layers.Count) return;
                // revert preview if exists
                var layer = layers[activeLayerIndex];
                if (layer.PreEditImage != null)
                {
                    layer.Image.Dispose();
                    layer.Image = new Bitmap(layer.PreEditImage);
                    layer.PreEditImage.Dispose();
                    layer.PreEditImage = null;
                    UpdateCanvas();
                }
                imageEditPanel.ResetToDefaults();
            };

            imageEditPanel.ApplyToAllSelectedClicked += (s, e) =>
            {
                // apply to all layers (visible) as a convenience
                for (int i = 0; i < layers.Count; i++)
                {
                    if (layers[i].Locked) continue;
                    SaveLayerUndo(i);
                    ApplyEffectsToLayer(false, i);
                }
            };

            canvasContainer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(45, 45, 48),
                AutoScroll = true
            };

            canvasBox = new PictureBox
            {
                BackColor = Color.DarkGray,
                SizeMode = PictureBoxSizeMode.AutoSize
            };
            canvasBox.Paint += CanvasBox_Paint;
            canvasBox.MouseDown += CanvasBox_MouseDown;
            canvasBox.MouseMove += CanvasBox_MouseMove;
            canvasBox.MouseUp += CanvasBox_MouseUp;
            canvasBox.MouseEnter += (s, e) => { mouseOverCanvas = true; canvasBox.Invalidate(); canvasBox.Focus(); };
            canvasBox.MouseLeave += (s, e) => { mouseOverCanvas = false; canvasBox.Invalidate(); };

            canvasContainer.Controls.Add(canvasBox);
            canvasContainer.Resize += (s, e) => CenterCanvas();

            // Bottom panel for Send buttons - centered below the canvas
            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                BackColor = Color.FromArgb(37, 37, 38),
                Padding = new Padding(4)
            };

            var bottomButtons = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent
            };

            sendBtn = new Button
            {
                Text = "Send to Canvas",
                Width = 160,
                Height = 36,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 122, 204),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            sendBtn.Click += SendBtn_Click;
            painterTooltip.SetToolTip(sendBtn, "Send merged image to main canvas at position 0,0");
            bottomButtons.Controls.Add(sendBtn);

            var sendGumpBtn = new Button
            {
                Text = "Send to Gump Editor",
                Width = 190,
                Height = 36,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 122, 122),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            sendGumpBtn.Click += SendGumpBtn_Click;
            painterTooltip.SetToolTip(sendGumpBtn, "Send merged image to GUMP Editor");
            bottomButtons.Controls.Add(sendGumpBtn);

            var send3DBtn = new Button
            {
                Text = "Send to 3D",
                Width = 150,
                Height = 36,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(110, 60, 160),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            send3DBtn.Click += Send3DBtn_Click;
            painterTooltip.SetToolTip(send3DBtn, "Send merged image to the 3D Editor as its init image");
            bottomButtons.Controls.Add(send3DBtn);

            sendMapUnderlayBtn = new Button
            {
                Text = "Send to Map (Underlay)",
                Width = 200,
                Height = 36,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(90, 80, 30),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            sendMapUnderlayBtn.Click += SendMapUnderlayBtn_Click;
            painterTooltip.SetToolTip(sendMapUnderlayBtn, "Send merged image to Map Editor as underlay plane");
            bottomButtons.Controls.Add(sendMapUnderlayBtn);

            sendMapOverlayBtn = new Button
            {
                Text = "Send to Map (Overlay)",
                Width = 195,
                Height = 36,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, 90, 120),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            sendMapOverlayBtn.Click += SendMapOverlayBtn_Click;
            painterTooltip.SetToolTip(sendMapOverlayBtn, "Send merged image to Map Editor as land overlay plane");
            bottomButtons.Controls.Add(sendMapOverlayBtn);

            bottomPanel.Controls.Add(bottomButtons);

            // Center the buttons when the panel resizes
            bottomPanel.Resize += (s, e) =>
            {
                bottomButtons.Left = (bottomPanel.ClientSize.Width - bottomButtons.Width) / 2;
                bottomButtons.Top = (bottomPanel.ClientSize.Height - bottomButtons.Height) / 2;
            };

            // Add controls in docking order: toolbar (top), imageEditPanel (left), layersPanel (right), bottomPanel (bottom), canvasContainer (fill)
            this.Controls.Add(canvasContainer);
            this.Controls.Add(bottomPanel);
            this.Controls.Add(layersPanel);
            this.Controls.Add(imageEditPanel);
            this.Controls.Add(toolbar);

            // Keyboard shortcuts
            this.KeyPreview = true;
            this.KeyDown += PainterForm_KeyDown;

            SetActiveTool(ToolMode.Brush);
            CreateCanvas();
        }

        private void BuildLayersPanel(ToolTip painterTooltip)
        {
            // Layers panel on the right
            layersPanel = new Panel
            {
                Dock = DockStyle.Right,
                Width = 320,
                BackColor = Color.FromArgb(34, 34, 36),
                Padding = new Padding(6)
            };

            var layersLabel = new Label { Text = "Layers", ForeColor = Color.White, Dock = DockStyle.Top, Height = 20 };
            layersPanel.Controls.Add(layersLabel);

            layersListBox = new ListBox { Dock = DockStyle.Top, Height = 400, Font = new Font("Consolas", 9) };
            layersListBox.SelectedIndexChanged += LayersListBox_SelectedIndexChanged;
            layersListBox.DoubleClick += (s, e) => { if (layersListBox.SelectedIndex >= 0) RenameLayer(layersListBox.SelectedIndex); };
            painterTooltip.SetToolTip(layersListBox, "Select layer to edit (double-click to rename)");
            layersPanel.Controls.Add(layersListBox);

            var layersButtonsRow1 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, FlowDirection = FlowDirection.LeftToRight };
            var layersButtonsRow2 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, FlowDirection = FlowDirection.LeftToRight };
            var layersButtonsRow3 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, FlowDirection = FlowDirection.LeftToRight };

            addLayerBtn = new Button { Text = "Add", Width = 48, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White };
            addLayerBtn.Click += (s, e) => { AddLayer(); };
            painterTooltip.SetToolTip(addLayerBtn, "Add new transparent layer");

            deleteLayerBtn = new Button { Text = "Del", Width = 48, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White };
            deleteLayerBtn.Click += (s, e) => { DeleteLayer(activeLayerIndex); };
            painterTooltip.SetToolTip(deleteLayerBtn, "Delete selected layer");

            lockLayerBtn = new Button { Text = "Lock", Width = 48, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White };
            lockLayerBtn.Click += (s, e) => { ToggleLockActiveLayer(); };
            painterTooltip.SetToolTip(lockLayerBtn, "Lock/unlock layer (prevents drawing)");

            pasteBtn = new Button { Text = "Paste", Width = 48, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White };
            pasteBtn.Click += (s, e) => { PasteFromClipboard(); };
            painterTooltip.SetToolTip(pasteBtn, "Paste image from clipboard as new layer");

            layerUpBtn = new Button { Text = "Up", Width = 48, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White };
            layerUpBtn.Click += (s, e) => MoveLayer(activeLayerIndex, -1);
            painterTooltip.SetToolTip(layerUpBtn, "Move layer up in stack");

            layerDownBtn = new Button { Text = "Down", Width = 48, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White };
            layerDownBtn.Click += (s, e) => MoveLayer(activeLayerIndex, +1);
            painterTooltip.SetToolTip(layerDownBtn, "Move layer down in stack");

            renameLayerBtn = new Button { Text = "Rename", Width = 60, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White };
            renameLayerBtn.Click += (s, e) => { if (layersListBox.SelectedIndex >= 0) RenameLayer(layersListBox.SelectedIndex); };
            painterTooltip.SetToolTip(renameLayerBtn, "Rename selected layer");

            visLayerBtn = new Button { Text = "Hide", Width = 48, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White };
            visLayerBtn.Click += (s, e) => { ToggleVisibility(activeLayerIndex); };
            painterTooltip.SetToolTip(visLayerBtn, "Show/hide selected layer");

            opacityTrack = new TrackBar { Minimum = 0, Maximum = 100, Value = 100, Width = 120, TickStyle = TickStyle.None };
            opacityTrack.Scroll += (s, e) => { if (activeLayerIndex >= 0) { layers[activeLayerIndex].Opacity = opacityTrack.Value / 100f; UpdateCanvas(); RebuildLayersUI(); } };
            painterTooltip.SetToolTip(opacityTrack, "Adjust layer opacity (0-100%)");

            layersButtonsRow1.Controls.Add(addLayerBtn);
            layersButtonsRow1.Controls.Add(deleteLayerBtn);
            layersButtonsRow1.Controls.Add(lockLayerBtn);
            layersButtonsRow1.Controls.Add(pasteBtn);

            layersButtonsRow2.Controls.Add(renameLayerBtn);
            layersButtonsRow2.Controls.Add(visLayerBtn);
            layersButtonsRow2.Controls.Add(opacityTrack);

            layersButtonsRow3.Controls.Add(layerUpBtn);
            layersButtonsRow3.Controls.Add(layerDownBtn);

            layersPanel.Controls.Add(layersButtonsRow3);
            layersPanel.Controls.Add(layersButtonsRow2);
            layersPanel.Controls.Add(layersButtonsRow1);

            // collapse button for layers panel (toggle)
            var layersCollapseBtn = new Button
            {
                Text = "<",
                Width = 20,
                Height = 20,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Top = 4,
                Left = layersPanel.Width - 26
            };
            layersCollapseBtn.Click += (s, e) =>
            {
                if (layersPanel.Tag == null)
                {
                    // collapse
                    layersPanel.Tag = layersPanel.Width;
                    layersListBox.Visible = false;
                    layersButtonsRow1.Visible = false;
                    layersButtonsRow2.Visible = false;
                    layersButtonsRow3.Visible = false;
                    layersLabel.Visible = false;
                    layersPanel.Width = 28;
                    layersCollapseBtn.Text = ">";
                }
                else
                {
                    // expand
                    layersListBox.Visible = true;
                    layersButtonsRow1.Visible = true;
                    layersButtonsRow2.Visible = true;
                    layersButtonsRow3.Visible = true;
                    layersLabel.Visible = true;
                    layersPanel.Width = (int)layersPanel.Tag;
                    layersPanel.Tag = null;
                    layersCollapseBtn.Text = "<";
                }
                CenterCanvas();
            };
            painterTooltip.SetToolTip(layersCollapseBtn, "Collapse/expand layers panel");
            layersPanel.Controls.Add(layersCollapseBtn);
        }

        private Button CreateToolButton(string text, ToolMode tool)
        {
            var btn = new Button
            {
                Text = text,
                Width = 55,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                Tag = tool
            };
            btn.Click += (s, e) => SetActiveTool(tool);
            return btn;
        }

        private void SetActiveTool(ToolMode tool)
        {
            currentTool = tool;

            brushBtn.BackColor = Color.FromArgb(60, 60, 60);
            pencilBtn.BackColor = Color.FromArgb(60, 60, 60);
            eraserBtn.BackColor = Color.FromArgb(60, 60, 60);
            fillBtn.BackColor = Color.FromArgb(60, 60, 60);
            lineBtn.BackColor = Color.FromArgb(60, 60, 60);
            rectBtn.BackColor = Color.FromArgb(60, 60, 60);
            circleBtn.BackColor = Color.FromArgb(60, 60, 60);
            moveBtn.BackColor = Color.FromArgb(60, 60, 60);
            selectBtn.BackColor = Color.FromArgb(60, 60, 60);

            var activeColor = Color.FromArgb(0, 122, 204);

            switch (tool)
            {
                case ToolMode.Brush: brushBtn.BackColor = activeColor; break;
                case ToolMode.Pencil: pencilBtn.BackColor = activeColor; break;
                case ToolMode.Eraser: eraserBtn.BackColor = activeColor; break;
                case ToolMode.Fill: fillBtn.BackColor = activeColor; break;
                case ToolMode.Line: lineBtn.BackColor = activeColor; break;
                case ToolMode.Rectangle: rectBtn.BackColor = activeColor; break;
                case ToolMode.Circle: circleBtn.BackColor = activeColor; break;
                case ToolMode.Move: moveBtn.BackColor = activeColor; break;
                case ToolMode.Select: selectBtn.BackColor = activeColor; break;
            }
        }

        private void CenterCanvas()
        {
            if (canvasBox == null || canvasContainer == null) return;
            if (!ImageHelper.IsValidImage(canvasBox.Image)) return;

            canvasBox.Left = Math.Max(0, (canvasContainer.ClientSize.Width - canvasBox.Width) / 2);
            canvasBox.Top = Math.Max(0, (canvasContainer.ClientSize.Height - canvasBox.Height) / 2);
        }

        private void ColorBtn_Click(object sender, EventArgs e)
        {
            using (var cd = new ColorDialog())
            {
                cd.Color = currentColor;
                cd.FullOpen = true;
                if (cd.ShowDialog() == DialogResult.OK)
                {
                    currentColor = cd.Color;
                    colorBtn.BackColor = currentColor;
                    colorBtn.ForeColor = currentColor.GetBrightness() > 0.5 ? Color.Black : Color.White;
                }
            }
        }

        private void BgColorBtn_Click(object sender, EventArgs e)
        {
            using (var cd = new ColorDialog())
            {
                cd.Color = backgroundColor;
                cd.FullOpen = true;
                if (cd.ShowDialog() == DialogResult.OK)
                {
                    backgroundColor = cd.Color;
                    bgColorBtn.BackColor = backgroundColor;
                    bgColorBtn.ForeColor = backgroundColor.GetBrightness() > 0.5 ? Color.Black : Color.White;
                }
            }
        }

        private void ZoomIn()
        {
            zoom = Math.Min(8.0f, zoom * 1.15f);
            UpdateCanvas();
            CenterCanvas();
        }

        private void ZoomOut()
        {
            zoom = Math.Max(0.125f, zoom / 1.15f);
            UpdateCanvas();
            CenterCanvas();
        }
    }
}
