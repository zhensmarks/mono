namespace PixelcutCompact.Models;

/// <summary>
/// Nilai default & konstanta untuk editor mask/selection di PreviewWindow.
/// Satu sumber kebenaran; PreviewWindowSettings merujuk ke sini.
/// </summary>
public static class EditorSettings
{
    // Brush
    public const int DefaultBrushSize = 60;
    public const int MinBrushSize = 1;
    public const int MaxBrushSize = 2000;
    public const double DefaultBrushHardness = 0.7;   // 0 = sangat lembut, 1 = tajam
    public const double DefaultBrushOpacity = 1.0;    // 0..1
    public const double DefaultBrushFlow = 1.0;       // 0..1 penambahan per cap (buildup)
    public const double DefaultBrushSpacing = 0.15;   // jarak stamp relatif terhadap diameter
    public const bool DefaultBrushRestoreMode = false; // false = erase

    // Magic wand
    public const int DefaultWandTolerance = 24;       // 0..255 jarak warna
    public const bool DefaultWandContiguous = true;

    // Selection
    public const double DefaultSelectionFeather = 0.0;
    public const bool DefaultAntiAliasSelection = true;

    // Undo
    public const int DefaultUndoSteps = 12;
    public const int DefaultUndoMemoryLimitMb = 256;
    public const int MinUndoSteps = 1;
    public const int MaxUndoSteps = 100;

    // Refine hair
    public const string DefaultRefineHairModel = "modnet";
    public const int DefaultRefineHairBandRadius = 16;    // lebar band unknown di trimap (px)
    public const int DefaultRefineHairFeather = 10;       // blend transition pada seam (px)
    public const bool DefaultSaveEditorAsCopy = false;    // false = in-place, true = simpan terpisah
    // Seleksi persisten
    public const int DefaultSelectionMode = 0;        // index SelectionCombineMode: 0 Replace
    public const bool DefaultWand8Connected = false;  // false = 4-arah
    public const int DefaultSelGrowPx = 4;            // Grow/Shrink default (px)
    public const int MaxSelGrowPx = 200;

    // Refine Edge brush (non-AI)
    public const int DefaultRefineEdgeSize = 40;      // diameter kuas (px)
    public const int DefaultRefineEdgeFeather = 4;    // feather transisi (px)
    public const int MinRefineEdgeSize = 4;
    public const int MaxRefineEdgeSize = 400;

    // Pen path (garis path overlay)
    public const double DefaultPenPathThickness = 2.0; // px, rentang 1..8
    public const double MinPenPathThickness = 1.0;
    public const double MaxPenPathThickness = 8.0;
    public const string DefaultPenPathColor = "#FFFFFF"; // putih + outline gelap = selalu terbaca

    // UI editor
    public const bool DefaultShowRightPanel = true;
    public const int DefaultAntsAnimationMs = 120;    // kecepatan marching ants
    public const string DefaultEditorOutputSuffix = "_edit";
}
