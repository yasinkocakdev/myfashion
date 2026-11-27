using System.ComponentModel;
using System.Diagnostics;

namespace MyFashion.Infrastructure.Processes;

/// <summary>Result of an external process. Standard output is kept as raw bytes (ffmpeg writes binary pixels).</summary>
public sealed record ProcessResult(int ExitCode, byte[] StandardOutput, string StandardError);

/// <summary>Thrown when an external tool cannot be started.</summary>
public sealed class ExternalToolException : Exception
{
    public ExternalToolException()
    {
    }

    public ExternalToolException(string message)
        : base(message)
    {
    }

    public ExternalToolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>
/// Runs an external tool without a shell: every argument goes through <see cref="ProcessStartInfo.ArgumentList"/>,
/// so characters like ";", "$( )" or spaces are never interpreted. The process tree is killed on timeout or cancel.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    private const int MaxStdoutBytes = 64 * 1024 * 1024;
    private const int MaxStderrChars = 16 * 1024;

    public async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new ExternalToolException($"Harici araç başlatılamadı: {fileName}. Kurulu mu?", ex);
        }

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            var stdoutTask = ReadBytesAsync(process.StandardOutput.BaseStream, linked.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(linked.Token);
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            return new ProcessResult(process.ExitCode, stdout, Truncate(stderr));
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"{Path.GetFileName(fileName)} {timeout.TotalSeconds:0.#} saniyede bitmedi.");
            }

            throw;
        }
    }

    private static async Task<byte[]> ReadBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxStdoutBytes)
            {
                throw new InvalidOperationException("Harici aracın çıktısı izin verilen boyutu aştı.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static string Truncate(string text) => text.Length <= MaxStderrChars ? text : text[..MaxStderrChars];

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }
}
