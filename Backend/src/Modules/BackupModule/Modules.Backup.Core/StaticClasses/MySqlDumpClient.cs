using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Modules.Backup.Core.StaticClasses;

public sealed record MySqlDumpClient(string Executable, bool IsMariaDb, IReadOnlySet<string> Options)
{
    public static async Task<MySqlDumpClient> Find(CancellationToken token = default)
    {
        foreach (var tool in new[] { "mysqldump", "mariadb-dump" })
        {
            var version = await Probe(tool, token, "--version");
            if (version is null) continue;
            var help = await Probe(tool, token, "--no-defaults", "--help");
            if (help is null) continue;
            return FromHelp(tool, version, help);
        }
        throw new DumpFailureException("mysqldump", null, "No usable MySQL/MariaDB dump client was found. Install mysqldump or mariadb-dump on the API host PATH and restart the API to refresh its environment.");
    }

    public static MySqlDumpClient FromHelp(string executable, string version, string help)
        => new(executable, version.Contains("MariaDB", StringComparison.OrdinalIgnoreCase),
            Regex.Matches(help, @"--([a-z][a-z0-9-]*)").Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal));

    public IReadOnlyList<string> Arguments(string host, int port, string user, string database, bool tls)
    {
        var args = new List<string>();
        // Detect actual capabilities: on many hosts mysqldump is a MariaDB alias.
        if (Options.Contains("ssl-mode")) args.Add(tls ? "--ssl-mode=VERIFY_IDENTITY" : "--ssl-mode=DISABLED");
        else if (Options.Contains("ssl-verify-server-cert")) args.Add(tls ? "--ssl-verify-server-cert" : "--ssl=0");
        else throw new DumpFailureException(Executable, null, "The dump client cannot enforce the configured TLS policy. Install a supported MySQL/MariaDB dump client.");

        args.AddRange(["--protocol=TCP", "--single-transaction", "--no-tablespaces", "--routines", "--events", "--triggers", "--default-character-set=utf8mb4"]);
        if (Options.Contains("column-statistics")) args.Add("--column-statistics=0");
        // Per-database archives are logical restores, not replication provisioning.
        if (!IsMariaDb && Options.Contains("set-gtid-purged")) args.Add("--set-gtid-purged=OFF");
        if (!tls && Options.Contains("get-server-public-key")) args.Add("--get-server-public-key");
        args.AddRange(["--host=" + host, "--port=" + port, "--user=" + user, "--databases", database]);
        return args;
    }

    private static async Task<string?> Probe(string tool, CancellationToken token, params string[] arguments)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(tool)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        }};
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, token);
        var started = false;
        try
        {
            started = process.Start();
            token.ThrowIfCancellationRequested();
            var stdout = process.StandardOutput.ReadToEndAsync(linked.Token);
            var stderr = process.StandardError.ReadToEndAsync(linked.Token);
            await process.WaitForExitAsync(linked.Token);
            await Task.WhenAll(stdout, stderr);
            return process.ExitCode == 0 ? await stdout : null;
        }
        catch (Win32Exception) { return null; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        finally { if (started && !process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
    }
}
