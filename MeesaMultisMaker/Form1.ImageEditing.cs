using MeesaMultisMaker.Utils;
using MeesaMultisMaker.AI;
using MeesaMultisMaker.ComfyUI;
using MeesaMultisMaker.Controls;
using MeesaMultisMaker.Mul;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    public partial class Form1
    {
        private ImageEditingForm _imageEditingForm;

        /// <summary>
        /// Initialize the Image Editing Form (floating window)
        /// </summary>
        private void InitializeImageEditingPanel()
        {
            // Don't create it yet - only when user opens it
            _imageEditingForm = null;
        }

    }
}

namespace MeesaMultisMaker
{
    public class TextureEditorForm : Form
    {
        private string mulFolder;
        private List<int> validTextureIds = new List<int>();
        private List<int> filteredTextureIds = new List<int>();
        private Dictionary<int, Bitmap> textureCache = new Dictionary<int, Bitmap>();
        private Dictionary<int, Bitmap> _originalTextureImages = new Dictionary<int, Bitmap>();
        private Dictionary<int, Bitmap> _modifiedTextureImages = new Dictionary<int, Bitmap>();
        private Dictionary<int, Bitmap> _rawAIOutputImages = new Dictionary<int, Bitmap>();

        private int selectedTextureId = -1;
        private bool _showingOriginal = false;
        private Bitmap _previewBitmap;
        private ComfyUIClient _comfyClient;
        private System.Threading.CancellationTokenSource _textureGenCts;

        private SplitContainer mainSplit;
        private ListView textureListView;
        private ImageList textureImageList;
        private TextBox searchBox;
        private PictureBox previewBox;
        private Label statusLabel;
        private Label infoLabel;
        private AISettingsPanel aiSettingsPanel;
        private ImageEditingPanel imageEditingPanel;

        public TextureEditorForm()
        {
            InitializeComponent();
            BeginLoadMulFolder();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _previewBitmap?.Dispose();

                foreach (var bmp in textureCache.Values) bmp?.Dispose();
                foreach (var bmp in _originalTextureImages.Values) bmp?.Dispose();
                foreach (var bmp in _modifiedTextureImages.Values) bmp?.Dispose();
                foreach (var bmp in _rawAIOutputImages.Values) bmp?.Dispose();

                textureCache.Clear();
                _originalTextureImages.Clear();
                _modifiedTextureImages.Clear();
                _rawAIOutputImages.Clear();
            }

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.Text = "Texture Editor - MeesaMultisMaker";
            this.Width = 1600;
            this.Height = 900;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = HolographicTheme.DarkBackground;

            mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 300,
                FixedPanel = FixedPanel.Panel1,
                BackColor = HolographicTheme.DarkBackground
            };
            HolographicTheme.ApplyToSplitContainer(mainSplit);

