using FrameStudio.App.Services;
using Xunit;

namespace FrameStudio.Tests;

public sealed class PngOutputWriterTests
{
    [Fact]
    public void ExistingOutputIsPreservedAndUniqueSuffixIsCreated()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"frame-studio-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var existingPath = Path.Combine(folder, "master-student.png");
            var existingBytes = new byte[] { 1, 2, 3 };
            var newBytes = new byte[] { 8, 9, 10, 11 };
            File.WriteAllBytes(existingPath, existingBytes);

            var actualPath = new PngOutputWriter().WriteNewPng(folder, "master-student", newBytes);

            Assert.Equal(Path.Combine(folder, "master-student-2.png"), actualPath);
            Assert.Equal(existingBytes, File.ReadAllBytes(existingPath));
            Assert.Equal(newBytes, File.ReadAllBytes(actualPath));
            Assert.Empty(Directory.GetFiles(folder, ".frame-studio-*.tmp"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ProtectedPathIsNeverUsedAsOutputName()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"frame-studio-protected-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var protectedPath = Path.Combine(folder, "master.png");
            var protectedBytes = new byte[] { 4, 5, 6 };
            File.WriteAllBytes(protectedPath, protectedBytes);

            var actualPath = new PngOutputWriter().WriteNewPng(folder, "master", new byte[] { 7 }, protectedPath);

            Assert.Equal(Path.Combine(folder, "master-2.png"), actualPath);
            Assert.Equal(protectedBytes, File.ReadAllBytes(protectedPath));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
