namespace FrameStudio.Engine;

internal sealed class FramePsdInputLimitException : NotSupportedException
{
    public FramePsdInputLimitException()
        : base("The PSD input exceeds the configured file-size limit.")
    {
    }
}
