# Editor UI - Photoshop-Style Implementation

## Status: UI Structure Complete ✓

### Layout Architecture (3-Row Grid)
```
Row 0: Top Toolbar (Tools + Actions) - HORIZONTAL LAYOUT
Row 1: Options Bar (Contextual, appears per tool)
Row 2: Main Content (Canvas + Right Panel)
```

### TOP TOOLBAR - Implemented ✓
**Location**: Grid.Row="0", Border with dark theme (#1A1D21)

#### Tool Groups:
1. **SELECTION TOOLS**
   - Move (V) - Icon: cross arrows
   - Rectangular Marquee (M) / Elliptical Marquee (Shift+M)
   - Lasso (L) - Icon: free lasso shape
   - Polygonal Lasso (Shift+L) - Icon: polygon outline
   - Magic Wand (W) - Icon: magic wand with sparkles

2. **PAINT TOOLS** 
   - Brush (B) - Icon: brush shape
   - Eraser (E) - Icon: eraser shape
   - Refine Edge (Shift+R) - Icon: search with circle

3. **VIEW TOOLS**
   - Hand/Pan (H) - Icon: hand with arrows

4. **QUICK ACTIONS**
   - Quick Mask (Q) - Icon: circle
   - Refine Hair AI - Icon: circle; uses the shared tool-button style

5. **UNDO/REDO**
   - Undo (Ctrl+Z) - Icon: arrow curved left
   - Redo (Ctrl+Y or Ctrl+Shift+Z) - Icon: arrow curved right

6. **SAVE**
   - Save (Ctrl+S) - Icon: save/disk; primary blue button style
   - Save As - no keyboard shortcut
   - Exit Edit (Esc); an unfinished path is canceled first
   - Select All (Ctrl+A), Deselect (Ctrl+D), Invert (Ctrl+Shift+I)
   - Grow (Ctrl+J), Shrink (Ctrl+Shift+J); brush size [ and ]; brush mode X

### OPTIONS BAR - Implemented ✓
**Location**: Grid.Row="0", HorizontalAlignment="Right"
- Shows contextual options based on active tool
- Brush options: Size, Hardness slider
- Wand options: Tolerance slider, Contiguous checkbox
- Selection Mode: Replace/Add/Subtract buttons

### Style System
- **Colors**: Dark surfaces (#1A1D21); editor controls (#1B2028/#39424E) with blue accents
- **Icons**: 16x16 PathIcon, neutral white/gray foreground
- **Tool Buttons**: 40x40 with 8px radius and a 1px border; compact controls use a 6px radius
- **Spacing**: 2px between buttons, 6px separator margins

---

## Next Steps: Wire Up Tool Logic

### In PreviewWindow.Editor.cs:
```csharp
// These handlers already exist and need tool logic:
- OnToolClick() -> SetActiveTool()
- OnEnterEditClick() / OnExitEditClick() -> EnterEditMode() / EndEditMode()
- OnUndoClick() / OnRedoClick() -> Undo/Redo logic
- OnSaveEditClick() / OnSaveAsEditClick() -> Save logic
```

### Tool-Specific Logic Needed:
1. **Move Tool** - Translate selection
2. **Lasso/PolyLasso/Pen** - Build selection paths
3. **Magic Wand** - Color-based selection
4. **Brush** - Paint on mask (restore mode)
5. **Eraser** - Paint on mask (erase mode)
6. **Refine Edge** - Local edge refinement
7. **Pan** - Navigate canvas
8. **Quick Mask** - Toggle mask visualization overlay

### Options Bar Updates:
Need to wire sliders & checkboxes to update tool settings in real-time:
- SldBrushSize → _settings.EditorBrushSize
- SldBrushHardness → _settings.EditorBrushHardness
- SldWandTolerance → _settings.EditorWandTolerance
- etc.

---

## UI/UX Notes
- **Icons are clear and distinct** - each tool is visually recognizable
- **Photoshop-familiar layout** - horizontal toolbar at top, options on right
- **Button states** - selected tools have a blue outline; Quick Mask has a red active state; keyboard focus keeps a pale-blue 2px outline
- **Responsive spacing** - 2-6px spacing keeps UI tight and scannable
- **Separator bars** - visual grouping between tool categories

---

## File References
- Main UI: `Views/PreviewWindow.axaml` (lines 305-449)
- Logic: `Views/PreviewWindow.Editor.cs` (existing handlers)
- Settings: `Models/EditorSettings.cs` (default values)
