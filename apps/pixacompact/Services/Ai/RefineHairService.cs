using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using PixelcutCompact.Services.Editing;

namespace PixelcutCompact.Services.Ai;

/// <summary>Hasil satu kali Refine Hair, siap dipasang ke <see cref="MaskEditSession"/>.</summary>
public sealed class RefineHairResult
{
    /// <summary>Mask alpha baru (panjang W*H) — gabungan mask lama + hasil matting di band unknown.</summary>
    public required byte[] Mask { get; init; }

    /// <summary>Bounding box area yang benar-benar diubah (untuk status/overlay), inklusif.</summary>
    public int MinX { get; init; }
    public int MinY { get; init; }
    public int MaxX { get; init; }
    public int MaxY { get; init; }

    /// <summary>True bila tidak ada piksel yang berubah (mis. mask kosong/penuh).</summary>
    public bool NoChange { get; init; }

    /// <summary>Execution provider ONNX yang dipakai (mis. "cpu", "dml").</summary>
    public string ExecutionProvider { get; init; } = "cpu";

    /// <summary>Durasi total inferensi (tanpa unduh model), untuk status bar.</summary>
    public TimeSpan Elapsed { get; init; }
}

/// <summary>
/// Orkestrasi Refine Hair: trimap dari mask sekarang → crop ke bounding box unknown
/// (+ margin) → inferensi matting ONNX → blend alpha baru HANYA di band unknown
/// dengan feather transisi, sehingga area solid tidak bergeser.
/// </summary>
public sealed class RefineHairService : IDisposable
{
    /// <summary>Margin di sekeliling bbox unknown saat meng-crop input model (piksel).</summary>
    private const int CropMargin = 32;

    /// <summary>Ambang perubahan alpha minimum agar dianggap "berubah" (anti-noise).</summary>
    private const int MinAlphaDelta = 1;

    private readonly MattingOnnxService _matting = new();

    /// <summary>Execution provider yang terakhir dilaporkan sesi ONNX.</summary>
    public string LastExecutionProvider { get; private set; } = "belum dimuat";

