namespace MeesaMultisMaker
{
 partial class Form1
 {
 /// <summary>
 /// Required designer variable.
 /// </summary>
 private System.ComponentModel.IContainer components = null;

 /// <summary>
 /// Clean up any resources being used.
 /// </summary>
 /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
 protected override void Dispose(bool disposing)
 {
 if (disposing && (components != null))
 {
 components.Dispose();
 }
 base.Dispose(disposing);
 }

 #region Windows Form Designer generated code

 /// <summary>
 /// Required method for Designer support - do not modify
 /// the contents of this method with the code editor.
 /// </summary>
 private void InitializeComponent()
 {
 this.components = new System.ComponentModel.Container();
 this.mainSplitContainer = new System.Windows.Forms.SplitContainer();
 this.palettePanel = new System.Windows.Forms.Panel();
 this.paletteListView = new System.Windows.Forms.ListView();
 this.searchTextBox = new System.Windows.Forms.TextBox();
 this.paletteFlowLayoutPanel = new System.Windows.Forms.FlowLayoutPanel();
 this.rightSplitContainer = new System.Windows.Forms.SplitContainer();
 this.designPictureBox = new System.Windows.Forms.PictureBox();
 this.controlsPanel = new System.Windows.Forms.Panel();
 this.widthLabel = new System.Windows.Forms.Label();
 this.widthNumericUpDown = new System.Windows.Forms.NumericUpDown();
 this.heightLabel = new System.Windows.Forms.Label();
 this.heightNumericUpDown = new System.Windows.Forms.NumericUpDown();
 this.createGridButton = new System.Windows.Forms.Button();
 this.zUpButton = new System.Windows.Forms.Button();
 this.zDownButton = new System.Windows.Forms.Button();
 this.deleteButton = new System.Windows.Forms.Button();
 this.exportButton = new System.Windows.Forms.Button();
 this.browseButton = new System.Windows.Forms.Button();
 this.layerUpButton = new System.Windows.Forms.Button();
 this.layerDownButton = new System.Windows.Forms.Button();
 this.layerTopButton = new System.Windows.Forms.Button();
 this.layerBottomButton = new System.Windows.Forms.Button();
 this.outputTextBox = new System.Windows.Forms.TextBox();
 this.folderBrowserDialog = new System.Windows.Forms.FolderBrowserDialog();
 this.imageList = new System.Windows.Forms.ImageList(this.components);
 this.imageList.ColorDepth = System.Windows.Forms.ColorDepth.Depth32Bit;
 this.imageList.ImageSize = new System.Drawing.Size(1,80);
 this.imageList.TransparentColor = System.Drawing.Color.Transparent;
 this.columnHeaderItem = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
 this.lockPanel = new System.Windows.Forms.Panel();
 this.unlockedGroup = new System.Windows.Forms.GroupBox();
 this.lockedGroup = new System.Windows.Forms.GroupBox();
 this.unlockedListBox = new System.Windows.Forms.ListBox();
 this.lockedListBox = new System.Windows.Forms.ListBox();
 this.unlockedToolPanel = new System.Windows.Forms.Panel();
 this.lockSelectedButton = new System.Windows.Forms.Button();
 this.lockedToolPanel = new System.Windows.Forms.Panel();
 this.unlockSelectedButton = new System.Windows.Forms.Button();
 this.addTrainingButton = new System.Windows.Forms.Button();
 this.generateButton = new System.Windows.Forms.Button();
 this.clearTrainingButton = new System.Windows.Forms.Button();
 this.exportCanvasButton = new System.Windows.Forms.Button();
 this.aiGeneratorPanel = new System.Windows.Forms.Panel();
 this.paletteSizeFilterPanel = new System.Windows.Forms.Panel();
 this.paletteInfoPanel = new System.Windows.Forms.Panel();
 this.showEmptySlotsCheckBox = new System.Windows.Forms.CheckBox();
 ((System.ComponentModel.ISupportInitialize)(this.mainSplitContainer)).BeginInit();
 this.mainSplitContainer.Panel1.SuspendLayout();
 this.mainSplitContainer.Panel2.SuspendLayout();
 this.mainSplitContainer.SuspendLayout();
 ((System.ComponentModel.ISupportInitialize)(this.rightSplitContainer)).BeginInit();
 this.rightSplitContainer.Panel1.SuspendLayout();
 this.rightSplitContainer.Panel2.SuspendLayout();
 this.rightSplitContainer.SuspendLayout();
 ((System.ComponentModel.ISupportInitialize)(this.designPictureBox)).BeginInit();
 this.controlsPanel.SuspendLayout();
 ((System.ComponentModel.ISupportInitialize)(this.widthNumericUpDown)).BeginInit();
 ((System.ComponentModel.ISupportInitialize)(this.heightNumericUpDown)).BeginInit();
 this.lockPanel.SuspendLayout();
 this.unlockedGroup.SuspendLayout();
 this.lockedGroup.SuspendLayout();
 this.SuspendLayout();
 // 
 // mainSplitContainer
 // 
 this.mainSplitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
 this.mainSplitContainer.Location = new System.Drawing.Point(0,0);
 this.mainSplitContainer.Name = "mainSplitContainer";
 // 
 // mainSplitContainer.Panel1
 // 
 this.mainSplitContainer.Panel1.Controls.Add(this.palettePanel);
 // 
 // mainSplitContainer.Panel2
 // 
 this.mainSplitContainer.Panel2.Controls.Add(this.rightSplitContainer);
 this.mainSplitContainer.Size = new System.Drawing.Size(1500,800);
 this.mainSplitContainer.SplitterDistance = 280;
 this.mainSplitContainer.TabIndex = 0;
 // 
 // rightSplitContainer - splits center content from right panels (AI + Image Editing)
 // 
 this.rightSplitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
 this.rightSplitContainer.Location = new System.Drawing.Point(0,0);
 this.rightSplitContainer.Name = "rightSplitContainer";
 // 
 // rightSplitContainer.Panel1 - main canvas area
 // 
 this.rightSplitContainer.Panel1.Controls.Add(this.lockPanel);
 this.rightSplitContainer.Panel1.Controls.Add(this.designPictureBox);
 this.rightSplitContainer.Panel1.Controls.Add(this.controlsPanel);
 this.rightSplitContainer.Panel1.Controls.Add(this.outputTextBox);
 // 
 // rightSplitContainer.Panel2 - AI Generator panel (Image Editing is now a floating window)
 // 
 this.rightSplitContainer.Panel2.Controls.Add(this.aiGeneratorPanel);
 this.rightSplitContainer.Size = new System.Drawing.Size(1216,800);
 this.rightSplitContainer.SplitterDistance = 976;
 this.rightSplitContainer.TabIndex = 0;
 // 
 // aiGeneratorPanel - placeholder for AI Generator UserControl
 // 
 this.aiGeneratorPanel.Dock = System.Windows.Forms.DockStyle.Fill;
 this.aiGeneratorPanel.Name = "aiGeneratorPanel";
 this.aiGeneratorPanel.BackColor = System.Drawing.Color.FromArgb(245, 245, 245);
 // 
 // palettePanel
 // 
 this.palettePanel.Controls.Add(this.paletteListView);      // Fill - added first, docks last
 this.palettePanel.Controls.Add(this.paletteInfoPanel);     // Bottom
 this.palettePanel.Controls.Add(this.showEmptySlotsCheckBox); // Above info panel
 this.palettePanel.Controls.Add(this.paletteSizeFilterPanel); // Top (below search)
 this.palettePanel.Controls.Add(this.searchTextBox);        // Top (at very top)
 this.palettePanel.Dock = System.Windows.Forms.DockStyle.Fill;
 this.palettePanel.Location = new System.Drawing.Point(0,0);
 this.palettePanel.Name = "palettePanel";
 this.palettePanel.Size = new System.Drawing.Size(280,800);
 this.palettePanel.TabIndex =0;
 // 
 // searchTextBox
 // 
 this.searchTextBox.Dock = System.Windows.Forms.DockStyle.Top;
 this.searchTextBox.Location = new System.Drawing.Point(0,0);
 this.searchTextBox.Name = "searchTextBox";
 this.searchTextBox.Size = new System.Drawing.Size(280,25);
 this.searchTextBox.Height = 25;
 this.searchTextBox.TabIndex =0;
 this.searchTextBox.Text = "Search...";
 this.searchTextBox.ForeColor = System.Drawing.Color.Gray;
 this.searchTextBox.Font = new System.Drawing.Font("Consolas", 10F);
 // 
 // paletteSizeFilterPanel
 // 
 this.paletteSizeFilterPanel.Dock = System.Windows.Forms.DockStyle.Top;
 this.paletteSizeFilterPanel.Height = 75;
 this.paletteSizeFilterPanel.Name = "paletteSizeFilterPanel";
 // 
 // paletteInfoPanel - shows selected item TileData info
 // 
 this.paletteInfoPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
 this.paletteInfoPanel.Height = 200;
 this.paletteInfoPanel.Name = "paletteInfoPanel";
 this.paletteInfoPanel.Padding = new System.Windows.Forms.Padding(5);
 // 
 // showEmptySlotsCheckBox
 // 
 this.showEmptySlotsCheckBox.Dock = System.Windows.Forms.DockStyle.Bottom;
 this.showEmptySlotsCheckBox.Height = 25;
 this.showEmptySlotsCheckBox.Name = "showEmptySlotsCheckBox";
 this.showEmptySlotsCheckBox.Text = "Show Empty Slots";
 this.showEmptySlotsCheckBox.Padding = new System.Windows.Forms.Padding(5, 3, 0, 3);
 // 
 // paletteListView
 // 
 this.paletteListView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
 this.columnHeaderItem});
 this.paletteListView.Dock = System.Windows.Forms.DockStyle.Fill;
 this.paletteListView.FullRowSelect = true;
 this.paletteListView.HideSelection = false;
 this.paletteListView.Location = new System.Drawing.Point(0,20);
 this.paletteListView.MultiSelect = true;
 this.paletteListView.Name = "paletteListView";
 this.paletteListView.OwnerDraw = true;
 this.paletteListView.Size = new System.Drawing.Size(280,780);
 this.paletteListView.SmallImageList = this.imageList;
 this.paletteListView.TabIndex =1;
 this.paletteListView.UseCompatibleStateImageBehavior = false;
 this.paletteListView.View = System.Windows.Forms.View.Details;
 this.paletteListView.VirtualMode = true;
 this.paletteListView.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.None;
 // 
 // columnHeaderItem
 // 
 this.columnHeaderItem.Text = "Item";
 this.columnHeaderItem.Width =180;
 // 
 // paletteFlowLayoutPanel
 // 
 this.paletteFlowLayoutPanel.AutoScroll = true;
 this.paletteFlowLayoutPanel.Dock = System.Windows.Forms.DockStyle.Fill;
 this.paletteFlowLayoutPanel.Location = new System.Drawing.Point(0,0);
 this.paletteFlowLayoutPanel.Name = "paletteFlowLayoutPanel";
 this.paletteFlowLayoutPanel.Size = new System.Drawing.Size(280,800);
 this.paletteFlowLayoutPanel.TabIndex =0;
 this.paletteFlowLayoutPanel.Visible = false;
 // 
 // designPictureBox
 // 
 this.designPictureBox.BackColor = System.Drawing.Color.White;
 this.designPictureBox.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
 this.designPictureBox.Dock = System.Windows.Forms.DockStyle.Fill;
 this.designPictureBox.Location = new System.Drawing.Point(0,50);
 this.designPictureBox.Name = "designPictureBox";
 this.designPictureBox.Size = new System.Drawing.Size(656,550);
 this.designPictureBox.TabIndex =0;
 this.designPictureBox.TabStop = true;
 this.designPictureBox.MouseWheel -= new System.Windows.Forms.MouseEventHandler(this.DesignPictureBox_MouseWheel);
 this.designPictureBox.MouseWheel += new System.Windows.Forms.MouseEventHandler(this.DesignPictureBox_MouseWheel);
 // 
 // controlsPanel
 // 
 this.controlsPanel.Controls.Add(this.widthLabel);
 this.controlsPanel.Controls.Add(this.widthNumericUpDown);
 this.controlsPanel.Controls.Add(this.heightLabel);
 this.controlsPanel.Controls.Add(this.heightNumericUpDown);
 this.controlsPanel.Controls.Add(this.createGridButton);
 this.controlsPanel.Controls.Add(this.zUpButton);
 this.controlsPanel.Controls.Add(this.zDownButton);
 this.controlsPanel.Controls.Add(this.deleteButton);
 this.controlsPanel.Controls.Add(this.exportButton);
 this.controlsPanel.Controls.Add(this.browseButton);
 this.controlsPanel.Controls.Add(this.layerUpButton);
 this.controlsPanel.Controls.Add(this.layerDownButton);
 this.controlsPanel.Controls.Add(this.layerTopButton);
 this.controlsPanel.Controls.Add(this.layerBottomButton);
 this.controlsPanel.Dock = System.Windows.Forms.DockStyle.Top;
 this.controlsPanel.Location = new System.Drawing.Point(0,0);
 this.controlsPanel.Name = "controlsPanel";
 this.controlsPanel.Size = new System.Drawing.Size(876,80);
 this.controlsPanel.TabIndex =1;
 // 
 // widthLabel
 // 
 this.widthLabel.AutoSize = true;
 this.widthLabel.Location = new System.Drawing.Point(10,16);
 this.widthLabel.Name = "widthLabel";
 this.widthLabel.Size = new System.Drawing.Size(38,13);
 this.widthLabel.TabIndex =0;
 this.widthLabel.Text = "Width:";
 // 
 // widthNumericUpDown
 // 
 this.widthNumericUpDown.Location = new System.Drawing.Point(54,14);
 this.widthNumericUpDown.Maximum = new decimal(new int[] {50,0,0,0});
 this.widthNumericUpDown.Minimum = new decimal(new int[] {1,0,0,0});
 this.widthNumericUpDown.Name = "widthNumericUpDown";
 this.widthNumericUpDown.Size = new System.Drawing.Size(50,20);
 this.widthNumericUpDown.TabIndex =1;
 this.widthNumericUpDown.Value = new decimal(new int[] {10,0,0,0});
 // 
 // heightLabel
 // 
 this.heightLabel.AutoSize = true;
 this.heightLabel.Location = new System.Drawing.Point(110,16);
 this.heightLabel.Name = "heightLabel";
 this.heightLabel.Size = new System.Drawing.Size(41,13);
 this.heightLabel.TabIndex =2;
 this.heightLabel.Text = "Height:";
 // 
 // heightNumericUpDown
 // 
 this.heightNumericUpDown.Location = new System.Drawing.Point(157,14);
 this.heightNumericUpDown.Maximum = new decimal(new int[] {50,0,0,0});
 this.heightNumericUpDown.Minimum = new decimal(new int[] {1,0,0,0});
 this.heightNumericUpDown.Name = "heightNumericUpDown";
 this.heightNumericUpDown.Size = new System.Drawing.Size(50,20);
 this.heightNumericUpDown.TabIndex =3;
 this.heightNumericUpDown.Value = new decimal(new int[] {10,0,0,0});
 // 
 // createGridButton
 // 
 this.createGridButton.Location = new System.Drawing.Point(215,12);
 this.createGridButton.Name = "createGridButton";
 this.createGridButton.Size = new System.Drawing.Size(80,25);
 this.createGridButton.TabIndex =4;
 this.createGridButton.Text = "Create Grid";
 this.createGridButton.UseVisualStyleBackColor = true;
 // 
 // zUpButton
 // 
 this.zUpButton.Location = new System.Drawing.Point(305,12);
 this.zUpButton.Name = "zUpButton";
 this.zUpButton.Size = new System.Drawing.Size(35,25);
 this.zUpButton.TabIndex =5;
 this.zUpButton.Text = "Z+";
 this.zUpButton.UseVisualStyleBackColor = true;
 // 
 // zDownButton
 // 
 this.zDownButton.Location = new System.Drawing.Point(345,12);
 this.zDownButton.Name = "zDownButton";
 this.zDownButton.Size = new System.Drawing.Size(35,25);
 this.zDownButton.TabIndex =6;
 this.zDownButton.Text = "Z-";
 this.zDownButton.UseVisualStyleBackColor = true;
 // 
 // deleteButton
 // 
 this.deleteButton.Location = new System.Drawing.Point(390,12);
 this.deleteButton.Name = "deleteButton";
 this.deleteButton.Size = new System.Drawing.Size(75,25);
 this.deleteButton.TabIndex =7;
 this.deleteButton.Text = "Delete";
 this.deleteButton.UseVisualStyleBackColor = true;
 // 
 // exportButton
 // 
 this.exportButton.Location = new System.Drawing.Point(475,12);
 this.exportButton.Name = "exportButton";
 this.exportButton.Size = new System.Drawing.Size(75,25);
 this.exportButton.TabIndex =8;
 this.exportButton.Text = "Export";
 this.exportButton.UseVisualStyleBackColor = true;
 // 
 // exportCanvasButton
 // 
  this.exportCanvasButton.Location = new System.Drawing.Point(380,42);
  this.exportCanvasButton.Name = "exportCanvasButton";
  this.exportCanvasButton.Size = new System.Drawing.Size(110,25);
  this.exportCanvasButton.TabIndex =17;
  this.exportCanvasButton.Text = "Export Canvas";
  this.exportCanvasButton.UseVisualStyleBackColor = true;
  
