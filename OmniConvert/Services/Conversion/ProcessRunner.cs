using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OmniConvert.Services.Conversion;

public static class ProcessRunner
{
    public sealed class ProcessResult
    {
        public required int ExitCode { get; init; }

        public required string StandardOutput { get; init; }

        public required string StandardError { get; init; }
    }

    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
            {
                throw new ConversionException($"无法启动转换引擎:{fileName}");
            }
        }
        catch (Win32Exception ex)
        {
            throw new ConversionException($"无法启动转换引擎:{fileName}。引擎文件可能缺失或损坏。", ex);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, CancellationToken.None).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            KillProcessTree(process);
            throw new ConversionException("转换超时,已终止转换进程。");
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return new ProcessResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = stdout,
            StandardError = stderr
        };
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }
}
