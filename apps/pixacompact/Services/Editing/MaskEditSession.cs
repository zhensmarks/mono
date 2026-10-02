using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using PixelcutCompact.Models;

namespace PixelcutCompact.Services.Editing;

/// <summary>
/// Jenis tool editor mask/selection di PreviewWindow.
/// <c>Pan</c> adalah perilaku default (viewer lama) agar user tidak kaget.
/// </summary>
public enum EditToolKind
{
    Pan = 0,
    Lasso = 1,
    PolyLasso = 2,
    MagicWand = 3,
    Pen = 4,
    Brush = 5,
    Eraser = 6,
    Move = 7, 
    RefineEdge = 8, 
    RectMarquee = 9, 
    EllipseMarquee = 10 
} 

/// <summary>
/// Sesi editing mask untuk satu gambar: menyimpan buffer RGB sumber,
/// mask alpha 8-bit, riwayat undo/redo, serta menghasilkan komposit untuk
/// pratinjau dan penyimpanan.
/// </summary>
public sealed class MaskEditSession : IDisposable
{
    /// <summary>Warna RGB asli hasil background removal (PNG transparan).</summary>
    public PixelBuffer Result { get; }

    /// <summary>Warna RGB gambar asli (untuk restore / sumber matting).</summary>
    public PixelBuffer? Original { get; private set; }

    /// <summary>Mask alpha 8-bit, panjang W*H. Ini sumber kebenaran seleksi.</summary>
    public byte[] Mask { get; private set; }

    /// <summary>
    /// Lapisan seleksi persisten (coverage 0..255) berukuran W*H. Tool
    /// menggambar ke sini; user menerapkannya ke <see cref="Mask"/> lewat aksi
    /// eksplisit (Hapus/Restore/Isi).
    /// </summary>
    public SelectionState Selection { get; }

    public int Width => Result.Width;
    public int Height => Result.Height;

    public MaskUndoStack Undo { get; }

    /// <summary>Sel benar bila ada perubahan yang belum disimpan ke disk.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>True bila gambar sumber yang sejajar tersedia untuk memulihkan RGB.</summary>
    public bool HasRestoreSource => Original != null && Original.Width == Width && Original.Height == Height;

    /// <summary>True bila mask meminta piksel di luar alpha asli hasil cutout.</summary>
    public bool HasPendingRestore
    {
        get
        {
            var result = Result.Bgra;
            for (int i = 0; i < Mask.Length; i++)
                if (Mask[i] > result[i * 4 + 3]) return true;
            return false;
        }
    }

    /// <summary>Jumlah operasi yang sudah dilakukan (untuk label undo).</summary>
    private string _lastLabel = "Edit";

