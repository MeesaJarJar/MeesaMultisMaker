# Animation Editor - User Guide

## Overview

The Animation Editor is a comprehensive tool for viewing, playing, editing, and exporting Ultima Online mobile animations. It supports all animation file formats (anim.mul through anim5.mul) and provides powerful features for animation manipulation.

## Features

### Animation Viewing
- **Browse all animation types**: Monster animations (anim.mul, anim2.mul, anim3.mul) and People animations (anim4.mul - 5 directions, anim5.mul - 8 directions)
- **Body ID search**: Quickly find specific mobile bodies by ID
- **Action/Direction selection**: View all available actions and directions for each body
- **Frame-by-frame viewing**: Examine individual frames in detail

### Playback Controls
- **Play/Stop**: Animate sequences with adjustable playback
- **Loop mode**: Automatically repeat animations
- **FPS control**: Adjust playback speed from 1-60 FPS
- **Frame navigation**: Jump to specific frames

### Export Options
- **Export Frames**: Save all frames as individual PNG images with metadata
- **Export Sprite Sheet**: Create organized sprite sheets with all frames
- **Metadata export**: Includes frame dimensions, center points, and animation info

### Editing Tools
- **Replace Single Frame**: Swap individual frames with custom images
- **Import Frames**: Load external images as animation frames (coming soon)
- **Save to MUL**: Write modified animations back to MUL files with automatic backup

## Interface Layout

### Top Panel
- **Animation Type**: Select from Monster (1-3) or People (1-2) animation sets
- **Body ID Search**: Direct navigation to specific body IDs
- **Playback Controls**: Play, Stop, Loop toggle, and FPS slider
- **Export/Import Buttons**: Frame export, sprite sheet export, import, replace, and save

### Left Panel (Selection Lists)
- **Body IDs**: List of all available bodies in the selected animation type
- **Actions**: Available actions for the selected body (walk, run, attack, etc.)
- **Directions**: Available directions (5 or 8 depending on type)
- **Frames**: Individual frames in the animation sequence

### Center Panel (Preview)
- **Animation Display**: Shows the current frame centered with crosshair
- **Bounding Box**: Yellow outline showing frame dimensions
- **Info Overlay**: Frame number, dimensions, and center point coordinates

### Status Bar
- Displays current operation status and loaded animation information

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| Space | Play/Stop animation |
| Left Arrow | Previous frame |
| Right Arrow | Next frame |
| Home | First frame |
| End | Last frame |

## Animation File Formats

### Animation Types

1. **Monster (anim.mul)**
   - Low detail monster animations
   - 5 directions
   - 22 actions per body

2. **Monster 2 (anim2.mul)**
   - High detail monster animations
   - 5 directions
   - 22 actions per body

3. **Monster 3 (anim3.mul)**
   - Additional monster animations
   - 5 directions
   - 22 actions per body

4. **People (anim4.mul)**
   - Human and humanoid animations
   - 5 directions
   - 35 actions per body

5. **People 2 (anim5.mul)**
   - Enhanced people animations
   - 8 directions
   - 35 actions per body

### Standard Actions

| Action ID | Name | Description |
|-----------|------|-------------|
| 0 | Walk | Basic walking animation |
| 1 | Walk (weapon) | Walking while holding weapon |
| 2 | Run | Running animation |
| 3 | Run (weapon) | Running while holding weapon |
| 4 | Stand | Idle standing pose |
| 5 | Fidget 1 | Idle animation variant 1 |
| 6 | Fidget 2 | Idle animation variant 2 |
| 7 | Stand (1H attack) | Standing with one-handed weapon |
| 8 | Stand (2H attack) | Standing with two-handed weapon |
| 9 | Attack 1 | Primary attack animation |
| 10 | Attack 2 | Secondary attack animation |
| 11 | Attack 3 | Tertiary attack animation |
| 12 | Attack (bow) | Bow shooting animation |
| 13 | Attack (crossbow) | Crossbow shooting animation |
| 14 | Get hit | Taking damage animation |
| 15 | Die 1 | Death animation variant 1 |
| 16 | Die 2 | Death animation variant 2 |
| 17 | On horse | Mounted on horse |
| 18 | Get hit (mounted) | Taking damage while mounted |
| 19 | Die (mounted) | Death while mounted |
| 20 | Attack (mounted) | Attacking while mounted |
| 21 | Bow | Bowing emote |
| 22 | Salute | Saluting emote |
| 23 | Eat | Eating animation |

