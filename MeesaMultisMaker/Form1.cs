using MeesaMultisMaker.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Text;
using System.Linq;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    public partial class Form1 : Form
    {
        private MulViewerForm _mulViewer;
        private MapViewerForm _mapViewer;
        private GumpEditorForm _gumpEditor;
        private ThreeDEditorForm _threeDEditor;
        private AudioEditorForm _audioEditor;
        private TextureEditorForm _textureEditor;
        private AnimationEditorForm _animationEditor;
        private AISettingsPanel _aiSettingsPanel;
        private Label maxZLabel;
        private TextBox maxZTextBox;
        private TrackBar minZTrackBar;
        private TrackBar maxZTrackBar;
        private Label minZValueLabel;
        private Label maxZValueLabel;

        public Form1()
        {
            InitializeComponent();
            this.Load += Form1_Load;

            // Prevent resizing below the point where controls become hidden
            this.MinimumSize = new System.Drawing.Size(1200, 600);

            // Apply holographic theme to the form
            ApplyHolographicTheme();

            // License check (first-run) - require acceptance
            if (!EnsureLicenseAccepted())
            {
                Close();
                return;
            }

            // Initialize the AI Settings Panel (docked on right side)
            InitializeAISettingsPanel();

            // Create shared tooltip for all buttons
            var buttonTooltip = new ToolTip
            {
                AutoPopDelay = 5000,
                InitialDelay = 500,
                ReshowDelay = 200,
                ShowAlways = true
            };

            // Canvas events
            this.designPictureBox.Paint += DesignPictureBox_Paint;
            this.designPictureBox.MouseDown += DesignPictureBox_MouseDown;
            this.designPictureBox.MouseMove += DesignPictureBox_MouseMove;
            this.designPictureBox.MouseUp += DesignPictureBox_MouseUp;
            this.designPictureBox.MouseWheel += DesignPictureBox_MouseWheel;
            this.designPictureBox.DragEnter += OnDesignPictureBox_DragEnter;
            this.designPictureBox.DragDrop += OnDesignPictureBox_DragDrop;
            this.designPictureBox.AllowDrop = true;

            // Palette events
            this.paletteListView.RetrieveVirtualItem += PaletteListView_RetrieveVirtualItem;
            this.paletteListView.DrawItem += PaletteListView_DrawItem;
            this.paletteListView.MouseDown += PaletteListView_MouseDown;
            this.paletteListView.CacheVirtualItems += PaletteListView_CacheVirtualItems;
            this.paletteListView.MouseMove += PaletteListView_MouseMove;
            this.paletteListView.MouseLeave += (s, e) => { lastHoveredIndex = -1; paletteToolTip?.Hide(paletteListView); };
            this.paletteListView.SelectedIndexChanged += PaletteListView_SelectedIndexChanged;

            // Initialize palette tooltip for TileData display
            InitializePaletteTooltip();

            // Initialize palette info panel for selected item display
            InitializePaletteInfoPanel();

            // Search events
            this.searchTextBox.TextChanged += SearchTextBox_TextChanged;
            this.searchTextBox.Enter += (s, e) =>
            {
                if (searchTextBox.Text == "Search..." && searchTextBox.ForeColor == HolographicTheme.TextMuted)
                {
                    searchTextBox.Text = string.Empty;
                    searchTextBox.ForeColor = HolographicTheme.TextPrimary;
                }
            };
            this.searchTextBox.Leave += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(searchTextBox.Text))
                {
                    searchTextBox.Text = "Search...";
                    searchTextBox.ForeColor = HolographicTheme.TextMuted;
                }
            };

            // Control buttons
            this.createGridButton.Click += CreateGridButton_Click;
            this.zUpButton.Click += ZUpButton_Click;
            this.zDownButton.Click += ZDownButton_Click;
            this.layerUpButton.Click += LayerUpButton_Click;
            this.layerDownButton.Click += LayerDownButton_Click;
            this.layerTopButton.Click += LayerTopButton_Click;
            this.layerBottomButton.Click += LayerBottomButton_Click;
            this.deleteButton.Click += DeleteButton_Click;
            this.exportButton.Click += ExportButton_Click;
            this.browseButton.Click += BrowseButton_Click;
            this.browseButton.Visible = false;
            this.lockSelectedButton.Click += (s, e) => ToggleLockFromList(unlockedListBox);
            this.unlockSelectedButton.Click += (s, e) => ToggleLockFromList(lockedListBox);

            // Add tooltips to designer buttons
            buttonTooltip.SetToolTip(createGridButton, "Create a new grid with the specified width and height");
            buttonTooltip.SetToolTip(zUpButton, "Move selected object up in Z height (+1)");
            buttonTooltip.SetToolTip(zDownButton, "Move selected object down in Z height (-1)");
            buttonTooltip.SetToolTip(layerUpButton, "Move selected object up in draw order");
            buttonTooltip.SetToolTip(layerDownButton, "Move selected object down in draw order");
            buttonTooltip.SetToolTip(layerTopButton, "Move selected object to top of draw order");
            buttonTooltip.SetToolTip(layerBottomButton, "Move selected object to bottom of draw order");
            buttonTooltip.SetToolTip(deleteButton, "Delete selected object from canvas");
            buttonTooltip.SetToolTip(exportButton, "Export multi structure as text format");
            buttonTooltip.SetToolTip(lockSelectedButton, "Lock selected object (prevents movement)");
            buttonTooltip.SetToolTip(unlockSelectedButton, "Unlock selected object (allows movement)");

            // Discord button - opens Discord invite link
            var discordBtn = CreateThemedButton("Discord", 555, 12, 70, ButtonStyle.Accent);
            discordBtn.Click += (s, e) =>
            {
                try
                {
                    System.Diagnostics.Process.Start("https://discord.com/invite/MpBe7cJDqV");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to open Discord link: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            buttonTooltip.SetToolTip(discordBtn, "Join our Discord community for support and sharing creations");
            this.controlsPanel.Controls.Add(discordBtn);

            // Generation buttons - Row 2 (temporarily removed: Add as Training, Generate Variant, Clear Training)
            // Settings button moved here
            var settingsBtn = CreateThemedButton("Settings", 10, 42, 80, ButtonStyle.Default);
            settingsBtn.Click += (s, e) =>
            {
                using (var settingsForm = new SettingsForm())
                {
                    if (settingsForm.ShowDialog(this) == DialogResult.OK)
                    {
                        var config = AppConfig.Instance;

                        // Reload art if path changed
                        if (!string.IsNullOrEmpty(config.ArtFolderPath) && config.ArtFolderPath != artFolderPath)
                        {
                            artFolderPath = config.ArtFolderPath;
                            LoadArt();
                        }

                        // Push updated MUL path to the open Map Editor
                        if (_mapViewer != null && !_mapViewer.IsDisposed
                            && !string.IsNullOrEmpty(config.MulFolderPath)
                            && config.MulFolderPath != _mapViewer.mulFolderPath)
                        {
                            _mapViewer.mulFolderPath = config.MulFolderPath;
                        }
                    }
                }
            };
            buttonTooltip.SetToolTip(settingsBtn, "Open application settings (art folder, ComfyUI URL, defaults)");
            this.controlsPanel.Controls.Add(settingsBtn);

            var exportCanvasBtn = CreateThemedButton("Export Canvas", 100, 42, 110, ButtonStyle.Warning);
            exportCanvasBtn.Click += (s, e) => ExportCanvasImage();
            buttonTooltip.SetToolTip(exportCanvasBtn, "Export the current canvas as a PNG image");
            this.controlsPanel.Controls.Add(exportCanvasBtn);

            // Clear Canvas button - removes all objects from canvas
            var clearCanvasBtn = CreateThemedButton("Clear", 220, 42, 60, ButtonStyle.Danger);
            clearCanvasBtn.Click += (s, e) => ClearCanvas();
            buttonTooltip.SetToolTip(clearCanvasBtn, "Remove all objects from the canvas");
            this.controlsPanel.Controls.Add(clearCanvasBtn);

            // Note: Use CTRL+V to paste into the main canvas (Form1)
            // The Map Editor (opened via 'Map Editor' button) has its own paste functionality

            // AutoPad button - evenly space all objects so they don't overlap
            var autoPadBtn = CreateThemedButton("AutoPad", 290, 42, 70, ButtonStyle.Warning);
            autoPadBtn.Click += (s, e) => AutoPadObjects();
            buttonTooltip.SetToolTip(autoPadBtn, "Evenly space all objects on the canvas so nothing overlaps");
            this.controlsPanel.Controls.Add(autoPadBtn);

            // Map Editor button
            var mapViewerBtn = CreateThemedButton("Map Editor", 370, 42, 100, ButtonStyle.Accent);
            mapViewerBtn.Click += (s, e) =>
            {
                if (_mapViewer == null || _mapViewer.IsDisposed)
                {
                    _mapViewer = new MapViewerForm
                    {
                        StartPosition = FormStartPosition.CenterScreen,
                        ShowInTaskbar = true
                    };
                    _mapViewer.FormClosed += (s2, e2) => { _mapViewer = null; };
                    _mapViewer.Show();
                }
                else
                {
                    if (!_mapViewer.Visible) _mapViewer.Show();
                }
            };
            buttonTooltip.SetToolTip(mapViewerBtn, "Open Map Editor to browse and edit map tiles");
            this.controlsPanel.Controls.Add(mapViewerBtn);

            // Mul Viewer button
            var mulViewerBtn = CreateThemedButton("Mul Viewer", 480, 42, 100, ButtonStyle.Accent);
            mulViewerBtn.Click += (s, e) =>
            {
                if (_mulViewer == null || _mulViewer.IsDisposed)
                {
                    _mulViewer = new MulViewerForm
                    {
                        StartPosition = FormStartPosition.CenterScreen,
                        ShowInTaskbar = true
                    };
                    _mulViewer.FormClosed += (s2, e2) => { _mulViewer = null; };
                    _mulViewer.Show();
                }
                else
                {
                    if (!_mulViewer.Visible) _mulViewer.Show();
                    _mulViewer.BringToFront();
                }
            };
            buttonTooltip.SetToolTip(mulViewerBtn, "Open MUL Viewer to browse existing multi structures");
            this.controlsPanel.Controls.Add(mulViewerBtn);

            // Image Editor button
            var imageEditorBtn = CreateThemedButton("Image Editor", 590, 42, 100, ButtonStyle.Accent);
            imageEditorBtn.Click += (s, e) => ShowImageEditingWindow();
            buttonTooltip.SetToolTip(imageEditorBtn, "Open Image Editor for effects (brightness, contrast, pixelization)");
            this.controlsPanel.Controls.Add(imageEditorBtn);

            // Painter button
            var painterBtn = CreateThemedButton("Painter", 700, 42, 80, ButtonStyle.Accent);
            painterBtn.Click += (s, e) =>
            {
                try
                {
                    var painter = new PainterForm();

                    Action<Bitmap, Bitmap> handler = null;
                    handler = (img, mask) =>
                    {
                        try
                        {
                            // determine non-empty alpha bounds
                            Rectangle bounds = Rectangle.Empty;
                            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

                            var rect = new Rectangle(0, 0, img.Width, img.Height);
                            var data = img.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                            try
                            {
                                int bytes = Math.Abs(data.Stride) * img.Height;
                                var buffer = new byte[bytes];
                                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, bytes);
                                int pixSize = 4;
                                for (int y = 0; y < img.Height; y++)
                                {
                                    int row = y * data.Stride;
                                    for (int x = 0; x < img.Width; x++)
                                    {
                                        int idx = row + x * pixSize;
                                        byte a = buffer[idx + 3];
                                        if (a != 0)
                                        {
                                            if (x < minX) minX = x;
                                            if (y < minY) minY = y;
                                            if (x > maxX) maxX = x;
                                            if (y > maxY) maxY = y;
                                        }
                                    }
                                }
                            }
                            finally { img.UnlockBits(data); }

                            if (minX <= maxX && minY <= maxY)
                                bounds = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);

                            if (bounds == Rectangle.Empty)
                            {
                                this.BeginInvoke(new Action(() => outputTextBox.AppendText("Painter: no non-empty pixels to send.\r\n")));
                                img.Dispose();
                                mask.Dispose();
                                return;
                            }

                            // Crop images
                            var croppedImg = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                            using (var g = Graphics.FromImage(croppedImg))
                            {
                                g.DrawImage(img, new Rectangle(0, 0, bounds.Width, bounds.Height), bounds, GraphicsUnit.Pixel);
                            }

                            var croppedMask = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                            using (var g2 = Graphics.FromImage(croppedMask))
                            {
                                g2.DrawImage(mask, new Rectangle(0, 0, bounds.Width, bounds.Height), bounds, GraphicsUnit.Pixel);
                            }

                            // Save copies to temp
                            var tmp = Path.GetTempPath();
                            var name = $"painter_{DateTime.Now:yyyyMMdd_HHmmss}";
                            var imgPath = Path.Combine(tmp, name + ".png");
                            var maskPath = Path.Combine(tmp, name + "_mask.png");
                            try
                            {
                                croppedImg.Save(imgPath, ImageFormat.Png);
                                croppedMask.Save(maskPath, ImageFormat.Png);
                            }
                            catch { }

                            // Add to placedObjects at grid 0,0
                            this.BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    int gx = 0;
                                    int gy = 0;
                                    var iso = GridToIso(gx, gy);
                                    var po = new PlacedObject
                                    {
                                        Image = croppedImg, // keep reference
                                        GraphicId = name,
                                        GridX = gx,
                                        GridY = gy,
                                        Z = 0,
                                        Flags = 0,
                                        IsoPosition = iso,
                                        Layer = placedObjects.Count
                                    };
                                    placedObjects.Add(po);
                                    RebuildLockLists();
                                    designPictureBox.Invalidate();
                                    outputTextBox.AppendText($"Painter added to canvas at {gx},{gy} - saved {imgPath}\r\n");
                                }
                                catch (Exception ex)
                                {
                                    outputTextBox.AppendText($"Painter add error: {ex.Message}\r\n");
                                }
                            }));

                            // Dispose originals and mask copy (croppedImg kept)
                            img.Dispose();
                            mask.Dispose();
                            croppedMask.Dispose();
                        }
                        catch (Exception ex)
                        {
                            this.BeginInvoke(new Action(() => outputTextBox.AppendText($"Painter save error: {ex.Message}\r\n")));
                        }
                    };

                    Action<Bitmap, Bitmap> gumpHandler = null;
                    gumpHandler = (img, mask) =>
                    {
                        Bitmap imgClone = null;
                        Bitmap maskClone = null;
                        try
                        {
                            imgClone = new Bitmap(img);
                            if (mask != null) maskClone = new Bitmap(mask);
                        }
                        catch (Exception ex)
                        {
                            this.BeginInvoke(new Action(() => outputTextBox.AppendText($"Painter -> Gump clone error: {ex.Message}\r\n")));
                            img.Dispose();
                            mask?.Dispose();
                            return;
                        }
                        finally
                        {
                            img.Dispose();
                            mask?.Dispose();
                        }

                        try
                        {
                            void ensureGump()
                            {
                                if (_gumpEditor == null || _gumpEditor.IsDisposed)
                                {
                                    _gumpEditor = new GumpEditorForm
                                    {
                                        StartPosition = FormStartPosition.CenterScreen,
                                        ShowInTaskbar = true
                                    };
                                    _gumpEditor.FormClosed += (s2, e2) => { _gumpEditor = null; };
                                    _gumpEditor.Show();
                                }
                                else
                                {
                                    if (!_gumpEditor.Visible) _gumpEditor.Show();
                                    _gumpEditor.BringToFront();
                                }
                            }

                            this.BeginInvoke(new Action(() =>
                            {
                                ensureGump();
                                _gumpEditor?.AddCustomImage(imgClone);
                                maskClone?.Dispose();
                            }));
                        }
                        catch (Exception ex)
                        {
                            this.BeginInvoke(new Action(() => outputTextBox.AppendText($"Painter -> Gump error: {ex.Message}\r\n")));
                            imgClone?.Dispose();
                            maskClone?.Dispose();
                        }
                    };

                    Action<Bitmap, Bitmap, bool> mapHandler = null;
                    mapHandler = (img, mask, asOverlay) =>
                    {
                        Bitmap imgClone = null;
                        try
                        {
                            imgClone = new Bitmap(img);
                        }
                        catch (Exception ex)
                        {
                            this.BeginInvoke(new Action(() => outputTextBox.AppendText($"Painter -> Map clone error: {ex.Message}\r\n")));
                            img.Dispose();
                            mask?.Dispose();
                            return;
                        }
                        finally
                        {
                            img.Dispose();
                            mask?.Dispose();
                        }

                        this.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                if (_mapViewer == null || _mapViewer.IsDisposed)
                                {
                                    _mapViewer = new MapViewerForm
                                    {
                                        StartPosition = FormStartPosition.CenterScreen,
                                        ShowInTaskbar = true
                                    };
                                    _mapViewer.FormClosed += (s2, e2) => { _mapViewer = null; };
                                    _mapViewer.Show();
                                }
                                else
                                {
                                    if (!_mapViewer.Visible) _mapViewer.Show();
                                    _mapViewer.BringToFront();
                                }

                                _mapViewer.AddPainterImageToMapLayer(imgClone, asOverlay);
                                outputTextBox.AppendText($"Painter sent image to Map Editor as {(asOverlay ? "overlay" : "underlay")}\r\n");
                            }
                            catch (Exception ex)
                            {
                                imgClone?.Dispose();
                                outputTextBox.AppendText($"Painter -> Map error: {ex.Message}\r\n");
                            }
                        }));
                    };

                    Action<Bitmap, Bitmap> threeDHandler = null;
                    threeDHandler = (img, mask) =>
                    {
                        Bitmap imgClone = null;
                        try
                        {
                            imgClone = new Bitmap(img);
                        }
                        catch (Exception ex)
                        {
                            this.BeginInvoke(new Action(() => outputTextBox.AppendText($"Painter -> 3D clone error: {ex.Message}\r\n")));
                            img.Dispose();
                            mask?.Dispose();
                            return;
                        }
                        finally
                        {
                            img.Dispose();
                            mask?.Dispose();
                        }

                        try
                        {
                            this.BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    EnsureThreeDEditor();
                                    _threeDEditor?.SetInitImage(imgClone);
                                    outputTextBox.AppendText("Painter sent image to 3D Editor as init image\r\n");
                                }
                                catch (Exception ex)
                                {
                                    outputTextBox.AppendText($"Painter -> 3D error: {ex.Message}\r\n");
                                }
                                finally
                                {
                                    imgClone?.Dispose();
                                }
                            }));
                        }
                        catch (Exception ex)
                        {
                            this.BeginInvoke(new Action(() => outputTextBox.AppendText($"Painter -> 3D error: {ex.Message}\r\n")));
                            imgClone?.Dispose();
                        }
                    };

                    painter.ImageReady += handler;
                    painter.ImageReadyForGump += gumpHandler;
                    painter.ImageReadyForMapEditor += mapHandler;
                    painter.ImageReadyFor3D += threeDHandler;
                    painter.FormClosed += (s2, e2) =>
                    {
                        painter.ImageReady -= handler;
                        painter.ImageReadyForGump -= gumpHandler;
                        painter.ImageReadyForMapEditor -= mapHandler;
                        painter.ImageReadyFor3D -= threeDHandler;
                        painter.Dispose();
                    };

                    painter.Show(); // modeless so user can Alt-Tab
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to open Painter: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            buttonTooltip.SetToolTip(painterBtn, "Open pixel art Painter with layers and drawing tools");
            this.controlsPanel.Controls.Add(painterBtn);

            // 3D Editor button (MultisMaker1 text-to-PLY + UO sprite viewport)
            var threeDBtn = CreateThemedButton("3D Editor", 400, 70, 95, ButtonStyle.Accent);
            threeDBtn.Click += (s, e) =>
            {
                try
                {
                    EnsureThreeDEditor();
                    return;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to open 3D Editor: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            buttonTooltip.SetToolTip(threeDBtn, "Generate 3D with ComfyUI MultisMaker1 and export UO isometric sprites");
            this.controlsPanel.Controls.Add(threeDBtn);

            // Audio Editor button (ComfyUI SFX / music / voice + UO sound.mul)
            var audioBtn = CreateThemedButton("Audio Editor", 500, 70, 95, ButtonStyle.Accent);
            audioBtn.Click += (s, e) =>
            {
                try
                {
                    EnsureAudioEditor();
                    return;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to open Audio Editor: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            buttonTooltip.SetToolTip(audioBtn, "Generate SFX, music and voice with ComfyUI; edit audio and write UO sound.mul slots");
            this.controlsPanel.Controls.Add(audioBtn);

            var sendSelToMapUnderBtn = CreateThemedButton("Sel?Map U", 10, 70, 95, ButtonStyle.Accent);
            sendSelToMapUnderBtn.Click += (s, e) => SendCurrentCanvasImageToMap(false);
            buttonTooltip.SetToolTip(sendSelToMapUnderBtn, "Send selected canvas object (or full canvas) to Map Editor as underlay");
            this.controlsPanel.Controls.Add(sendSelToMapUnderBtn);

            var sendSelToMapOverBtn = CreateThemedButton("Sel?Map O", 110, 70, 95, ButtonStyle.Accent);
            sendSelToMapOverBtn.Click += (s, e) => SendCurrentCanvasImageToMap(true);
            buttonTooltip.SetToolTip(sendSelToMapOverBtn, "Send selected canvas object (or full canvas) to Map Editor as overlay");
            this.controlsPanel.Controls.Add(sendSelToMapOverBtn);

            // GUMP Editor button
            var gumpEditorBtn = CreateThemedButton("GUMP Editor", 788, 42, 95, ButtonStyle.Accent);
            gumpEditorBtn.Click += (s, e) =>
            {
                try
                {
                    if (_gumpEditor == null || _gumpEditor.IsDisposed)
                    {
                        _gumpEditor = new GumpEditorForm
                        {
                            StartPosition = FormStartPosition.CenterScreen,
                            ShowInTaskbar = true
                        };
                        _gumpEditor.FormClosed += (s2, e2) => { _gumpEditor = null; };
                        _gumpEditor.Show();
                    }
                    else
                    {
                        if (!_gumpEditor.Visible) _gumpEditor.Show();
                        _gumpEditor.BringToFront();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to open GUMP Editor: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            buttonTooltip.SetToolTip(gumpEditorBtn, "Open GUMP Editor to create and edit UI graphics");
            this.controlsPanel.Controls.Add(gumpEditorBtn);

            // Texture Editor button
            var textureEditorBtn = CreateThemedButton("Texture Editor", 210, 70, 110, ButtonStyle.Accent);
            textureEditorBtn.Click += (s, e) =>
            {
                try
                {
                    if (_textureEditor == null || _textureEditor.IsDisposed)
                    {
                        _textureEditor = new TextureEditorForm
                        {
                            StartPosition = FormStartPosition.CenterScreen,
                            ShowInTaskbar = true
                        };
                        _textureEditor.FormClosed += (s2, e2) => { _textureEditor = null; };
                        _textureEditor.Show();
                    }
                    else
                    {
                        if (!_textureEditor.Visible) _textureEditor.Show();
                        _textureEditor.BringToFront();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to open Texture Editor: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            buttonTooltip.SetToolTip(textureEditorBtn, "Open Texture Editor to edit land textures from texmaps.mul");
            this.controlsPanel.Controls.Add(textureEditorBtn);

            // Check for Updates button
            var updateBtn = CreateThemedButton("Updates", 325, 70, 70, ButtonStyle.Default);
            updateBtn.Click += async (s, e) =>
            {
                updateBtn.Enabled = false;
                updateBtn.Text = "...";
                try
                {
                    await UpdateChecker.CheckAndPromptAsync(this, silent: false);
                }
                finally
                {
                    updateBtn.Enabled = true;
                    updateBtn.Text = "Updates";
                }
            };
            buttonTooltip.SetToolTip(updateBtn, $"Check for updates (current: {UpdateChecker.CurrentVersionString})");
            this.controlsPanel.Controls.Add(updateBtn);

            // Slice Tool button
            var sliceBtn = CreateThemedButton("Slice Tool", 10, 145, 110, ButtonStyle.Warning);
            sliceBtn.Click += (s, e) => ToggleSliceMode();
            buttonTooltip.SetToolTip(sliceBtn, "Toggle slice mode to cut objects into smaller pieces");
            this.controlsPanel.Controls.Add(sliceBtn);

            // Remove Duplicates button
            var removeDupesBtn = CreateThemedButton("Remove Dupes", 130, 145, 85, ButtonStyle.Danger);
            removeDupesBtn.Click += (s, e) => RemoveDuplicateObjects();
            buttonTooltip.SetToolTip(removeDupesBtn, "Remove duplicate objects by GraphicId (keeps first of each unique ID)");
            this.controlsPanel.Controls.Add(removeDupesBtn);

            // AI export/import buttons - Row 4
            var exportPartsBtn = CreateThemedButton("Export Parts", 10, 100, 110, ButtonStyle.Default);
            exportPartsBtn.Click += (s, e) => ExportComponentsForAi();
            buttonTooltip.SetToolTip(exportPartsBtn, "Export selected objects as individual images for AI processing");
            this.controlsPanel.Controls.Add(exportPartsBtn);

            var importPartsBtn = CreateThemedButton("Import Parts", 130, 100, 110, ButtonStyle.Default);
            importPartsBtn.Click += (s, e) => ImportComponentsFromAi();
            buttonTooltip.SetToolTip(importPartsBtn, "Import AI-processed images back to canvas");
            this.controlsPanel.Controls.Add(importPartsBtn);

            // Import Multi Text button - parses text from output textbox
            var importMultiTextBtn = CreateThemedButton("Import Multi Text", 250, 100, 120, ButtonStyle.Success);
            importMultiTextBtn.Click += (s, e) => ImportTextButton_Click(null, EventArgs.Empty);
            buttonTooltip.SetToolTip(importMultiTextBtn, "Import multi structure from text in output box");
            this.controlsPanel.Controls.Add(importMultiTextBtn);

            // Max Z Height label and textbox
            this.maxZLabel = new Label { Text = "Max Z Height", Left = 380, Top = 103, Width = 80, Height = 20, TextAlign = ContentAlignment.MiddleLeft };
            HolographicTheme.ApplyToLabel(this.maxZLabel);
            this.controlsPanel.Controls.Add(this.maxZLabel);

            this.maxZTextBox = new TextBox { Left = 465, Top = 100, Width = 40, Height = 25, Text = "100" };
            HolographicTheme.ApplyToTextBox(this.maxZTextBox);
            buttonTooltip.SetToolTip(this.maxZTextBox, "Maximum Z height for exported multi structure");
            this.controlsPanel.Controls.Add(this.maxZTextBox);

            // Transform controls - Row 4 (next to Max Z Height)
            var scaleUpBtn = CreateThemedButton("S+", 515, 100, 35, ButtonStyle.Default);
            scaleUpBtn.Click += (s, e) => ScaleSelectedObjects(SCALE_INCREMENT);
            buttonTooltip.SetToolTip(scaleUpBtn, "Scale selected object up (increase size)");
            this.controlsPanel.Controls.Add(scaleUpBtn);

            var scaleDownBtn = CreateThemedButton("S-", 555, 100, 35, ButtonStyle.Default);
            scaleDownBtn.Click += (s, e) => ScaleSelectedObjects(-SCALE_INCREMENT);
            buttonTooltip.SetToolTip(scaleDownBtn, "Scale selected object down (decrease size)");
            this.controlsPanel.Controls.Add(scaleDownBtn);

            // Rotate button - toggles free rotation mode (drag to rotate any angle)
            var rotateBtn = CreateThemedButton("Rot", 595, 100, 40, ButtonStyle.Accent);
            rotateBtn.Click += (s, e) => ToggleRotateMode();
            buttonTooltip.SetToolTip(rotateBtn, "Toggle rotation mode (drag to rotate selected object)");
            this.controlsPanel.Controls.Add(rotateBtn);

            var flipHBtn = CreateThemedButton("FH", 640, 100, 35, ButtonStyle.Default);
            flipHBtn.Click += (s, e) => FlipSelectedObjects(true, false);
            buttonTooltip.SetToolTip(flipHBtn, "Flip selected object horizontally");
            this.controlsPanel.Controls.Add(flipHBtn);

            var flipVBtn = CreateThemedButton("FV", 680, 100, 35, ButtonStyle.Default);
            flipVBtn.Click += (s, e) => FlipSelectedObjects(false, true);
            buttonTooltip.SetToolTip(flipVBtn, "Flip selected object vertically");
            this.controlsPanel.Controls.Add(flipVBtn);

            // Skew/Distort button - toggles skew mode for selected object
            var skewBtn = CreateThemedButton("Skew", 720, 100, 50, ButtonStyle.Accent);
            skewBtn.Click += (s, e) => ToggleSkewMode();
            buttonTooltip.SetToolTip(skewBtn, "Toggle skew mode (drag corners to distort)");
            this.controlsPanel.Controls.Add(skewBtn);

            // Artwork offset controls - nudge selected art on its tile in
            // whole pixels (+/- X and Y, same as Alt+Arrow keys). Absolute
            // set (not nudge): type the offset, all selected take it.
            var offXLabel = new Label { Text = "OffX", Left = 778, Top = 103, Width = 36, Height = 20, TextAlign = ContentAlignment.MiddleLeft };
            HolographicTheme.ApplyToLabel(offXLabel);
            this.controlsPanel.Controls.Add(offXLabel);
            this.offsetXNum = new NumericUpDown { Left = 814, Top = 100, Width = 58, Height = 25, Minimum = -999, Maximum = 999, Increment = 1 };
            HolographicTheme.ApplyToNumericUpDown(this.offsetXNum);
            this.offsetXNum.ValueChanged += OffsetNumeric_Changed;
            buttonTooltip.SetToolTip(this.offsetXNum, "Artwork X offset in pixels (Alt+Left/Right does the same, Shift = x5)");
            this.controlsPanel.Controls.Add(this.offsetXNum);
            var offYLabel = new Label { Text = "OffY", Left = 876, Top = 103, Width = 36, Height = 20, TextAlign = ContentAlignment.MiddleLeft };
            HolographicTheme.ApplyToLabel(offYLabel);
            this.controlsPanel.Controls.Add(offYLabel);
            this.offsetYNum = new NumericUpDown { Left = 912, Top = 100, Width = 58, Height = 25, Minimum = -999, Maximum = 999, Increment = 1 };
            HolographicTheme.ApplyToNumericUpDown(this.offsetYNum);
            this.offsetYNum.ValueChanged += OffsetNumeric_Changed;
            buttonTooltip.SetToolTip(this.offsetYNum, "Artwork Y offset in pixels (Alt+Up/Down does the same, Shift = x5)");
            this.controlsPanel.Controls.Add(this.offsetYNum);
            var offsetResetBtn = CreateThemedButton("O0", 974, 100, 38, ButtonStyle.Default);
            offsetResetBtn.Click += (s, e) => ResetSelectedObjectsOffset();
            buttonTooltip.SetToolTip(offsetResetBtn, "Reset artwork offset to (0,0) (same as Ctrl+T without touching scale/rotation)");
            this.controlsPanel.Controls.Add(offsetResetBtn);
            this._offsetTimer = new System.Windows.Forms.Timer { Interval = 250 };
            this._offsetTimer.Tick += (s, e) => { try { RefreshOffsetControls(); } catch { } };
            this._offsetTimer.Start();

            // Animation Editor button (grouped with 3D/Audio Editors above)
            var animEditorBtn = CreateThemedButton("Anim Editor", 600, 70, 95, ButtonStyle.Accent);
            animEditorBtn.Click += (s, e) =>
            {
                try
                {
                    if (_animationEditor == null || _animationEditor.IsDisposed)
                    {
                        _animationEditor = new AnimationEditorForm
                        {
                            StartPosition = FormStartPosition.CenterScreen,
                            ShowInTaskbar = true
                        };
                        _animationEditor.FormClosed += (s2, e2) => { _animationEditor = null; };
                        _animationEditor.Show();
                    }
                    else
                    {
                        if (!_animationEditor.Visible) _animationEditor.Show();
                        _animationEditor.BringToFront();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to open Animation Editor: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            buttonTooltip.SetToolTip(animEditorBtn, "Open Animation Editor to view and edit mobile animations");
            this.controlsPanel.Controls.Add(animEditorBtn);

            // Z Filter Controls - Row 5
            var zFilterLabel = new Label { Text = "Z Filter:", Left = 220, Top = 150, Width = 60, Height = 20, TextAlign = ContentAlignment.MiddleLeft };
            HolographicTheme.ApplyToLabel(zFilterLabel, true);
            this.controlsPanel.Controls.Add(zFilterLabel);

            var minZLabel = new Label { Text = "Min:", Left = 285, Top = 150, Width = 30, Height = 20, TextAlign = ContentAlignment.MiddleLeft };
            HolographicTheme.ApplyToLabel(minZLabel);
            this.controlsPanel.Controls.Add(minZLabel);

            this.minZTrackBar = new TrackBar { Left = 320, Top = 146, Width = 150, Height = 25, Minimum = -127, Maximum = 127, Value = -127, TickFrequency = 10, SmallChange = 1, LargeChange = 1 };
            HolographicTheme.ApplyToTrackBar(this.minZTrackBar);
            this.minZTrackBar.ValueChanged += (s, e) => { minZValueLabel.Text = minZTrackBar.Value.ToString(); designPictureBox.Invalidate(); };
            buttonTooltip.SetToolTip(this.minZTrackBar, "Filter: hide objects below this Z height");
            this.controlsPanel.Controls.Add(this.minZTrackBar);

            this.minZValueLabel = new Label { Left = 475, Top = 150, Width = 35, Height = 20, Text = "-127", TextAlign = ContentAlignment.MiddleLeft };
            HolographicTheme.ApplyToLabel(this.minZValueLabel);
            this.controlsPanel.Controls.Add(this.minZValueLabel);

            var maxZFilterLabel = new Label { Text = "Max:", Left = 520, Top = 150, Width = 35, Height = 20, TextAlign = ContentAlignment.MiddleLeft };
            HolographicTheme.ApplyToLabel(maxZFilterLabel);
            this.controlsPanel.Controls.Add(maxZFilterLabel);

            this.maxZTrackBar = new TrackBar { Left = 560, Top = 146, Width = 150, Height = 25, Minimum = -127, Maximum = 127, Value = 127, TickFrequency = 10, SmallChange = 1, LargeChange = 1 };
            HolographicTheme.ApplyToTrackBar(this.maxZTrackBar);
            this.maxZTrackBar.ValueChanged += (s, e) => { maxZValueLabel.Text = maxZTrackBar.Value.ToString(); designPictureBox.Invalidate(); };
            buttonTooltip.SetToolTip(this.maxZTrackBar, "Filter: hide objects above this Z height");
            this.controlsPanel.Controls.Add(this.maxZTrackBar);

            this.maxZValueLabel = new Label { Left = 715, Top = 150, Width = 35, Height = 20, Text = "127", TextAlign = ContentAlignment.MiddleLeft };
            HolographicTheme.ApplyToLabel(this.maxZValueLabel);
            this.controlsPanel.Controls.Add(this.maxZValueLabel);

            var resetZFilterBtn = CreateThemedButton("Reset Z", 760, 147, 70, ButtonStyle.Default);
            resetZFilterBtn.Click += (s, e) => { minZTrackBar.Value = -127; maxZTrackBar.Value = 127; };
            buttonTooltip.SetToolTip(resetZFilterBtn, "Reset Z filter to show all objects");
            this.controlsPanel.Controls.Add(resetZFilterBtn);

            hideFloorsButton = CreateThemedButton("Hide Floors", 835, 147, 95, ButtonStyle.Default);
            hideFloorsButton.Click += (s, e) => ToggleHideFloors();
            buttonTooltip.SetToolTip(hideFloorsButton, "Hide floor tiles (TileData Surface/Bridge) so walls can be edited. Click again to show.");
            this.controlsPanel.Controls.Add(hideFloorsButton);

            // Increase controls panel height
            this.controlsPanel.Height = 180;

            // Keyboard
            this.KeyPreview = true;
            this.KeyDown += Form1_KeyDown;

            // Lock/Unlock lists
            this.unlockedListBox.DoubleClick += (s, e) => ToggleLockFromList(unlockedListBox);
            this.lockedListBox.DoubleClick += (s, e) => ToggleLockFromList(lockedListBox);
            this.unlockedListBox.SelectedIndexChanged += (s, e) => SelectFromList(unlockedListBox);
            this.lockedListBox.SelectedIndexChanged += (s, e) => SelectFromList(lockedListBox);
        }

        /// <summary>
        /// Toggles visibility of floor tiles (TileRole.Floor via TileData flags
        /// with Z-heuristic fallback) so walls/interiors of houses and multis
        /// can be edited without floors blocking selection. Purely a canvas
        /// aid - hidden floors still export. Objects the user hid manually (H)
        /// are never touched.
        /// </summary>
        private void ToggleHideFloors()
        {
            if (!hideFloorsActive)
            {
                var floorLevels = Generation.MultiRules.DetectFloorLevels(placedObjects.Select(o => o.Z));
                int count = 0;
                foreach (var obj in placedObjects)
                {
                    if (obj.Hidden || obj.Image == null) continue;
                    int gidVal;
                    if (!TryParseGraphicId(obj.GraphicId, out gidVal) || gidVal < 0 || gidVal > 0xFFFF) continue;
                    var role = Generation.MultiRules.ClassifyRole((ushort)gidVal, obj.Z, obj.Flags, floorLevels);
                    if (role == Generation.TileRole.Floor)
                    {
                        obj.Hidden = true;
                        floorsHiddenByToggle.Add(obj);
                        count++;
                    }
                }
                hideFloorsActive = true;
                if (hideFloorsButton != null) hideFloorsButton.Text = "Show Floors";
                outputTextBox.AppendText($"Hid {count} floor tile(s). Click Show Floors to restore.\r\n");
            }
            else
            {
                int count = 0;
                foreach (var obj in floorsHiddenByToggle.ToList())
                {
                    if (placedObjects.Contains(obj))
                    {
                        obj.Hidden = false;
                        count++;
                    }
                }
                floorsHiddenByToggle.Clear();
                hideFloorsActive = false;
                if (hideFloorsButton != null) hideFloorsButton.Text = "Hide Floors";
                outputTextBox.AppendText($"Restored {count} floor tile(s).\r\n");
            }
            designPictureBox.Invalidate();
        }

        /// <summary>
        /// Creates a themed button with the holographic style
        /// </summary>
        private Button CreateThemedButton(string text, int left, int top, int width, ButtonStyle style = ButtonStyle.Default)
        {
            var btn = new Button
            {
                Text = text,
                Left = left,
                Top = top,
                Width = width,
                Height = 25
            };
            HolographicTheme.ApplyToButton(btn, style);
            return btn;
        }

        /// <summary>
        /// Apply holographic theme to the entire form
        /// </summary>
        private void ApplyHolographicTheme()
        {
            // Apply to form
            HolographicTheme.ApplyToForm(this);

            // Apply to main containers
            HolographicTheme.ApplyToSplitContainer(mainSplitContainer);
            HolographicTheme.ApplyToSplitContainer(rightSplitContainer);

            // Apply to panels
            HolographicTheme.ApplyToPanel(controlsPanel);
            HolographicTheme.ApplyToPanel(palettePanel);
            HolographicTheme.ApplyToPanel(lockPanel);

            // Apply to canvas
            HolographicTheme.ApplyToCanvas(designPictureBox);

            // Apply to palette
            HolographicTheme.ApplyToListView(paletteListView);
            HolographicTheme.ApplyToTextBox(searchTextBox);
            searchTextBox.ForeColor = HolographicTheme.TextMuted;

            // Apply to output textbox
            HolographicTheme.ApplyToTextBox(outputTextBox);

            // Apply to lock/unlock groups and lists
            HolographicTheme.ApplyToGroupBox(unlockedGroup);
            HolographicTheme.ApplyToGroupBox(lockedGroup);
            HolographicTheme.ApplyToListBox(unlockedListBox);
            HolographicTheme.ApplyToListBox(lockedListBox);

            // Apply to numeric controls
            HolographicTheme.ApplyToNumericUpDown(widthNumericUpDown);
            HolographicTheme.ApplyToNumericUpDown(heightNumericUpDown);

            // Apply to labels
            HolographicTheme.ApplyToLabel(widthLabel);
            HolographicTheme.ApplyToLabel(heightLabel);

            // Apply to existing designer buttons
            HolographicTheme.ApplyToButton(createGridButton);
            HolographicTheme.ApplyToButton(zUpButton);
            HolographicTheme.ApplyToButton(zDownButton);
            HolographicTheme.ApplyToButton(deleteButton, ButtonStyle.Danger);
            HolographicTheme.ApplyToButton(exportButton, ButtonStyle.Success);
            HolographicTheme.ApplyToButton(browseButton);
            HolographicTheme.ApplyToButton(layerUpButton);
            HolographicTheme.ApplyToButton(layerDownButton);
            HolographicTheme.ApplyToButton(layerTopButton);
            HolographicTheme.ApplyToButton(layerBottomButton);
            HolographicTheme.ApplyToButton(lockSelectedButton, ButtonStyle.Warning);
            HolographicTheme.ApplyToButton(unlockSelectedButton);
        }

        private void InitializeAISettingsPanel()
        {
            // Create and add the AI Settings Panel to the right side
            _aiSettingsPanel = new AISettingsPanel();
            _aiSettingsPanel.Dock = DockStyle.Fill;

            // Set fixed width for the AI settings panel area - increased for better label visibility
            rightSplitContainer.FixedPanel = FixedPanel.Panel2;
            rightSplitContainer.Panel2MinSize = 320;
            rightSplitContainer.SplitterDistance = rightSplitContainer.Width - 320;
            rightSplitContainer.IsSplitterFixed = true;
            // Wire up button events from the panel
            _aiSettingsPanel.AIRegenSelectedClicked += async (s, e) => await AIRegenSelected();
            _aiSettingsPanel.AIRegenAsOneClicked += async (s, e) => await AIRegenSelectedAsOne();
            _aiSettingsPanel.StopAIGenerationClicked += (s, e) => CancelAIGeneration();
            _aiSettingsPanel.RevertAIChangesClicked += (s, e) => RevertAIChanges();
            _aiSettingsPanel.RevertOGClicked += (s, e) => RevertToOriginalArt();
            _aiSettingsPanel.UnifyIdsClicked += (s, e) => UnifySameIdArt();
            _aiSettingsPanel.SaveToMulClicked += (s, e) => SaveChangesToMul();
            _aiSettingsPanel.SaveToNewSlotClicked += (s, e) => SaveToNewSlots();
            _aiSettingsPanel.SaveCanvasAsNewClicked += (s, e) => SaveCanvasAsNew();
            _aiSettingsPanel.PushToJarJarClicked += async (s, e) => await PushToJarJar();
            _aiSettingsPanel.OldNewClicked += (s, e) => ToggleOldNew();

            // Add to the AI panel placeholder in the designer
            this.aiGeneratorPanel.Controls.Add(_aiSettingsPanel);
        }

        /// <summary>
        /// Gets the current AI settings from the docked panel
        /// </summary>
        private BatchSettings GetAISettingsFromPanel()
        {
            return new BatchSettings
            {
                Url = _aiSettingsPanel.ComfyUrl,
                Prompt = _aiSettingsPanel.Prompt,
                Negative = _aiSettingsPanel.NegativePrompt,
                Steps = _aiSettingsPanel.Steps,
                Cfg = _aiSettingsPanel.Cfg,
                Denoise = _aiSettingsPanel.Denoise,
                Seed = _aiSettingsPanel.Seed,
                Sampler = _aiSettingsPanel.Sampler,
                Scheduler = _aiSettingsPanel.Scheduler
            };
        }

        /// <summary>
        /// Updates the AI panel status message
        /// </summary>
        private void SetAIStatus(string message, Color? color = null)
        {
            _aiSettingsPanel?.SetStatus(message, color);
        }

        /// <summary>
        /// Gets whether black pixel dropping is enabled
        /// </summary>
        private bool GetDropBlackPixelsEnabled()
        {
            return _aiSettingsPanel?.DropBlackPixels ?? false;
        }

        /// <summary>
        /// Gets the threshold for black pixel dropping (0-255)
        /// </summary>
        private int GetBlackPixelThreshold()
        {
            return _aiSettingsPanel?.BlackThreshold ?? 15;
        }

        /// <summary>
        /// Gets the resolution width for AI generation
        /// </summary>
        private int GetAIResolutionWidth()
        {
            return _aiSettingsPanel?.ResolutionWidth ?? 512;
        }

        /// <summary>
        /// Gets the resolution height for AI generation
        /// </summary>
        private int GetAIResolutionHeight()
        {
            return _aiSettingsPanel?.ResolutionHeight ?? 512;
        }

        /// <summary>
        /// Gets whether to preserve original alpha mask when splitting composites
        /// </summary>
        private bool GetPreserveAlphaMask()
        {
            return _aiSettingsPanel?.PreserveAlphaMask ?? true;
        }

        private void OnDesignPictureBox_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.Bitmap) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnDesignPictureBox_DragDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.Bitmap)) return;
            PushUndo();

            Point pt = designPictureBox.PointToClient(new Point(e.X, e.Y));
            Point gridPos = IsoToGrid(pt.X, pt.Y);
            gridPos.X = Math.Max(0, Math.Min(gridWidth - 1, gridPos.X));
            gridPos.Y = Math.Max(0, Math.Min(gridHeight - 1, gridPos.Y));

            // Check for multi-item drag
            var multiIds = e.Data.GetData("MultiItemIds") as ushort[];
            if (multiIds != null && multiIds.Length > 1)
            {
                // Arrange items in a grid pattern centered on the drop position
                int cols = (int)Math.Ceiling(Math.Sqrt(multiIds.Length));
                int startX = gridPos.X - cols / 2;
                int startY = gridPos.Y - cols / 2;

                for (int i = 0; i < multiIds.Length; i++)
                {
                    int gx = startX + (i % cols);
                    int gy = startY + (i / cols);
                    gx = Math.Max(0, Math.Min(gridWidth - 1, gx));
                    gy = Math.Max(0, Math.Min(gridHeight - 1, gy));

                    Image img = null;
                    try { img = LoadStaticItemImage(multiIds[i]); }
                    catch { continue; }
                    if (img == null) continue;

                    string gid = $"0x{multiIds[i]:X4}";
                    Point isoPos = GridToIso(gx, gy);
                    placedObjects.Add(new PlacedObject
                    {
                        Image = img,
                        GraphicId = gid,
                        GridX = gx,
                        GridY = gy,
                        Z = 0,
                        Flags = 1,
                        IsoPosition = isoPos,
                        Layer = placedObjects.Count
                    });
                }
            }
            else
            {
                // Single item drop
                Image img = (Image)e.Data.GetData(DataFormats.Bitmap);
                string graphicId = e.Data.GetData("GraphicId") as string;
                Point isoPos = GridToIso(gridPos.X, gridPos.Y);
                placedObjects.Add(new PlacedObject
                {
                    Image = img,
                    GraphicId = graphicId,
                    GridX = gridPos.X,
                    GridY = gridPos.Y,
                    Z = 0,
                    Flags = 1,
                    IsoPosition = isoPos,
                    Layer = placedObjects.Count
                });
            }

            RebuildLockLists();
            designPictureBox.Invalidate();
        }

        private bool EnsureLicenseAccepted()
        {
            try
            {
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MeesaMultisMaker");
                string flagPath = Path.Combine(appData, "license.accepted");
                if (File.Exists(flagPath)) return true;

                string licenseText = "License file not found.";
                try
                {
                    var licensePath = FindLicenseFile();
                    if (!string.IsNullOrEmpty(licensePath)) licenseText = File.ReadAllText(licensePath);
                }
                catch { }

                var result = ShowLicenseDialog(licenseText);
                if (result)
                {
                    Directory.CreateDirectory(appData);
                    File.WriteAllText(flagPath, $"Accepted {DateTime.UtcNow:O}");
                    return true;
                }
                return false;
            }
            catch
            {
                // If anything fails, block usage until license explicitly accepted
                MessageBox.Show("Unable to verify license acceptance. The application will close.", "License Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private string FindLicenseFile()
        {
            try
            {
                var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                for (int i = 0; i < 6 && dir != null; i++)
                {
                    var candidate = Path.Combine(dir.FullName, "LICENSE");
                    if (File.Exists(candidate)) return candidate;
                    dir = dir.Parent;
                }
            }
            catch { }
            return null;
        }

        private bool ShowLicenseDialog(string licenseText)
        {
            using (var dlg = new Form())
            {
                dlg.Text = "MEESA MULTIS MAKER � LICENSE";
                dlg.Width = 700;
                dlg.Height = 600;
                dlg.StartPosition = FormStartPosition.CenterScreen;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;

                var browser = new WebBrowser
                {
                    Dock = DockStyle.Fill,
                    AllowWebBrowserDrop = false,
                    IsWebBrowserContextMenuEnabled = false,
                    ScriptErrorsSuppressed = true,
                    WebBrowserShortcutsEnabled = false
                };
                browser.DocumentText = RenderLicenseHtml(licenseText);

                var buttonsPanel = new Panel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(10) };
                var agreeBtn = new Button { Text = "I Agree", DialogResult = DialogResult.OK, Width = 100, Height = 30, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };
                var declineBtn = new Button { Text = "Decline", DialogResult = DialogResult.Cancel, Width = 100, Height = 30, Anchor = AnchorStyles.Right | AnchorStyles.Bottom };

                declineBtn.Left = buttonsPanel.Width - declineBtn.Width - 10;
                agreeBtn.Left = declineBtn.Left - agreeBtn.Width - 10;
                declineBtn.Top = agreeBtn.Top = 10;

                buttonsPanel.Controls.Add(agreeBtn);
                buttonsPanel.Controls.Add(declineBtn);
                buttonsPanel.Resize += (s, e) =>
                {
                    declineBtn.Left = buttonsPanel.Width - declineBtn.Width - 10;
                    agreeBtn.Left = declineBtn.Left - agreeBtn.Width - 10;
                };

                dlg.Controls.Add(browser);
                dlg.Controls.Add(buttonsPanel);
                dlg.AcceptButton = agreeBtn;
                dlg.CancelButton = declineBtn;

                return dlg.ShowDialog() == DialogResult.OK;
            }
        }

        private string RenderLicenseHtml(string licenseText)
        {
            var sb = new StringBuilder();
            sb.Append("<html><head><meta charset='UTF-8'><style>");
            sb.Append("body{font-family:'Segoe UI',sans-serif;font-size:10pt;padding:10px;background:#f7f7f7;color:#222;}");
            sb.Append("h1{font-size:16pt;margin:0 0 6px;} h2{font-size:13pt;margin:12px 0 4px;} h3{font-size:11pt;margin:10px 0 4px;}");
            sb.Append("p{margin:4px 0;} ul{margin:4px 0 8px 18px;} li{margin:2px 0;}");
            sb.Append("</style></head><body>");

            var lines = (licenseText ?? string.Empty).Replace("\r\n", "\n").Split(new[] { '\n' }, StringSplitOptions.None);
            bool inList = false;
            foreach (var raw in lines)
            {
                var line = raw ?? string.Empty;
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (inList) { sb.Append("</ul>"); inList = false; }
                    sb.Append("<p></p>");
                    continue;
                }

                if (line.StartsWith("### "))
                {
                    if (inList) { sb.Append("</ul>"); inList = false; }
                    sb.AppendFormat("<h3>{0}</h3>", WebUtility.HtmlEncode(line.Substring(4)));
                }
                else if (line.StartsWith("## "))
                {
                    if (inList) { sb.Append("</ul>"); inList = false; }
                    sb.AppendFormat("<h2>{0}</h2>", WebUtility.HtmlEncode(line.Substring(3)));
                }
                else if (line.StartsWith("# "))
                {
                    if (inList) { sb.Append("</ul>"); inList = false; }
                    sb.AppendFormat("<h1>{0}</h1>", WebUtility.HtmlEncode(line.Substring(2)));
                }
                else if (line.StartsWith("- "))
                {
                    if (!inList) { sb.Append("<ul>"); inList = true; }
                    sb.AppendFormat("<li>{0}</li>", WebUtility.HtmlEncode(line.Substring(2)));
                }
                else
                {
                    if (inList) { sb.Append("</ul>"); inList = false; }
                    sb.AppendFormat("<p>{0}</p>", WebUtility.HtmlEncode(line));
                }
            }
            if (inList) sb.Append("</ul>");
            sb.Append("</body></html>");
            return sb.ToString();
        }

        /// <summary>
        /// Show the shared 3D editor instance (creating it on first use),
        /// wiring its SpriteReady output back to the main canvas.
        /// </summary>
        /// <summary>
        /// Show the shared Audio Editor instance (creating it on first use).
        /// </summary>
        private void EnsureAudioEditor()
        {
            if (_audioEditor == null || _audioEditor.IsDisposed)
            {
                var editor = new AudioEditorForm();
                editor.FormClosed += (s2, e2) =>
                {
                    try { editor.Dispose(); } catch { }
                    _audioEditor = null;
                };
                _audioEditor = editor;
                editor.Show(); // modeless so user can keep working
            }
            else
            {
                if (!_audioEditor.Visible) _audioEditor.Show();
                _audioEditor.BringToFront();
            }
        }

        private void EnsureThreeDEditor()
        {
            if (_threeDEditor == null || _threeDEditor.IsDisposed)
            {
                var editor = new ThreeDEditorForm();
                Action<Bitmap, string> spriteHandler = null;
                spriteHandler = (bmp, name) =>
                {
                    try
                    {
                        if (bmp == null) return;
                        this.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                int gx = 0, gy = 0;
                                var iso = GridToIso(gx, gy);
                                var po = new PlacedObject
                                {
                                    Image = bmp, // keep reference
                                    GraphicId = name,
                                    GridX = gx,
                                    GridY = gy,
                                    Z = 0,
                                    Flags = 0,
                                    IsoPosition = iso,
                                    Layer = placedObjects.Count
                                };
                                placedObjects.Add(po);
                                RebuildLockLists();
                                designPictureBox.Invalidate();
                                outputTextBox.AppendText("3D Editor added '" + name + "' to canvas.\r\n");
                            }
                            catch (Exception ex)
                            {
                                outputTextBox.AppendText("3D Editor add error: " + ex.Message + "\r\n");
                                try { bmp.Dispose(); } catch { }
                            }
                        }));
                    }
                    catch (Exception ex)
                    {
                        this.BeginInvoke(new Action(() => outputTextBox.AppendText("3D Editor error: " + ex.Message + "\r\n")));
                    }
                };
                editor.SpriteReady += spriteHandler;
                Action<System.Collections.Generic.List<ThreeDEditorForm.TileSprite>> tilesHandler = null;
                tilesHandler = (sprites) =>
                {
                    try
                    {
                        if (sprites == null || sprites.Count == 0) return;
                        this.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                // Center the incoming mosaic on the canvas
                                // grid: tile coords from the editor are
                                // origin-relative (can start negative), so
                                // placing them raw strands the batch in a
                                // corner, half off the grid.
                                int minBX = int.MaxValue, maxBX = int.MinValue;
                                int minBY = int.MaxValue, maxBY = int.MinValue;
                                foreach (var s in sprites)
                                {
                                    if (s == null || s.Image == null) continue;
                                    if (s.GridX < minBX) minBX = s.GridX;
                                    if (s.GridX > maxBX) maxBX = s.GridX;
                                    if (s.GridY < minBY) minBY = s.GridY;
                                    if (s.GridY > maxBY) maxBY = s.GridY;
                                }
                                int shiftX = 0, shiftY = 0;
                                if (minBX <= maxBX && minBY <= maxBY)
                                {
                                    shiftX = (int)System.Math.Round(((gridWidth - 1) - (minBX + maxBX)) / 2.0);
                                    shiftY = (int)System.Math.Round(((gridHeight - 1) - (minBY + maxBY)) / 2.0);
                                }
                                int added = 0;
                                foreach (var s in sprites)
                                {
                                    if (s == null || s.Image == null) continue;
                                    int gx = s.GridX + shiftX;
                                    int gy = s.GridY + shiftY;
                                    var iso = GridToIso(gx, gy);
                                    var po = new PlacedObject
                                    {
                                        Image = s.Image, // ownership to canvas
                                        GraphicId = s.Name,
                                        GridX = gx,
                                        GridY = gy,
                                        Z = s.Z,
                                        Flags = 0,
                                        IsoPosition = iso,
                                        Layer = placedObjects.Count
                                    };
                                    placedObjects.Add(po);
                                    added++;
                                }
                                PushUndo();
                                RebuildLockLists();
                                designPictureBox.Invalidate();
                                outputTextBox.AppendText("3D Editor added " + added + " tile(s) to canvas.\r\n");
                            }
                            catch (Exception ex)
                            {
                                outputTextBox.AppendText("3D Editor tiles error: " + ex.Message + "\r\n");
                            }
                        }));
                    }
                    catch (Exception ex)
                    {
                        this.BeginInvoke(new Action(() => outputTextBox.AppendText("3D Editor error: " + ex.Message + "\r\n")));
                    }
                };
                editor.TilesReady += tilesHandler;
                editor.FormClosed += (s2, e2) =>
                {
                    try { editor.SpriteReady -= spriteHandler; editor.TilesReady -= tilesHandler; editor.Dispose(); } catch { }
                    _threeDEditor = null;
                };
                _threeDEditor = editor;
                editor.Show(); // modeless so user can keep working
            }
            else
            {
                if (!_threeDEditor.Visible) _threeDEditor.Show();
                _threeDEditor.BringToFront();
            }
        }

        private void SendCurrentCanvasImageToMap(bool asOverlay)
        {
            Bitmap toSend = null;

            var selectedObj = selectedObjects.FirstOrDefault();
            if (selectedObj != null && selectedObj.Image != null)
            {
                toSend = new Bitmap(selectedObj.Image);
            }
            else
            {
                try
                {
                    toSend = RenderCanvasToImage();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not render canvas image: {ex.Message}", "Send to Map",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            try
            {
                if (_mapViewer == null || _mapViewer.IsDisposed)
                {
                    _mapViewer = new MapViewerForm
                    {
                        StartPosition = FormStartPosition.CenterScreen,
                        ShowInTaskbar = true
                    };
                    _mapViewer.FormClosed += (s2, e2) => { _mapViewer = null; };
                    _mapViewer.Show();
                }
                else
                {
                    if (!_mapViewer.Visible) _mapViewer.Show();
                    _mapViewer.BringToFront();
                }

                _mapViewer.AddPainterImageToMapLayer(toSend, asOverlay);
                OutputLog($"Sent canvas image to Map Editor as {(asOverlay ? "overlay" : "underlay")}");
            }
            catch (Exception ex)
            {
                toSend?.Dispose();
                MessageBox.Show($"Failed to send to Map Editor: {ex.Message}", "Send to Map",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
