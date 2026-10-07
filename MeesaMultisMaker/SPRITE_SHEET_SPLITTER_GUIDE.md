# Sprite Sheet Splitter - User Guide

## Overview

The **Sprite Sheet Splitter** automatically detects individual sprites from a single image and separates them into individual layers. This is perfect for animation frames, sprite sheets, and collections of game assets.

## What It Does

Given an image like this:

```
[🐪 🐪 🐪 🐪 🐪]  ← Multiple sprites on
[🐪 🐪 🐪 🐪 🐪]     white/black background
[🐪 🐪 🐪 🐪 🐪]     (not perfectly grid-aligned)
```

The Sprite Sheet Splitter will:
1. ✅ Remove the background (white, black, or auto-detected)
2. ✅ Detect each individual sprite using smart object detection
3. ✅ Extract each sprite with its bounding box
4. ✅ Create a separate layer for each sprite
5. ✅ Preserve original positions

## Where to Use It

### 📋 GUMP Editor
- **Location**: Top toolbar → **"Split"** button (next to "Import")
- **Use Case**: Split UI elements, icons, buttons from sprite sheets
- **Workflow**: 
  1. Copy sprite sheet to clipboard (or select file)
  2. Click **"Split"** button
  3. Adjust detection options
  4. Each sprite becomes an editable layer!

### 🎬 Animation Editor (Future)
- Will create animation frames from sprite sheets
- Perfect for walk cycles, attack animations, etc.

### 🎨 Painter/Image Editor (Future)
- Split reference sheets
- Extract individual elements for compositing

## How to Use

### Step 1: Prepare Sprite Sheet

**Option A: From Clipboard**
1. Copy sprite sheet image to clipboard
2. Click **"Split"** button
3. Done! Image loads automatically

**Option B: From File**
1. Click **"Split"** button
2. Select image file when prompted
3. Done!

### Step 2: Configure Detection

The **Sprite Sheet Splitter Dialog** appears with:

**Left Side**: Preview with detected sprites outlined in red boxes

**Right Side**: Detection Options

```
┌─ Detection Options ──────────────┐
│                                   │
│ Detected: 25 sprites             │ ← Count of found sprites
│                                   │
│ Color Tolerance: [30      ]▼     │ ← Background removal strength
│                                   │
│ Min Sprite Width:  [5   ]▼       │ ← Filter out tiny objects
│ Min Sprite Height: [5   ]▼       │
│                                   │
│ Padding (pixels): [2   ]▼        │ ← Extra space around sprites
│                                   │
│ ☑ Remove Anti-Aliasing Halo      │ ← Clean edges
│                                   │
│ Sort Order:                       │
│ [Left to Right, Top to Bottom]▼  │ ← Layer ordering
│                                   │
│ [🔍 Re-Detect Sprites]           │ ← Apply changes
│                                   │
└───────────────────────────────────┘
```

### Step 3: Adjust Options (If Needed)

**If sprites are missing**:
- ✅ Increase "Color Tolerance" (try 40-50)
- ✅ Decrease "Min Sprite Width/Height"
- ✅ Click "Re-Detect Sprites"

**If too many false detections**:
- ✅ Decrease "Color Tolerance" (try 20-25)
- ✅ Increase "Min Sprite Width/Height"
- ✅ Click "Re-Detect Sprites"

**If sprites have halos/outlines**:
- ✅ Check "Remove Anti-Aliasing Halo"
- ✅ Click "Re-Detect Sprites"

### Step 4: Split Into Layers

1. Verify all sprites are detected (red boxes)
2. Click **"Split Into Layers"**
3. Done! Each sprite is now a separate layer ✨

## Detection Options Explained

### 🎨 Color Tolerance (0-255)
**What it does**: Controls how aggressive background removal is

| Value | Effect | Use When |
|-------|--------|----------|
| 10-20 | Very strict | Clean, solid color backgrounds |
| 30-40 | **Default** | Most sprite sheets |
| 50-100 | Very aggressive | Gradients, compressed JPEGs |

### 📏 Min Sprite Width/Height (pixels)
**What it does**: Ignores objects smaller than this size

**Default**: 5 × 5 pixels

**Use Cases**:
- Increase to 10-20: Filter out dust/noise
- Decrease to 3-5: Keep tiny details (sparkles, particles)

### 📦 Padding (pixels)
**What it does**: Extra space around each extracted sprite

**Default**: 2 pixels

