using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using PixelcutCompact.Services;
using Xunit;

namespace PixelcutCompact.Tests;

public sealed class RembgResourceManagerProcessTests
{
    [Fact]
    public async Task RunProcessAsync_DrainsBothStreamsBeforeReturning()
    {
        var output = new ConcurrentBag<string>();
        var command = CreateCommand(
            "i=0; while [ $i -lt 4000 ]; do printf 'stdout-0123456789012345678901234567890123456789\\n'; printf 'stderr-0123456789012345678901234567890123456789\\n' >&2; i=$((i+1)); done; printf 'stdout-marker\\n'; printf 'stderr-marker\\n' >&2",
            "1..4000 | ForEach-Object { Write-Output 'stdout-0123456789012345678901234567890123456789'; [Console]::Error.WriteLine('stderr-0123456789012345678901234567890123456789') }; Write-Output 'stdout-marker'; [Console]::Error.WriteLine('stderr-marker')");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var result = await RembgResourceManager.RunProcessAsync(
            command.FileName,
            command.Arguments,
            Path.GetTempPath(),
            line => output.Add(line),
            timeout.Token);

        Assert.True(result);
        Assert.Contains(output, line => line.Contains("stdout-marker", StringComparison.Ordinal));
        Assert.Contains(output, line => line.Contains("stderr-marker", StringComparison.Ordinal));
        Assert.True(output.Count >= 8002, $"Expected both streams to be fully drained; received {output.Count} lines.");
    }

    [Fact]
    public async Task RunProcessAsync_ReportsNonzeroExitCode()
    {
        var command = CreateCommand("exit 7", "exit 7");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RembgResourceManager.RunProcessAsync(
                command.FileName,
                command.Arguments,
                Path.GetTempPath(),
                onOutput: null,
                CancellationToken.None));

        Assert.Contains("Exit: 7", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunProcessAsync_CancellationKillsTheProcessAndReturnsPromptly()
    {
        var command = CreateCommand("sleep 30", "Start-Sleep -Seconds 30");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var startedAt = DateTime.UtcNow;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RembgResourceManager.RunProcessAsync(
                command.FileName,
                command.Arguments,
                Path.GetTempPath(),
                onOutput: null,
                cancellation.Token));

        Assert.True(DateTime.UtcNow - startedAt < TimeSpan.FromSeconds(5), "Cancellation should stop the child instead of waiting for its full duration.");
    }

    private static (string FileName, string Arguments) CreateCommand(string unixCommand, string windowsPowerShellCommand)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ("powershell.exe", $"-NoProfile -NonInteractive -Command \"{windowsPowerShellCommand}\"");
        }

        return ("/bin/sh", $"-c \"{unixCommand}\"");
    }
}
