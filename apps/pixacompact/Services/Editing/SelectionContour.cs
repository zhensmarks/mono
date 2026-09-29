using System;
using System.Collections.Generic;

namespace PixelcutCompact.Services.Editing;

/// <summary>
/// Penelusur kontur (marching squares) pada buffer coverage seleksi 8-bit.
/// Menghasilkan daftar loop polygon tertutup (dalam koordinat piksel) yang
/// dipakai untuk menggambar marching ants dan menghitung bounds.
/// Grid diberi padding nol satu piksel sehingga loop selalu tertutup, termasuk
/// saat seleksi menyentuh tepi kanvas.
/// </summary>
public static class SelectionContour
{
    public const byte DefaultThreshold = 128;

    /// <summary>Telusuri kontur seleksi pada ambang default (128).</summary>
    public static List<List<Vec2>> Trace(byte[] coverage, int width, int height)
        => Trace(coverage, width, height, DefaultThreshold);

    /// <summary>
    /// Telusuri kontur pada ambang tertentu. Mengembalikan daftar loop
    /// (masing-masing ≥3 titik). Kosong bila tidak ada coverage melewati ambang.
    /// </summary>
    public static List<List<Vec2>> Trace(byte[] coverage, int width, int height, byte threshold)
    {
        var result = new List<List<Vec2>>();
        if (coverage == null || width <= 0 || height <= 0) return result;
        if (coverage.Length < width * height) return result;

        // Corner grid berukuran (w+2) x (h+2); indeks 0 dan w+1/h+1 = padding nol.
        int cw = width + 2;
        int ch = height + 2;

        // Batasi pemindaian ke bounding box coverage >= threshold supaya biaya
        // sebanding luas seleksi (bukan seluruh kanvas) saat seleksi kecil.
        int bMinX = int.MaxValue, bMinY = int.MaxValue, bMaxX = -1, bMaxY = -1;
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                if (coverage[row + x] >= threshold)
                {
                    if (x < bMinX) bMinX = x;
                    if (x > bMaxX) bMaxX = x;
                    if (y < bMinY) bMinY = y;
                    if (y > bMaxY) bMaxY = y;
                }
            }
        }
        if (bMaxX < 0) return result;
        int iStart = Math.Max(0, bMinX - 1);
        int iEnd = Math.Min(cw - 2, bMaxX + 1);
        int jStart = Math.Max(0, bMinY - 1);
        int jEnd = Math.Min(ch - 2, bMaxY + 1);

        byte Val(int i, int j)
        {
            if (i < 1 || i > width || j < 1 || j > height) return 0;
            return coverage[(j - 1) * width + (i - 1)];
        }

        // Titik potong pada tepi grid, dihitung sekali agar endpoint identik.
        var hPts = new Dictionary<long, Vec2>();
        var vPts = new Dictionary<long, Vec2>();

        // Kunci titik tepi: i di 32-bit atas, j di 32-bit bawah. Bit 62 dipakai
        // sebagai penanda titik VERTIKAL, sehingga H/V tidak pernah bertabrakan
        // untuk gambar besar (skema i*1e6 lama collides saat width >= 1000).
        const long VFlag = 1L << 62;
        long HKey(int i, int j) => ((long)i << 32) | (uint)j;
        long VKey(int i, int j) => VFlag | ((long)i << 32) | (uint)j;

        // Koordinat grid (i,j) adalah sudut kiri-atas piksel (i-1,j-1); pusat
        // piksel (i-1,j-1) berada di (i-0.5, j-0.5). Maka titik potong tepi
        // harus digeser -0.5 agar sejajar dengan tepi mask (bukan bias +0.5).
        Vec2 HPoint(int i, int j)
        {
            long k = HKey(i, j);
            if (hPts.TryGetValue(k, out var p)) return p;
            double a = Val(i, j), b = Val(i + 1, j);
            double t = (threshold - a) / (b - a);
            p = new Vec2(i + t - 0.5, j - 0.5);
            hPts[k] = p;
            return p;
        }

        Vec2 VPoint(int i, int j)
        {
            long k = VKey(i, j);
            if (vPts.TryGetValue(k, out var p)) return p;
            double a = Val(i, j), b = Val(i, j + 1);
            double t = (threshold - a) / (b - a);
            p = new Vec2(i - 0.5, j + t - 0.5);
            vPts[k] = p;
            return p;
        }

        // Segmen: pasangan key titik tepi.
        var segments = new List<(long A, long B)>();

        void AddSeg(long a, long b) => segments.Add((a, b));

        for (int j = jStart; j <= jEnd; j++)
        {
            for (int i = iStart; i <= iEnd; i++)
            {
                bool a = Val(i, j) >= threshold;
                bool b = Val(i + 1, j) >= threshold;
                bool c = Val(i + 1, j + 1) >= threshold;
                bool d = Val(i, j + 1) >= threshold;
                int idx = (a ? 1 : 0) | (b ? 2 : 0) | (c ? 4 : 0) | (d ? 8 : 0);
                if (idx == 0 || idx == 15) continue;

                long top = HKey(i, j);
                long bottom = HKey(i, j + 1);
                long left = VKey(i, j);
                long right = VKey(i + 1, j);

                // Pastikan titik-titik tepi yang dipakai sudah terhitung.
                if (idx is 1 or 2 or 3 or 5 or 7 or 9 or 11 or 13 or 14) _ = HPoint(i, j);       // top
                if (idx is 4 or 6 or 7 or 8 or 10 or 11 or 12 or 13 or 14) _ = HPoint(i, j + 1); // bottom
                if (idx is 1 or 3 or 5 or 8 or 9 or 10 or 11 or 12 or 14) _ = VPoint(i, j);       // left
                if (idx is 2 or 4 or 5 or 6 or 8 or 10 or 12 or 13 or 14) _ = VPoint(i + 1, j);   // right

                switch (idx)
                {
                    case 1: case 14: AddSeg(left, top); break;
                    case 2: case 13: AddSeg(top, right); break;
                    case 3: case 12: AddSeg(left, right); break;
                    case 4: case 11: AddSeg(right, bottom); break;
                    case 6: case 9: AddSeg(top, bottom); break;
                    case 7: case 8: AddSeg(bottom, left); break;
                    case 5:
                        {
                            double center = (Val(i, j) + Val(i + 1, j) + Val(i + 1, j + 1) + Val(i, j + 1)) * 0.25;
                            if (center >= threshold)
                            {
                                AddSeg(top, right);
                                AddSeg(bottom, left);
                            }
                            else
                            {
                                AddSeg(left, top);
                                AddSeg(right, bottom);
                            }
                            break;
                        }
                    case 10:
                        {
                            double center = (Val(i, j) + Val(i + 1, j) + Val(i + 1, j + 1) + Val(i, j + 1)) * 0.25;
                            if (center >= threshold)
                            {
                                AddSeg(left, top);
                                AddSeg(right, bottom);
                            }
                            else
                            {
                                AddSeg(top, right);
                                AddSeg(bottom, left);
                            }
                            break;
                        }
                }
            }
        }

        if (segments.Count == 0) return result;

        // Bangun adjacency untuk merangkai segmen menjadi loop tertutup.
        var adj = new Dictionary<long, List<long>>();
        void Link(long a, long b)
        {
            if (!adj.TryGetValue(a, out var la)) { la = new List<long>(2); adj[a] = la; }
            la.Add(b);
        }
        foreach (var s in segments) { Link(s.A, s.B); Link(s.B, s.A); }

        var used = new HashSet<(long, long)>();
        (long A, long B) SegKey(long a, long b) => a < b ? (a, b) : (b, a);
        Vec2 Pt(long k)
        {
            if ((k & VFlag) != 0)
            {
                long v = k & ~VFlag;
                return VPoint((int)(v >> 32), (int)(v & 0xFFFFFFFFL));
            }
            return HPoint((int)(k >> 32), (int)(k & 0xFFFFFFFFL));
        }

        foreach (var s in segments)
        {
            if (used.Contains(SegKey(s.A, s.B))) continue;

            var loop = new List<Vec2>();
            long start = s.A;
            long current = s.A;
            long prev = -1;

            while (true)
            {
                loop.Add(Pt(current));
                used.Add(SegKey(current, prev < 0 ? s.B : prev));

                // Pilih tetangga berikutnya yang segmennya belum dipakai.
                long next = -1;
                if (adj.TryGetValue(current, out var nbrs))
                {
                    foreach (var n in nbrs)
                    {
                        if (n == prev) continue;
                        if (!used.Contains(SegKey(current, n))) { next = n; break; }
                    }
                }
                if (next < 0) break;
                prev = current;
                current = next;
                if (current == start) break;
            }

            if (loop.Count >= 3) result.Add(loop);
        }

        return result;
    }
}
