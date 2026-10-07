# Meesa Multis Maker

## Overview
Meesa Multis Maker is a Windows desktop tool (WinForms, .NET Framework 4.8.1) for composing, editing, exporting, and AI-regenerating isometric game assets used in Ultima Online-style multis and gumps. It provides a canvas to place item art on an isometric diamond grid, supports complex transforms, and integrates AI pipelines for texture and static regeneration.

## Major Windows
- `Form1` (Main App)
  - Canvas with isometric grid for placing item art.
  - Palette panel with search/filter and TileData info.
  - Controls for grid creation, Z/L layering, transforms (scale/rotate/flip/skew), locking, export/import, and max Z filter.
  - Multi-selection, marquee, drag/pan/zoom, clipboard, and undo/redo.
  - Slice Tool for creating vertical slices of a placed image based on selected diamond tiles.
  - Painter launcher and Image Editor/GUMP Editor integration.
  - AI Settings dock (ComfyUI URL, prompts, steps/CFG/denoise/seed, sampler/scheduler, resolution, drop-black-pixels).

- `MulViewerForm`
  - Browser for MUL assets (land tiles/statics), viewing IDs and art.

- `MapViewerForm`
  - Map rendering with AI replacement workflow for tiles/statics, context-aware inpainting, old/new toggle.

- `GumpEditorForm`
  - Editor for gump images, supports adding custom images, multi-selection, regeneration, and layer moves.

- `PainterForm`
  - Pixel/paint tool that can send output back to the main canvas or Gump Editor.

## Key Features
- Isometric canvas with grid snapping, Z and layer ordering, pan/zoom, and selection/marquee.
- Transform tools: scale, rotate (free + quick), flip, pixel offsets, skew/distort with corner handles.
- Slice Tool: select tiles to produce vertical slices of the selected object; slices align to the chosen tiles while preserving original spacing.
- Lock/unlock lists to protect objects from edits.
- Export/import: canvas export, parts export/import, multi text import, and AI-ready component export.
- Undo/redo and clipboard (multi-object copy/paste with transforms preserved).
- AI integration (ComfyUI): regen selected tiles/statics, regen as one, revert AI changes, old/new toggle, drop-black-pixels mask.
- TileData info display for palette items.

## Tech Stack
- .NET Framework 4.8.1, WinForms, GDI+ rendering.
- Custom MUL readers (`Mul` namespace) for art/gump assets.
- ComfyUI client for AI workflows (img2img, inpaint).

## Common Workflows
- Place assets: drag from palette to canvas, arrange with grid snap, layer/Z controls.
- Transform: select and scale/rotate/flip/skew or nudge with pixel offsets.
- Slice: enable Slice Tool, click tiles to select bottoms, right-click/retoggle to generate aligned slices.
- AI regenerate: select tiles/statics and run ComfyUI pipelines; toggle old/new or revert.
- Export: canvas image, parts for AI, import back after generation.

## Notes
- Target framework: .NET Framework 4.8.1.
- Designed for Ultima Online�style diamond tiles (`44x44`).
- Multi-selection and locking are respected across delete/move/transform and slice operations.