            var leftPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = HolographicTheme.PanelBackground
            };

            var paletteLabel = new Label
            {
                Text = "LAND TEXTURE PALETTE",
                Dock = DockStyle.Top,
                Height = 25,
                ForeColor = HolographicTheme.CyanAccent,
                BackColor = HolographicTheme.ControlBackground,
                Font = new Font("Consolas", 10, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };

            searchBox = new TextBox
            {
                Dock = DockStyle.Top,
                Height = 25,
                Text = "Search texture id...",
                ForeColor = HolographicTheme.TextMuted
            };
            HolographicTheme.ApplyToTextBox(searchBox);
            searchBox.Enter += (s, e) =>
            {
                if (searchBox.Text == "Search texture id..." && searchBox.ForeColor == HolographicTheme.TextMuted)
                {
                    searchBox.Text = "";
                    searchBox.ForeColor = HolographicTheme.TextPrimary;
                }
            };
            searchBox.Leave += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(searchBox.Text))
                {
                    searchBox.Text = "Search texture id...";
                    searchBox.ForeColor = HolographicTheme.TextMuted;
                }
            };
            searchBox.TextChanged += SearchBox_TextChanged;

            textureListView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.LargeIcon,
                BackColor = HolographicTheme.InputBackground,
                ForeColor = HolographicTheme.TextPrimary,
                BorderStyle = BorderStyle.None,
                VirtualMode = true,
                VirtualListSize = 0,
                OwnerDraw = true,
                MultiSelect = true
            };
            textureImageList = new ImageList
            {
                ImageSize = new Size(80, 80),
                ColorDepth = ColorDepth.Depth32Bit
            };
            textureListView.LargeImageList = textureImageList;
            textureListView.RetrieveVirtualItem += TextureListView_RetrieveVirtualItem;
            textureListView.DrawItem += TextureListView_DrawItem;
            textureListView.CacheVirtualItems += TextureListView_CacheVirtualItems;
            textureListView.SelectedIndexChanged += TextureListView_SelectedIndexChanged;

            leftPanel.Controls.Add(textureListView);
            leftPanel.Controls.Add(searchBox);
            leftPanel.Controls.Add(paletteLabel);

            var rightPanel = new Panel { Dock = DockStyle.Fill, BackColor = HolographicTheme.DarkBackground };

            var topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 68,
                BackColor = HolographicTheme.ControlBackground,
                Padding = new Padding(8)
            };

            var replaceButton = CreateThemedButton("Replace From File", 10, 8, 130, ButtonStyle.Accent);
            replaceButton.Click += (s, e) => ReplaceSelectedTextureFromFile();
            topPanel.Controls.Add(replaceButton);

            var restoreButton = CreateThemedButton("Restore", 145, 8, 70, ButtonStyle.Warning);
            restoreButton.Click += (s, e) => RestoreSelectedOriginal();
            topPanel.Controls.Add(restoreButton);

            var saveButton = CreateThemedButton("Save To MUL", 220, 8, 95, ButtonStyle.Success);
            saveButton.Click += (s, e) => SaveTexturesToMul();
            topPanel.Controls.Add(saveButton);

            infoLabel = new Label
            {
                Left = 10,
                Top = 40,
                Width = 500,
                Height = 20,
                ForeColor = HolographicTheme.TextSecondary,
                Font = new Font("Consolas", 9f),
                Text = "No texture selected"
            };
            topPanel.Controls.Add(infoLabel);

            var sidebarTabs = new TabControl
            {
                Dock = DockStyle.Right,
                Width = 360,
                Appearance = TabAppearance.Normal
            };

            var aiTab = new TabPage("AI") { BackColor = HolographicTheme.PanelBackground };
            aiSettingsPanel = new AISettingsPanel { Dock = DockStyle.Fill };
            aiSettingsPanel.AIRegenSelectedClicked += (s, e) => RegenerateSelectedTexture();
            aiSettingsPanel.RevertAIChangesClicked += (s, e) => RevertAIChanges();
            aiSettingsPanel.OldNewClicked += (s, e) => ToggleOldNew();
            aiSettingsPanel.BlackPixelSettingsChanged += (s, e) => ReapplyBlackPixelRemoval();
            aiSettingsPanel.SaveToMulClicked += (s, e) => SaveTexturesToMul();
            aiSettingsPanel.HideRegenAsOneButton();
            aiSettingsPanel.HideRevertOGButton();
            aiSettingsPanel.HideUnifyButton();
            aiSettingsPanel.HideSaveCanvasAsNewButton();
            aiSettingsPanel.StopAIGenerationClicked += (s, e) => CancelTextureGeneration();
            aiSettingsPanel.SetPushToJarJarEnabled(false);
            aiTab.Controls.Add(aiSettingsPanel);

            var imageFxTab = new TabPage("Image FX") { BackColor = HolographicTheme.PanelBackground };
            imageEditingPanel = new ImageEditingPanel { Dock = DockStyle.Fill };
            imageEditingPanel.EffectsChanged += (s, e) => ApplyImageEffectsToSelected();
            imageEditingPanel.ApplyClicked += (s, e) => ApplyImageEffectsToSelected();
            imageEditingPanel.ApplyToAllSelectedClicked += (s, e) => ApplyImageEffectsToSelected(applyToAllSelected: true);
            imageEditingPanel.ResetClicked += (s, e) => RestoreSelectedOriginal();
            imageFxTab.Controls.Add(imageEditingPanel);

            sidebarTabs.TabPages.Add(aiTab);
            sidebarTabs.TabPages.Add(imageFxTab);

            previewBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(35, 35, 40),
                SizeMode = PictureBoxSizeMode.Zoom
            };

            var statusPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 24,
                BackColor = HolographicTheme.ControlBackground
            };

            statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = HolographicTheme.TextSecondary,
                Font = new Font("Consolas", 9),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(5, 0, 0, 0),
                Text = "Ready"
            };
            statusPanel.Controls.Add(statusLabel);

            rightPanel.Controls.Add(previewBox);
            rightPanel.Controls.Add(sidebarTabs);
            rightPanel.Controls.Add(statusPanel);
            rightPanel.Controls.Add(topPanel);

            mainSplit.Panel1.Controls.Add(leftPanel);
            mainSplit.Panel2.Controls.Add(rightPanel);
            this.Controls.Add(mainSplit);
        }

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

        private void BeginLoadMulFolder()
        {
            var config = AppConfig.Instance;
            mulFolder = config.MulFolderPath;

            if (string.IsNullOrEmpty(mulFolder) || !Directory.Exists(mulFolder))
            {
                mulFolder = config.FindMulFolder();
            }

            if (string.IsNullOrEmpty(mulFolder))
            {
                SetStatus("Warning: MUL folder not configured. Set it in Settings.");
                return;
            }

            string idxPath = Path.Combine(mulFolder, "texidx.mul");
            string mulPath = Path.Combine(mulFolder, "texmaps.mul");

            if (!File.Exists(idxPath) || !File.Exists(mulPath))
            {
                SetStatus($"No texture files found in: {mulFolder}");
                return;
            }

            SetStatus("Loading land textures...");
            validTextureIds = TexMapReader.GetValidTextureIds(mulFolder);
            filteredTextureIds = new List<int>(validTextureIds);
            PopulateTextureList();
            SetStatus($"Loaded {validTextureIds.Count} textures");
        }

        private void PopulateTextureList()
        {
            textureListView.VirtualListSize = filteredTextureIds.Count;
            textureListView.Invalidate();
        }

        private void SearchBox_TextChanged(object sender, EventArgs e)
        {
            string search = searchBox.Text.Trim();
            if (string.IsNullOrEmpty(search) || search == "Search texture id...")
            {
                filteredTextureIds = new List<int>(validTextureIds);
            }
            else
            {
                string s = search.ToUpperInvariant();
                if (s.StartsWith("0X")) s = s.Substring(2);

                filteredTextureIds = validTextureIds.Where(id =>
                {
                    string hex = id.ToString("X4");
                    return hex.Contains(s) || id.ToString().Contains(s);
                }).ToList();
            }

            PopulateTextureList();
        }

        private void TextureListView_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            if (e.ItemIndex < 0 || e.ItemIndex >= filteredTextureIds.Count)
            {
                e.Item = new ListViewItem("?");
                return;
            }

            int textureId = filteredTextureIds[e.ItemIndex];
            e.Item = new ListViewItem($"0x{textureId:X4}") { Tag = textureId };
        }

        private void TextureListView_DrawItem(object sender, DrawListViewItemEventArgs e)
        {
            using (var bg = new SolidBrush(e.Item.Selected ? HolographicTheme.SelectionCyan : HolographicTheme.InputBackground))
            {
                e.Graphics.FillRectangle(bg, e.Bounds);
            }

            if (!(e.Item.Tag is int textureId)) return;

            var img = GetTextureImage(textureId);
            if (img != null)
            {
                int maxSize = Math.Min(e.Bounds.Width - 6, e.Bounds.Height - 22);
                float scale = Math.Min((float)maxSize / img.Width, (float)maxSize / img.Height);
                scale = Math.Min(scale, 1f);

                int w = Math.Max(1, (int)(img.Width * scale));
                int h = Math.Max(1, (int)(img.Height * scale));
                int x = e.Bounds.Left + (e.Bounds.Width - w) / 2;
                int y = e.Bounds.Top + 2;

                e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                e.Graphics.DrawImage(img, x, y, w, h);
            }

            var textRect = new Rectangle(e.Bounds.Left, e.Bounds.Bottom - 18, e.Bounds.Width, 18);
            TextRenderer.DrawText(e.Graphics, e.Item.Text, textureListView.Font, textRect,
                e.Item.Selected ? Color.White : HolographicTheme.TextPrimary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
        }

        private void TextureListView_CacheVirtualItems(object sender, CacheVirtualItemsEventArgs e)
        {
            int start = e.StartIndex;
            int end = Math.Min(e.EndIndex, filteredTextureIds.Count - 1);

            for (int i = start; i <= end; i++)
            {
                int id = filteredTextureIds[i];
                if (!textureCache.ContainsKey(id))
                {
                    var bmp = TexMapReader.LoadTexture(mulFolder, (ushort)id);
                    if (bmp != null)
                    {
                        textureCache[id] = bmp;
                    }
                }
            }
        }

        private void TextureListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (textureListView.SelectedIndices.Count == 0)
            {
                selectedTextureId = -1;
                infoLabel.Text = "No texture selected";
                return;
            }

            int index = textureListView.SelectedIndices[0];
            if (index < 0 || index >= filteredTextureIds.Count) return;

            selectedTextureId = filteredTextureIds[index];
            _showingOriginal = false;
            aiSettingsPanel.SetOldNewButtonText("Old/New");
            UpdatePreviewFromSelected();

            int selectedCount = GetSelectedTextureIds().Count;
            if (selectedCount > 1)
            {
                SetStatus($"Selected {selectedCount} textures");
            }
        }

        private List<int> GetSelectedTextureIds()
        {
            var ids = new List<int>();
            foreach (int index in textureListView.SelectedIndices)
            {
                if (index >= 0 && index < filteredTextureIds.Count)
                {
                    ids.Add(filteredTextureIds[index]);
                }
            }
            return ids;
        }

        private Bitmap GetTextureImage(int textureId)
        {
            if (_modifiedTextureImages.TryGetValue(textureId, out var modified) && modified != null)
            {
                return modified;
            }

            if (textureCache.TryGetValue(textureId, out var cached) && cached != null)
            {
                return cached;
            }

            var bmp = TexMapReader.LoadTexture(mulFolder, (ushort)textureId);
            if (bmp != null)
            {
                textureCache[textureId] = bmp;
            }

            return bmp;
        }

        private void UpdatePreviewFromSelected()
        {
            if (selectedTextureId < 0)
            {
                SetPreviewImage(null);
                return;
            }

            Bitmap source;
            if (_showingOriginal && _originalTextureImages.TryGetValue(selectedTextureId, out var orig))
            {
                source = orig;
            }
            else
            {
                source = GetTextureImage(selectedTextureId);
            }

            if (source == null)
            {
                SetPreviewImage(null);
                infoLabel.Text = $"Texture 0x{selectedTextureId:X4} (failed to load)";
                return;
            }

            SetPreviewImage(new Bitmap(source));
            infoLabel.Text = $"Texture 0x{selectedTextureId:X4}  {source.Width}x{source.Height}";
            aiSettingsPanel.SetOldNewEnabled(_originalTextureImages.ContainsKey(selectedTextureId));
            aiSettingsPanel.SetPendingChanges(_modifiedTextureImages.Count);
            textureListView.Invalidate();
        }

        private void SetPreviewImage(Bitmap bmp)
        {
            var old = _previewBitmap;
            _previewBitmap = bmp;
            previewBox.Image = _previewBitmap;
            old?.Dispose();
        }

        private void EnsureOriginalStored(int textureId, Bitmap current)
        {
            if (textureId < 0 || current == null) return;

            if (!_originalTextureImages.ContainsKey(textureId))
            {
                _originalTextureImages[textureId] = new Bitmap(current);
            }
        }

        private void ReplaceModifiedTexture(int textureId, Bitmap newImage)
        {
            if (_modifiedTextureImages.TryGetValue(textureId, out var old))
            {
                old?.Dispose();
            }

            _modifiedTextureImages[textureId] = newImage;
            _showingOriginal = false;
            aiSettingsPanel.SetOldNewButtonText("Old/New");
            UpdatePreviewFromSelected();
        }

        private void ReplaceSelectedTextureFromFile()
        {
            if (selectedTextureId < 0)
            {
                SetStatus("Select a texture first.");
                return;
            }

            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff";
                if (ofd.ShowDialog() != DialogResult.OK) return;

                try
                {
                    var current = GetTextureImage(selectedTextureId);
                    if (current == null)
                    {
                        SetStatus("Could not load selected texture.");
                        return;
                    }

                    EnsureOriginalStored(selectedTextureId, current);

                    using (var loaded = new Bitmap(ofd.FileName))
                    {
                        int target = current.Width == 128 ? 128 : 64;
                        Bitmap replacement;

                        if (loaded.Width != target || loaded.Height != target)
                        {
                            replacement = new Bitmap(target, target, PixelFormat.Format32bppArgb);
                            using (var g = Graphics.FromImage(replacement))
                            {
                                g.Clear(Color.Transparent);
                                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                g.DrawImage(loaded, 0, 0, target, target);
                            }
                        }
                        else
                        {
                            replacement = new Bitmap(loaded);
                        }

                        ReplaceModifiedTexture(selectedTextureId, replacement);
                        SetStatus($"Replaced texture 0x{selectedTextureId:X4}");
                    }
                }
                catch (Exception ex)
                {
                    SetStatus($"Replace failed: {ex.Message}");
                }
            }
        }

        private void ApplyImageEffectsToSelected(bool applyToAllSelected = false)
        {
            var targetIds = applyToAllSelected ? GetSelectedTextureIds() : new List<int> { selectedTextureId };
            targetIds = targetIds.Where(id => id >= 0).Distinct().ToList();

            if (targetIds.Count == 0)
                return;

            try
            {
                int applied = 0;
                foreach (int textureId in targetIds)
                {
                    var current = GetTextureImage(textureId);
                    if (current == null)
                        continue;

                    EnsureOriginalStored(textureId, current);

                    Bitmap source = new Bitmap(_originalTextureImages[textureId]);
                    Bitmap result = ImageEffects.ApplyEffects(
                        source,
                        imageEditingPanel.Brightness,
                        imageEditingPanel.Contrast,
                        imageEditingPanel.Hue,
                        imageEditingPanel.Saturation,
                        imageEditingPanel.PixelSize,
                        imageEditingPanel.PixelizeEnabled,
                        imageEditingPanel.PaletteColors,
                        imageEditingPanel.NoiseIntensity,
                        imageEditingPanel.DitherLevels,
                        imageEditingPanel.EdgeDarkening,
                        imageEditingPanel.ColorBands,
                        imageEditingPanel.FillHoles,
                        imageEditingPanel.RemoveBackgroundEnabled,
                        imageEditingPanel.BackgroundMode,
                        imageEditingPanel.BackgroundColor,
                        imageEditingPanel.BackgroundThreshold
                    );
                    source.Dispose();

                    ReplaceModifiedTexture(textureId, result);
                    applied++;
                }

                if (applied > 0)
                {
                    if (applied == 1)
                        SetStatus($"Applied image FX to 0x{targetIds[0]:X4}");
                    else
                        SetStatus($"Applied image FX to {applied} textures");
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Image FX error: {ex.Message}");
            }
        }

        private void RestoreSelectedOriginal()
        {
            if (selectedTextureId < 0)
                return;

            if (_originalTextureImages.TryGetValue(selectedTextureId, out var original))
            {
                ReplaceModifiedTexture(selectedTextureId, new Bitmap(original));
                SetStatus($"Restored original texture 0x{selectedTextureId:X4}");
            }
        }

        private async Task<T> AwaitTexturePollWithCancel<T>(Task<T> pollTask, System.Threading.CancellationToken token, ComfyUIClient client)
        {
            while (!pollTask.IsCompleted)
            {
                try { await Task.Delay(500, token); }
                catch (OperationCanceledException)
                {
                    try { await client.Interrupt(); } catch { }
                    throw;
                }
            }
            return await pollTask;
        }

        private void CancelTextureGeneration()
        {
            var cts = _textureGenCts;
            if (cts == null) return;
            try { cts.Cancel(); } catch { }
            try
            {
                var url = aiSettingsPanel?.ComfyUrl;
                if (!string.IsNullOrWhiteSpace(url))
                {
                    var client = new ComfyUIClient(url);
                    _ = client.Interrupt();
                }
            }
            catch { }
            SetStatus("Stopping...");
        }

        private async void RegenerateSelectedTexture()
        {
            if (_textureGenCts != null)
            {
                SetStatus("Generation already running - press STOP to cancel.");
                aiSettingsPanel.SetStatus("Already running", Color.Orange);
                return;
            }
            _textureGenCts = new System.Threading.CancellationTokenSource();
            var textureCts = _textureGenCts;
            aiSettingsPanel.SetStopEnabled(true);
            try
            {
                await RegenerateSelectedTextureCore(textureCts);
            }
            catch (OperationCanceledException)
            {
                SetStatus("Stopped by user.");
                aiSettingsPanel.SetStatus("Stopped by user", Color.Orange);
            }
            catch (Exception ex)
            {
                SetStatus($"AI regeneration error: {ex.Message}");
                aiSettingsPanel.SetStatus($"Error: {ex.Message}", Color.Red);
            }
            finally
            {
                aiSettingsPanel.SetStopEnabled(false);
                _textureGenCts = null;
                textureCts.Dispose();
            }
        }

        private async Task RegenerateSelectedTextureCore(System.Threading.CancellationTokenSource textureCts)
        {
            if (selectedTextureId < 0)
            {
                SetStatus("Select a texture first.");
                aiSettingsPanel.SetStatus("Select a texture", Color.Orange);
                return;
            }

            var current = GetTextureImage(selectedTextureId);
            if (current == null)
            {
                SetStatus("Could not load selected texture.");
                return;
            }

            SetStatus("Regenerating selected texture with AI...");
            aiSettingsPanel.SetStatus("Regenerating...", HolographicTheme.CyanAccent);

            try
            {
                var selectedBackend = aiSettingsPanel.SelectedBackend;
                if (selectedBackend == AIBackend.ComfyUI)
                {
                    if (string.IsNullOrEmpty(aiSettingsPanel.ComfyUrl))
                    {
                        aiSettingsPanel.SetStatus("No URL configured", Color.Red);
                        return;
                    }
                    _comfyClient = new ComfyUIClient(aiSettingsPanel.ComfyUrl);
                }
                else
                {
                    bool available = await ImageGeneratorFactory.EnsureBackendAvailable(selectedBackend, this);
                    if (!available)
                    {
                        aiSettingsPanel.SetStatus("Models not available", Color.Red);
                        return;
                    }
                }

                EnsureOriginalStored(selectedTextureId, current);

                int sourceWidth = current.Width;
                int sourceHeight = current.Height;
                int aiWidth = aiSettingsPanel.ResolutionWidth;
                int aiHeight = aiSettingsPanel.ResolutionHeight;

                float scaleToAI = 1.0f;
                if (sourceWidth < aiWidth || sourceHeight < aiHeight)
                {
                    float scaleW = (float)aiWidth / sourceWidth;
                    float scaleH = (float)aiHeight / sourceHeight;
                    scaleToAI = Math.Max(scaleW, scaleH);
                }

                Bitmap imageForAI;
                if (scaleToAI > 1.0f)
                {
                    int scaledWidth = ((int)(sourceWidth * scaleToAI) + 7) / 8 * 8;
                    int scaledHeight = ((int)(sourceHeight * scaleToAI) + 7) / 8 * 8;

                    imageForAI = new Bitmap(scaledWidth, scaledHeight, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(imageForAI))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.Clear(Color.Transparent);
                        g.DrawImage(current, 0, 0, scaledWidth, scaledHeight);
                    }
                    aiWidth = scaledWidth;
                    aiHeight = scaledHeight;
                }
                else
                {
                    imageForAI = new Bitmap(current);
                    aiWidth = sourceWidth;
                    aiHeight = sourceHeight;
                }

                Bitmap newBmp = null;

                if (selectedBackend != AIBackend.ComfyUI)
                {
                    throw new NotSupportedException("Only ComfyUI backend is supported.");
                }
                else
                {
                    byte[] imageBytes;
                    using (var ms = new MemoryStream())
                    {
                        imageForAI.Save(ms, ImageFormat.Png);
                        imageBytes = ms.ToArray();
                    }

                    string uploadedFilename = await _comfyClient.UploadImage(imageBytes, $"texture_{selectedTextureId}.png");

                    long effSeed = aiSettingsPanel.Seed ?? new Random().Next();
                    SetStatus($"Generating texture 0x{selectedTextureId:X4} with seed {effSeed}...");

                    string workflow = Image2ImageWorkflow.CreateWorkflow(
                        aiSettingsPanel.Prompt,
                        aiSettingsPanel.NegativePrompt,
                        uploadedFilename,
                        aiWidth, aiHeight,
                        aiSettingsPanel.Steps,
                        aiSettingsPanel.Cfg,
                        aiSettingsPanel.Denoise,
                        effSeed,
                        aiSettingsPanel.Sampler,
                        aiSettingsPanel.Scheduler,
                        aiSettingsPanel.Checkpoint);

                    string promptId = await _comfyClient.QueuePrompt(workflow);
                    if (string.IsNullOrEmpty(promptId))
                    {
                        aiSettingsPanel.SetStatus("Queue failed", Color.Red);
                        imageForAI.Dispose();
                        return;
                    }

                    var images = await AwaitTexturePollWithCancel(_comfyClient.GetGeneratedImages(promptId, maxAttempts: 120, pollIntervalMs: 1000), textureCts.Token, _comfyClient);
                    textureCts.Token.ThrowIfCancellationRequested();
                    if (images.Count == 0)
                    {
                        aiSettingsPanel.SetStatus("No images", Color.Red);
                        imageForAI.Dispose();
                        return;
                    }

                    byte[] newImageData = await _comfyClient.DownloadImage(images[0]);
                    if (newImageData == null || newImageData.Length < 8)
                    {
                        aiSettingsPanel.SetStatus("Download failed", Color.Red);
                        imageForAI.Dispose();
                        return;
                    }
                    using (var ms = new MemoryStream(newImageData))
                    {
                        newBmp = new Bitmap(ms);
                    }
                }

                imageForAI.Dispose();

                if (newBmp == null)
                {
                    aiSettingsPanel.SetStatus("Generation failed", Color.Red);
                    return;
                }

                if (newBmp.Width != sourceWidth || newBmp.Height != sourceHeight)
                {
                    var resizedBmp = new Bitmap(sourceWidth, sourceHeight, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(resizedBmp))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.Clear(Color.Transparent);
                        g.DrawImage(newBmp, 0, 0, sourceWidth, sourceHeight);
                    }
                    newBmp.Dispose();
                    newBmp = resizedBmp;
                }

                if (_rawAIOutputImages.TryGetValue(selectedTextureId, out var oldRaw))
                {
                    oldRaw?.Dispose();
                }
                _rawAIOutputImages[selectedTextureId] = new Bitmap(newBmp);

                if (aiSettingsPanel.DropBlackPixels)
                {
                    newBmp = RemoveBlackPixels(newBmp, aiSettingsPanel.BlackThreshold);
                }

                ReplaceModifiedTexture(selectedTextureId, newBmp);
                aiSettingsPanel.SetOldNewEnabled(true);
                aiSettingsPanel.SetPendingChanges(_modifiedTextureImages.Count);
                aiSettingsPanel.SetStatus("Regenerated!", Color.LimeGreen);
                SetStatus($"Regenerated texture 0x{selectedTextureId:X4}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                SetStatus($"AI regeneration error: {ex.Message}");
                aiSettingsPanel.SetStatus($"Error: {ex.Message}", Color.Red);
            }
        }

        private Bitmap RemoveBlackPixels(Bitmap source, int threshold)
        {
            var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);

            var sourceData = source.LockBits(new Rectangle(0, 0, source.Width, source.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var resultData = result.LockBits(new Rectangle(0, 0, result.Width, result.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            try
            {
                int byteCount = Math.Abs(sourceData.Stride) * source.Height;
                byte[] srcPixels = new byte[byteCount];
                byte[] dstPixels = new byte[byteCount];

                System.Runtime.InteropServices.Marshal.Copy(sourceData.Scan0, srcPixels, 0, byteCount);

                int stride = sourceData.Stride;

                for (int y = 0; y < source.Height; y++)
                {
                    int rowOffset = y * stride;
                    for (int x = 0; x < source.Width; x++)
                    {
                        int offset = rowOffset + x * 4;
                        byte b = srcPixels[offset];
                        byte g = srcPixels[offset + 1];
                        byte r = srcPixels[offset + 2];
                        byte a = srcPixels[offset + 3];

                        if (r <= threshold && g <= threshold && b <= threshold)
                        {
                            dstPixels[offset] = 0;
                            dstPixels[offset + 1] = 0;
                            dstPixels[offset + 2] = 0;
                            dstPixels[offset + 3] = 0;
                        }
                        else
                        {
                            dstPixels[offset] = b;
                            dstPixels[offset + 1] = g;
                            dstPixels[offset + 2] = r;
                            dstPixels[offset + 3] = a;
                        }
                    }
                }

                System.Runtime.InteropServices.Marshal.Copy(dstPixels, 0, resultData.Scan0, byteCount);
            }
            finally
            {
                source.UnlockBits(sourceData);
                result.UnlockBits(resultData);
            }

            source.Dispose();
            return result;
        }

        private void ReapplyBlackPixelRemoval()
        {
            if (selectedTextureId < 0)
                return;

            if (!_rawAIOutputImages.TryGetValue(selectedTextureId, out var rawImage) || rawImage == null)
                return;

            Bitmap processed = new Bitmap(rawImage);
            if (aiSettingsPanel.DropBlackPixels)
            {
                processed = RemoveBlackPixels(processed, aiSettingsPanel.BlackThreshold);
            }

            ReplaceModifiedTexture(selectedTextureId, processed);
        }

        private void ToggleOldNew()
        {
            if (selectedTextureId < 0)
                return;

            if (!_originalTextureImages.ContainsKey(selectedTextureId) || !_modifiedTextureImages.ContainsKey(selectedTextureId))
            {
                SetStatus("No old/new pair available for selected texture.");
                return;
            }

            _showingOriginal = !_showingOriginal;
            aiSettingsPanel.SetOldNewButtonText(_showingOriginal ? "Show New" : "Old/New");
            UpdatePreviewFromSelected();
        }

        private void RevertAIChanges()
        {
            if (_originalTextureImages.Count == 0)
            {
                SetStatus("No AI changes to revert.");
                return;
            }

            foreach (var kvp in _originalTextureImages)
            {
                if (_modifiedTextureImages.TryGetValue(kvp.Key, out var old))
                {
                    old?.Dispose();
                }
                _modifiedTextureImages[kvp.Key] = new Bitmap(kvp.Value);
            }

            _showingOriginal = false;
            UpdatePreviewFromSelected();
            aiSettingsPanel.SetOldNewButtonText("Old/New");
            aiSettingsPanel.SetPendingChanges(_modifiedTextureImages.Count);
            aiSettingsPanel.SetStatus("Reverted", Color.LimeGreen);
            SetStatus("Reverted AI changes to originals.");
        }

        private void SaveTexturesToMul()
        {
            if (_modifiedTextureImages.Count == 0)
            {
                MessageBox.Show("No modified textures to save.", "Save Textures", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (string.IsNullOrEmpty(mulFolder) || !Directory.Exists(mulFolder))
            {
                MessageBox.Show("MUL folder not configured.", "Save Textures", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string idxPath = Path.Combine(mulFolder, "texidx.mul");
            string mulPath = Path.Combine(mulFolder, "texmaps.mul");
            if (!File.Exists(idxPath) || !File.Exists(mulPath))
            {
                MessageBox.Show("texidx.mul/texmaps.mul not found in configured MUL folder.", "Save Textures", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int saved = 0;
            int failed = 0;
            var errors = new List<string>();

            foreach (var kvp in _modifiedTextureImages)
            {
                try
                {
                    if (TexMapReader.SaveTexture(mulFolder, kvp.Key, kvp.Value))
                    {
                        saved++;
                    }
                    else
                    {
                        failed++;
                        errors.Add($"Texture 0x{kvp.Key:X4}: write failed");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"Texture 0x{kvp.Key:X4}: {ex.Message}");
                }
            }

            if (failed == 0)
            {
                MessageBox.Show($"Saved {saved} texture(s) to texmaps.mul.", "Save Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                SetStatus($"Saved {saved} textures.");
                aiSettingsPanel.SetStatus($"Saved {saved}", HolographicTheme.ButtonSuccess);
            }
            else
            {
                MessageBox.Show($"Saved {saved}, failed {failed}.\n\n{string.Join("\n", errors)}", "Save Partial", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus($"Saved {saved}/{saved + failed} textures.");
                aiSettingsPanel.SetStatus($"Saved {saved}/{saved + failed}", HolographicTheme.ButtonWarning);
            }

            textureCache.Clear();
            validTextureIds = TexMapReader.GetValidTextureIds(mulFolder);
            filteredTextureIds = new List<int>(validTextureIds);
            PopulateTextureList();
            UpdatePreviewFromSelected();
        }

        private void SetStatus(string message)
        {
            if (statusLabel.InvokeRequired)
            {
                statusLabel.Invoke(new Action(() => SetStatus(message)));
                return;
            }
            statusLabel.Text = message;
        }
    }
}

namespace MeesaMultisMaker
{
    public partial class Form1
    {
        /// <summary>
        /// Show or focus the image editing window
        /// </summary>
        private void ShowImageEditingWindow()
        {
            if (_imageEditingForm == null || _imageEditingForm.IsDisposed)
            {
                _imageEditingForm = new ImageEditingForm
                {
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(this.Right - 320, this.Top + 100)
                };

                // Wire up events
                _imageEditingForm.ApplyEffects += ImageEditingForm_ApplyEffects;
                _imageEditingForm.ApplyEffectsToAll += ImageEditingForm_ApplyEffectsToAll;
                _imageEditingForm.ResetRequested += ImageEditingForm_ResetRequested;
                _imageEditingForm.RealTimePreview += ImageEditingForm_RealTimePreview;

                _imageEditingForm.FormClosed += (s, e) => { _imageEditingForm = null; };
                _imageEditingForm.Show(this);

                // Update status with selection count
                int count = selectedObjects.Count;
                if (count == 0)
                {
                    _imageEditingForm.SetStatus("Ready - Select items to edit", Color.FromArgb(100, 200, 255));
                }
                else if (count == 1)
                {
                    _imageEditingForm.SetStatus($"Editing 1 item", Color.FromArgb(100, 200, 255));
                }
                else
                {
                    _imageEditingForm.SetStatus($"Editing {count} items", Color.FromArgb(100, 200, 255));
                }
            }
            else
            {
                if (!_imageEditingForm.Visible)
                    _imageEditingForm.Show(this);
                _imageEditingForm.BringToFront();
                _imageEditingForm.Focus();

                // Update status with current selection count
                int count = selectedObjects.Count;
                if (count == 0)
                {
                    _imageEditingForm.SetStatus("Ready - Select items to edit", Color.FromArgb(100, 200, 255));
                }
                else if (count == 1)
                {
                    _imageEditingForm.SetStatus($"Editing 1 item", Color.FromArgb(100, 200, 255));
                }
                else
                {
                    _imageEditingForm.SetStatus($"Editing {count} items", Color.FromArgb(100, 200, 255));
                }
            }
        }

        /// <summary>
        /// Apply image effects to the first selected object
        /// </summary>
        private void ImageEditingForm_ApplyEffects(object sender, ImageEffectEventArgs e)
        {
            if (selectedObjects.Count > 0)
            {
                PushUndo();

                var obj = selectedObjects.First();
                ApplyImageEffectsToObject(obj, e);

                designPictureBox.Invalidate();
                _imageEditingForm?.SetStatus($"Applied effects to {obj.GraphicId}", Color.Green);
                return;
            }

            var paletteIds = GetSelectedPaletteItemIds();
            if (paletteIds.Count == 0)
            {
                _imageEditingForm?.SetStatus("No objects selected", Color.Orange);
                return;
            }

            ApplyImageEffectsToPaletteItems(new[] { paletteIds[0] }, e);
        }

        /// <summary>
        /// Apply image effects to all selected objects
        /// </summary>
        private void ImageEditingForm_ApplyEffectsToAll(object sender, ImageEffectEventArgs e)
        {
            if (selectedObjects.Count > 0)
            {
                PushUndo();

                int count = 0;
                foreach (var obj in selectedObjects.ToList())
                {
                    ApplyImageEffectsToObject(obj, e);
                    count++;
                }

                designPictureBox.Invalidate();
                _imageEditingForm?.SetStatus($"Applied effects to {count} object(s)", Color.Green);
                return;
            }

            var paletteIds = GetSelectedPaletteItemIds();
            if (paletteIds.Count == 0)
            {
                _imageEditingForm?.SetStatus("No objects selected", Color.Orange);
                return;
            }

            ApplyImageEffectsToPaletteItems(paletteIds, e);
        }

        /// <summary>
        /// Reset image editing controls to default values
        /// </summary>
        private void ImageEditingForm_ResetRequested(object sender, EventArgs e)
        {
            _imageEditingForm?.ResetControls();
            _imageEditingForm?.SetStatus("Reset to defaults", Color.FromArgb(100, 200, 255));
        }

        /// <summary>
        /// Real-time preview handler - applies effects immediately as controls change
        /// </summary>
        private void ImageEditingForm_RealTimePreview(object sender, ImageEffectEventArgs e)
        {
            if (selectedObjects.Count == 0)
                return;

            // Apply to ALL selected objects for real-time preview
            foreach (var obj in selectedObjects.ToList())
            {
                ApplyImageEffectsToObject(obj, e, isPreview: true);
            }

            designPictureBox.Invalidate();
        }

        private List<ushort> GetSelectedPaletteItemIds()
        {
            var ids = new List<ushort>();
            if (paletteListView == null || filteredStaticIds == null)
                return ids;

            foreach (int index in paletteListView.SelectedIndices)
            {
                if (index >= 0 && index < filteredStaticIds.Count)
                    ids.Add(filteredStaticIds[index]);
            }

            return ids.Distinct().ToList();
        }

        private void ApplyImageEffectsToPaletteItems(IEnumerable<ushort> itemIds, ImageEffectEventArgs effects)
        {
            var ids = itemIds?.Distinct().ToList() ?? new List<ushort>();
            if (ids.Count == 0)
                return;

            int applied = 0;
            foreach (var itemId in ids)
            {
                var current = LoadStaticItemImage(itemId) as Bitmap;
                if (current == null)
                    continue;

                Bitmap source = new Bitmap(current);
                Bitmap result = null;
                try
                {
                    result = ImageEffects.ApplyEffects(
                        source,
                        effects.Brightness,
                        effects.Contrast,
                        effects.Hue,
                        effects.Saturation,
                        effects.PixelSize,
                        effects.PixelizeEnabled,
                        effects.PaletteColors,
                        effects.NoiseIntensity,
                        effects.DitherLevels,
                        effects.EdgeDarkening,
                        effects.ColorBands,
                        effects.FillHoles
                    );
                }
                finally
                {
                    source.Dispose();
                }

                if (result == null)
                    continue;

                lock (imageCache)
                {
                    imageCache[$"0x{itemId:X4}"] = result;
                }

                MarkArtModified(itemId, result);

                foreach (var obj in placedObjects)
                {
                    if (TryParseGraphicId(obj.GraphicId, out int objItemId) && objItemId == itemId)
                    {
                        obj.PreEditImage = obj.PreEditImage ?? new Bitmap(obj.Image);
                        obj.Image = new Bitmap(result);
                        obj.IsEdited = true;
                    }
                }

                applied++;
            }

            paletteListView?.Invalidate();
            designPictureBox?.Invalidate();

            if (applied > 0)
            {
                _imageEditingForm?.SetStatus($"Applied effects to {applied} palette item(s)", Color.Green);
            }
            else
            {
                _imageEditingForm?.SetStatus("No palette images could be processed", Color.Orange);
            }
        }

        /// <summary>
        /// Apply image effects to a single PlacedObject
        /// </summary>
        private void ApplyImageEffectsToObject(PlacedObject obj, ImageEffectEventArgs effects, bool isPreview = false)
        {
            if (obj.Image == null) return;

            try
            {
                // Backup original image if not already done
                if (obj.PreEditImage == null)
                {
                    obj.PreEditImage = new Bitmap(obj.Image);
                }

                // For preview mode, always use the PreEditImage as source
                // For final apply, also use PreEditImage to ensure clean application
                Bitmap source = obj.PreEditImage != null ? new Bitmap(obj.PreEditImage) : new Bitmap(obj.Image);
                Bitmap result = ImageEffects.ApplyEffects(
                    source,
                    effects.Brightness,
                    effects.Contrast,
                    effects.Hue,
                    effects.Saturation,
                    effects.PixelSize,
                    effects.PixelizeEnabled,
                    effects.PaletteColors,
                    effects.NoiseIntensity,
                    effects.DitherLevels,
                    effects.EdgeDarkening,
                    effects.ColorBands,
                    effects.FillHoles
                );

                // Update object image
                if (!isPreview)
                {
                    // Only dispose the old image if this is a final apply (not preview)
                    obj.Image?.Dispose();
                }
                obj.Image = result;
                obj.IsEdited = true;

                source.Dispose();
            }
            catch (Exception ex)
            {
                _imageEditingForm?.SetStatus($"Error: {ex.Message}", Color.Red);
                outputTextBox.AppendText($"Image editing error on {obj.GraphicId}: {ex.Message}\r\n");
            }
        }

        /// <summary>
        /// Revert image edits for selected objects
        /// </summary>
        private void RevertImageEdits()
        {
            if (selectedObjects.Count == 0)
            {
                _imageEditingForm?.SetStatus("No objects selected", Color.Orange);
                return;
            }

            PushUndo();

            int count = 0;
            foreach (var obj in selectedObjects.ToList())
            {
                if (obj.PreEditImage != null)
                {
                    obj.Image?.Dispose();
                    obj.Image = new Bitmap(obj.PreEditImage);
                    obj.PreEditImage?.Dispose();
                    obj.PreEditImage = null;
                    obj.IsEdited = false;
                    count++;
                }
            }

            designPictureBox.Invalidate();
            _imageEditingForm?.SetStatus($"Reverted {count} object(s)", Color.Green);
            outputTextBox.AppendText($"Reverted image edits on {count} object(s)\r\n");
        }
    }
}
