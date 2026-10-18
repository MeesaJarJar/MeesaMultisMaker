using MeesaMultisMaker.Mul;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    public partial class Form1
    {
        private string artFolderPath;
        private int gridWidth = 10;
        private int gridHeight = 10;

        private const int TILE_WIDTH = 44;
        private const int TILE_HEIGHT = 44;
        private const int Z_PIXEL = 4; // visual pixels per Z level

        private readonly List<PlacedObject> placedObjects = new List<PlacedObject>();
        private PlacedObject selectedObject = null; // primary
        private readonly HashSet<PlacedObject> selectedObjects = new HashSet<PlacedObject>(); // multi-select set
        private string[] allImageFiles = new string[0];
        private string[] filteredImageFiles = new string[0];
        private bool isLoading = false;

        // Changed from Dictionary<int, Image> to Dictionary<string, Image> to support both
        // MUL-based (keyed by hex ID like "0x1234") and PNG-based (keyed by index) loading
        private readonly Dictionary<string, Image> imageCache = new Dictionary<string, Image>();
        private readonly Dictionary<string, string> idToPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // TileData reader for item names and properties
        private readonly TileDataReader tileDataReader = new TileDataReader();

        // Hue color palettes (hues.mul) — mirrors MapViewerForm.cs:133-134
        private readonly HuesReader huesReader = new HuesReader();
        private string loadedHuesFolder = null;

        // Hued bitmap cache keyed by (artId, hueId, partial) — mirrors MapViewerForm.Rendering.cs:285-293
        private readonly Dictionary<(int artId, int hueId, bool partial), Bitmap> huedArtCache
            = new Dictionary<(int, int, bool), Bitmap>();

        // Tooltip for showing TileData properties on hover
        private ToolTip paletteToolTip;
        private int lastHoveredIndex = -1;

        // Info panel controls for selected item display
        private PictureBox paletteInfoPreview;
        private Label paletteInfoNameLabel;
        private Label paletteInfoIdLabel;
        private Label paletteInfoPropertiesLabel;
        private Label paletteInfoFlagsLabel;
        private int? selectedPaletteItemId = null;

        private bool isDragging = false;
        private Point dragStartPoint;
        private Point canvasOffset = new Point(200, 50); // pan offset in screen pixels
        private float zoom = 1.0f; // zoom factor

        // Panning
        private bool isPanning = false;
        private Point panDragStart;
        private Point panOffsetStart;

        // Group-drag helpers
        private readonly Dictionary<PlacedObject, Point> dragStartGridByObject = new Dictionary<PlacedObject, Point>();
        private Point anchorStartGrid; // grid of the primary at drag start

        // Marquee selection
        private bool isMarquee = false;
        private bool marqueeDeselect = false; // ALT marquee
        private Point marqueeStart;
        private Rectangle marqueeRect;

        // Clipboard for copy/paste
        private List<PlacedObject> clipboardObjects = null;

        // Monotonic counter for Explorer file-drop placeholder GraphicIds
        // ("drop_N"). GetUniqueDropPlaceholderId skips values already in use
        // so IDs can never collide with real hex art IDs or each other.
        private int _dropPlaceholderCounter = 1;

        private readonly Stack<List<PlacedObject>> undoStack = new Stack<List<PlacedObject>>();
        private readonly Stack<List<PlacedObject>> redoStack = new Stack<List<PlacedObject>>();
        private readonly HashSet<PlacedObject> lockedObjects = new HashSet<PlacedObject>();

        // Generation system
        private readonly List<List<PlacedObject>> trainingStructures = new List<List<PlacedObject>>();

        // Shared cancellation for AI Regen runs (STOP button + ESC).
        // Non-null while AIRegenSelected / AIRegenSelectedAsOne is in flight.
        private CancellationTokenSource _aiGenerationCts;

        // Transformation constants
        private const float SCALE_INCREMENT = 0.1f;
        private const float SCALE_MIN = 0.1f;
        private const float SCALE_MAX = 5.0f;
        private const float ROTATION_INCREMENT = 90f; // Degrees for quick rotation
        private const float ROTATION_FINE_INCREMENT = 15f; // Degrees for fine rotation (Shift+R)
        private const int PIXEL_OFFSET_INCREMENT = 1; // Pixels for fine movement
        private const int PIXEL_OFFSET_FAST = 5; // Pixels for fast movement (Shift)

        // Skew/Distort mode
        private bool isSkewMode = false; // When true, shows skew handles on selected object
        private int skewDraggingCorner = -1; // -1 = not dragging, 0-3 = corner index being dragged
        private PointF skewDragStart; // Starting position when drag began
        private PointF skewCornerStartOffset; // Original corner offset when drag began
        private const int SKEW_HANDLE_SIZE = 10; // Size of corner handles in pixels
        private const int SKEW_HANDLE_HIT_RADIUS = 12; // Hit test radius for handles

        // Skew corner indices
        private const int SKEW_CORNER_TOP_LEFT = 0;
        private const int SKEW_CORNER_TOP_RIGHT = 1;
        private const int SKEW_CORNER_BOTTOM_RIGHT = 2;
        private const int SKEW_CORNER_BOTTOM_LEFT = 3;

        // Free Rotation mode
        private bool isRotateMode = false; // When true, shows rotation handle on selected object
        private bool isRotateDragging = false; // True when actively dragging rotation handle
        private PointF rotateDragStart; // Starting position when rotation drag began
        private float rotateStartAngle; // Original rotation when drag began
        private const int ROTATE_HANDLE_DISTANCE = 50; // Distance of rotation handle from center
        private const int ROTATE_HANDLE_SIZE = 12; // Size of rotation handle in pixels
        private const int ROTATE_HANDLE_HIT_RADIUS = 14; // Hit test radius for rotation handle

        // Slice tool state
        private bool isSliceMode = false; // When true, click tiles to define slice area
        private readonly HashSet<Point> sliceTiles = new HashSet<Point>();
        private PlacedObject sliceTarget = null; // target object being sliced

        // Hide Floors toggle state (house/multi building aid)
        private bool hideFloorsActive = false;
        private Button hideFloorsButton;
        private readonly HashSet<PlacedObject> floorsHiddenByToggle = new HashSet<PlacedObject>();

        // Unify Same IDs: losing artwork variants stashed per art ID so the
        // user can re-pick (swap) the winning style later.
        private readonly Dictionary<string, List<Bitmap>> unifyCandidates =
            new Dictionary<string, List<Bitmap>>(StringComparer.OrdinalIgnoreCase);

        // Canvas context menu
        private ContextMenuStrip editedImageContextMenu;
        private ToolStripMenuItem saveToMulContextMenuItem;

        // Per-object artwork offset controls (toolbar): mirrors
        // PlacedObject.PixelOffsetX/Y of the current selection. The offsets
        // shift art on its tile non-destructively (no bitmap padding).
        private NumericUpDown offsetXNum;
        private NumericUpDown offsetYNum;
        private bool _offsetSync;
        private System.Windows.Forms.Timer _offsetTimer;

        // Canvas hue-preview controls (paint-time only; never mutates PlacedObject.Image).
        // huePreviewIdNum: 0 = off. Mode Auto passes the hue ID bare so
        // SplitRawHue falls back to TileFlag.PartialHue; Partial adds 0x8000;
        // Full passes the hue ID bare plus forceFull=true to force partial=false.
        private CheckBox huePreviewCheckBox;
        private NumericUpDown huePreviewIdNum;
        private ComboBox huePreviewModeCombo;
        private bool _hueMissingWarned;

        // Partial-hue mask overlay toggle (paint-time only; never mutates
        // PlacedObject.Image). When on, non-gray pixels are tinted magenta
        // per-frame via ImageEffects.BuildPartialHueMaskOverlay.
        private CheckBox partialHueMaskCheckBox;

        // Hue name picker (dropdown showing "id - name", still storing the
        // numeric ID in huePreviewIdNum). Falls back to bare IDs when
        // hues.mul is absent or names are empty.
        private ComboBox huePreviewNameCombo;
        private bool _hueNameSync;
    }
}
