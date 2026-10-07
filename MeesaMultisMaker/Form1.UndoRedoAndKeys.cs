using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;

namespace MeesaMultisMaker
{
 public partial class Form1
 {
  private void PushUndo()
  {
  var snapshot = placedObjects.Select(o => o.CloneForUndo()).ToList();
  undoStack.Push(snapshot);
  // Cap depth: drop oldest entries (bottom of stack) and free their bitmaps
  while (undoStack.Count > 50)
  {
  var tmp = new List<List<PlacedObject>>();
  while (undoStack.Count > 0) tmp.Add(undoStack.Pop());
  var oldest = tmp[tmp.Count - 1];
  tmp.RemoveAt(tmp.Count - 1);
  for (int i = tmp.Count - 1; i >= 0; i--) undoStack.Push(tmp[i]);
  DisposeSnapshot(oldest);
  }
  redoStack.Clear();
  }

  private static void DisposeSnapshot(System.Collections.Generic.List<PlacedObject> snapshot)
  {
  if (snapshot == null) return;
  foreach (var o in snapshot)
  {
  if (o == null) continue;
  try { o.Image?.Dispose(); } catch { }
  try { o.OriginalImage?.Dispose(); } catch { }
  try { o.PreEditImage?.Dispose(); } catch { }
  }
  }

  private void RestoreState(System.Collections.Generic.List<PlacedObject> snapshot)
  {
  // Remember locks by identity so they survive the ref rebuild below
  var lockedKeys = new HashSet<string>(lockedObjects.Where(o => o != null).Select(o => (o.GraphicId ?? "") + "|" + o.GridX + "," + o.GridY + "," + o.Z + "," + o.Layer));
  // Clear current selection before restoring state
  selectedObjects.Clear();
  selectedObject = null;
  isSkewMode = false;
  isRotateMode = false;
  // Drop stale drag/marquee state so undo never resumes a dead gesture
  isDragging = false;
  isPanning = false;
  isMarquee = false;
  marqueeRect = Rectangle.Empty;

  placedObjects.Clear();
  placedObjects.AddRange(snapshot.Select(o => o.CloneForUndo()));
  lockedObjects.Clear();
  foreach (var o in placedObjects)
  {
  string key = (o.GraphicId ?? "") + "|" + o.GridX + "," + o.GridY + "," + o.Z + "," + o.Layer;
  if (lockedKeys.Contains(key)) lockedObjects.Add(o);
  }
  RebuildLockLists();
  designPictureBox.Invalidate();
  }

 /// <summary>
 /// Checks if the currently focused control is a text input control (TextBox, ComboBox, etc.)
 /// If so, we should not intercept keyboard shortcuts meant for typing.
 /// </summary>
 private bool IsTextInputFocused()
 {
 var focused = GetFocusedControl(this);
 if (focused == null) return false;
 
 // Check if focused control is a text input type
 if (focused is TextBox || focused is ComboBox || focused is NumericUpDown || focused is RichTextBox)
 return true;
 
 return false;
 }
 
 /// <summary>
 /// Recursively finds the currently focused control
 /// </summary>
 private Control GetFocusedControl(Control parent)
 {
 if (parent == null) return null;
 
 var container = parent as IContainerControl;
 if (container != null)
 {
 var active = container.ActiveControl;
 if (active != null)
 {
 return GetFocusedControl(active);
 }
 }
 
 return parent.Focused ? parent : null;
 }

