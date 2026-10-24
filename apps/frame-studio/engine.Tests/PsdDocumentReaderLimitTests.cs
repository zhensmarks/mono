using Xunit;

namespace FrameStudio.Engine.Tests;

public sealed class PsdDocumentReaderLimitTests
{
    [Fact]
    public void RejectsOversizedSeekableInputBeforeParsingAndLeavesStreamOpen()
    {
        using var input = new MemoryStream(new byte[8], writable: false);
        var reader = new PsdDocumentReader(maxInputFileBytes: 4);

        var exception = Assert.ThrowsAny<NotSupportedException>(() => reader.Read(input));

        Assert.Contains("file-size limit", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, input.Position);
        Assert.True(input.CanRead);
    }
}