**Why it matters**:
- Prevents clipping at edges
- Maintains anti-aliasing
- Adds "breathing room"

### ✨ Remove Anti-Aliasing Halo
**What it does**: Removes semi-transparent pixels at edges

**When to use**:
- ☑ Sprites have colored outlines/halos
- ☑ Background removal left artifacts
- ☐ Sprites look correct already

### 🔢 Sort Order

**Left to Right, Top to Bottom** (default)
```
1 2 3 4 5
6 7 8 9 10
11 12 13 14 15
```
✅ Best for: Walk cycles, animation frames

**Top to Bottom, Left to Right**
```
1 4 7 10 13
2 5 8 11 14
3 6 9 12 15
```
✅ Best for: Vertical sprite sheets

**None (Detection Order)**
- Order sprites were found
- ✅ Best for: Non-ordered collections

## Example Workflows

### 📸 Example 1: Walk Cycle (5 frames)

**Input**: Single image with 5 walking poses
1. Copy walk cycle image
2. Click **"Split"**
3. Adjust tolerance if needed
4. Click **"Split Into Layers"**
5. ✅ Result: 5 layers named "Sprite 1" through "Sprite 5"
6. Export each layer or use in animation

### 🎮 Example 2: Game Icons (20+ icons)

**Input**: Icon sprite sheet with varying sizes
1. Load sprite sheet file
2. Click **"Split"**
3. Set "Min Width: 16" to filter noise
4. Set "Padding: 4" for extra space
5. Sort: "Left to Right, Top to Bottom"
6. Click **"Split Into Layers"**
7. ✅ Result: Each icon in its own layer, ready to edit!

### 🐪 Example 3: Camel Animation (25 frames, your example)

**Input**: 5 rows × 5 columns of camel sprites
1. Paste camel sprite sheet
2. Click **"Split"**
3. Default settings work great!
4. Sort: "Left to Right, Top to Bottom"
5. Click **"Split Into Layers"**
6. ✅ Result: 25 layers, Frame 1-25, in correct order!

## Technical Details

### Background Detection Algorithm
1. **Auto-Detection**: Samples image corners and edges
2. **Most Common Color**: Uses most frequent edge color
3. **Tolerance**: Removes colors within RGB distance threshold

### Object Detection Algorithm
1. **Transparency Mask**: Creates binary mask (opaque vs transparent)
2. **Connected Component Analysis**: Finds clusters of opaque pixels using flood fill
3. **Bounding Box**: Calculates minimal rectangle for each cluster
4. **Filtering**: Removes objects below size threshold

### Performance
| Image Size | Sprite Count | Detection Time | Memory |
|------------|--------------|----------------|--------|
| 1024×1024 | 10-20 | < 1 second | ~5 MB |
| 2048×2048 | 50-100 | 2-3 seconds | ~20 MB |
| 4096×4096 | 100+ | 5-10 seconds | ~80 MB |

## Troubleshooting

### ❌ Problem: No Sprites Detected

**Causes**:
- Tolerance too low
- Min size too high
- Background not uniform

**Solutions**:
1. ✅ Increase "Color Tolerance" to 50-80
2. ✅ Decrease "Min Sprite Width/Height" to 3
3. ✅ Check image in preview - is background actually removed?
4. ✅ Try manually specifying background color (future feature)

### ❌ Problem: Too Many False Positives

**Causes**:
- Tolerance too high
- Min size too low
- Noise in image

**Solutions**:
1. ✅ Decrease "Color Tolerance" to 15-25
2. ✅ Increase "Min Sprite Width/Height" to 10-20
3. ✅ Clean up source image before splitting

### ❌ Problem: Sprites Have Colored Halos

**Cause**: Anti-aliasing or JPEG compression artifacts

**Solution**:
1. ✅ Check "Remove Anti-Aliasing Halo"
2. ✅ Increase "Color Tolerance" slightly
3. ✅ Click "Re-Detect"

### ❌ Problem: Sprites Are Clipped

**Cause**: Padding too small

**Solution**:
1. ✅ Increase "Padding" to 3-5 pixels
2. ✅ Click "Re-Detect"

### ❌ Problem: Wrong Sort Order

**Cause**: Sprites not aligned in grid

**Solutions**:
1. ✅ Try different sort order
2. ✅ Use "None" and manually rearrange layers
3. ✅ Note: Row/column tolerance is ~50 pixels

## Keyboard Shortcuts (In Dialog)

