using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace PixelcutCompact.Services.Editing;

/// <summary>
/// Stack undo/redo untuk buffer mask (byte[] panjang W*H).
/// Snapshot dikompresi DEFLATE dan dibatasi jumlah step + total memori.
/// Juga menyediakan daftar label (untuk History panel) dan lompat ke indeks.
/// </summary>
public sealed class MaskUndoStack
{
    private sealed class Snapshot
    {
        public byte[] Compressed = Array.Empty<byte>();
        public int Length;
        public string Label = "";
        public long MemoryBytes;
    }

    private readonly List<Snapshot> _undo = new();
    private readonly List<Snapshot> _redo = new();

    /// <summary>Label paralel dengan <see cref="_redo"/> (label operasi yang di-undo).</summary>
    private readonly List<string> _redoLabels = new();

    private readonly int _maxSteps;
    private readonly long _maxMemoryBytes;
    private long _usedBytes;

    public MaskUndoStack(int maxSteps, int maxMemoryMb)
    {
        _maxSteps = Math.Max(1, maxSteps);
        _maxMemoryBytes = Math.Max(16, maxMemoryMb) * 1024L * 1024L;
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;
    public long MemoryBytes => _usedBytes;

    // ========================
    // HISTORY (untuk panel History)
    // ========================

    /// <summary>Indeks kondisi saat ini dalam daftar <see cref="Labels"/>.</summary>
    public int CurrentIndex => _undo.Count;

    /// <summary>Jumlah total kondisi dalam riwayat (termasuk kondisi kini).</summary>
    public int Count => _undo.Count + 1 + _redo.Count;

    /// <summary>
    /// Daftar label tiap kondisi, dari terlama ke terbaru. Indeks 0 = kondisi
    /// awal ("Awal"); label lain = nama operasi yang menghasilkan kondisi itu.
    /// </summary>
    public IReadOnlyList<string> Labels
    {
        get
        {
            var list = new List<string>(Count) { "Awal" };
            for (int k = 1; k <= _undo.Count; k++)
                list.Add(_undo[k - 1].Label);
            // Redo: S[N+1] = _redo[^1] dengan label _redoLabels[^1].
            for (int p = _redoLabels.Count; p >= 1; p--)
                list.Add(_redoLabels[p - 1]);
            return list;
        }
    }

    /// <summary>
    /// Lompat ke kondisi <paramref name="index"/>. Mengembalikan mask baru
    /// (null bila index tidak valid / sudah di posisi itu). Label = operasi
    /// terakhir yang dijalankan saat melompat.
    /// </summary>
    public byte[]? JumpTo(int index, byte[] currentMask, out string label)
    {
        label = "";
        if (index < 0 || index >= Count) return null;
        if (index == CurrentIndex) return null;

        byte[]? mask = currentMask;
        if (index < CurrentIndex)
        {
            while (CurrentIndex > index)
            {
                var m = Undo(mask!, out label);
                if (m == null) break;
                mask = m;
            }
        }
        else
        {
            while (CurrentIndex < index)
            {
                var m = Redo(mask!, out label);
                if (m == null) break;
                mask = m;
            }
        }
        return mask;
    }

    // ========================
    // PUSH / UNDO / REDO
    // ========================

    /// <summary>Snapshot kondisi SEKARANG sebelum sebuah operasi dilakukan.</summary>
    public void Push(byte[] mask, string label)
    {
        var snap = Compress(mask, label);
        _undo.Add(snap);
        _usedBytes += snap.MemoryBytes;

        // Buang redo saat ada edit baru
        ClearRedo();

        Trim();
    }

    public byte[]? Undo(byte[] currentMask, out string label)
    {
        label = "";
        if (_undo.Count == 0) return null;

        var snap = _undo[^1];

        // Simpan kondisi sekarang ke redo agar bisa di-redo. Label redo =
        // nama operasi yang sedang di-undo.
        var current = Compress(currentMask, snap.Label);
        _redo.Add(current);
        _redoLabels.Add(snap.Label);
        _usedBytes += current.MemoryBytes;

        _undo.RemoveAt(_undo.Count - 1);
        _usedBytes -= snap.MemoryBytes;
        label = snap.Label;
        Trim();
        return Decompress(snap);
    }

    public byte[]? Redo(byte[] currentMask, out string label)
    {
        label = "";
        if (_redo.Count == 0) return null;

        string redoLabel = _redoLabels[^1];

        var current = Compress(currentMask, redoLabel);
        _undo.Add(current);
        _usedBytes += current.MemoryBytes;

        var snap = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _redoLabels.RemoveAt(_redoLabels.Count - 1);
        _usedBytes -= snap.MemoryBytes;
        label = snap.Label;
        Trim();
        return Decompress(snap);
    }

    public void Clear()
    {
        _undo.Clear();
        ClearRedo();
        _usedBytes = 0;
    }

    private void ClearRedo()
    {
        foreach (var s in _redo) _usedBytes -= s.MemoryBytes;
        _redo.Clear();
        _redoLabels.Clear();
    }

    private void Trim()
    {
        while (_undo.Count > _maxSteps)
        {
            _usedBytes -= _undo[0].MemoryBytes;
            _undo.RemoveAt(0);
        }
        // Buang redo terlama bila melebihi jumlah step
        while (_redo.Count > _maxSteps)
        {
            _usedBytes -= _redo[0].MemoryBytes;
            _redo.RemoveAt(0);
            _redoLabels.RemoveAt(0);
        }
        // Hormati batas memori: buang snapshot terlama di undo dulu, lalu redo.
        while (_usedBytes > _maxMemoryBytes && (_undo.Count > 0 || _redo.Count > 0))
        {
            if (_undo.Count > _redo.Count && _undo.Count > 0)
            {
                _usedBytes -= _undo[0].MemoryBytes;
                _undo.RemoveAt(0);
            }
            else if (_redo.Count > 0)
            {
                _usedBytes -= _redo[0].MemoryBytes;
                _redo.RemoveAt(0);
                _redoLabels.RemoveAt(0);
            }
            else if (_undo.Count > 0)
            {
                _usedBytes -= _undo[0].MemoryBytes;
                _undo.RemoveAt(0);
            }
            else break;
        }
    }

    private static Snapshot Compress(byte[] mask, string label)
    {
        using var ms = new MemoryStream();
        using (var ds = new DeflateStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        {
            ds.Write(mask, 0, mask.Length);
        }
        var bytes = ms.ToArray();
        return new Snapshot
        {
            Compressed = bytes,
            Length = mask.Length,
            Label = label,
            MemoryBytes = bytes.Length + 64
        };
    }

    private static byte[] Decompress(Snapshot snap)
    {
        var result = new byte[snap.Length];
        using var ms = new MemoryStream(snap.Compressed);
        using var ds = new DeflateStream(ms, CompressionMode.Decompress);
        int read = 0;
        while (read < result.Length)
        {
            int n = ds.Read(result, read, result.Length - read);
            if (n <= 0) break;
            read += n;
        }
        return result;
    }
}