    /// <summary>
    /// Jalankan refine hair pada sesi edit.
    /// </summary>
    /// <param name="session">Sesi mask — hasil akan dikembalikan sebagai mask baru (belum dipasang).</param>
    /// <param name="spec">Model matting yang dipakai (default MODNet).</param>
    /// <param name="bandRadius">Lebar band unknown (px), biasanya dari settings.</param>
    /// <param name="feather">Feather transisi di tepi band (px) agar tidak ada seam.</param>
    /// <param name="selection">Bila tidak null, band dibatasi hanya di dalam selection ini.</param>
    /// <param name="ct">Cancellation token (tombol Batal saat spinner).</param>
    public async Task<RefineHairResult> RefineAsync(
        MaskEditSession session,
        MattingModelSpec spec,
        int bandRadius,
        int feather,
        MaskRegion? selection,
        CancellationToken ct = default)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));
        if (spec == null) throw new ArgumentNullException(nameof(spec));

        var sw = Stopwatch.StartNew();
        int w = session.Width, h = session.Height;
        byte[] currentMask = (byte[])session.Mask.Clone();

        // 1) Trimap dari mask sekarang; opsional dibatasi selection.
        //    Selection di-rasterisasi dulu ke buffer biner karena TrimapBuilder
        //    bekerja pada buffer 8-bit, bukan region geometris.
        TrimapResult trimap;
        if (selection != null)
        {
            var selBuf = new byte[w * h];
            MaskRasterizer.FillRegion(selection, selBuf, w, h, 255, MaskCombineOp.Replace, false, 0);
            trimap = TrimapBuilder.BuildWithin(currentMask, selBuf, w, h, bandRadius);
        }
        else
        {
            trimap = TrimapBuilder.Build(currentMask, w, h, bandRadius);
        }

        if (!trimap.HasUnknown)
        {
            // Mask solid seluruhnya (kosong atau penuh) → tidak ada tepi untuk di-refine.
            return new RefineHairResult
            {
                Mask = currentMask,
                NoChange = true,
                ExecutionProvider = LastExecutionProvider,
                Elapsed = sw.Elapsed
            };
        }

        // 2) Sumber RGB untuk matting: pakai original bila tersedia (lebih bersih
        //    di tepi rambut, tanpa halo dari hasil rembg), fallback ke result.
        var source = session.Original ?? session.Result;
        if (source.Width != w || source.Height != h)
            source = source.Resize(w, h);

        // 3) Crop ke bbox unknown + margin supaya inferensi cepat di gambar besar.
        int cx0 = Math.Max(0, trimap.MinX - CropMargin);
        int cy0 = Math.Max(0, trimap.MinY - CropMargin);
        int cx1 = Math.Min(w - 1, trimap.MaxX + CropMargin);
        int cy1 = Math.Min(h - 1, trimap.MaxY + CropMargin);
        int cw = cx1 - cx0 + 1;
        int ch = cy1 - cy0 + 1;

        var cropRgb = CropBuffer(source, cx0, cy0, cw, ch);
        byte[]? cropTrimap = spec.AcceptsTrimap ? CropBytes(trimap.Trimap, w, cx0, cy0, cw, ch) : null;

        // 4) Inferensi (di-thread terpisah di dalam MattingOnnxService).
        MatteResult matte = await _matting.MatteAsync(cropRgb, cropTrimap, spec, ct).ConfigureAwait(false);
        LastExecutionProvider = matte.ExecutionProvider;

        if (matte.Width != cw || matte.Height != ch)
        {
            // Jaga-jaga: resize alpha hasil agar cocok dengan crop.
            var tmp = PixelBuffer.FromAlpha(matte.Alpha, matte.Width, matte.Height);
            tmp = tmp.Resize(cw, ch);
            matte = new MatteResult
            {
                Alpha = tmp.ExtractAlphaMask(),
                Width = cw,
                Height = ch,
                ExecutionProvider = matte.ExecutionProvider
            };
        }

        // 5) Blend: alpha baru hanya menimpa di dalam band unknown (dengan feather).
        var result = (byte[])currentMask.Clone();
        var weight = BuildBlendWeight(trimap.Trimap, w, cx0, cy0, cw, ch, feather);

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        int changed = 0;

        for (int y = 0; y < ch; y++)
        {
            ct.ThrowIfCancellationRequested();
            int rowT = (cy0 + y) * w;
            int rowC = y * cw;

            for (int x = 0; x < cw; x++)
            {
                double t = weight[rowC + x];
                if (t <= 0.0) continue; // di luar band → area solid tidak disentuh.

                int gi = rowT + cx0 + x;
                byte oldA = currentMask[gi];
                byte newA = matte.Alpha[rowC + x];

                int blended = (int)Math.Round(oldA + (newA - oldA) * t);
                if (blended < 0) blended = 0;
                else if (blended > 255) blended = 255;

                if (Math.Abs(blended - oldA) >= MinAlphaDelta)
                {
                    result[gi] = (byte)blended;
                    changed++;
                    int gx = cx0 + x, gy = cy0 + y;
                    if (gx < minX) minX = gx;
                    if (gx > maxX) maxX = gx;
                    if (gy < minY) minY = gy;
                    if (gy > maxY) maxY = gy;
                }
            }
        }

        sw.Stop();

        bool noChange = changed == 0;
        return new RefineHairResult
        {
            Mask = result,
            MinX = noChange ? 0 : minX,
            MinY = noChange ? 0 : minY,
            MaxX = noChange ? -1 : maxX,
            MaxY = noChange ? -1 : maxY,
            NoChange = noChange,
            ExecutionProvider = matte.ExecutionProvider,
            Elapsed = sw.Elapsed
        };
    }

    /// <summary>
    /// Bobot blend 0..1 per piksel crop: 1.0 di dalam band unknown (posisi trimap == 128),
    /// lalu di-feather turun ke 0.0 ke arah luar band. Feather dihitung sebagai jarak
    /// bernormalisasi dari piksel unknown terdekat menggunakan box blur dua arah.
    /// </summary>
    private static double[] BuildBlendWeight(byte[] trimap, int w, int cx0, int cy0, int cw, int ch, int feather)
    {
        // Mask biner unknown di ruang crop.
        var unknown = new byte[cw * ch];
        for (int y = 0; y < ch; y++)
        {
            int rowT = (cy0 + y) * w;
            int rowC = y * cw;
            for (int x = 0; x < cw; x++)
            {
                if (trimap[rowT + cx0 + x] == 128) unknown[rowC + x] = 255;
            }
        }

        int f = Math.Max(0, feather);
        if (f == 0)
        {
            var hard = new double[cw * ch];
            for (int i = 0; i < hard.Length; i++) hard[i] = unknown[i] > 0 ? 1.0 : 0.0;
            return hard;
        }

        // Feather = "melebar" dari band ke arah luar: dilate bertingkat (box, 2 pass).
        int r = Math.Max(1, f / 2);
        var spread = (byte[])unknown.Clone();
        for (int pass = 0; pass < 2; pass++)
            spread = BoxDilate(spread, cw, ch, r);

        // Bobot = min(1, unknown_lebar / 255) tapi paksa 1.0 tepat di band asli.
        var weight = new double[cw * ch];
        for (int i = 0; i < weight.Length; i++)
        {
            if (unknown[i] > 0) { weight[i] = 1.0; continue; }
            double t = spread[i] / 255.0;
            // smoothstep agar transisi halus (tidak linear).
            weight[i] = t * t * (3.0 - 2.0 * t);
            if (weight[i] < 0.02) weight[i] = 0.0;
        }

        return weight;
    }

    /// <summary>Max filter (dilate) separable pada buffer 8-bit, radius r (chebyshev box).</summary>
    private static byte[] BoxDilate(byte[] src, int w, int h, int r)
    {
        var tmp = new byte[src.Length];
        var dst = new byte[src.Length];

        // Pass horizontal.
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                byte best = 0;
                int x0 = Math.Max(0, x - r);
                int x1 = Math.Min(w - 1, x + r);
                for (int xx = x0; xx <= x1; xx++)
                {
                    byte v = src[row + xx];
                    if (v > best) best = v;
                }
                tmp[row + x] = best;
            }
        }

        // Pass vertikal.
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                byte best = 0;
                int y0 = Math.Max(0, y - r);
                int y1 = Math.Min(h - 1, y + r);
                for (int yy = y0; yy <= y1; yy++)
                {
                    byte v = tmp[yy * w + x];
                    if (v > best) best = v;
                }
                dst[y * w + x] = best;
            }
        }

        return dst;
    }

    /// <summary>Ambil sub-rect dari PixelBuffer non-premultiplied (alpha diabaikan → opaque).</summary>
    private static PixelBuffer CropBuffer(PixelBuffer src, int x0, int y0, int w, int h)
    {
        var dst = PixelBuffer.Create(w, h); // opaque hitam, akan ditimpa
        for (int y = 0; y < h; y++)
        {
            int srcRow = (y0 + y) * src.Width + x0;
            int dstRow = y * w;
            for (int x = 0; x < w; x++)
            {
                int si = (srcRow + x) * 4;
                int di = (dstRow + x) * 4;
                dst.Bgra[di + 0] = src.Bgra[si + 0];
                dst.Bgra[di + 1] = src.Bgra[si + 1];
                dst.Bgra[di + 2] = src.Bgra[si + 2];
                dst.Bgra[di + 3] = 255;
            }
        }
        return dst;
    }

    /// <summary>Ambil sub-rect dari buffer 8-bit single-channel.</summary>
    private static byte[] CropBytes(byte[] src, int srcW, int x0, int y0, int w, int h)
    {
        var dst = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            int srcRow = (y0 + y) * srcW + x0;
            Buffer.BlockCopy(src, srcRow, dst, y * w, w);
        }
        return dst;
    }

    /// <summary>Provider yang dipakai sesi terakhir (untuk status bar).</summary>
    public string DescribeProvider() => _matting.ExecutionProviderDescription;

    /// <summary>Buang sesi ONNX (dipanggil saat window ditutup / gambar berganti).</summary>
    public void Reset()
    {
        _matting.DisposeSession();
        LastExecutionProvider = "belum dimuat";
    }

    public void Dispose() => _matting.Dispose();
}
