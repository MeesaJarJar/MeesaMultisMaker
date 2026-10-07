using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    /// <summary>
    /// Painter form for creating and editing images with layers support.
    /// 
    /// This is a partial class split across multiple files:
    /// - PainterForm.cs: Constructor, events, dispose
    /// - PainterForm.Fields.cs (this file): Field declarations and nested types
    /// - PainterForm.UI.cs: InitializeComponent and UI setup
    /// - PainterForm.Drawing.cs: Drawing tools and paint operations
    /// - PainterForm.Layers.cs: Layer management and undo/redo
    /// </summary>
    public partial class PainterForm : Form
    {
        public event Action<Bitmap, Bitmap> ImageReady;
        public event Action<Bitmap, Bitmap> ImageReadyForGump;
        public event Action<Bitmap, Bitmap, bool> ImageReadyForMapEditor;
        public event Action<Bitmap, Bitmap> ImageReadyFor3D;

        private PictureBox canvasBox;
        private Bitmap canvasBmp; // used as temporary composite when needed
        private Bitmap maskBmp = null; // kept for compatibility but layers are primary
        private Color currentColor = Color.Black;
        private Color backgroundColor = Color.White;
        private int brushSize = 16;
        private bool drawing = false;
        private bool drawMask = false;
        private Point lastPt;
        private ToolMode currentTool = ToolMode.Brush;

        // Mouse preview
        private Point mousePos;
        private bool mouseOverCanvas = false;

        // Layers
        private class Layer
        {
            public Bitmap Image;
            public Bitmap Mask;
            public Bitmap SourceImage;
            public PointF SourceOffset;
            public float SourceScale = 1.0f;
            public string Name;
            public bool Locked;
            public Bitmap PreEditImage;
            public bool Visible = true;
            public float Opacity = 1.0f;
        }
        private List<Layer> layers = new List<Layer>();
        private int activeLayerIndex = 0;
        private bool suppressLayerListEvents = false;
        private float zoom = 1.0f;

        // Per-layer multi-level undo stacks
        private List<Stack<Bitmap>> layerUndoImage = new List<Stack<Bitmap>>();
        private List<Stack<Bitmap>> layerUndoMask = new List<Stack<Bitmap>>();
        private const int UNDO_DEPTH = 20;

        // UI Controls - Toolbar
        private Panel toolbar;
        private Panel canvasContainer;
        private ComboBox sizeCombo;
        private Button colorBtn;
        private Button bgColorBtn;
        private Button maskToggleBtn;
        private TrackBar sizeTrack;
        private Label sizeLabel;
        private Button brushBtn;
        private Button pencilBtn;
        private Button eraserBtn;
        private Button fillBtn;
        private Button lineBtn;
        private Button rectBtn;
        private Button circleBtn;
        private Button moveBtn;
        private Button selectBtn;
        private Button clearBtn;
        private Button undoBtn;
        private Button saveBtn;
        private Button loadBtn;
        private Button copyBtn;
        private Button cutBtn;
        private Button removeBgBtn;
        private Button sendBtn;
        private Button sendMapUnderlayBtn;
        private Button sendMapOverlayBtn;
        private CheckBox fillShapeCheck;

        // Layers UI
        private Panel layersPanel;
        private ListBox layersListBox;
        private Button addLayerBtn;
        private Button deleteLayerBtn;
        private Button lockLayerBtn;
        private Button pasteBtn;
        private Button layerUpBtn;
        private Button layerDownBtn;
        private Button renameLayerBtn;
        private Button visLayerBtn;
        private TrackBar opacityTrack;
        private Controls.ImageEditingPanel imageEditPanel;

        // Shape drawing state
        private Point shapeStart;
        private bool drawingShape = false;

        // Move tool state
        private bool movingLayer = false;
        private Point moveStart;
        private PointF moveStartSourceOffset;
        private Bitmap moveStartImage;
        private Bitmap moveStartMask;

        // Selection state
        private bool isSelecting = false;
        private bool hasSelection = false;
        private Point selectionStart;
        private Rectangle selectionRect = Rectangle.Empty;

        private enum ToolMode
        {
            Brush,
            Pencil,
            Eraser,
            Fill,
            Line,
            Rectangle,
            Circle,
            Move,
            Select
        }
    }
}