### Directions

**5 Direction Set:**
- 0: South
- 1: Southeast  
- 2: East
- 3: Northeast
- 4: North

**8 Direction Set (People2):**
- 0: South
- 1: Southeast
- 2: East
- 3: Northeast
- 4: North
- 5: Northwest
- 6: West
- 7: Southwest

## Workflow Examples

### Viewing an Animation

1. Select animation type from dropdown
2. Choose body ID from the list (or use search)
3. Select an action (e.g., "0 - Walk")
4. Select a direction
5. Click Play to view the animation

### Exporting Frames

1. Load the animation you want to export
2. Click "Export Frames"
3. Select a destination folder
4. Frames are saved as PNG files with metadata.txt

### Creating a Sprite Sheet

1. Load the animation
2. Click "Export Sprite Sheet"
3. Choose filename and location
4. Sprite sheet is created with all frames arranged in a grid

### Replacing a Frame

1. Load the animation
2. Select the frame to replace from the Frames list
3. Click "Replace Frame"
4. Select the replacement image
5. Preview updates immediately
6. Click "Save to MUL" to write changes (original files are backed up)

### Batch Editing Multiple Animations

1. Export frames for multiple animations
2. Edit frames in external image editor (Photoshop, GIMP, etc.)
3. Use "Import Frames" to reload modified images (coming soon)
4. Save each animation back to MUL files

## Technical Details

### Frame Storage Format

Animations use a palette-based run-length encoding (RLE) format:
- 256-color palette per animation (RGB555 format)
- RLE compressed pixel data for efficiency
- Frame header contains dimensions and center point offset

### Center Point

Each frame has a center point (CenterX, CenterY) that determines:
- Where the frame is anchored relative to the mobile's position
- How the frame aligns when playing the animation
- The visual "pivot point" for the sprite

### File Safety

When saving animations:
- Original MUL files are automatically backed up with timestamp
- Backup format: `filename.mul.backup_YYYYMMDDHHMMSS`
- Backups are created before any write operations
- You can manually restore from backups if needed

## Troubleshooting

### "No animation found"
- Not all body IDs have all actions/directions
- Try a different action or direction
- Check that the animation type is correct

### "Failed to load image"
- Ensure the MUL folder path is configured in Settings
- Verify that anim.mul and anim.idx files exist
- Check that the files are not corrupted

### Playback stuttering
- Lower the FPS if the system is struggling
- Animations with many large frames may play slower
- Close other applications to free up resources

### Save failed
- Ensure you have write permissions to the MUL folder
- Check that there is sufficient disk space for backups
- Verify the MUL files are not opened by another application

## Tips and Best Practices

1. **Always backup before editing**: While automatic backups are created, keep an additional manual backup of your MUL files

2. **Match frame dimensions**: When replacing frames, try to keep similar dimensions to the original for best results

3. **Preserve transparency**: Ensure replacement images have proper alpha channels for transparency

4. **Test in-game**: After saving modifications, test the animations in UO to verify they display correctly

5. **Use sprite sheets for external editing**: Export as sprite sheet, edit the entire sheet, then reimport individual frames

6. **Frame center points matter**: When creating custom frames, ensure center points align properly for smooth animation

## Future Enhancements

Planned features for future updates:
- Full import frames functionality
- Batch frame replacement
- Animation preview with customizable backgrounds
- Frame interpolation for smoother animations
- Onion skinning for frame-by-frame editing
- Color palette editing
- Animation mixing and blending

## Credits

Part of Meesa Multis Maker by MeesaJarJar
Animation format documentation based on UO community research

## Support

For questions, bug reports, or feature requests:
- Discord: https://discord.com/invite/MpBe7cJDqV
- GitHub: Report issues on the project repository