  // 
  // browseButton
  // 
  this.browseButton.Location = new System.Drawing.Point(630,12);
  this.browseButton.Name = "browseButton";
  this.browseButton.Size = new System.Drawing.Size(90,25);
  this.browseButton.TabIndex =9;
  this.browseButton.Text = "MUL Folder...";
  this.browseButton.UseVisualStyleBackColor = true;
 // 
 // layerUpButton
 // 
 this.layerUpButton.Location = new System.Drawing.Point(730,12);
 this.layerUpButton.Name = "layerUpButton";
 this.layerUpButton.Size = new System.Drawing.Size(35,25);
 this.layerUpButton.TabIndex =11;
 this.layerUpButton.Text = "L+";
 this.layerUpButton.UseVisualStyleBackColor = true;
 // 
 // layerDownButton
 // 
 this.layerDownButton.Location = new System.Drawing.Point(770,12);
 this.layerDownButton.Name = "layerDownButton";
 this.layerDownButton.Size = new System.Drawing.Size(35,25);
 this.layerDownButton.TabIndex =12;
 this.layerDownButton.Text = "L-";
 this.layerDownButton.UseVisualStyleBackColor = true;
 // 
 // layerTopButton
 // 
 this.layerTopButton.Location = new System.Drawing.Point(810,12);
 this.layerTopButton.Name = "layerTopButton";
 this.layerTopButton.Size = new System.Drawing.Size(35,25);
 this.layerTopButton.TabIndex =13;
 this.layerTopButton.Text = "Top";
 this.layerTopButton.UseVisualStyleBackColor = true;
 // 
 // layerBottomButton
 // 
 this.layerBottomButton.Location = new System.Drawing.Point(850,12);
 this.layerBottomButton.Name = "layerBottomButton";
 this.layerBottomButton.Size = new System.Drawing.Size(35,25);
 this.layerBottomButton.TabIndex =14;
 this.layerBottomButton.Text = "Bot";
 this.layerBottomButton.UseVisualStyleBackColor = true;
 // 
 // addTrainingButton
 // 
 this.addTrainingButton.Location = new System.Drawing.Point(10,42);
 this.addTrainingButton.Name = "addTrainingButton";
 this.addTrainingButton.Size = new System.Drawing.Size(110,25);
 this.addTrainingButton.TabIndex =14;
 this.addTrainingButton.Text = "Add as Training";
 this.addTrainingButton.UseVisualStyleBackColor = true;
 // 
 // generateButton
 // 
  this.generateButton.Location = new System.Drawing.Point(130,42);
  this.generateButton.Name = "generateButton";
  this.generateButton.Size = new System.Drawing.Size(120,25);
  this.generateButton.TabIndex =15;
  this.generateButton.Text = "Generate Variant";
  this.generateButton.UseVisualStyleBackColor = true;
  this.generateButton.BackColor = System.Drawing.Color.LightGreen;
  // 
  // clearTrainingButton
  // 
  this.clearTrainingButton.Location = new System.Drawing.Point(260,42);
  this.clearTrainingButton.Name = "clearTrainingButton";
  this.clearTrainingButton.Size = new System.Drawing.Size(110,25);
  this.clearTrainingButton.TabIndex =16;
  this.clearTrainingButton.Text = "Clear Training";
  this.clearTrainingButton.UseVisualStyleBackColor = true;
  
