using MeesaMultisMaker.ComfyUI;
using MeesaMultisMaker.Utils;
using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    public class ImageGeneratorForm : Form
    {
        private TextBox promptTextBox;
        private TextBox negativePromptTextBox;
        private NumericUpDown widthNumeric;
        private NumericUpDown heightNumeric;
        private NumericUpDown stepsNumeric;
        private NumericUpDown cfgNumeric;
        private NumericUpDown denoiseNumeric;
        private TextBox seedTextBox;
        private Button generateButton;
        private CheckBox autoRandomSeedCheckBox;
        private PictureBox previewPictureBox;
        private Label statusLabel;
        private ProgressBar progressBar;
        private Button saveButton;
        private Button useAsReferenceButton;
        private CheckBox autoViewCheckBox;
        private TextBox debugTextBox;
        private TextBox comfyUrlTextBox;
        private Button testConnectionButton;
        private ComboBox workflowTypeComboBox;
        private ComboBox samplerComboBox;
        private ComboBox schedulerComboBox;

        private Button selectInputImageButton;
        private Label inputImageLabel;
        private NumericUpDown resizeWidthNumeric;
        private NumericUpDown resizeHeightNumeric;
        private Panel img2imgPanel;
        private PictureBox inputImagePreview;

        private string _inputImagePath;

        private ComfyUIClient _comfyClient;
        private Image _generatedImage;
        private string _lastPromptId;

        public event Action<Image> ImageGenerated;

        protected override bool ShowWithoutActivation
        {
            get { return false; }
        }

        public ImageGeneratorForm()
        {
            InitializeComponent();
            _comfyClient = new ComfyUIClient(comfyUrlTextBox.Text);
            _ = CheckComfyUIConnectionAsync();
        }

        private async Task CheckComfyUIConnectionAsync()
        {
            try
            {
                await CheckComfyUIConnection();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking ComfyUI connection: {ex.Message}");
            }
        }

        private async Task CheckComfyUIConnection()
        {
            var activeForm = Form.ActiveForm;

            var connected = await _comfyClient.TestConnection();
            if (connected)
            {
                statusLabel.Text = $"✓ Connected to ComfyUI at {comfyUrlTextBox.Text}";
                statusLabel.ForeColor = Color.Green;
                generateButton.Enabled = true;
            }
            else
            {
                statusLabel.Text = $"✗ ComfyUI not detected at {comfyUrlTextBox.Text}";
                statusLabel.ForeColor = Color.Red;
                generateButton.Enabled = false;
            }

            if (activeForm != null && activeForm != this && !activeForm.IsDisposed)
            {
            }
        }

        private void InitializeComponent()
        {
            this.Text = "AI Image Generator (SDXL Turbo)";
            this.Width = 900;
            this.Height = 800;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimumSize = new Size(900, 800);
            this.TopMost = false;
            this.ShowInTaskbar = true;

            var mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 450
            };

            var controlsPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                AutoScroll = true
            };

            int y = 10;

            var urlLabel = new Label
            {
                Text = "ComfyUI URL:",
                Location = new Point(10, y),
                Width = 100,
                Height = 20,
                Font = new Font(this.Font, FontStyle.Bold)
            };
            controlsPanel.Controls.Add(urlLabel);

            comfyUrlTextBox = new TextBox
            {
                Location = new Point(120, y),
                Width = 620,
                Height = 20
            };
            comfyUrlTextBox.Text = AppConfig.Instance.ComfyUIUrl;
            controlsPanel.Controls.Add(comfyUrlTextBox);

            testConnectionButton = new Button
            {
                Text = "Test",
                Location = new Point(750, y),
                Width = 60,
                Height = 23
            };
            testConnectionButton.Click += async (s, e) =>
     {
         _comfyClient = new ComfyUIClient(comfyUrlTextBox.Text);
         await CheckComfyUIConnection();
     };
            controlsPanel.Controls.Add(testConnectionButton);

            y += 35;

            var workflowLabel = new Label
            {
                Text = "Workflow:",
                Location = new Point(10, y),
                Width = 100,
                Height = 20,
                Font = new Font(this.Font, FontStyle.Bold)
            };
            controlsPanel.Controls.Add(workflowLabel);

            workflowTypeComboBox = new ComboBox
            {
                Location = new Point(120, y),
                Width = 200,
                Height = 21,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            workflowTypeComboBox.Items.AddRange(new object[] { "Image Generation", "Image 2 Image Generation" });
            workflowTypeComboBox.SelectedIndex = 0;
            workflowTypeComboBox.SelectedIndexChanged += WorkflowType_Changed;
            controlsPanel.Controls.Add(workflowTypeComboBox);

            y += 35;

            var promptLabel = new Label
            {
                Text = "Prompt:",
                Location = new Point(10, y),
                Width = 100,
                Height = 20,
                Font = new Font(this.Font, FontStyle.Bold)
            };
            controlsPanel.Controls.Add(promptLabel);

            y += 25;

            promptTextBox = new TextBox
            {
                Location = new Point(10, y),
                Width = 800,
                Height = 60,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical
            };
            promptTextBox.Text = "a 3d model isometric, a horse with cart on a black background";
            controlsPanel.Controls.Add(promptTextBox);

            y += 70;

            var negPromptLabel = new Label
            {
                Text = "Negative Prompt:",
                Location = new Point(10, y),
                Width = 120,
                Height = 20
            };
            controlsPanel.Controls.Add(negPromptLabel);

            y += 25;

            negativePromptTextBox = new TextBox
            {
                Location = new Point(10, y),
                Width = 800,
                Height = 40,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical
            };
            controlsPanel.Controls.Add(negativePromptTextBox);

            y += 50;

            var settingsPanel = new FlowLayoutPanel
            {
                Location = new Point(10, y),
                Width = 850,
                Height = 30,
                FlowDirection = FlowDirection.LeftToRight
            };

            var widthLabel = new Label
            {
                Text = "Width:",
                Width = 50,
                TextAlign = ContentAlignment.MiddleLeft
            };
            settingsPanel.Controls.Add(widthLabel);

            widthNumeric = new NumericUpDown
            {
                Width = 70,
                Minimum = 256,
                Maximum = 2048,
                Value = 1024,
                Increment = 64
            };
            settingsPanel.Controls.Add(widthNumeric);

            var heightLabel = new Label
            {
                Text = "Height:",
                Width = 50,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(10, 0, 0, 0)
            };
            settingsPanel.Controls.Add(heightLabel);

            heightNumeric = new NumericUpDown
            {
                Width = 70,
                Minimum = 256,
                Maximum = 2048,
                Value = 1024,
                Increment = 64
            };
            settingsPanel.Controls.Add(heightNumeric);

            var stepsLabel = new Label
            {
                Text = "Steps:",
                Width = 50,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(10, 0, 0, 0)
            };
            settingsPanel.Controls.Add(stepsLabel);

            stepsNumeric = new NumericUpDown
            {
                Width = 60,
                Minimum = 1,
                Maximum = 100,
                Value = 20
            };
            settingsPanel.Controls.Add(stepsNumeric);

            var cfgLabel = new Label
            {
                Text = "CFG:",
                Width = 40,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(10, 0, 0, 0)
            };
            settingsPanel.Controls.Add(cfgLabel);

            cfgNumeric = new NumericUpDown
            {
                Width = 60,
                Minimum = 1,
                Maximum = 30,
                Value = 6,
                DecimalPlaces = 1,
                Increment = 0.5m
            };
            settingsPanel.Controls.Add(cfgNumeric);

            var denoiseLabel = new Label
            {
                Text = "Denoise:",
                Width = 60,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(10, 0, 0, 0)
            };
            settingsPanel.Controls.Add(denoiseLabel);

            denoiseNumeric = new NumericUpDown
            {
                Width = 60,
                Minimum = 0,
                Maximum = 1,
                Value = 1,
                DecimalPlaces = 2,
                Increment = 0.05m
            };
            settingsPanel.Controls.Add(denoiseNumeric);

            var seedLabel = new Label
            {
                Text = "Seed:",
                Width = 50,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(10, 0, 0, 0)
            };
            settingsPanel.Controls.Add(seedLabel);

            seedTextBox = new TextBox
            {
                Width = 120,
                Enabled = false,
                BackColor = Color.LightGray
            };
            settingsPanel.Controls.Add(seedTextBox);

            autoRandomSeedCheckBox = new CheckBox
            {
                Text = "Auto Random",
                Width = 100,
                Checked = true,
                Margin = new Padding(5, 7, 0, 0)
            };
            autoRandomSeedCheckBox.CheckedChanged += (s, e) =>
            {
                if (autoRandomSeedCheckBox.Checked)
                {
                    seedTextBox.Text = "";
                    seedTextBox.Enabled = false;
                    seedTextBox.BackColor = Color.LightGray;
                }
                else
                {
                    seedTextBox.Enabled = true;
                    seedTextBox.BackColor = Color.White;
                }
            };
            settingsPanel.Controls.Add(autoRandomSeedCheckBox);

            controlsPanel.Controls.Add(settingsPanel);

            y += 40;

            var samplerSchedulerPanel = new FlowLayoutPanel
            {
                Location = new Point(10, y),
                Width = 850,
                Height = 30,
                FlowDirection = FlowDirection.LeftToRight
            };

            var samplerLabel = new Label
            {
                Text = "Sampler:",
                Width = 60,
                TextAlign = ContentAlignment.MiddleLeft
            };
            samplerSchedulerPanel.Controls.Add(samplerLabel);

            samplerComboBox = new ComboBox
            {
                Width = 150,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            samplerComboBox.Items.AddRange(new object[] {
      "euler", "euler_cfg_pp", "euler_ancestral", "euler_ancestral_cfg_pp",
 "heun", "heunpp2", "dpm_2", "dpm_2_ancestral", "lms", "dpm_fast",
  "dpm_adaptive", "dpmpp_2s_ancestral", "dpmpp_2s_ancestral_cfg_pp",
        "dpmpp_sde", "dpmpp_sde_gpu", "dpmpp_2m", "dpmpp_2m_cfg_pp",
      "dpmpp_2m_sde", "dpmpp_2m_sde_gpu", "dpmpp_3m_sde", "dpmpp_3m_sde_gpu",
   "ddpm", "lcm", "ipndm", "ipndm_v", "deis"
      });
            samplerComboBox.SelectedIndex = 0;
            samplerSchedulerPanel.Controls.Add(samplerComboBox);

            var schedulerLabel = new Label
            {
                Text = "Scheduler:",
                Width = 70,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(10, 0, 0, 0)
            };
            samplerSchedulerPanel.Controls.Add(schedulerLabel);

            schedulerComboBox = new ComboBox
            {
                Width = 150,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            schedulerComboBox.Items.AddRange(new object[] {
        "simple", "normal", "karras", "exponential", "sgm_uniform",
                "ddim_uniform", "beta", "linear_quadratic", "kl_optimal"
});
            schedulerComboBox.SelectedIndex = 0;
            samplerSchedulerPanel.Controls.Add(schedulerComboBox);

            controlsPanel.Controls.Add(samplerSchedulerPanel);

            y += 40;

            img2imgPanel = new Panel
            {
                Location = new Point(10, y),
                Width = 850,
                Height = 120,
                Visible = false,
                BorderStyle = BorderStyle.FixedSingle
            };

            var img2imgLabel = new Label
            {
                Text = "Input Image:",
                Location = new Point(5, 10),
                Width = 80,
                Font = new Font(this.Font, FontStyle.Bold)
            };
            img2imgPanel.Controls.Add(img2imgLabel);

            selectInputImageButton = new Button
            {
                Text = "Select Image...",
                Location = new Point(90, 7),
                Width = 120,
                Height = 25
            };
            selectInputImageButton.Click += SelectInputImage_Click;
            img2imgPanel.Controls.Add(selectInputImageButton);

            inputImageLabel = new Label
            {
                Text = "No image selected",
                Location = new Point(220, 10),
                Width = 300,
                ForeColor = Color.Gray
            };
            img2imgPanel.Controls.Add(inputImageLabel);

            inputImagePreview = new PictureBox
            {
                Location = new Point(730, 5),
                Width = 110,
                Height = 110,
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.LightGray
            };
            img2imgPanel.Controls.Add(inputImagePreview);

            var resizeLabel = new Label
            {
                Text = "Resize:",
                Location = new Point(5, 50),
                Width = 60
            };
            img2imgPanel.Controls.Add(resizeLabel);

            var resizeWidthLabel = new Label
            {
                Text = "W:",
                Width = 25,
                TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(70, 52)
            };
            img2imgPanel.Controls.Add(resizeWidthLabel);

            resizeWidthNumeric = new NumericUpDown
            {
                Location = new Point(100, 50),
                Width = 70,
                Minimum = 64,
                Maximum = 2048,
                Value = 512,
                Increment = 64
            };
            img2imgPanel.Controls.Add(resizeWidthNumeric);

            var resizeHeightLabel = new Label
            {
                Text = "H:",
                Width = 25,
                TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(180, 52)
            };
            img2imgPanel.Controls.Add(resizeHeightLabel);

            resizeHeightNumeric = new NumericUpDown
            {
                Location = new Point(210, 50),
                Width = 70,
                Minimum = 64,
                Maximum = 2048,
                Value = 512,
                Increment = 64
            };
            img2imgPanel.Controls.Add(resizeHeightNumeric);

            controlsPanel.Controls.Add(img2imgPanel);

            y += 130;

            var buttonPanel = new FlowLayoutPanel
            {
                Location = new Point(10, y),
                Width = 850,
                Height = 40,
                FlowDirection = FlowDirection.LeftToRight
            };

            generateButton = new Button
            {
                Text = "Generate Image",
                Width = 130,
                Height = 35,
                BackColor = Color.LightGreen,
                Font = new Font(this.Font, FontStyle.Bold),
                Enabled = false
            };
            generateButton.Click += async (s, e) => await GenerateImage();
            buttonPanel.Controls.Add(generateButton);

            saveButton = new Button
            {
                Text = "Save Image",
                Width = 100,
                Height = 35,
                Enabled = false
            };
            saveButton.Click += SaveImage_Click;
            buttonPanel.Controls.Add(saveButton);

            useAsReferenceButton = new Button
            {
                Text = "Use as Reference",
                Width = 130,
                Height = 35,
                Enabled = false
            };
            useAsReferenceButton.Click += UseAsReference_Click;
            buttonPanel.Controls.Add(useAsReferenceButton);

            autoViewCheckBox = new CheckBox
            {
                Text = "Auto-view after generation",
                Checked = true,
                AutoSize = true,
                Margin = new Padding(10, 10, 0, 0)
            };
            buttonPanel.Controls.Add(autoViewCheckBox);

            controlsPanel.Controls.Add(buttonPanel);

            y += 50;

            statusLabel = new Label
            {
                Location = new Point(10, y),
                Width = 850,
                Height = 20,
                Text = "Checking ComfyUI connection..."
            };
            controlsPanel.Controls.Add(statusLabel);

            y += 25;

            progressBar = new ProgressBar
            {
                Location = new Point(10, y),
                Width = 850,
                Height = 20,
                Style = ProgressBarStyle.Marquee,
                Visible = false
            };
            controlsPanel.Controls.Add(progressBar);

            y += 25;

            debugTextBox = new TextBox
            {
                Location = new Point(10, y),
                Width = 850,
                Height = 60,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(250, 250, 250),
                Font = new Font("Consolas", 8f),
                Text = "Debug output will appear here..."
            };
            controlsPanel.Controls.Add(debugTextBox);

            mainSplit.Panel1.Controls.Add(controlsPanel);

            var previewPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                BackColor = Color.FromArgb(240, 240, 240)
            };

            previewPictureBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            previewPanel.Controls.Add(previewPictureBox);

            mainSplit.Panel2.Controls.Add(previewPanel);

            this.Controls.Add(mainSplit);
        }

        private async Task GenerateImage()
        {
            try
            {
                generateButton.Enabled = false;
                progressBar.Visible = true;
                statusLabel.Text = "Queueing prompt...";
                statusLabel.ForeColor = Color.Blue;
                debugTextBox.Clear();

                long? seed = null;
                if (!string.IsNullOrWhiteSpace(seedTextBox.Text))
                {
                    if (long.TryParse(seedTextBox.Text, out long parsedSeed))
                        seed = parsedSeed;
                }

                if (autoRandomSeedCheckBox.Checked)
                {
                    seed = null;
                    debugTextBox.AppendText("Using auto-random seed\r\n");
                }
                else if (seed.HasValue)
                {
                    debugTextBox.AppendText($"Using seed: {seed.Value}\r\n");
                }

                string workflow;
                bool isImg2Img = workflowTypeComboBox.SelectedIndex == 1;

                if (isImg2Img)
                {
                    if (string.IsNullOrEmpty(_inputImagePath))
                    {
                        MessageBox.Show("Please select an input image first.", "Input Required",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    statusLabel.Text = "Uploading input image...";
                    debugTextBox.AppendText("Uploading input image to ComfyUI...\r\n");

                    string uploadedFilename;
                    try
                    {
                        var imageBytes = System.IO.File.ReadAllBytes(_inputImagePath);
                        uploadedFilename = await _comfyClient.UploadImage(imageBytes, System.IO.Path.GetFileName(_inputImagePath));
                        debugTextBox.AppendText($"Uploaded as: {uploadedFilename}\r\n");
                    }
                    catch (Exception uploadEx)
                    {
                        debugTextBox.AppendText($"Upload failed: {uploadEx.Message}\r\n");
                        throw;
                    }

                    workflow = Image2ImageWorkflow.CreateWorkflow(
                     promptTextBox.Text,
                       negativePromptTextBox.Text,
                  uploadedFilename,
                         (int)resizeWidthNumeric.Value,
                  (int)resizeHeightNumeric.Value,
                    (int)stepsNumeric.Value,
                    (double)cfgNumeric.Value,
                    (double)denoiseNumeric.Value,
                      seed,
                 samplerComboBox.SelectedItem.ToString(),
                        schedulerComboBox.SelectedItem.ToString()
                  );
                }
                else
                {
                    workflow = DreamshaperWorkflow.CreateWorkflow(
                  promptTextBox.Text,
                          negativePromptTextBox.Text,
              (int)widthNumeric.Value,
                 (int)heightNumeric.Value,
             (int)stepsNumeric.Value,
                           (double)cfgNumeric.Value,
                       seed,
           samplerComboBox.SelectedItem.ToString(),
                          schedulerComboBox.SelectedItem.ToString()
                         );
                }

                debugTextBox.AppendText($"Workflow created: {workflow.Length} chars\r\n");

                try
                {
                    _lastPromptId = await _comfyClient.QueuePrompt(workflow);
                }
                catch (Exception queueEx)
                {
                    debugTextBox.AppendText($"Queue exception: {queueEx.Message}\r\n");
                    throw;
                }

                if (string.IsNullOrEmpty(_lastPromptId))
                {
                    statusLabel.Text = "Failed to get prompt ID from ComfyUI";
                    statusLabel.ForeColor = Color.Red;
                    debugTextBox.AppendText("ERROR: No prompt ID returned\r\n");
                    debugTextBox.AppendText("Check Visual Studio Output window for raw ComfyUI response\r\n");
                    return;
                }

                debugTextBox.AppendText($"Prompt queued with ID: {_lastPromptId}\r\n");
                statusLabel.Text = $"Generating... (Prompt ID: {_lastPromptId})";

                debugTextBox.AppendText("Polling for generated images (this may take 30-60 seconds)...\r\n");
                var images = await _comfyClient.GetGeneratedImages(_lastPromptId, maxAttempts: 60, pollIntervalMs: 1000);
                debugTextBox.AppendText($"Found {images.Count} image(s)\r\n");

                foreach (var img in images)
                {
                    debugTextBox.AppendText($"  - {img.Filename} (subfolder='{img.Subfolder}', type='{img.Type}')\r\n");
                }

                if (images.Count == 0)
                {
                    statusLabel.Text = "ERROR: No images in response";
                    statusLabel.ForeColor = Color.Red;
                    debugTextBox.AppendText("ERROR: No images found in ComfyUI history.\r\n");
                    debugTextBox.AppendText("Check that ComfyUI is running and the output folder exists.\r\n");
                    return;
                }

                debugTextBox.AppendText($"Downloading image: {images[0].Filename}\r\n");
                var imageData = await _comfyClient.DownloadImage(images[0]);
                if (imageData == null || imageData.Length == 0)
                {
                    debugTextBox.AppendText("ERROR: Download from ComfyUI failed.\r\n");
                    statusLabel.Text = "Download failed";
                    statusLabel.ForeColor = Color.Red;
                    return;
                }

                using (var ms = new MemoryStream(imageData))
                {
                    _generatedImage?.Dispose();
                    _generatedImage = Image.FromStream(ms);
                    previewPictureBox.Image = _generatedImage;
                }

                statusLabel.Text = $"✓ Generated successfully! ({images[0].Filename})";
                statusLabel.ForeColor = Color.Green;
                saveButton.Enabled = true;
                useAsReferenceButton.Enabled = true;

                ImageGenerated?.Invoke(_generatedImage);

                if (autoViewCheckBox.Checked && previewPictureBox.Image != null)
                {
                    debugTextBox.AppendText("Image displayed in preview.\r\n");
                }

                debugTextBox.AppendText("Generation complete!\r\n");
            }
            catch (Exception ex)
            {
                statusLabel.Text = $"Error: {ex.Message}";
                statusLabel.ForeColor = Color.Red;
                debugTextBox.AppendText($"ERROR: {ex.Message}\r\n");
                debugTextBox.AppendText($"Stack trace: {ex.StackTrace}\r\n");
            }
            finally
            {
                generateButton.Enabled = true;
                progressBar.Visible = false;
            }
        }

        private void SaveImage_Click(object sender, EventArgs e)
        {
            if (_generatedImage == null)
            {
                MessageBox.Show("No image to save.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (var sfd = new SaveFileDialog())
            {
                sfd.Filter = "PNG Image|*.png|JPEG Image|*.jpg|All Files|*.*";
                sfd.DefaultExt = "png";
                sfd.FileName = $"generated_{DateTime.Now:yyyyMMdd_HHmmss}.png";

                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        _generatedImage.Save(sfd.FileName);
                        statusLabel.Text = $"Saved to: {sfd.FileName}";
                        statusLabel.ForeColor = Color.Green;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to save image: {ex.Message}", "Error",
                              MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void UseAsReference_Click(object sender, EventArgs e)
        {
            if (_generatedImage == null)
            {
                MessageBox.Show("No image to use.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ImageGenerated?.Invoke(_generatedImage);
            MessageBox.Show("Image sent to main application.", "Success",
                           MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void WorkflowType_Changed(object sender, EventArgs e)
        {
            bool isImg2Img = workflowTypeComboBox.SelectedIndex == 1;
            img2imgPanel.Visible = isImg2Img;
        }

        private void SelectInputImage_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All Files|*.*";
                ofd.Title = "Select Input Image";

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        _inputImagePath = ofd.FileName;
                        inputImageLabel.Text = System.IO.Path.GetFileName(_inputImagePath);
                        inputImageLabel.ForeColor = Color.Black;

                        // Safely dispose old image
                        if (inputImagePreview.Image != null)
                        {
                            var oldImage = inputImagePreview.Image;
                            inputImagePreview.Image = null;
                            oldImage.Dispose();
                        }

                        using (var fs = new FileStream(_inputImagePath, FileMode.Open, FileAccess.Read))
                        {
                            // Create a copy so we don't lock the file
                            using (var tempImage = Image.FromStream(fs))
                            {
                                inputImagePreview.Image = new Bitmap(tempImage);
                            }
                        }

                        debugTextBox.AppendText($"Input image loaded: {System.IO.Path.GetFileName(_inputImagePath)}\r\n");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to load image: {ex.Message}", "Error",
               MessageBoxButtons.OK, MessageBoxIcon.Error);
                        inputImageLabel.Text = "Failed to load image";
                        inputImageLabel.ForeColor = Color.Red;
                    }
                }
            }
        }
    }
}
