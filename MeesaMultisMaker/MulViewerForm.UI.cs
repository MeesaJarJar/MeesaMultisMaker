using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    public partial class MulViewerForm
    {
        private void InitializeComponent()
        {
            Text = "Mul Viewer";
            Width = 1000;
            Height = 760;
            HolographicTheme.ApplyToForm(this);

            // Create shared tooltip
            mulViewerTooltip = new ToolTip
            {
                AutoPopDelay = 5000,
                InitialDelay = 500,
                ReshowDelay = 200,
                ShowAlways = true
            };

            pathBox = new TextBox { Dock = DockStyle.Top, Height = 22, Text = DefaultUOPath, ReadOnly = true };
            mulViewerTooltip.SetToolTip(pathBox, "Path to UO installation folder");
            
            legacyCheck = new CheckBox { Text = "Legacy export (6 cols)", Dock = DockStyle.Top, Checked = true, Height = 20 };
            legacyCheck.CheckedChanged += (s, e) => RefreshExport();
            mulViewerTooltip.SetToolTip(legacyCheck, "Use legacy 6-column export format (ID X Y Z Visible Flags)");
            
            normalizeCheck = new CheckBox { Text = "Normalize (min X/Y =0, sort X,Y)", Dock = DockStyle.Top, Checked = true, Height = 20 };
            normalizeCheck.CheckedChanged += (s, e) => RefreshExport();
            mulViewerTooltip.SetToolTip(normalizeCheck, "Normalize coordinates so minimum X/Y is 0 and sort by position");
            
            showArtCheck = new CheckBox { Text = "Show actual art", Dock = DockStyle.Top, Checked = true, Height = 20 };
            showArtCheck.CheckedChanged += (s, e) => previewBox?.Invalidate();
            mulViewerTooltip.SetToolTip(showArtCheck, "Display actual art graphics instead of placeholders");
            
            hideEmptyCheck = new CheckBox { Text = "Hide empty slots", Dock = DockStyle.Top, Checked = true, Height = 20 };
            hideEmptyCheck.CheckedChanged += (s, e) => RefreshMultiList();
            mulViewerTooltip.SetToolTip(hideEmptyCheck, "Hide multi entries with no components");

            BuildGenerationPanel();
            BuildMultiList();
            BuildPreviewPanel();
            BuildPartsAndExportPanel();
            BuildSplitContainers();

            status = new StatusStrip();
            statusLabel = new ToolStripStatusLabel();
            status.Items.Add(statusLabel);

            Controls.Add(split);
            Controls.Add(genPanel);
            Controls.Add(showArtCheck);
            Controls.Add(hideEmptyCheck);
            Controls.Add(normalizeCheck);
            Controls.Add(legacyCheck);
            Controls.Add(pathBox);
            Controls.Add(status);

            this.Shown += (s, e) => 
            { 
                HolographicTheme.ApplyToAllControls(this);
                string mulFolder = AppConfig.Instance.FindMulFolder();
                if (!string.IsNullOrEmpty(mulFolder) && Directory.Exists(mulFolder))
                {
                    pathBox.Text = mulFolder;
                    LoadEntries();
                }
                else if (Directory.Exists(pathBox.Text)) 
                    LoadEntries(); 
                else 
                    statusLabel.Text = "No valid UO folder found"; 
            };
        }

        private void BuildGenerationPanel()
        {
            genPanel = new Panel { Dock = DockStyle.Top, Height = 80, Padding = new Padding(6, 4, 6, 4) };
            
            var wLbl = new Label { Text = "W:", AutoSize = true, Left = 6, Top = 12 };
            genWidth = new NumericUpDown { Minimum = 1, Maximum = 64, Value = 16, Left = wLbl.Right + 4, Top = 8, Width = 50 };
            mulViewerTooltip.SetToolTip(genWidth, "Generated output width in tiles");
            
            var hLbl = new Label { Text = "H:", AutoSize = true, Left = genWidth.Right + 8, Top = 12 };
            genHeight = new NumericUpDown { Minimum = 1, Maximum = 64, Value = 16, Left = hLbl.Right + 4, Top = 8, Width = 50 };
            mulViewerTooltip.SetToolTip(genHeight, "Generated output height in tiles");
            
            var sLbl = new Label { Text = "Similarity:", AutoSize = true, Left = genHeight.Right + 12, Top = 12 };
            genSimilarity = new TrackBar { Minimum = 1, Maximum = 100, TickFrequency = 10, Value = 60, Left = sLbl.Right + 6, Top = 5, Width = 180, Height = 30 };
            mulViewerTooltip.SetToolTip(genSimilarity, "Pattern matching similarity threshold (higher = stricter)");
            
            genButton = new Button { Text = "Generate New", Left = genSimilarity.Right + 12, Top = 8, Width = 110, Height = 24 };
            genButton.Click += GenButton_Click;
            mulViewerTooltip.SetToolTip(genButton, "Generate new structure using WFC from selected patterns");
            
            viewLogBtn = new Button { Text = "View Log", Left = genButton.Right + 8, Top = 8, Width = 80, Height = 24 };
            viewLogBtn.Click += (s, e) => OpenLogFile();
            mulViewerTooltip.SetToolTip(viewLogBtn, "Open WFC generation log file");
            
            epochsLabel = new Label { Text = "Attempts:", AutoSize = true, Left = 6, Top = 52 };
            genEpochs = new NumericUpDown { Minimum = 1, Maximum = 2000, Value = 100, Left = epochsLabel.Right + 4, Top = 48, Width = 60 };
            mulViewerTooltip.SetToolTip(genEpochs, "Generation attempts (capped at 500)");
            
            lrLabel = new Label { Text = "Temp:", AutoSize = true, Left = genEpochs.Right + 10, Top = 52 };
            genLr = new NumericUpDown { DecimalPlaces = 3, Increment = 0.01M, Minimum = 0.001M, Maximum = 1.000M, Value = 0.12M, Left = lrLabel.Right + 4, Top = 48, Width = 60 };
            mulViewerTooltip.SetToolTip(genLr, "Sampling temperature: low = deterministic, high = more variety");
            
            kernelLabel = new Label { Text = "Kernel:", AutoSize = true, Left = genLr.Right + 10, Top = 52 };
            genKernel = new NumericUpDown { Minimum = 2, Maximum = 5, Value = 2, Left = kernelLabel.Right + 4, Top = 48, Width = 45 };
            mulViewerTooltip.SetToolTip(genKernel, "Pattern kernel size (NxN tiles)");
            
            chkRequireAllClasses = new CheckBox { Text = "Require all classes", AutoSize = true, Left = genKernel.Right + 10, Top = 50, Checked = false };
            mulViewerTooltip.SetToolTip(chkRequireAllClasses, "Require all tile types appear in output");
            
            useWfc = new CheckBox { Text = "Use WFC", AutoSize = true, Left = chkRequireAllClasses.Right + 20, Top = 50, Checked = true };
            mulViewerTooltip.SetToolTip(useWfc, "Use Wave Function Collapse algorithm for generation");
            
            genPanel.Controls.AddRange(new Control[] { wLbl, genWidth, hLbl, genHeight, sLbl, genSimilarity, genButton, viewLogBtn, epochsLabel, genEpochs, lrLabel, genLr, kernelLabel, genKernel, chkRequireAllClasses, useWfc });
        }

        private void BuildMultiList()
        {
            multiList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, SelectionMode = System.Windows.Forms.SelectionMode.MultiExtended };
            multiList.SelectedIndexChanged += MultiList_SelectedIndexChanged;
        }

        private void BuildPreviewPanel()
        {
            previewBox = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.CenterImage };
            previewBox.Paint += PreviewBox_Paint;
            previewBox.MouseWheel += PreviewBox_MouseWheel;
            previewBox.MouseDown += PreviewBox_MouseDown;
            previewBox.MouseMove += PreviewBox_MouseMove;
            previewBox.MouseUp += PreviewBox_MouseUp;
            previewBox.Cursor = Cursors.Hand;
            mulViewerTooltip.SetToolTip(previewBox, "Multi preview - scroll to zoom, drag to pan");

            zoomPanel = new Panel { Dock = DockStyle.Top, Height = 28, BackColor = Color.FromArgb(20, 25, 35) };
            zoomLabel = new Label { Text = "Zoom: 100%", AutoSize = true, Left = 6, Top = 6, ForeColor = Color.White };
            
            zoomOutBtn = new Button { Text = "-", Left = 90, Top = 2, Width = 24, Height = 22, FlatStyle = FlatStyle.Flat };
            zoomOutBtn.Click += (s, e) => AdjustZoom(-ZOOM_STEP);
            mulViewerTooltip.SetToolTip(zoomOutBtn, "Zoom out");
            
            zoomInBtn = new Button { Text = "+", Left = 116, Top = 2, Width = 24, Height = 22, FlatStyle = FlatStyle.Flat };
            zoomInBtn.Click += (s, e) => AdjustZoom(ZOOM_STEP);
            mulViewerTooltip.SetToolTip(zoomInBtn, "Zoom in");
            
            zoomResetBtn = new Button { Text = "Reset", Left = 146, Top = 2, Width = 50, Height = 22, FlatStyle = FlatStyle.Flat };
            zoomResetBtn.Click += (s, e) => { zoomLevel = 1.0f; panOffsetX = 0f; panOffsetY = 0f; UpdateZoomLabel(); previewBox.Invalidate(); };
            mulViewerTooltip.SetToolTip(zoomResetBtn, "Reset zoom and pan to default");
            
            zoomPanel.Controls.AddRange(new Control[] { zoomLabel, zoomOutBtn, zoomInBtn, zoomResetBtn });
        }

        private void BuildPartsAndExportPanel()
        {
            partsView = new ListView { Dock = DockStyle.Top, Height = 150, View = View.Details, FullRowSelect = true, GridLines = true };
            colId = new ColumnHeader { Text = "ID", Width = 90 };
            colX = new ColumnHeader { Text = "X", Width = 60 };
            colY = new ColumnHeader { Text = "Y", Width = 60 };
            colZ = new ColumnHeader { Text = "Z", Width = 60 };
            colFlags = new ColumnHeader { Text = "Flags", Width = 80 };
            var colRole = new ColumnHeader { Text = "Role", Width = 60 };
            var colFloor = new ColumnHeader { Text = "Floor", Width = 50 };
            partsView.Columns.AddRange(new[] { colId, colX, colY, colZ, colFlags, colRole, colFloor });
            mulViewerTooltip.SetToolTip(partsView, "Component list - shows all tiles in selected multi with structural role and floor level");

            exportBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
            mulViewerTooltip.SetToolTip(exportBox, "Export text format - can be copied and imported");

            copyBtn = new Button { Text = "Copy Lines", Dock = DockStyle.Bottom, Height = 28 };
            copyBtn.Click += (s, e) => { try { Clipboard.SetText(exportBox.Text); } catch { } };
            mulViewerTooltip.SetToolTip(copyBtn, "Copy export text to clipboard");

            validateBtn = new Button { Text = "Validate Multi", Dock = DockStyle.Bottom, Height = 28, BackColor = Color.FromArgb(200, 140, 30), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            validateBtn.Click += ValidateBtn_Click;
            mulViewerTooltip.SetToolTip(validateBtn, "Validate selected multi against UO house structure rules (floor separation, foundation, walls, etc.)");

            sendToCanvasBtn = new Button { Text = "Send to Canvas", Dock = DockStyle.Bottom, Height = 32, BackColor = Color.FromArgb(0, 122, 204), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font(Font.FontFamily, 9f, FontStyle.Bold) };
            sendToCanvasBtn.Click += SendToCanvasBtn_Click;
            mulViewerTooltip.SetToolTip(sendToCanvasBtn, "Send selected multi components to main canvas (replaces existing)");
        }

        private void BuildSplitContainers()
        {
            splitRight = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 200 };
            
            var previewContainer = new Panel { Dock = DockStyle.Fill };
            previewContainer.Controls.Add(previewBox);
            previewContainer.Controls.Add(zoomPanel);
            splitRight.Panel1.Controls.Add(previewContainer);
            
            var addToCanvasBtn = new Button { Text = "Add to Canvas", Dock = DockStyle.Bottom, Height = 32, BackColor = Color.FromArgb(0, 168, 107), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font(Font.FontFamily, 9f, FontStyle.Bold) };
            addToCanvasBtn.Click += AddToCanvasBtn_Click;
            mulViewerTooltip.SetToolTip(addToCanvasBtn, "Add selected multi to canvas (keeps existing objects)");

            var trainFromMultisBtn = new Button { Text = "Train from Selected", Dock = DockStyle.Bottom, Height = 32, BackColor = Color.FromArgb(140, 80, 200), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font(Font.FontFamily, 9f, FontStyle.Bold) };
            trainFromMultisBtn.Click += TrainFromSelectedMultis_Click;
            mulViewerTooltip.SetToolTip(trainFromMultisBtn, "Add selected multi(s) as training data for building generation");

            splitRight.Panel2.Controls.Add(trainFromMultisBtn);
            splitRight.Panel2.Controls.Add(addToCanvasBtn);
            splitRight.Panel2.Controls.Add(sendToCanvasBtn);
            splitRight.Panel2.Controls.Add(validateBtn);
            splitRight.Panel2.Controls.Add(copyBtn);
            splitRight.Panel2.Controls.Add(exportBox);
            splitRight.Panel2.Controls.Add(partsView);
            
            split = new SplitContainer { Dock = DockStyle.Fill };
            split.Panel1.Controls.Add(multiList);
            split.Panel2.Controls.Add(splitRight);
        }

        private void AdjustZoom(float delta)
        {
            zoomLevel = Math.Max(ZOOM_MIN, Math.Min(ZOOM_MAX, zoomLevel + delta));
            UpdateZoomLabel();
            previewBox.Invalidate();
        }

        private void UpdateZoomLabel()
        {
            if (zoomLabel != null)
                zoomLabel.Text = $"Zoom: {(int)(zoomLevel * 100)}%";
        }
    }
}