  // 
  // outputTextBox
  // 
  this.outputTextBox.Dock = System.Windows.Forms.DockStyle.Bottom;
  this.outputTextBox.Location = new System.Drawing.Point(0,650);
  this.outputTextBox.Multiline = true;
  this.outputTextBox.Name = "outputTextBox";
  this.outputTextBox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
  this.outputTextBox.Size = new System.Drawing.Size(656,150);
  this.outputTextBox.TabIndex =2;
 // 
 // lockPanel
 // 
 this.lockPanel.Dock = System.Windows.Forms.DockStyle.Right;
 this.lockPanel.Width = 200;
 this.lockPanel.Name = "lockPanel";
 this.lockPanel.Padding = new System.Windows.Forms.Padding(4);
 this.lockPanel.TabIndex =50;
 // 
 // unlockedGroup
 // 
 this.unlockedGroup.Dock = System.Windows.Forms.DockStyle.Top;
 this.unlockedGroup.Height =200;
 this.unlockedGroup.Text = "Unlocked";
 this.unlockedGroup.Padding = new System.Windows.Forms.Padding(6);
 // 
 // unlockedToolPanel
 // 
 this.unlockedToolPanel.Dock = System.Windows.Forms.DockStyle.Top;
 this.unlockedToolPanel.Height =30;
 this.unlockedToolPanel.Padding = new System.Windows.Forms.Padding(6,4,6,4);
 this.unlockedToolPanel.Controls.Add(this.lockSelectedButton);
 // 
 // lockSelectedButton
 // 
 this.lockSelectedButton.Dock = System.Windows.Forms.DockStyle.Right;
 this.lockSelectedButton.Width =100;
 this.lockSelectedButton.Text = "Lock Selected";
 this.lockSelectedButton.Name = "lockSelectedButton";
 // 
 // lockedGroup
 // 
 this.lockedGroup.Dock = System.Windows.Forms.DockStyle.Fill;
 this.lockedGroup.Text = "Locked";
 this.lockedGroup.Padding = new System.Windows.Forms.Padding(6);
 // 
 // lockedToolPanel
 // 
 this.lockedToolPanel.Dock = System.Windows.Forms.DockStyle.Top;
 this.lockedToolPanel.Height =30;
 this.lockedToolPanel.Padding = new System.Windows.Forms.Padding(6,4,6,4);
 this.lockedToolPanel.Controls.Add(this.unlockSelectedButton);
 // 
 // unlockSelectedButton
 // 
 this.unlockSelectedButton.Dock = System.Windows.Forms.DockStyle.Right;
 this.unlockSelectedButton.Width =120;
 this.unlockSelectedButton.Text = "Unlock Selected";
 this.unlockSelectedButton.Name = "unlockSelectedButton";
 // 
 // unlockedListBox
 // 
 this.unlockedListBox.Dock = System.Windows.Forms.DockStyle.Fill;
 this.unlockedListBox.Name = "unlockedListBox";
 this.unlockedListBox.IntegralHeight = false;
 this.unlockedListBox.HorizontalScrollbar = true;
 this.unlockedListBox.SelectionMode = System.Windows.Forms.SelectionMode.MultiExtended;
 // 
 // lockedListBox
 // 
 this.lockedListBox.Dock = System.Windows.Forms.DockStyle.Fill;
 this.lockedListBox.Name = "lockedListBox";
 this.lockedListBox.IntegralHeight = false;
 this.lockedListBox.HorizontalScrollbar = true;
 this.lockedListBox.SelectionMode = System.Windows.Forms.SelectionMode.MultiExtended;
 // 
 // Form1
 // 
 this.AutoScaleDimensions = new System.Drawing.SizeF(6F,13F);
 this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1600,800);
 this.Controls.Add(this.mainSplitContainer);
 this.Name = "Form1";
 this.Text = $"Meesa Multis Maker {UpdateChecker.CurrentVersionString} - github.com/MeesaJarJar";
 this.mainSplitContainer.Panel1.ResumeLayout(false);
 this.mainSplitContainer.Panel2.ResumeLayout(false);
 ((System.ComponentModel.ISupportInitialize)(this.mainSplitContainer)).EndInit();
 this.mainSplitContainer.ResumeLayout(false);
 this.rightSplitContainer.Panel1.ResumeLayout(false);
 this.rightSplitContainer.Panel1.PerformLayout();
 this.rightSplitContainer.Panel2.ResumeLayout(false);
 ((System.ComponentModel.ISupportInitialize)(this.rightSplitContainer)).EndInit();
 this.rightSplitContainer.ResumeLayout(false);
 ((System.ComponentModel.ISupportInitialize)(this.designPictureBox)).EndInit();
 this.controlsPanel.ResumeLayout(false);
 this.controlsPanel.PerformLayout();
 ((System.ComponentModel.ISupportInitialize)(this.widthNumericUpDown)).EndInit();
 ((System.ComponentModel.ISupportInitialize)(this.heightNumericUpDown)).EndInit();
 this.lockPanel.ResumeLayout(false);
 this.unlockedGroup.ResumeLayout(false);
 this.lockedGroup.ResumeLayout(false);
 this.ResumeLayout(false);
 // Compose groups: add tool panels and list boxes
 this.unlockedGroup.Controls.Clear();
 this.unlockedGroup.Controls.Add(this.unlockedListBox);
 this.unlockedGroup.Controls.Add(this.unlockedToolPanel);
 this.lockedGroup.Controls.Clear();
 this.lockedGroup.Controls.Add(this.lockedListBox);
 this.lockedGroup.Controls.Add(this.lockedToolPanel);
 // Compose right panel
 this.lockPanel.Controls.Clear();
 this.lockPanel.Controls.Add(this.lockedGroup);
 this.lockPanel.Controls.Add(this.unlockedGroup);
 }

 #endregion

 private System.Windows.Forms.SplitContainer mainSplitContainer;
 private System.Windows.Forms.SplitContainer rightSplitContainer;
 private System.Windows.Forms.Panel palettePanel;
 private System.Windows.Forms.ListView paletteListView;
 private System.Windows.Forms.TextBox searchTextBox;
 private System.Windows.Forms.FlowLayoutPanel paletteFlowLayoutPanel;
 private System.Windows.Forms.PictureBox designPictureBox;
 private System.Windows.Forms.Panel controlsPanel;
 private System.Windows.Forms.Label widthLabel;
 private System.Windows.Forms.NumericUpDown widthNumericUpDown;
 private System.Windows.Forms.Label heightLabel;
 private System.Windows.Forms.NumericUpDown heightNumericUpDown;
 private System.Windows.Forms.Button createGridButton;
 private System.Windows.Forms.Button zUpButton;
 private System.Windows.Forms.Button zDownButton;
 private System.Windows.Forms.Button deleteButton;
 private System.Windows.Forms.Button exportButton;
 private System.Windows.Forms.Button browseButton;
 private System.Windows.Forms.TextBox outputTextBox;
 private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog;
 private System.Windows.Forms.ImageList imageList;
 private System.Windows.Forms.ColumnHeader columnHeaderItem;
 private System.Windows.Forms.Panel lockPanel;
 private System.Windows.Forms.GroupBox unlockedGroup;
 private System.Windows.Forms.GroupBox lockedGroup;
 private System.Windows.Forms.ListBox unlockedListBox;
 private System.Windows.Forms.ListBox lockedListBox;
  private System.Windows.Forms.Button layerUpButton;
  private System.Windows.Forms.Button layerDownButton;
  private System.Windows.Forms.Button layerTopButton;
  private System.Windows.Forms.Button layerBottomButton;
  private System.Windows.Forms.Panel unlockedToolPanel;
  private System.Windows.Forms.Button lockSelectedButton;
  private System.Windows.Forms.Panel lockedToolPanel;
  private System.Windows.Forms.Button unlockSelectedButton;
  private System.Windows.Forms.Button addTrainingButton;
  private System.Windows.Forms.Button generateButton;
  private System.Windows.Forms.Button clearTrainingButton;
  private System.Windows.Forms.Button exportCanvasButton;
  private System.Windows.Forms.Panel aiGeneratorPanel;
  private System.Windows.Forms.Panel paletteSizeFilterPanel;
  private System.Windows.Forms.Panel paletteInfoPanel;
  private System.Windows.Forms.CheckBox showEmptySlotsCheckBox;
  }
}
