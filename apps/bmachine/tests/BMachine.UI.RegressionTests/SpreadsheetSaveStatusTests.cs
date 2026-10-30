using BMachine.UI.ViewModels;
using Xunit;

namespace BMachine.UI.RegressionTests;

public sealed class SpreadsheetSaveStatusTests
{
    [Fact]
    public void CompletedSaveReportsAcceptanceWithoutClaimingIndependentVerification()
    {
        var message = SpreadsheetSaveStatus.GetCompletionMessage(3);

        Assert.Equal("Save request accepted for 3 cells.", message);
        Assert.False(message.Contains("verified", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EmptySaveReportsNoChanges()
    {
        Assert.Equal("No changes to save.", SpreadsheetSaveStatus.GetCompletionMessage(0));
    }
}
