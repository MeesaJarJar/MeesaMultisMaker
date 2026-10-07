using System;
using System.Collections.Generic;
using System.Linq;

namespace MeesaMultisMaker
{
    public partial class Form1
    {
        private void ZUpButton_Click(object sender, EventArgs e)
        {
            var targets = selectedObjects.Count > 0 ? selectedObjects.Where(o => !o.Hidden).ToList() : (selectedObject != null && !selectedObject.Hidden ? new List<PlacedObject> { selectedObject } : null);
            if (targets == null || targets.Count == 0) return;
            PushUndo();
            foreach (var obj in targets) obj.Z += 1;
            designPictureBox.Invalidate();
        }

        private void ZDownButton_Click(object sender, EventArgs e)
        {
            var targets = selectedObjects.Count > 0 ? selectedObjects.Where(o => !o.Hidden).ToList() : (selectedObject != null && !selectedObject.Hidden ? new List<PlacedObject> { selectedObject } : null);
            if (targets == null || targets.Count == 0) return;
            PushUndo();
            foreach (var obj in targets) obj.Z -= 1;
            designPictureBox.Invalidate();
        }

        private void MoveLayer(int delta)
        {
            var selection = selectedObjects.Count > 0 ? selectedObjects.Where(o => !o.Hidden).ToHashSet() : (selectedObject != null && !selectedObject.Hidden ? new HashSet<PlacedObject> { selectedObject } : null);
            if (selection == null || selection.Count == 0) return;
            PushUndo();
            var ordered = placedObjects.OrderBy(o => o.Layer).ToList();
            // Build index map
            var indexByObj = new Dictionary<PlacedObject, int>();
            for (int i = 0; i < ordered.Count; i++) indexByObj[ordered[i]] = i;
            if (delta > 0)
            {
                for (int i = ordered.Count - 2; i >= 0; i--)
                {
                    var obj = ordered[i];
                    if (!selection.Contains(obj)) continue;
                    if (i + 1 >= ordered.Count) continue;
                    if (selection.Contains(ordered[i + 1])) continue; // keep relative order of selection
                                                                      // swap i and i+1
                    var tmp = ordered[i + 1]; ordered[i + 1] = ordered[i]; ordered[i] = tmp;
                }
            }
            else if (delta < 0)
            {
                for (int i = 1; i < ordered.Count; i++)
                {
                    var obj = ordered[i];
                    if (!selection.Contains(obj)) continue;
                    if (i - 1 < 0) continue;
                    if (selection.Contains(ordered[i - 1])) continue;
                    var tmp = ordered[i - 1]; ordered[i - 1] = ordered[i]; ordered[i] = tmp;
                }
            }
            // write back contiguous layers
            for (int i = 0; i < ordered.Count; i++) ordered[i].Layer = i;
            RebuildLockLists();
            designPictureBox.Invalidate();
        }

        private void LayerUpButton_Click(object sender, EventArgs e) => MoveLayer(+1);
        private void LayerDownButton_Click(object sender, EventArgs e) => MoveLayer(-1);

        private void LayerTopButton_Click(object sender, EventArgs e)
        {
            var selection = selectedObjects.Count > 0 ? selectedObjects.Where(o => !o.Hidden).ToHashSet() : (selectedObject != null && !selectedObject.Hidden ? new HashSet<PlacedObject> { selectedObject } : null);
            if (selection == null || selection.Count == 0) return;
            PushUndo();

            // Get all objects ordered by layer
            var ordered = placedObjects.OrderBy(o => o.Layer).ToList();

            // Separate selected and non-selected objects
            var nonSelected = ordered.Where(o => !selection.Contains(o)).ToList();
            var selected = ordered.Where(o => selection.Contains(o)).ToList();

            // Rebuild: non-selected first, then selected (selected go to top)
            var newOrder = nonSelected.Concat(selected).ToList();

            // Reassign contiguous layers
            for (int i = 0; i < newOrder.Count; i++) newOrder[i].Layer = i;

            RebuildLockLists();
            designPictureBox.Invalidate();
        }

        private void LayerBottomButton_Click(object sender, EventArgs e)
        {
            var selection = selectedObjects.Count > 0 ? selectedObjects.Where(o => !o.Hidden).ToHashSet() : (selectedObject != null && !selectedObject.Hidden ? new HashSet<PlacedObject> { selectedObject } : null);
            if (selection == null || selection.Count == 0) return;
            PushUndo();

            // Get all objects ordered by layer
            var ordered = placedObjects.OrderBy(o => o.Layer).ToList();

            // Separate selected and non-selected objects
            var nonSelected = ordered.Where(o => !selection.Contains(o)).ToList();
            var selected = ordered.Where(o => selection.Contains(o)).ToList();

            // Rebuild: selected first, then non-selected (selected go to bottom)
            var newOrder = selected.Concat(nonSelected).ToList();

            // Reassign contiguous layers
            for (int i = 0; i < newOrder.Count; i++) newOrder[i].Layer = i;

            RebuildLockLists();
            designPictureBox.Invalidate();
        }

        private void DeleteButton_Click(object sender, EventArgs e)
        {
            var targets = selectedObjects.Count > 0 ? selectedObjects.ToList() : (selectedObject != null ? new List<PlacedObject> { selectedObject } : null);
            if (targets == null || targets.Count == 0) return;
            PushUndo();
            foreach (var obj in targets)
            {
                placedObjects.Remove(obj);
                lockedObjects.Remove(obj);
            }
            selectedObjects.Clear();
            selectedObject = null;
            // compact layers
            var reOrdered = placedObjects.OrderBy(o => o.Layer).ToList();
            for (int i = 0; i < reOrdered.Count; i++) reOrdered[i].Layer = i;
            RebuildLockLists();
            designPictureBox.Invalidate();
        }

        private void RemoveDuplicateObjects()
        {
            if (placedObjects.Count == 0) return;

            PushUndo();

            var seen = new HashSet<string>();
            var toRemove = new List<PlacedObject>();

            foreach (var obj in placedObjects)
            {
                if (!seen.Add(obj.GraphicId))
                {
                    toRemove.Add(obj);
                }
            }

            foreach (var obj in toRemove)
            {
                placedObjects.Remove(obj);
                lockedObjects.Remove(obj);
                if (obj == selectedObject) selectedObject = null;
                selectedObjects.Remove(obj);
            }

            var reOrdered = placedObjects.OrderBy(o => o.Layer).ToList();
            for (int i = 0; i < reOrdered.Count; i++) reOrdered[i].Layer = i;

            RebuildLockLists();
            designPictureBox.Invalidate();
            outputTextBox.AppendText($"Removed {toRemove.Count} duplicate(s). {placedObjects.Count} objects remaining.\r\n");
        }
    }
}
