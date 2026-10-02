using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PixelcutCompact.Services.Editing;

namespace PixelcutCompact.Services.Ai;

/// <summary>Hasil matting: alpha 0..255 pada ukuran asli gambar.</summary>
public sealed class MatteResult
{
    public required byte[] Alpha { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required string ExecutionProvider { get; init; }
}

/// <summary>
/// Menjalankan inferensi matting ONNX (MODNet / BiRefNet).
/// Sesi dibuat lazy dan dipakai ulang; inferensi dijaga satu per satu.
/// </summary>
public sealed class MattingOnnxService : IDisposable
{
    private InferenceSession? _session;
    private MattingModelSpec? _loadedSpec;
    private string _inputName = "input";
    private string _provider = "CPU";
    private readonly object _gate = new();
    private readonly SemaphoreSlim _runGate = new(1, 1);

    /// <summary>Provider yang benar-benar dipakai sesi saat ini (untuk status bar).</summary>
    public string Provider => _provider;
    /// <summary>Deskripsi provider untuk status bar (mis. "CPU", "DML").</summary>
    public string ExecutionProviderDescription => _session == null ? "belum dimuat" : _provider;


    /// <summary>True bila sesi sudah dimuat.</summary>
    public bool IsLoaded => _session != null;

    /// <summary>Muat sesi untuk model tertentu. Bila model sama dan sudah dimuat, no-op.</summary>
    public void EnsureLoaded(MattingModelSpec spec)
    {
        lock (_gate)
        {
            if (_session != null && _loadedSpec?.Id == spec.Id && _loadedSpec?.Sha256 == spec.Sha256) return;

            DisposeSession();
            if (!OnnxModelManager.HasVerifiedSha256(spec))
                throw new InvalidOperationException("Model refused: no valid pinned SHA-256 checksum is available.");
            if (!OnnxModelManager.IsInstalled(spec))
                throw new InvalidOperationException("Model is missing or its SHA-256 checksum does not match the manifest.");

            var path = OnnxModelManager.PathFor(spec);

            var options = new SessionOptions();
            options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;

            // Coba EP akselerasi, fallback ke CPU. Tidak semua build punya DML/CUDA.
            foreach (var ep in new[] { "DmlExecutionProvider", "CUDAExecutionProvider" })
            {
                try
                {
                    options.AppendExecutionProvider(ep);
                    _provider = ep;
                    break;
                }
                catch
                {
                    // EP tidak tersedia di build/runtime ini — lanjut.
                }
            }
            if (_provider == "CPU") _provider = "CPU";

            try
            {
                _session = new InferenceSession(path, options);
            }
            catch
            {
                // Bila EP akselerasi membuat sesi gagal, coba murni CPU.
                var cpu = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
                _session = new InferenceSession(path, cpu);
                _provider = "CPU";
            }

            // Nama input dibaca dari metadata (jangan hardcode).
            var inputMeta = _session.InputMetadata;
            _inputName = inputMeta.Count > 0 ? inputMeta.Keys.First() : "input";
            _loadedSpec = spec;
        }
    }

    /// <summary>
    /// Jalankan matting pada gambar RGB (Buffer non-premultiplied).
    /// <paramref name="trimap"/> hanya dipakai bila model menerimanya.
    /// </summary>
    public async Task<MatteResult> MatteAsync(PixelBuffer rgb, byte[]? trimap,
        MattingModelSpec spec, CancellationToken ct)
    {
        EnsureLoaded(spec);

        await _runGate.WaitAsync(ct);
        try
        {
            return await Task.Run(() => RunInference(rgb, trimap, spec, ct), ct);
        }
        finally
        {
            _runGate.Release();
        }
    }

    private MatteResult RunInference(PixelBuffer rgb, byte[]? trimap, MattingModelSpec spec, CancellationToken ct)
    {
        var session = _session ?? throw new InvalidOperationException("Sesi ONNX belum dimuat.");

        int srcW = rgb.Width, srcH = rgb.Height;
        int inW, inH = default;

        // MODNet: resize shortest-edge (kelipatan div). BiRefNet: persegi 1024.
        if (spec.SquareResize)
        {
            inW = spec.InputSize;
            inH = spec.InputSize;
        }
        else
        {
            int se = Math.Max(1, spec.ShortestEdge);
            double scale = srcW < srcH ? (double)se / srcW : (double)se / srcH;
            inW = Math.Max(1, (int)Math.Round(srcW * scale));
            inH = Math.Max(1, (int)Math.Round(srcH * scale));
            int div = Math.Max(1, spec.SizeDivisibility);
            inW = RoundUp(inW, div);
            inH = RoundUp(inH, div);
        }

        // Preprocess: NCHW float32, ternormalisasi.
        var tensor = new DenseTensor<float>(new[] { 1, 3, inH, inW });
        double[] mean = spec.Mean, std = spec.Std;

        for (int y = 0; y < inH; y++)
        {
            ct.ThrowIfCancellationRequested();
            double fy = (y + 0.5) * srcH / inH - 0.5;
            int y0 = (int)Math.Floor(fy);
            double wy = fy - y0;
            if (y0 < 0) { y0 = 0; wy = 0; }
            int y1 = Math.Min(y0 + 1, srcH - 1);
            if (y0 >= srcH) y0 = srcH - 1;

            for (int x = 0; x < inW; x++)
            {
                double fx = (x + 0.5) * srcW / inW - 0.5;
                int x0 = (int)Math.Floor(fx);
                double wx = fx - x0;
                if (x0 < 0) { x0 = 0; wx = 0; }
                int x1 = Math.Min(x0 + 1, srcW - 1);
                if (x0 >= srcW) x0 = srcW - 1;

                int i00 = rgb.Index(x0, y0), i10 = rgb.Index(x1, y0);
                int i01 = rgb.Index(x0, y1), i11 = rgb.Index(x1, y1);

                for (int c = 0; c < 3; c++)
                {
                    // Buffer internal B,G,R → kanal tensor 0=R,1=G,2=B.
                    int off = c switch { 0 => 2, 1 => 1, _ => 0 };
                    double top = rgb.Bgra[i00 + off] * (1 - wx) + rgb.Bgra[i10 + off] * wx;
                    double bot = rgb.Bgra[i01 + off] * (1 - wx) + rgb.Bgra[i11 + off] * wx;
                    double v = top * (1 - wy) + bot * wy;
                    v = (v / 255.0 - mean[c]) / std[c];
                    tensor[0, c, y, x] = (float)v;
                }
            }
        }

        // Bangun input. Trimap hanya bila model mendukung.
        var inputs = new List<NamedOnnxValue>();
        if (spec.AcceptsTrimap && trimap != null)
        {
            var triTensor = BuildTrimapTensor(trimap, srcW, srcH, inW, inH);
            var meta = session.InputMetadata;
            var names = meta.Keys.ToList();
            var trimapName = names.Count > 1 ? names[1] : "trimap";
            inputs.Add(NamedOnnxValue.CreateFromTensor(_inputName, tensor));
            inputs.Add(NamedOnnxValue.CreateFromTensor(trimapName, triTensor));
        }
        else
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor(_inputName, tensor));
        }

