using System.Collections.Generic;

namespace MeesaMultisMaker
{
    /// <summary>
    /// Manages undo/redo stacks for map editing operations.
    /// Supports configurable maximum depth with oldest-first eviction.
    /// </summary>
    public class UndoRedoManager
    {
        private readonly List<MapAction> _undoStack = new List<MapAction>();
        private readonly List<MapAction> _redoStack = new List<MapAction>();
        private readonly int _maxDepth;

        public UndoRedoManager(int maxDepth = 200)
        {
            _maxDepth = maxDepth;
        }

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;
        public int UndoCount => _undoStack.Count;
        public int RedoCount => _redoStack.Count;

        public string UndoDescription =>
            CanUndo ? _undoStack[_undoStack.Count - 1].Description : null;

        public string RedoDescription =>
            CanRedo ? _redoStack[_redoStack.Count - 1].Description : null;

        /// <summary>
        /// Record a completed action for future undo.
        /// Clears the redo stack (new actions invalidate redo history).
        /// </summary>
        public void RecordAction(MapAction action)
        {
            if (action == null || !action.HasChanges) return;

            _undoStack.Add(action);
            _redoStack.Clear();

            // Evict oldest entries when exceeding max depth
            while (_undoStack.Count > _maxDepth)
                _undoStack.RemoveAt(0);
        }

        /// <summary>
        /// Pop the most recent action from the undo stack and push it
        /// onto the redo stack.  The caller must apply the undo.
        /// </summary>
        public MapAction Undo()
        {
            if (!CanUndo) return null;
            int last = _undoStack.Count - 1;
            var action = _undoStack[last];
            _undoStack.RemoveAt(last);
            _redoStack.Add(action);
            return action;
        }

        /// <summary>
        /// Pop the most recent action from the redo stack and push it
        /// back onto the undo stack.  The caller must re-apply the action.
        /// </summary>
        public MapAction Redo()
        {
            if (!CanRedo) return null;
            int last = _redoStack.Count - 1;
            var action = _redoStack[last];
            _redoStack.RemoveAt(last);
            _undoStack.Add(action);
            return action;
        }

        /// <summary>
        /// Clear all undo and redo history.
        /// </summary>
        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }
    }
}