| Key | Action |
|-----|--------|
| **Enter** | Split Into Layers |
| **Escape** | Cancel |
| **Ctrl + Mouse Wheel** | Zoom preview |

## Tips & Best Practices

### ✅ DO:
- Use PNG format for sprite sheets (lossless, transparency support)
- Keep backgrounds uniform (solid white or black)
- Ensure sprites don't overlap
- Leave ~5-10 pixels between sprites
- Use power-of-2 sizes for optimal performance (512×512, 1024×1024)

### ❌ DON'T:
- Use heavily compressed JPEGs (causes detection issues)
- Mix sprites with backgrounds that vary in color
- Overlap sprites in the sheet
- Use extremely large sheets (> 4096×4096) without testing first

## Advanced Usage

### Batch Processing (Future Feature)
- Process multiple sprite sheets at once
- Save detection presets
- Auto-name layers based on position

### Custom Background Color (Future Feature)
- Click to select background color
- Eyedropper tool for precise selection
- Multiple background colors support

### Grid Mode (Future Feature)
- Perfect grid detection
- Specify rows × columns
- Equal-sized sprite extraction

## Integration

### GUMP Editor
```
Workflow: Split → Edit → Export
1. Split sprite sheet into layers
2. Edit individual sprites (scale, rotate, effects)
3. Export as single image or animation frames
```

### Animation Editor (Coming Soon)
```
Workflow: Split → Sequence → Animate
1. Split sprite sheet
2. Automatically create animation sequence
3. Set frame rate, loop, export GIF/MP4
```

## FAQ

**Q: Can I split GIF animations?**  
A: Yes! Each frame will be detected as a separate sprite.

**Q: What's the maximum number of sprites?**  
A: No hard limit, but 100+ sprites may take longer to process.

**Q: Can I undo after splitting?**  
A: Yes! Ctrl+Z works after splitting (will remove all created layers).

**Q: What if sprites are touching?**  
A: They'll be detected as one sprite. Manually separate them first, or add padding in the source image.

**Q: Can I split from a screenshot?**  
A: Yes! Copy screenshot to clipboard, click "Split".

**Q: Does it work with isometric sprites?**  
A: Yes! Any 2D sprite format works. Detection is shape-based, not grid-based.

**Q: What about rotated sprites?**  
A: They'll be detected with their bounding box. The sprite itself won't be rotated back.

## Known Limitations

1. **Overlapping Sprites**: Detected as single sprite
2. **Complex Backgrounds**: May require manual cleanup
3. **Very Small Sprites**: (< 5×5 px) may be filtered out
4. **Animated Backgrounds**: Not supported (use solid color)
5. **Transparent Sprites**: Can't detect on transparent background (add solid color first)

## Version History

### v1.0 (Current)
- ✅ Automatic background detection
- ✅ Connected component analysis
- ✅ Adjustable detection parameters
- ✅ Layer creation
- ✅ Preview with bounding boxes
- ✅ Multiple sort orders

### v1.1 (Planned)
- 🔄 Manual background color selection
- 🔄 Grid mode for perfect alignment
- 🔄 Batch processing
- 🔄 Detection presets
- 🔄 Animation frame export

---

## Quick Reference Card

```
┌─────────────────────────────────────────────────┐
│     SPRITE SHEET SPLITTER - QUICK REF           │
├─────────────────────────────────────────────────┤
│ 📋 Access: GUMP Editor → "Split" button         │
│                                                  │
│ ⚙️ Basic Settings:                              │
│    • Tolerance: 30 (default)                    │
│    • Min Size: 5×5 px                           │
│    • Padding: 2 px                              │
│    • Sort: Left→Right, Top→Bottom              │
│                                                  │
│ 🔧 Adjust If:                                   │
│    • Missing sprites: ↑ tolerance, ↓ min size  │
│    • False positives: ↓ tolerance, ↑ min size  │
│    • Halos: ☑ Remove Halo                       │
│    • Clipped: ↑ padding                         │
│                                                  │
│ ✅ Good Sources:                                │
│    • PNG files (best)                           │
│    • Solid backgrounds                          │
│    • 5-10px spacing                             │
│                                                  │
│ ❌ Avoid:                                        │
│    • Compressed JPEGs                           │
│    • Gradient backgrounds                       │
│    • Overlapping sprites                        │
└─────────────────────────────────────────────────┘
```

---

**🎉 Happy Sprite Splitting!**

Need help? Check the examples above or refer to the troubleshooting section.
