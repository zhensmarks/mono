using Avalonia.Headless.XUnit;
using BMachine.UI.ViewModels;
using BMachine.UI.Views;
using Xunit;

namespace BMachine.UI.RegressionTests;

public sealed class SpreadsheetWindowCloseTests
{
    [AvaloniaFact]
    public void ClosingWithUnsavedEditsAndChoosingKeepOpenPreservesChanges()
    {
        var vm = new SpreadsheetViewModel(new FakeDatabase()) { HasChanges = true };
        var window = new TestSpreadsheetWindow(discardChanges: false) { DataContext = vm };
        try
        {
            window.Show();
            window.Close();

            Assert.True(window.IsVisible);
            Assert.True(vm.HasChanges);
            Assert.Equal(1, window.ConfirmationCount);
        }
        finally
        {
            vm.HasChanges = false;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ClosingDuringSaveOrLoadIsCancelledWithoutDiscardPrompt()
    {
        var vm = new SpreadsheetViewModel(new FakeDatabase()) { IsLoading = true, HasChanges = true };
        var window = new TestSpreadsheetWindow(discardChanges: true) { DataContext = vm };
        try
        {
            window.Show();
            window.Close();

            Assert.True(window.IsVisible);
            Assert.True(vm.HasChanges);
            Assert.Equal(0, window.ConfirmationCount);
            Assert.Contains("finish before closing", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            vm.IsLoading = false;
            vm.HasChanges = false;
            window.Close();
        }
    }

    private sealed class TestSpreadsheetWindow(bool discardChanges) : SpreadsheetWindow
    {
        public int ConfirmationCount { get; private set; }

        protected override Task<bool> AskToDiscardUnsavedChangesAsync()
        {
            ConfirmationCount++;
            return Task.FromResult(discardChanges);
        }
    }
}