    public MaskEditSession(PixelBuffer result, PixelBuffer? original, int undoSteps, int undoMemoryMb)
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
        Original = original;
        Mask = result.ExtractAlphaMask();
        Undo = new MaskUndoStack(undoSteps, undoMemoryMb);
        Selection = new SelectionState(result.Width, result.Height);
    }

    /// <summary>Pasang gambar asli secara lazy (untuk Refine Hair / restore).</summary>
    public bool SetOriginalIfMissing(PixelBuffer original)
    {
        if (Original != null || original == null || original.Width != Width || original.Height != Height)
            return false;
        Original = original;
        return true;
    }

    public void SetOriginal(PixelBuffer? original) => Original = original;
    // UNDO / REDO
    // ========================

    /// <summary>
    /// Snapshot kondisi sekarang sebelum sebuah perubahan, dengan label aksi.
    /// Dipanggil oleh tool tepat sebelum memodifikasi mask.
    /// </summary>
    public void BeginEdit(string label)
    {
        _lastLabel = label;
        Undo.Push(Mask, label);
        IsDirty = true;
    }

    public bool CanUndo => Undo.CanUndo;
    public bool CanRedo => Undo.CanRedo;
    public string LastLabel => _lastLabel;
    public long UndoMemoryBytes => Undo.MemoryBytes;

    public string? UndoAction()
    {
        var restored = Undo.Undo(Mask, Selection.Coverage, out var label, out var restoredSel);
        if (restored == null) return null;
        Mask = restored;
        if (restoredSel != null) Selection.CopyFrom(restoredSel);
        IsDirty = true;
        _lastLabel = label;
        return label;
    }

    public string? RedoAction()
    {
        var restored = Undo.Redo(Mask, Selection.Coverage, out var label, out var restoredSel);
        if (restored == null) return null;
        Mask = restored;
        if (restoredSel != null) Selection.CopyFrom(restoredSel);
        IsDirty = true;
        _lastLabel = label;
        return label;
    }

    /// <summary>Push snapshot untuk operasi seleksi (mask tidak berubah, seleksi berubah).</summary>
    public void PushSelectionUndo(string label)
    {
        Undo.Push(Mask, (byte[])Selection.Coverage.Clone(), label);
        _lastLabel = label;
    }

    // ========================
    // OPERASI MASK
    // ========================

    public void ApplyBrushStroke(System.Collections.Generic.List<BrushStamp> stamps)
    {
        if (stamps == null || stamps.Count == 0) return;
        BeginEdit(stamps[0].Restore ? "Brush Restore" : "Brush Erase");
        foreach (var st in stamps)
            BrushTool.Stamp(Mask, Width, Height, st);
    }

    public void FillRegion(MaskRegion region, byte value, MaskCombineOp op, bool antiAlias, double feather)
        => FillRegion(region, value, op, antiAlias, feather, "Selection Fill");

    public void FillMaskRegion(MaskRegion region, byte value, MaskCombineOp op, bool antiAlias, double feather)
        => MaskRasterizer.FillRegion(region, Mask, Width, Height, value, op, antiAlias, feather);

    /// <summary>
    /// Isi region ke mask dengan label undo eksplisit. Tidak menambah entri
    /// undo bila mask tidak berubah (no-op).
    /// </summary>
    public void FillRegion(MaskRegion region, byte value, MaskCombineOp op, bool antiAlias, double feather, string label)
    {
        var before = (byte[])Mask.Clone();
        MaskRasterizer.FillRegion(region, Mask, Width, Height, value, op, antiAlias, feather);
        if (!MaskChanged(before))
        {
            Buffer.BlockCopy(before, 0, Mask, 0, Mask.Length);
            return;
        }
        Undo.Push(before, label);
        IsDirty = true;
        _lastLabel = label;
    }

    // ========================
    // SELEKSI PERSISTEN → MASK
    // ========================

    /// <summary>
    /// Terapkan lapisan seleksi (<see cref="Selection"/>) ke mask: tiap piksel
    /// dengan coverage c di-blend menuju <paramref name="value"/> sebesar c.
    /// <paramref name="value"/> = 0 → hapus, 255 → restore/isi.
    /// Satu entri undo; no-op tidak menambah entri.
    /// </summary>
    public void ApplySelectionToMask(byte value, string label, bool fillAll = false)
    {
        var before = (byte[])Mask.Clone();
        if (fillAll)
        {
            // Ala Photoshop: tanpa seleksi, fill seluruh mask (0 = sembunyikan semua,
            // 255 = tampilkan/kembalikan seluruh gambar).
            for (int i = 0; i < Mask.Length; i++) Mask[i] = value;
        }
        else
        {
            var cov = Selection.Coverage;
            for (int i = 0; i < Mask.Length; i++)
            {
                int c = cov[i];
                if (c == 0) continue;
                if (c >= 255) { Mask[i] = value; continue; }
                int v = Mask[i];
                Mask[i] = (byte)(v + (value - v) * c / 255);
            }
        }
        if (!MaskChanged(before))
        {
            Buffer.BlockCopy(before, 0, Mask, 0, Mask.Length);
            return;
        }
        Undo.Push(before, label);
        IsDirty = true;
        _lastLabel = label;
    }

    /// <summary>
    /// Bangun seleksi dari area mask: coverage 255 bila mask &gt; threshold,
    /// else 0 (hard). Dipakai tombol "Select from mask".
    /// </summary>
    public void SelectionFromMask(byte threshold)
    {
        var cov = Selection.Coverage;
        for (int i = 0; i < cov.Length; i++)
            cov[i] = Mask[i] > threshold ? (byte)255 : (byte)0;
        Selection.NotifyChanged();
    }

    /// <summary>
    /// Terapkan region biner (byte[] nilai 0/255 dari MagicWand) langsung ke mask.
    /// <paramref name="value"/> = nilai baru untuk piksel di dalam region.
    /// </summary>
    public void ApplyRawMaskRegion(byte[] region, byte value, string label)
    {
        if (region == null || region.Length != Mask.Length) return;
        var before = (byte[])Mask.Clone();
        for (int i = 0; i < Mask.Length; i++)
            if (region[i] != 0) Mask[i] = value;
        if (!MaskChanged(before))
        {
            Buffer.BlockCopy(before, 0, Mask, 0, Mask.Length);
            return;
        }
        Undo.Push(before, label);
        IsDirty = true;
        _lastLabel = label;
    }

    public void InvertMask() => ApplyMaskOperation("Invert", MaskOperations.Invert);

    public void InvertWithin(MaskRegion region)
    {
        var before = (byte[])Mask.Clone();
        var tmp = (byte[])Mask.Clone();
        for (int i = 0; i < tmp.Length; i++) tmp[i] = (byte)(255 - tmp[i]);
        var cov = new byte[tmp.Length];
        var cover = new MaskRegion(region.Points, region.EvenOdd);
        MaskRasterizer.FillRegion(cover, cov, Width, Height, 255, MaskCombineOp.Replace, false, 0);
        for (int i = 0; i < Mask.Length; i++)
            if (cov[i] > 0) Mask[i] = tmp[i];
        CommitMaskOperation(before, "Invert Selection");
    }

    public void SelectAll()
    {
        if (IsMaskUniform(255)) return;
        ApplyMaskOperation("Select All", mask => MaskOperations.Fill(mask, 255));
    }

    public void SelectNone()
    {
        if (IsMaskUniform(0)) return;
        ApplyMaskOperation("Select None", mask => MaskOperations.Fill(mask, 0));
    }

    public void Defringe(int radius)
    {
        BeginEdit("Defringe");
        MaskOperations.Defringe(Result, Mask, radius);
    }

    public void ShiftEdge(int radius, bool expand)
        => ApplyMaskOperation(expand ? "Expand" : "Contract",
            mask => MaskOperations.ShiftEdge(mask, Width, Height, radius, expand));

    public void FeatherMask(int radius)
        => ApplyMaskOperation("Feather",
            mask => MaskOperations.Feather(mask, Width, Height, radius));


    /// <summary>
    /// Tulis mask langsung tanpa menambah entri undo. Dipakai saat drag Move
    /// (snapshot pra-drag sudah diambil via <see cref="BeginEdit"/>), sehingga
    /// satu drag = satu entri undo.
    /// </summary>
    public void SetMaskRaw(byte[] mask)
    {
        if (mask == null || mask.Length != Mask.Length) return;
        Buffer.BlockCopy(mask, 0, Mask, 0, Mask.Length);
    }

    /// <summary>Ganti seluruh mask (mis. hasil Refine Hair), sebagai 1 entri undo.</summary>
    public void ReplaceMask(byte[] newMask, string label)
    {
        if (newMask == null || newMask.Length != Mask.Length) return;
        var before = (byte[])Mask.Clone();
        Buffer.BlockCopy(newMask, 0, Mask, 0, Mask.Length);
        CommitMaskOperation(before, label);
    }

    /// <summary>Kembalikan region (warna) asli ke dalam result, pada area seleksi.</summary>
    public void RestoreFromOriginal()
    {
        if (Original == null || Original.Width != Width || Original.Height != Height) return;
        BeginEdit("Restore Original");
        var src = Original.Bgra;
        var dst = Result.Bgra;
        for (int i = 0; i < Mask.Length; i++)
        {
            int a = Mask[i];
            if (a == 0) continue;
            int bi = i * 4;
            if (a == 255)
            {
                dst[bi + 0] = src[bi + 0];
                dst[bi + 1] = src[bi + 1];
                dst[bi + 2] = src[bi + 2];
            }
            else
            {
                double t = a / 255.0;
                for (int c = 0; c < 3; c++)
                    dst[bi + c] = (byte)(dst[bi + c] + (src[bi + c] - dst[bi + c]) * t + 0.5);
            }
        }
    }

    // ========================
    // KOMPOSIT
    // ========================

    /// <summary>
    /// Bangun komposit. Piksel yang alpha-nya dipulihkan memakai RGB sumber asli,
    /// sedangkan area cutout yang tidak dipulihkan mempertahankan RGB hasil matting.
    /// </summary>
    public PixelBuffer Composite(bool applyMask = true)
    {
        var buf = new PixelBuffer(Width, Height);
        CompositeInto(buf, PixelBounds.Full(Width, Height), applyMask);
        return buf;
    }

    /// <summary>Tulis komposit ke buffer yang sudah ada (tanpa alokasi baru),
    /// lalu unggah ke WriteableBitmap via WriteTo. Dipakai pratinjau cepat.</summary>
    public void CompositeInto(PixelBuffer dst, bool applyMask = true)
    {
        CompositeInto(dst, PixelBounds.Full(Width, Height), applyMask);
    }

    /// <summary>Komposit hanya area yang berubah; RGB pulih dari Original saat mask melewati alpha awal.</summary>
    public void CompositeInto(PixelBuffer dst, PixelBounds bounds, bool applyMask = true)
    {
        if (dst == null) throw new ArgumentNullException(nameof(dst));
        if (dst.Width != Width || dst.Height != Height)
            throw new ArgumentException("Ukuran buffer tujuan harus sama dengan sesi.", nameof(dst));
        bounds = bounds.ClampTo(Width, Height);
        if (bounds.IsEmpty) return;

        var result = Result.Bgra;
        var destination = dst.Bgra;
        var original = HasRestoreSource ? Original!.Bgra : null;
        for (int y = bounds.Y; y < bounds.Bottom; y++)
        {
            int pixel = y * Width + bounds.X;
            int end = pixel + bounds.Width;
            for (; pixel < end; pixel++)
            {
                int byteIndex = pixel * 4;
                byte sourceAlpha = result[byteIndex + 3];
                destination[byteIndex] = result[byteIndex];
                destination[byteIndex + 1] = result[byteIndex + 1];
                destination[byteIndex + 2] = result[byteIndex + 2];

                if (!applyMask)
                {
                    destination[byteIndex + 3] = sourceAlpha;
                    continue;
                }

                byte maskAlpha = pixel < Mask.Length ? Mask[pixel] : sourceAlpha;
                if (maskAlpha > sourceAlpha && original != null)
                {
                    destination[byteIndex] = original[byteIndex];
                    destination[byteIndex + 1] = original[byteIndex + 1];
                    destination[byteIndex + 2] = original[byteIndex + 2];
                    int sourceImageAlpha = original[byteIndex + 3];
                    destination[byteIndex + 3] = (byte)((maskAlpha * sourceImageAlpha + 127) / 255);
                }
                else
                {
                    // Without aligned source pixels, never turn transparent cutout RGB into opaque black.
                    destination[byteIndex + 3] = maskAlpha > sourceAlpha ? sourceAlpha : maskAlpha;
                }
            }
        }
    }

    /// <summary>
    /// Komposit untuk pratinjau dengan skala tertentu (menghemat memori saat
    /// zoom < 1). Mengembalikan bitmap yang sudah dimiliki caller.
    /// </summary>
    public Bitmap CompositeBitmap(bool applyMask = true)
    {
        var buf = Composite(applyMask);
        return buf.ToAvaloniaBitmap();
    }

    // ========================
    // SAVE
    // ========================

    /// <summary>
    /// Tulis hasil ke PNG secara atomic (temp + move).
    /// </summary>
    public void BakeToFile(string path)
    {
        if (HasPendingRestore && !HasRestoreSource)
            throw new InvalidOperationException("Piksel gambar asli masih dimuat; tunggu sebelum menyimpan hasil restore.");

        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);

        var temp = path + ".tmp_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var buf = Composite();
        var png = buf.ToPngBytes();
        System.IO.File.WriteAllBytes(temp, png);

        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        System.IO.File.Move(temp, path);

        IsDirty = false;
    }

    /// <summary>Ukuran hasil komposit penuh (RGB + alpha mask).</summary>
    public void MarkSaved() => IsDirty = false;

    /// <summary>Tandai ada perubahan (mis. Move selection memindahkan isi mask).</summary>
    public void MarkDirty() => IsDirty = true;

    public void Dispose()
    {
        Original = null;
        Undo.Clear();
    }
    private bool MaskChanged(byte[] before)
    {
        if (before == null || before.Length != Mask.Length) return true;
        for (int i = 0; i < Mask.Length; i++)
            if (before[i] != Mask[i]) return true;
        return false;
    }
    private bool IsMaskUniform(byte value)
    {
        for (int i = 0; i < Mask.Length; i++)
            if (Mask[i] != value) return false;
        return true;
    }
    private void ApplyMaskOperation(string label, Action<byte[]> operation)
    {
        var before = (byte[])Mask.Clone();
        operation(Mask);
        CommitMaskOperation(before, label);
    }

    private void CommitMaskOperation(byte[] before, string label)
    {
        if (!MaskChanged(before))
        {
            Buffer.BlockCopy(before, 0, Mask, 0, Mask.Length);
            return;
        }
        Undo.Push(before, label);
        IsDirty = true;
        _lastLabel = label;
    }
}
