using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using PixelcutCompact.Services.Ai;
using Xunit;

namespace PixelcutCompact.Editing.Tests;

public sealed class RefineHairModelSafetyTests
{
    [Fact]
    public void ModNetManifestPinsVerifiedCompatibleArtifactAndBiRefNetRemainsDisabled()
    {
        var modNet = OnnxModelManager.ModNet;
        Assert.True(OnnxModelManager.HasVerifiedSha256(modNet));
        Assert.Equal("07c308cf0fc7e6e8b2065a12ed7fc07e1de8febb7dc7839d7b7f15dd66584df9", modNet.Sha256);
        Assert.Contains("fa2fa546052fba4c08921230a26cc69a333fca12", modNet.Url, StringComparison.Ordinal);
        Assert.Equal(25_888_640, modNet.SizeBytes);
        Assert.Equal("Apache-2.0", modNet.License);
        Assert.Equal(512, modNet.ShortestEdge);
        Assert.Equal(32, modNet.SizeDivisibility);
        Assert.Equal(new[] { 0.5, 0.5, 0.5 }, modNet.Mean);
        Assert.Equal(new[] { 0.5, 0.5, 0.5 }, modNet.Std);
        Assert.False(OnnxModelManager.HasVerifiedSha256(OnnxModelManager.BiRefNetLite));
        Assert.False(OnnxModelManager.IsInstalled(OnnxModelManager.BiRefNetLite));
    }

    [Theory]
    [InlineData(25_888_640, true)]
    [InlineData(25_888_639, false)]
    [InlineData(25_888_641, false)]
    public void ModNetManifestAcceptsOnlyExactPublishedArtifactSize(long size, bool expected)
    {
        Assert.Equal(expected, OnnxModelManager.HasExpectedSize(OnnxModelManager.ModNet, size));
    }

    [Fact]
    public async Task EnsureAvailableRejectsUnpinnedModelBeforeNetworkOrDiskWrites()
    {
        var spec = TestSpec("test-unpinned-" + Guid.NewGuid().ToString("N"), "");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => OnnxModelManager.EnsureAvailableAsync(spec, null, CancellationToken.None));

        Assert.Contains("SHA-256", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(OnnxModelManager.PathFor(spec)));
    }

    [Fact]
    public void IsInstalledRequiresActualFileToMatchPinnedDigest()
    {
        var id = "test-verified-" + Guid.NewGuid().ToString("N");
        var bytes = new byte[1_000_001];
        bytes[0] = 0x5A;
        var digest = Convert.ToHexString(SHA256.HashData(bytes));
        var spec = TestSpec(id, digest);
        var path = OnnxModelManager.PathFor(spec);
        Directory.CreateDirectory(OnnxModelManager.ModelsDirectory);
        File.WriteAllBytes(path, bytes);

        try
        {
            Assert.True(OnnxModelManager.IsInstalled(spec));
            Assert.False(OnnxModelManager.IsInstalled(TestSpec(id, new string('0', 64))));
            Assert.False(OnnxModelManager.IsInstalled(TestSpec(id, "")));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static MattingModelSpec TestSpec(string id, string sha256) => new()
    {
        Id = id,
        DisplayName = "Test model",
        Url = "https://127.0.0.1/unreachable-model.onnx",
        Sha256 = sha256,
        SizeBytes = 1_000_001,
        License = "test",
        Mean = new[] { 0.0, 0.0, 0.0 },
        Std = new[] { 1.0, 1.0, 1.0 }
    };
}
