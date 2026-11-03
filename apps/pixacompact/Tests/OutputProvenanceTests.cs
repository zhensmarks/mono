using System;
using System.IO;
using PixelcutCompact.Services;
using Xunit;

namespace PixelcutCompact.Editing.Tests;

/// <summary>
/// OutputProvenance = bukti bahwa output benar-benar berasal dari input tertentu
/// (path + ukuran + mtime). Skip tanpa bukti = bug "input beda output" lama.
/// </summary>
public class OutputProvenanceTests : IDisposable
{
    private readonly string _dir;

    public OutputProvenanceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "pxa-prov-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
        // reset cache statis antar test (refleksi kecil)
        typeof(OutputProvenance)
            .GetField("_cache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, null);
        typeof(OutputProvenance)
            .GetField("_dirty", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, false);
    }

    private (string input, string output) MakePair(string name = "a")
    {
        var input = Path.Combine(_dir, name + ".jpg");
        var output = Path.Combine(_dir, name + ".png");
        File.WriteAllBytes(input, new byte[2048]);   // input >0
        File.WriteAllBytes(output, new byte[4096]);  // output >1KB
        return (input, output);
    }

    [Fact]
    public void TanpaRecord_TidakSkip()
    {
        var (input, output) = MakePair();
        // Output ada & >1KB tapi tidak ada record -> JANGAN skip (diproses ulang).
        Assert.False(OutputProvenance.ShouldSkip(input, output));
    }

    [Fact]
    public void RecordLaluSkip_InputTidakBerubah()
    {
        var (input, output) = MakePair();
        OutputProvenance.Record(input, output);
        OutputProvenance.SaveIfDirty();
        Assert.True(OutputProvenance.ShouldSkip(input, output));
    }

    [Fact]
    public void InputBerganti_TidakSkip()
    {
        var (input, output) = MakePair();
        OutputProvenance.Record(input, output);
        OutputProvenance.SaveIfDirty();

        // Simulasi user mengganti foto (ukuran beda) -> output lama tidak dipercaya.
        File.WriteAllBytes(input, new byte[3000]);
        Assert.False(OutputProvenance.ShouldSkip(input, output));
    }

    [Fact]
    public void OutputHilangAtauKecil_TidakSkip()
    {
        var (input, output) = MakePair();
        OutputProvenance.Record(input, output);
        OutputProvenance.SaveIfDirty();

        File.Delete(output);
        Assert.False(OutputProvenance.ShouldSkip(input, output));

        File.WriteAllBytes(output, new byte[100]); // <1KB = korup
        Assert.False(OutputProvenance.ShouldSkip(input, output));
    }

    [Fact]
    public void RecordOutputLain_TidakSkip()
    {
        var (input1, output1) = MakePair("one");
        var (input2, _) = MakePair("two");
        OutputProvenance.Record(input1, output1);
        OutputProvenance.SaveIfDirty();

        // Input kedua tidak boleh mengklaim output milik input pertama.
        Assert.False(OutputProvenance.ShouldSkip(input2, output1));
    }

    [Fact]
    public void SaveLoad_Roundtrip()
    {
        var (input, output) = MakePair();
        OutputProvenance.Record(input, output);
        OutputProvenance.SaveIfDirty();

        // Reset cache -> load ulang dari disk harus tetap skip.
        typeof(OutputProvenance)
            .GetField("_cache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .SetValue(null, null);
        Assert.True(OutputProvenance.ShouldSkip(input, output));
    }
}
