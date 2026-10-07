using System.Linq;
using System.Windows.Forms;

namespace MeesaMultisMaker
{
    public partial class Form1
    {
        private bool _isSyncingSelection = false;

        private void SelectFromList(ListBox list)
        {
            if (list == null) return;
            if (_isSyncingSelection) return; // Prevent recursion

            // Sync canvas selection with list selection (multi-select supported)
            selectedObjects.Clear();
            foreach (var item in list.SelectedItems)
            {
                var obj = item as PlacedObject;
                if (obj != null) selectedObjects.Add(obj);
            }
            selectedObject = selectedObjects.FirstOrDefault();
            designPictureBox.Invalidate();
        }

        private void ToggleLockFromList(ListBox list)
        {
            if (list.SelectedItems.Count == 0) return;
            var items = list.SelectedItems.Cast<object>().OfType<PlacedObject>().ToList();
            bool locking = ReferenceEquals(list, unlockedListBox);
            foreach (var entry in items)
            {
                if (locking) lockedObjects.Add(entry); else lockedObjects.Remove(entry);
            }
            RebuildLockLists();
            // Maintain selection after moving: select moved items in target list and reflect on canvas
            if (locking)
            {
                lockedListBox.ClearSelected();
                foreach (var e in items)
                {
                    int idx = lockedListBox.Items.IndexOf(e);
                    if (idx >= 0) lockedListBox.SetSelected(idx, true);
                }
                SelectFromList(lockedListBox);
            }
            else
            {
                unlockedListBox.ClearSelected();
                foreach (var e in items)
                {
                    int idx = unlockedListBox.Items.IndexOf(e);
                    if (idx >= 0) unlockedListBox.SetSelected(idx, true);
                }
                SelectFromList(unlockedListBox);
            }
            designPictureBox.Invalidate();
        }

        private void RebuildLockLists()
        {
            var unlocked = placedObjects.Where(o => !lockedObjects.Contains(o)).OrderBy(o => o.Layer).ThenBy(o => o.Z).ToList();
            var locked = placedObjects.Where(o => lockedObjects.Contains(o)).OrderBy(o => o.Layer).ThenBy(o => o.Z).ToList();
            unlockedListBox.BeginUpdate(); lockedListBox.BeginUpdate();
            unlockedListBox.Items.Clear(); lockedListBox.Items.Clear();
            foreach (var o in unlocked) unlockedListBox.Items.Add(o);
            foreach (var o in locked) lockedListBox.Items.Add(o);
            unlockedListBox.EndUpdate(); lockedListBox.EndUpdate();

            // Auto-sync listbox selection to match canvas selection
            SyncListBoxSelections();
        }

        private void SyncListBoxSelections()
        {
            if (_isSyncingSelection) return; // Prevent recursion

            try
            {
                _isSyncingSelection = true;

                // Make a copy to avoid collection modification during enumeration
                var selectedCopy = selectedObjects.ToList();

                unlockedListBox.BeginUpdate();
                lockedListBox.BeginUpdate();

                unlockedListBox.ClearSelected();
                lockedListBox.ClearSelected();

                foreach (var obj in selectedCopy)
                {
                    // Find the object in either unlocked or locked list
                    int unlockedIdx = unlockedListBox.Items.IndexOf(obj);
                    if (unlockedIdx >= 0)
                    {
                        unlockedListBox.SetSelected(unlockedIdx, true);
                    }
                    else
                    {
                        int lockedIdx = lockedListBox.Items.IndexOf(obj);
                        if (lockedIdx >= 0)
                        {
                            lockedListBox.SetSelected(lockedIdx, true);
                        }
                    }
                }

                unlockedListBox.EndUpdate();
                lockedListBox.EndUpdate();
            }
            finally
            {
                _isSyncingSelection = false;
            }
        }
    }
}
