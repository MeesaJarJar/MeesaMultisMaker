using MeesaMultisMaker.Mul;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    /// <summary>
    /// MUL Viewer form for viewing and generating multi structures.
    /// 
    /// This is a partial class split across multiple files:
    /// - MulViewerForm.cs: Constructor, core methods, entry loading
    /// - MulViewerForm.Fields.cs (this file): Field declarations and nested types
    /// - MulViewerForm.UI.cs: InitializeComponent and UI setup
    /// - MulViewerForm.Preview.cs: Preview painting, zoom, and pan
    /// - MulViewerForm.Generation.cs: WFC generation and pattern building
    /// </summary>
    public partial class MulViewerForm
    {
        private const string DefaultUOPath = @"C:\Program Files (x86)\UOForever\UO";

        // UI Controls
        private TextBox pathBox;
        private ListBox multiList;
        private ListView partsView;
        private ColumnHeader colId, colX, colY, colZ, colFlags;
        private TextBox exportBox;
        private Button copyBtn;
        private Button sendToCanvasBtn;
        private Button validateBtn;
        private CheckBox legacyCheck;
        private CheckBox normalizeCheck;
        private SplitContainer split;
        private SplitContainer splitRight;
        private PictureBox previewBox;
        private StatusStrip status;
        private ToolStripStatusLabel statusLabel;
        private List<MultiEntry> entries;
        private ToolTip mulViewerTooltip;

        // Generation controls
        private NumericUpDown genWidth;
        private NumericUpDown genHeight;
        private TrackBar genSimilarity;
        private Label kernelLabel;
        private NumericUpDown genKernel;
        private Button genButton;
        private Button viewLogBtn;
        private Label epochsLabel;
        private NumericUpDown genEpochs;
        private Label lrLabel;
        private NumericUpDown genLr;
        private CheckBox chkRequireAllClasses;
        private Panel genPanel;
        private CheckBox useWfc;
        private CheckBox showArtCheck;
        private CheckBox hideEmptyCheck;

        // Art image cache
        private readonly Dictionary<int, Image> artCache = new Dictionary<int, Image>();
        private string currentMulFolder;

        // Generated content for preview
        private List<MultiComponent> generatedComponents = null;
        private bool showingGenerated = false;

        // Zoom controls
        private float zoomLevel = 1.0f;
        private const float ZOOM_MIN = 0.1f;
        private const float ZOOM_MAX = 5.0f;
        private const float ZOOM_STEP = 0.1f;
        private Panel zoomPanel;
        private Label zoomLabel;
        private Button zoomInBtn;
        private Button zoomOutBtn;
        private Button zoomResetBtn;

        // Pan controls
        private float panOffsetX = 0f;
        private float panOffsetY = 0f;
        private bool isPanning = false;
        private Point panStartPoint;
        private float panStartOffsetX;
        private float panStartOffsetY;

        // Nested types for generation
        private struct TileZ
        {
            public int TileId;
            public short Z;
        }

        private class Dataset
        {
            public List<int[,]> grids = new List<int[,]>();
            public List<List<TileZ>[,]> columns = new List<List<TileZ>[,]>();
        }

        private class ColumnPattern : IEquatable<ColumnPattern>
        {
            public List<TileZ> Items { get; set; } = new List<TileZ>();

            public bool Equals(ColumnPattern other)
            {
                if (other == null) return false;
                if (Items.Count != other.Items.Count) return false;
                for (int i = 0; i < Items.Count; i++)
                {
                    if (Items[i].TileId != other.Items[i].TileId || Items[i].Z != other.Items[i].Z)
                        return false;
                }
                return true;
            }

            public override bool Equals(object obj) => Equals(obj as ColumnPattern);

            public override int GetHashCode()
            {
                int hash = 17;
                foreach (var item in Items)
                    hash = hash * 31 + item.TileId.GetHashCode() + item.Z.GetHashCode() * 7;
                return hash;
            }
        }

        private class ColumnRegistry
        {
            private Dictionary<ColumnPattern, int> _patternToId = new Dictionary<ColumnPattern, int>();
            private List<ColumnPattern> _idToPattern = new List<ColumnPattern>();

            public int GetOrCreateId(ColumnPattern pattern)
            {
                if (_patternToId.TryGetValue(pattern, out int id))
                    return id;

                id = _idToPattern.Count;
                _patternToId[pattern] = id;
                _idToPattern.Add(pattern);
                return id;
            }

            public int Count => _idToPattern.Count;

            public ColumnPattern GetById(int id)
            {
                if (id >= 0 && id < _idToPattern.Count)
                    return _idToPattern[id];
                return new ColumnPattern();
            }
        }

        private class Sample
        {
            public int[] Ctx;
            public int Y;
        }

        private class SimpleCnn
        {
            public SimpleCnn(List<int> classIds, int ctxSize) { }
            public void Train(List<Sample> samples, int epochs, double lr, int? seed) { }
            public void BuildPriorsFromSamples(List<Sample> samples) { }
            public void SetAdjacency(Dictionary<int, HashSet<int>> rightOf, Dictionary<int, HashSet<int>> belowOf) { }
            public double[] PredictProbaEval(int[] ctx) => new double[] { 1.0 };
            public int[,] Generate(int w, int h, int seed, double temperature, double mixWithPriors, double topP)
            {
                return new int[w, h];
            }
        }

        private class ListBoxEntryItem
        {
            public MultiEntry Entry { get; }
            public int Width { get; }
            public int Height { get; }

            public ListBoxEntryItem(MultiEntry entry, int w, int h)
            {
                Entry = entry;
                Width = w;
                Height = h;
            }

            public override string ToString()
            {
                return $"[{Entry.Index}] {Entry.Components.Count} parts (offset {Entry.Offset}, len {Entry.Length}) {Width}x{Height}";
            }
        }
    }
}
