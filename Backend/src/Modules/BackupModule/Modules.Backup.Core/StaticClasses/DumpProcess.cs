using System.Diagnostics;
using System.ComponentModel;
using System.Text;

namespace Modules.Backup.Core.StaticClasses;

public static class DumpProcess
{
    public static async Task Run(string executable, IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string?> environment, string output, TimeSpan? timeoutLimit = null, CancellationToken cancellationToken = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        foreach (var (key, value) in environment) process.StartInfo.Environment[key] = value;
        using var timeout = new CancellationTokenSource(timeoutLimit ?? TimeSpan.FromMinutes(30));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);
        FileStream? file = null;
        Task<string>? stderr = null;
        var started = false;
        var completed = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            file = SafeBackupPath.CreatePrivateFile(output);
            try { started = process.Start(); }
            catch (Win32Exception)
            {
                throw new DumpFailureException(executable, null, "The dump client could not start. Check its installation, PATH and executable permissions on the API host.");
            }
            // Dump tools must fail instead of waiting indefinitely for an interactive password.
            process.StandardInput.Close();
            stderr = ReadDiagnostic(process.StandardError);
            await process.StandardOutput.BaseStream.CopyToAsync(file, linked.Token);
            await process.WaitForExitAsync(linked.Token);
            var diagnostic = await stderr;
            if (process.ExitCode != 0) throw new DumpFailureException(executable, process.ExitCode, DumpFailureException.Explain(diagnostic));
            await file.FlushAsync(linked.Token);
            completed = true;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new DumpFailureException(executable, null, "The database export exceeded its time limit. Check database load and network connectivity.");
        }
        finally
        {
            if (started && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            if (stderr is not null) await stderr;
            if (file is not null)
            {
                await file.DisposeAsync();
                if (!completed) File.Delete(output);
            }
        }
    }

    private static async Task<string> ReadDiagnostic(StreamReader reader)
    {
        const int limit = 8192;
        var diagnostic = new StringBuilder(limit);
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
            if (diagnostic.Length < limit) diagnostic.Append(buffer, 0, Math.Min(read, limit - diagnostic.Length));
        return diagnostic.ToString();
    }
}
