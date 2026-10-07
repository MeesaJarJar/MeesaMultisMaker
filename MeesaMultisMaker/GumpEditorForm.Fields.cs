using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using MeesaMultisMaker.ComfyUI;
using MeesaMultisMaker.Controls;

namespace MeesaMultisMaker
{
    /// <summary>
    /// Selection mode for the canvas
    /// </summary>
    public enum SelectionMode
    {
        Single,
        Rectangle
    }

    /// <summary>
    /// GUMP Editor form - allows viewing, editing, and creating GUMP graphics.
    /// Uses a simple grid-based canvas instead of isometric view.
    /// Supports layers, gump palette browser, and AI generation.
    /// </summary>
    public partial class GumpEditorForm : Form
    {
        // Canvas settings
        private int canvasWidth = 640;
        private int canvasHeight = 480;
        private float zoom = 1.0f;
        private Point panOffset = Point.Empty;
        private bool isPanning = false;
        private Point lastPanPoint;

        // Grid settings
        private int gridSize = 44;
        private bool showGrid = true;

        // Placed gump objects on canvas
        private List<PlacedGump> placedGumps = new List<PlacedGump>();
        private PlacedGump selectedGump = null;
        private PlacedGump hoveredGump = null;
        private bool isDragging = false;
        private Point dragOffset;

        // Transform mode for mouse operations
        private enum TransformMode
        {
            None,
            Move,
            Resize,
            Rotate,
            Skew
        }
        private TransformMode currentTransformMode = TransformMode.None;
        private int resizeHandle = -1; // 0-7 for 8 handles, -1 for none
        private Point transformStart;
        private float startRotation;
        private float startScaleX;
        private float startScaleY;
        private int startWidth;
        private int startHeight;

        // Multi-selection support
        private readonly List<PlacedGump> selectedGumps = new List<PlacedGump>();
        private Point dragStart;
        private List<DragItem> dragSelection = new List<DragItem>();
        
        // Selection rectangle
        private bool isSelecting = false;
        private Point selectionStart;
        private Point selectionEnd;
        private Rectangle selectionRect;

        private struct DragItem
        {
            public PlacedGump Gump;
            public int StartX;
            public int StartY;
        }

        // Gump palette
        private List<int> validGumpIds = new List<int>();
        private List<int> filteredGumpIds = new List<int>();
        private ConcurrentDictionary<int, Bitmap> gumpCache = new ConcurrentDictionary<int, Bitmap>();
        private int selectedPaletteGump = -1;
        private CancellationTokenSource _loadingCts;
        private volatile bool _isLoading = false;
        private Dictionary<int, Bitmap> _originalGumpImages = new Dictionary<int, Bitmap>();
        private Dictionary<int, Bitmap> _regeneratedGumpImages = new Dictionary<int, Bitmap>();
        private Dictionary<int, Bitmap> _rawAIOutputImages = new Dictionary<int, Bitmap>(); // Raw AI output before black pixel removal
        private bool _showingOriginal = false; // For Old/New toggle

        // Sort controls
        private ComboBox sortComboBox;
        private CheckBox sortDescCheckBox;

        // Cached gump dimensions for sorting (avoid repeated file I/O)
        private Dictionary<int, Size> _gumpDimensionCache = new Dictionary<int, Size>();

        // MUL folder path
        private string mulFolder;

        // Layers
        private class Layer
        {
            public string Name { get; set; }
            public bool Visible { get; set; } = true;
            public bool Locked { get; set; } = false;
            public float Opacity { get; set; } = 1.0f;
            public List<PlacedGump> Gumps { get; set; } = new List<PlacedGump>();
        }
        private List<Layer> layers = new List<Layer>();
        private int activeLayerIndex = 0;
        private bool suppressLayerListEvents = false;

        // Undo/Redo
        private Stack<List<PlacedGump>> undoStack = new Stack<List<PlacedGump>>();
        private Stack<List<PlacedGump>> redoStack = new Stack<List<PlacedGump>>();
        private const int MAX_UNDO = 20;

        // UI Controls
        private SplitContainer mainSplit;
        private Panel palettePanel;
        private ListView gumpListView;
        private TextBox searchBox;
        private Panel canvasPanel;
        private PictureBox canvasBox;
        private Panel controlsPanel;
        private Panel layersPanel;
        private ListBox layersListBox;
        private Panel statusPanel;
        private Label statusLabel;
        private ImageList gumpImageList;
        private ToolTip tooltip;

        // Controls panel elements
        private NumericUpDown canvasWidthUpDown;
        private NumericUpDown canvasHeightUpDown;
        private NumericUpDown gridSizeUpDown;
        private CheckBox showGridCheckBox;
        private Button deleteBtn;
        private Button layerUpBtn;
        private Button layerDownBtn;
        private Button exportBtn;
        
        // AI Generation - use the same AISettingsPanel as main form
        private AISettingsPanel aiSettingsPanel;
        private ImageEditingPanel imageEditingPanel;
        private TabControl rightToolsTabs;
        private ComfyUIClient _comfyClient;
        private CancellationTokenSource _gumpGenCts;
        private MapViewerForm _mapViewerBridge;

        // Minimum size for AI generation
        private const int MIN_AI_SIZE = 512;
    }
}