        using var results = session.Run(inputs);

        // Ambil output pertama, normalisasi min-max ke 0..255.
        var output = results.First();
        var outTensor = output.AsTensor<float>();
        var dims = outTensor.Dimensions;

        // MODNet: [1,1,H,W] atau [1,H,W]; BiRefNet: [1,1,H,W].
        int outH = dims.Length >= 4 ? dims[2] : dims.Length == 3 ? dims[1] : (int)Math.Sqrt(outTensor.Length);
        int outW = dims.Length >= 4 ? (int)dims[3] : dims.Length == 3 ? (int)dims[2] : (int)(outTensor.Length / Math.Max(1, outH));

        var raw = new float[outW * outH];
        int idx = 0;
        // Bila ada batch/channel, ambil elemen channel pertama.
        foreach (var v in outTensor)
        {
            if (idx >= raw.Length) break;
            raw[idx++] = v;
        }

        // Normalisasi: MODNet/BiRefNet sudah mengeluarkan alpha 0..1 (sigmoid).
        // Hanya pakai min-max bila output jelas di luar rentang itu (mis. logits),
        // supaya alpha tidak terdistorsi (min-max memaksa 0..1 walau aslinya sudah benar).
        float mn = float.MaxValue, mx = float.MinValue;
        foreach (var v in raw)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) continue;
            if (v < mn) mn = v;
            if (v > mx) mx = v;
        }
        bool alreadyUnit = mn >= -0.01f && mx <= 1.01f;
        float range = mx - mn;
        if (range < 1e-6f) range = 1f;

        // Resize alpha kembali ke ukuran asli (bilinear).
        var alpha = new byte[srcW * srcH];
        for (int y = 0; y < srcH; y++)
        {
            ct.ThrowIfCancellationRequested();
            double fy = (y + 0.5) * outH / srcH - 0.5;
            int y0 = (int)Math.Floor(fy);
            double wy = fy - y0;
            if (y0 < 0) { y0 = 0; wy = 0; }
            int y1 = Math.Min(y0 + 1, outH - 1);
            if (y0 >= outH) y0 = outH - 1;

            for (int x = 0; x < srcW; x++)
            {
                double fx = (x + 0.5) * outW / srcW - 0.5;
                int x0 = (int)Math.Floor(fx);
                double wx = fx - x0;
                if (x0 < 0) { x0 = 0; wx = 0; }
                int x1 = Math.Min(x0 + 1, outW - 1);
                if (x0 >= outW) x0 = outW - 1;

                double top = raw[y0 * outW + x0] * (1 - wx) + raw[y0 * outW + x1] * wx;
                double bot = raw[y1 * outW + x0] * (1 - wx) + raw[y1 * outW + x1] * wx;
                double v = top * (1 - wy) + bot * wy;
                double norm = alreadyUnit ? v : (v - mn) / range;
                if (norm < 0) norm = 0;
                if (norm > 1) norm = 1;
                alpha[y * srcW + x] = (byte)(norm * 255 + 0.5);
            }
        }

        return new MatteResult
        {
            Alpha = alpha,
            Width = srcW,
            Height = srcH,
            ExecutionProvider = _provider
        };
    }

    private static DenseTensor<float> BuildTrimapTensor(byte[] trimap, int srcW, int srcH, int inW, int inH)
    {
        var t = new DenseTensor<float>(new[] { 1, 1, inH, inW });
        for (int y = 0; y < inH; y++)
        {
            int sy = (int)((y + 0.5) * srcH / inH);
            if (sy >= srcH) sy = srcH - 1;
            for (int x = 0; x < inW; x++)
            {
                int sx = (int)((x + 0.5) * srcW / inW);
                if (sx >= srcW) sx = srcW - 1;
                t[0, 0, y, x] = trimap[sy * srcW + sx] / 255f;
            }
        }
        return t;
    }

    private static int RoundUp(int value, int multiple)
    {
        if (multiple <= 1) return value;
        return (int)(Math.Ceiling(value / (double)multiple) * multiple);
    }

    public void Dispose()
    {
        lock (_gate) DisposeSession();
    }

    /// <summary>Buang sesi (mis. saat gambar berganti agar memori native dilepas).</summary>
    public void DisposeSession()
    {
        _session?.Dispose();
        _session = null;
        _loadedSpec = null;
    }
}
