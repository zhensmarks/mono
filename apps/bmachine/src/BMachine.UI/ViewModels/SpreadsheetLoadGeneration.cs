namespace BMachine.UI.ViewModels;

internal sealed class SpreadsheetLoadGeneration
{
    private long _generation;

    public long Start() => Interlocked.Increment(ref _generation);

    public bool IsCurrent(long generation)
        => generation == Volatile.Read(ref _generation);
}