 private void Form1_KeyDown(object sender, KeyEventArgs e)
 {
 // If user is typing in a text input (like the AI Settings panel), don't intercept single-key shortcuts
 bool isTyping = IsTextInputFocused();
 
 // Escape exits slice mode
 if (!isTyping && e.KeyCode == Keys.Escape && isSliceMode)
 {
     ExitSliceMode();
     e.Handled = true;
     return;
 }
 
 // Delete selected objects with Delete key (allow even when typing - Delete in textbox is handled by textbox)
 if (e.KeyCode == Keys.Delete && !isTyping)
 {
 if (selectedObject != null || (selectedObjects != null && selectedObjects.Count >0))
 {
 DeleteButton_Click(this, EventArgs.Empty);
 e.Handled = true; return;
 }
 }
 // Copy (Ctrl+C) - Allow Ctrl shortcuts even when typing, but the control will handle them
 if (e.Control && e.KeyCode == Keys.C && !isTyping)
 {
 var set = selectedObjects.Count >0 ? selectedObjects.ToList() : (selectedObject != null ? new List<PlacedObject>{ selectedObject } : null);
 if (set != null && set.Count >0)
 {
  // snapshot deep-copy (clone images), normalize positions relative to min grid of selection
  int minX = set.Min(o=>o.GridX), minY = set.Min(o=>o.GridY);
  clipboardObjects = set.Select(o =>
  {
  var c = o.CloneForUndo();
  c.GridX -= minX;
  c.GridY -= minY;
  c.IsoPosition = GridToIso(c.GridX, c.GridY);
  c.Hidden = false;
  return c;
  }).ToList();
 }
 e.Handled = true; return;
 }
 // Paste (Ctrl+V) - Allow Ctrl shortcuts even when typing, but the control will handle them
 if (e.Control && e.KeyCode == Keys.V && !isTyping)
 {
 if (clipboardObjects != null && clipboardObjects.Count >0)
 {
 PushUndo();
 // determine paste anchor: mouse over canvas -> tile under mouse; else0,0
 Point clientPos = designPictureBox.PointToClient(Cursor.Position);
 bool onCanvas = clientPos.X >=0 && clientPos.Y >=0 && clientPos.X < designPictureBox.Width && clientPos.Y < designPictureBox.Height;
 Point anchor = onCanvas ? IsoToGrid(clientPos.X, clientPos.Y) : new Point(0,0);
 int baseLayer = placedObjects.Count;
 selectedObjects.Clear(); selectedObject = null; isSkewMode = false;
 foreach (var src in clipboardObjects)
 {
 int gx = Math.Max(0, Math.Min(gridWidth -1, anchor.X + src.GridX));
 int gy = Math.Max(0, Math.Min(gridHeight -1, anchor.Y + src.GridY));
 var iso = GridToIso(gx, gy);
  var clone = src.CloneForUndo();
  clone.GridX = gx;
  clone.GridY = gy;
  clone.IsoPosition = iso;
  clone.Layer = baseLayer++;
  clone.Hidden = false;
 placedObjects.Add(clone);
 selectedObjects.Add(clone);
 }
 selectedObject = selectedObjects.FirstOrDefault();
 RebuildLockLists();
 designPictureBox.Invalidate();
 }
 e.Handled = true; return;
 }

 // H key shortcuts - DO NOT intercept when typing in text inputs
 if (e.KeyCode == Keys.H && e.Shift && !isTyping)
 {
  if (placedObjects.Any(o => o.Hidden)) PushUndo();
  foreach (var o in placedObjects) o.Hidden = false;
 designPictureBox.Invalidate();
 e.Handled = true; return;
 }
 if (e.KeyCode == Keys.H && !isTyping)
 {
 if (selectedObject != null || (selectedObjects != null && selectedObjects.Count >0))
 {
 var set = selectedObjects.Count >0 ? selectedObjects : new HashSet<PlacedObject> { selectedObject };
  PushUndo();
  foreach (var o in set) o.Hidden = true;
 selectedObjects.Clear(); selectedObject = null;
 designPictureBox.Invalidate();
 e.Handled = true; return;
 }
 }

 // Undo/Redo - these are Ctrl shortcuts, let textbox handle its own Ctrl+Z
 if (e.Control && e.KeyCode == Keys.Z && !isTyping)
 {
 if (undoStack.Count >0)
 {
  var current = placedObjects.Select(o => o.CloneForUndo()).ToList();
 redoStack.Push(current);
  var prev = undoStack.Pop();
  RestoreState(prev);
  DisposeSnapshot(prev);
 }
 e.Handled = true; return;
 }
 if (e.Control && e.KeyCode == Keys.Y && !isTyping)
 {
 if (redoStack.Count >0)
 {
  var current = placedObjects.Select(o => o.CloneForUndo()).ToList();
 undoStack.Push(current);
  var next = redoStack.Pop();
  RestoreState(next);
  DisposeSnapshot(next);
 }
 e.Handled = true; return;
 }
 
 // Numpad movement - don't intercept when typing
 if (!isTyping && selectedObject != null && !lockedObjects.Contains(selectedObject))
 {
 int dx =0, dy =0;
 switch (e.KeyCode)
 {
 case Keys.NumPad1: dx = -1; dy = +1; break;
 case Keys.NumPad2: dx =0; dy = +1; break;
 case Keys.NumPad3: dx = +1; dy = +1; break;
 case Keys.NumPad4: dx = -1; dy =0; break;
 case Keys.NumPad6: dx = +1; dy =0; break;
 case Keys.NumPad7: dx = -1; dy = -1; break;
 case Keys.NumPad8: dx =0; dy = -1; break;
 case Keys.NumPad9: dx = +1; dy = -1; break;
 default: break;
 }
 if (dx != 0 || dy != 0)
 {
 PushUndo();
 var moveSet = selectedObjects.Count >0 ? selectedObjects : new HashSet<PlacedObject> { selectedObject };
 foreach (var obj in moveSet)
 {
 if (lockedObjects.Contains(obj) || obj.Hidden) continue;
 var gx = Math.Max(0, Math.Min(gridWidth -1, obj.GridX + dx));
 var gy = Math.Max(0, Math.Min(gridHeight -1, obj.GridY + dy));
 obj.GridX = gx; obj.GridY = gy; obj.IsoPosition = GridToIso(gx, gy);
 }
 designPictureBox.Invalidate();
 e.Handled = true;
 return;
 }
 }
 
 // Transform shortcuts - don't intercept when typing
 if (!isTyping && selectedObject != null)
 {
 // Escape = Exit skew mode or rotate mode
 if (e.KeyCode == Keys.Escape && (isSkewMode || isRotateMode))
 {
 if (isSkewMode)
 {
 isSkewMode = false;
 outputTextBox.AppendText("Skew mode cancelled.\r\n");
 }
 if (isRotateMode)
 {
 isRotateMode = false;
 outputTextBox.AppendText("Rotate mode cancelled.\r\n");
 }
 designPictureBox.Invalidate();
 e.Handled = true;
 return;
 }
 
 // K = Toggle Skew mode
 if (e.KeyCode == Keys.K && !e.Control && !e.Shift)
 {
 ToggleSkewMode();
 e.Handled = true;
 return;
 }
 
 // Shift+K = Reset skew
 if (e.KeyCode == Keys.K && e.Shift)
 {
 ResetSelectedObjectsSkew();
 e.Handled = true;
 return;
 }
 
 // R = Toggle Rotate mode (free rotation), Shift+R = rotate 15�, Ctrl+R = rotate -90�
 if (e.KeyCode == Keys.R && !isSkewMode && !isRotateMode)
 {
 if (e.Shift)
 {
 // Shift+R = Quick rotate 15 degrees
 RotateSelectedObjects(ROTATION_FINE_INCREMENT);
 }
 else if (e.Control)
 {
 // Ctrl+R = Quick rotate -90 degrees
 RotateSelectedObjects(-ROTATION_INCREMENT);
 }
 else
 {
 // R alone = Toggle free rotate mode
 ToggleRotateMode();
 }
 e.Handled = true;
 return;
 }
 
 // Shift+R while in rotate mode = reset rotation to 0
 if (e.KeyCode == Keys.R && e.Shift && isRotateMode)
 {
 SetSelectedObjectsRotation(0);
 e.Handled = true;
 return;
 }
 
 // + / = = Scale Up (Shift for faster)
 if (e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Add)
 {
 float scaleAmount = e.Shift ? SCALE_INCREMENT * 2 : SCALE_INCREMENT;
 ScaleSelectedObjects(scaleAmount);
 e.Handled = true;
 return;
 }
 
 // - = Scale Down (Shift for faster)
 if (e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract)
 {
 float scaleAmount = e.Shift ? SCALE_INCREMENT * 2 : SCALE_INCREMENT;
 ScaleSelectedObjects(-scaleAmount);
 e.Handled = true;
 return;
 }
 
 // F = Flip Horizontal (Shift+F = Flip Vertical)
 if (e.KeyCode == Keys.F && !isSkewMode && !isRotateMode)
 {
 if (e.Shift)
 FlipSelectedObjects(false, true); // Vertical
 else
 FlipSelectedObjects(true, false); // Horizontal
 e.Handled = true;
 return;
 }
 
 // T = Reset Transform (including skew)
 if (e.KeyCode == Keys.T && e.Control)
 {
 ResetSelectedObjectsTransform();
 isSkewMode = false;
 isRotateMode = false;
 e.Handled = true;
 return;
 }
 
 // Arrow keys with Alt = Pixel offset (fine movement)
 if ((ModifierKeys & Keys.Alt) == Keys.Alt)
 {
 int pixelDx = 0, pixelDy = 0;
 int amount = e.Shift ? PIXEL_OFFSET_FAST : PIXEL_OFFSET_INCREMENT;
 
 switch (e.KeyCode)
 {
 case Keys.Left: pixelDx = -amount; break;
 case Keys.Right: pixelDx = amount; break;
 case Keys.Up: pixelDy = -amount; break;
 case Keys.Down: pixelDy = amount; break;
 }
 
 if (pixelDx != 0 || pixelDy != 0)
 {
 MoveSelectedObjectsPixels(pixelDx, pixelDy);
 e.Handled = true;
 return;
 }
 }
 }
 
 // Escape when no selection but in skew/rotate mode
 if (!isTyping && e.KeyCode == Keys.Escape && (isSkewMode || isRotateMode))
 {
 isSkewMode = false;
 isRotateMode = false;
 designPictureBox.Invalidate();
 e.Handled = true;
 return;
 }
 }
 }
}
