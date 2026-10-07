using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    public partial class GumpEditorForm
    {
        // Layer management
        private void AddLayer(string name = null)
        {
            var layer = new Layer { Name = name ?? $"Layer {layers.Count}" };
            layers.Add(layer);
            activeLayerIndex = layers.Count - 1;
            RefreshLayersList();
        }

        private void DeleteLayer(int index)
        {
            if (index < 0 || index >= layers.Count) return;
            if (layers.Count <= 1) return;

            PushUndo();

            var layer = layers[index];
            foreach (var gump in layer.Gumps)
            {
                placedGumps.Remove(gump);
                gump.Image?.Dispose();
            }
            layers.RemoveAt(index);
            activeLayerIndex = Math.Max(0, Math.Min(activeLayerIndex, layers.Count - 1));

            RefreshLayersList();
            canvasBox.Invalidate();
        }

        private void MoveLayer(int delta)
        {
            int newIndex = activeLayerIndex + delta;
            if (newIndex < 0 || newIndex >= layers.Count) return;

            var temp = layers[activeLayerIndex];
            layers[activeLayerIndex] = layers[newIndex];
            layers[newIndex] = temp;
            activeLayerIndex = newIndex;

            RefreshLayersList();
            canvasBox.Invalidate();
        }

        private void ToggleLayerVisibility()
        {
            if (activeLayerIndex < 0 || activeLayerIndex >= layers.Count) return;
            layers[activeLayerIndex].Visible = !layers[activeLayerIndex].Visible;
            RefreshLayersList();
            canvasBox.Invalidate();
        }

        private void ToggleLayerLock()
        {
            if (activeLayerIndex < 0 || activeLayerIndex >= layers.Count) return;
            layers[activeLayerIndex].Locked = !layers[activeLayerIndex].Locked;
            RefreshLayersList();
        }

        private void RefreshLayersList()
        {
            suppressLayerListEvents = true;
            try
            {
                layersListBox.Items.Clear();
                for (int i = 0; i < layers.Count; i++)
                {
                    var layer = layers[i];
                    string prefix = i == activeLayerIndex ? "> " : "  ";
                    string suffix = "";
                    if (!layer.Visible) suffix += " (H)";
                    if (layer.Locked) suffix += " (L)";
                    layersListBox.Items.Add($"{prefix}{layer.Name}{suffix}");
                }
                if (activeLayerIndex >= 0 && activeLayerIndex < layersListBox.Items.Count)
                    layersListBox.SelectedIndex = activeLayerIndex;
            }
            finally
            {
                suppressLayerListEvents = false;
            }
        }

        private void LayersListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (suppressLayerListEvents) return;
            if (layersListBox.SelectedIndex >= 0)
            {
                activeLayerIndex = layersListBox.SelectedIndex;
                RefreshLayersList();
            }
        }

        private void MoveSelectedToLayer(int delta)
        {
            if (selectedGump == null) return;

            int currentLayer = selectedGump.LayerIndex;
            int newLayer = currentLayer + delta;

            if (newLayer < 0 || newLayer >= layers.Count) return;
            if (layers[newLayer].Locked) return;

            PushUndo();

            layers[currentLayer].Gumps.Remove(selectedGump);
            layers[newLayer].Gumps.Add(selectedGump);
            selectedGump.LayerIndex = newLayer;

            canvasBox.Invalidate();
            SetStatus($"Moved GUMP to {layers[newLayer].Name}");
        }

        private void DeleteSelected()
        {
            if (selectedGumps.Count == 0) return;

            PushUndo();

            foreach (var gump in selectedGumps.ToList())
            {
                var layer = layers[gump.LayerIndex];
                layer.Gumps.Remove(gump);
                placedGumps.Remove(gump);
                gump.Image?.Dispose();
            }
            selectedGumps.Clear();
            selectedGump = null;

            canvasBox.Invalidate();
            SetStatus("Deleted selected GUMP(s)");
        }

        private void ClearCanvas()
        {
            if (MessageBox.Show("Clear all GUMPs from the canvas?", "Confirm Clear",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            PushUndo();

            foreach (var layer in layers)
            {
                foreach (var gump in layer.Gumps)
                {
                    gump.Image?.Dispose();
                }
                layer.Gumps.Clear();
            }
            placedGumps.Clear();
            selectedGump = null;
            selectedGumps.Clear();

            canvasBox.Invalidate();
            SetStatus("Canvas cleared");
        }

        // Undo/Redo
        private void PushUndo()
        {
            var state = placedGumps.Select(g => g.Clone()).ToList();
            undoStack.Push(state);
            redoStack.Clear();

            // Remove oldest undo states if we exceed the max
            while (undoStack.Count > MAX_UNDO)
            {
                // Convert to array to get the oldest (bottom) item
                var allStates = undoStack.ToArray();
                
                // Clear and rebuild the stack without the oldest item
                undoStack.Clear();
                // Push back in reverse order (skip the last one which is the oldest)
                for (int i = allStates.Length - 2; i >= 0; i--)
                {
                    undoStack.Push(allStates[i]);
                }
                
                // Dispose the removed state's images
                var oldest = allStates[allStates.Length - 1];
                foreach (var g in oldest)
                {
                    g.Image?.Dispose();
                }
            }
        }

        private void Undo()
        {
            if (undoStack.Count == 0)
            {
                SetStatus("Nothing to undo");
                return;
            }

            var current = placedGumps.Select(g => g.Clone()).ToList();
            redoStack.Push(current);

            var state = undoStack.Pop();
            RestoreState(state);
            SetStatus("Undo");
        }

        private void Redo()
        {
            if (redoStack.Count == 0)
            {
                SetStatus("Nothing to redo");
                return;
            }

            var current = placedGumps.Select(g => g.Clone()).ToList();
            undoStack.Push(current);

            var state = redoStack.Pop();
            RestoreState(state);
            SetStatus("Redo");
        }

        private void RestoreState(List<PlacedGump> state)
        {
            // Dispose current gump images
            foreach (var gump in placedGumps)
            {
                gump.Image?.Dispose();
            }
            placedGumps.Clear();

            // Clear layer gump lists
            foreach (var layer in layers)
            {
                layer.Gumps.Clear();
            }

            // Restore gumps from state
            foreach (var gump in state)
            {
                // Ensure layer index is valid
                if (gump.LayerIndex < 0 || gump.LayerIndex >= layers.Count)
                {
                    gump.LayerIndex = 0;
                }
                
                if (gump.LayerIndex >= 0 && gump.LayerIndex < layers.Count)
                {
                    layers[gump.LayerIndex].Gumps.Add(gump);
                }
                placedGumps.Add(gump);
            }

            selectedGump = null;
            selectedGumps.Clear();
            canvasBox.Invalidate();
        }
    }
}
