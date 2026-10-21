using Modules.Backup.Core.StaticClasses;
using OctoBackup.DumpToolFixture;
using Xunit;

public sealed class DumpProcessTests
{
    private static string Output() => Path.Combine(Path.GetTempPath(), "octobackup-dump-" + Guid.NewGuid() + ".sql");
    private static string Fixture => typeof(DumpToolFixtureMarker).Assembly.Location;

    [Fact]
    public async Task SuccessfulDumpPreservesOutput_AndDoesNotOverwriteExistingFiles()
    {
        var path = Output();
        await DumpProcess.Run("dotnet", [Fixture, "success"], new Dictionary<string, string?>(), path);
        Assert.Contains("fixture-row", await File.ReadAllTextAsync(path));
        await Assert.ThrowsAsync<IOException>(() => DumpProcess.Run("dotnet", [Fixture, "success"], new Dictionary<string, string?>(), path));
        Assert.Contains("fixture-row", await File.ReadAllTextAsync(path));
        File.Delete(path);
    }

    [Fact]
    public async Task FailureDrainsLargeStderr_DeletesPartialOutput_AndReturnsNoSecrets()
    {
        var path = Output();
        var failure = await Assert.ThrowsAsync<DumpFailureException>(() => DumpProcess.Run("dotnet", [Fixture, "failure"],
            new Dictionary<string, string?> { ["MYSQL_PWD"] = "private-fixture-password" }, path, TimeSpan.FromSeconds(10)));
        Assert.Contains("exit code 2", failure.Message);
        Assert.Contains("SHOW_ROUTINE", failure.SafeMessage);
        Assert.DoesNotContain("private-fixture-password", failure.ToString());
        Assert.DoesNotContain("sensitive_routine", failure.ToString());
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task TimeoutTerminatesChildAndDeletesPartialOutput()
    {
        var path = Output();
        var failure = await Assert.ThrowsAsync<DumpFailureException>(() => DumpProcess.Run("dotnet", [Fixture, "timeout"],
            new Dictionary<string, string?>(), path, TimeSpan.FromMilliseconds(750)));
        Assert.Contains("time limit", failure.SafeMessage);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task RequestedCancellationTerminatesChildWithoutBeingReportedAsTimeout()
    {
        var path = Output();
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(750));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DumpProcess.Run("dotnet", [Fixture, "timeout"],
            new Dictionary<string, string?>(), path, cancellationToken: cancelled.Token));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task MissingClientHasActionableErrorAndNoPartialFile()
    {
        var path = Output();
        var failure = await Assert.ThrowsAsync<DumpFailureException>(() => DumpProcess.Run("octobackup-missing-tool-" + Guid.NewGuid(), [], new Dictionary<string, string?>(), path));
        Assert.Contains("PATH", failure.SafeMessage);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ClientCapabilitiesHandleWorkbenchLegacyAndMariaDbAliases_WithoutWeakeningTls()
    {
        var workbench = MySqlDumpClient.FromHelp("mysqldump", "mysqldump  Ver 8.0.43", "--ssl-mode --column-statistics --get-server-public-key --set-gtid-purged");
        var args = workbench.Arguments("db.example", 54321, "backup", "app", false);
        Assert.Contains("--column-statistics=0", args);
        Assert.Contains("--set-gtid-purged=OFF", args);
        Assert.Contains("--protocol=TCP", args);
        Assert.Contains("--port=54321", args);
        Assert.Contains("--routines", args);
        Assert.Contains("--get-server-public-key", args);
        var secure = workbench.Arguments("db.example", 3306, "backup", "app", true);
        Assert.Contains("--ssl-mode=VERIFY_IDENTITY", secure);
        Assert.DoesNotContain("--get-server-public-key", secure);
        var legacy = MySqlDumpClient.FromHelp("mysqldump", "mysqldump  Ver 5.7", "--ssl-mode --set-gtid-purged");
        Assert.DoesNotContain("--column-statistics=0", legacy.Arguments("localhost", 3306, "backup", "app", false));
        var mariaAlias = MySqlDumpClient.FromHelp("mysqldump", "mysqldump Ver 10.19 Distrib 10.11-MariaDB", "--ssl --ssl-verify-server-cert");
        var mariaArgs = mariaAlias.Arguments("localhost", 3306, "backup", "app", true);
        Assert.Contains("--ssl-verify-server-cert", mariaArgs);
        Assert.DoesNotContain("--set-gtid-purged=OFF", mariaArgs);
        Assert.DoesNotContain("--ssl-mode=VERIFY_IDENTITY", mariaArgs);
        var unsupported = MySqlDumpClient.FromHelp("mysqldump", "unknown", "--host");
        Assert.Throws<DumpFailureException>(() => unsupported.Arguments("localhost", 3306, "backup", "app", true));
    }

    [Theory]
    [InlineData("mysqldump: Got error: 1045: Access denied for user 'private' (using password: YES)", "authentication")]
    [InlineData("mysqldump: SSL connection error: certificate verify failed", "TLS")]
    [InlineData("mysqldump: unknown variable 'ssl-mode=DISABLED'", "does not support")]
    [InlineData("mysqldump: Couldn't execute SELECT FROM information_schema.COLUMN_STATISTICS", "column-statistics")]
    [InlineData("mysqldump: Couldn't execute 'FLUSH TABLES': Access denied; you need RELOAD or FLUSH_TABLES (1227)", "--set-gtid-purged=OFF")]
    public void ProviderErrorsBecomeSafeActionableMessages(string diagnostic, string expected)
    {
        var message = DumpFailureException.Explain(diagnostic);
        Assert.Contains(expected, message);
        Assert.DoesNotContain("'private'", message);
    }
}
