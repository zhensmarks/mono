using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PixelcutCompact.Services;

public sealed class RembgCliService
{
    public string? ExecutablePath { get; set; }
    public string Model { get; set; } = "u2netp";

    public async Task ProcessImageAsync(string inputPath, string outputPath, CancellationToken ct)
    {
        var model = string.IsNullOrWhiteSpace(Model) ? "u2netp" : Model.Trim();
        var rembgArgs = $"i -m {model} \"{inputPath}\" \"{outputPath}\"";

        try
        {
            await RunProcessAsync(GetRembgCommand(), rembgArgs, ct);
            return;
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is Win32Exception)
        {
            // Fallback to python module invocation.
        }

        await RunProcessAsync("python", $"-m rembg {rembgArgs}", ct);
    }

    private string GetRembgCommand()
    {
        return string.IsNullOrWhiteSpace(ExecutablePath) ? "rembg" : ExecutablePath!;
    }

    private static async Task RunProcessAsync(string fileName, string arguments, CancellationToken ct)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Tidak bisa menjalankan command: {fileName}");
            }
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"Command tidak ditemukan: {fileName}. Pastikan rembg sudah terpasang.", ex);
        }

        var readStdout = Task.Run(async () =>
        {
            while (!process.StandardOutput.EndOfStream)
            {
                var line = await process.StandardOutput.ReadLineAsync();
                if (!string.IsNullOrWhiteSpace(line))
                    stdout.AppendLine(line);
            }
        }, ct);

        var readStderr = Task.Run(async () =>
        {
            while (!process.StandardError.EndOfStream)
            {
                var line = await process.StandardError.ReadLineAsync();
                if (!string.IsNullOrWhiteSpace(line))
                    stderr.AppendLine(line);
            }
        }, ct);

        try
        {
            await process.WaitForExitAsync(ct);
            await Task.WhenAll(readStdout, readStderr);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        if (process.ExitCode != 0)
        {
            var err = stderr.ToString().Trim();
            if (string.IsNullOrWhiteSpace(err))
                err = stdout.ToString().Trim();
            if (string.IsNullOrWhiteSpace(err))
                err = $"ExitCode {process.ExitCode}";

            throw new InvalidOperationException($"rembg gagal: {err}");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(true);
        }
        catch { }
    }
}
