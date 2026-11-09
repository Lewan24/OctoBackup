using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Modules.Backup.Application.Static;
using Modules.Backup.Core.StaticClasses;
using Modules.Shared.Common;
using Npgsql;
using Xunit;

public class ProviderConfigurationTests
{
    [Fact]
    public void ConfiguredTools_Are_Absolute_Deployment_Paths_And_Never_Silently_Fall_Back()
    {
        Assert.Throws<DumpFailureException>(() => DumpToolPaths.Configured("../mysqldump", "DatabaseTools:MySqlDumpPath"));
        Assert.Throws<DumpFailureException>(() => DumpToolPaths.Postgres(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+"-missing")));
        var file = Path.GetTempFileName();
        try { Assert.Equal(file, DumpToolPaths.Postgres(file)); Assert.Equal(new[] { file }, DumpToolPaths.MySqlCandidates(file)); }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Connection_Diagnostics_Classify_Without_Leaking_Provider_Details()
    {
        var auth = new PostgresException("private query and password", "ERROR", "ERROR", "28P01");
        var result = DatabaseConnectionErrors.Describe(auth);
        Assert.Contains("authentication", result); Assert.DoesNotContain("private", result);
        Assert.Contains("key ring", DatabaseConnectionErrors.Describe(new CryptographicException("private-secret")));
        Assert.Contains("TLS", DatabaseConnectionErrors.Describe(new NpgsqlException("private", new AuthenticationException("secret certificate"))));
        Assert.Contains("Docker", DatabaseConnectionErrors.Describe(new SocketException((int)SocketError.HostNotFound)));
        Assert.Contains("pg_hba", DatabaseConnectionErrors.Describe(new PostgresException("private", "ERROR", "ERROR", "28000")));
        Assert.Contains("does not exist", DatabaseConnectionErrors.Describe(new PostgresException("private", "ERROR", "ERROR", "3D000")));
    }

    [Fact]
    public void SqlServer_Maps_A_Dedicated_Staging_Share_Without_Exposing_Application_Storage()
    {
        var data = Path.Combine(Path.GetTempPath(),"octobackup-storage-"+Guid.NewGuid());
        var old = DbCommon.DataDirectory;
        var stage = Path.Combine(Path.GetTempPath(),"octobackup-stage-"+Guid.NewGuid());
        Directory.CreateDirectory(stage);
        try
        {
            DbCommon.Configure(data,null);
            var output = Path.Combine(SafeBackupPath.Root,"server","probe","fixture.bak");
            var linux = SqlServerBackupStorage.Resolve(output,"/var/opt/mssql/backup",stage);
            Assert.True(linux.CopyRequired); Assert.Equal(Path.Combine(stage,"fixture.bak"),linux.LocalPath);
            Assert.Equal("/var/opt/mssql/backup/fixture.bak",linux.RemotePath);
            var windows = SqlServerBackupStorage.Resolve(output,@"D:\Backups",stage);
            Assert.Equal(@"D:\Backups\fixture.bak",windows.RemotePath);
            Assert.Throws<DumpFailureException>(() => SqlServerBackupStorage.Resolve(output,null,stage));
            Assert.Throws<DumpFailureException>(() => SqlServerBackupStorage.Resolve(output,"relative",stage));
            Assert.Throws<DumpFailureException>(() => SqlServerBackupStorage.Resolve(Path.Combine(stage,"outside.bak"),"/backup",stage));
            if (!OperatingSystem.IsWindows())
            {
                File.CreateSymbolicLink(Path.Combine(stage,"fixture.bak"),Path.Combine(stage,"missing"));
                Assert.Throws<DumpFailureException>(() => SqlServerBackupStorage.Resolve(output,"/backup",stage));
            }
        }
        finally { DbCommon.Configure(old,null); Directory.Delete(stage,true); }
    }

    [Theory]
    [InlineData("pg_dump: error: aborting because of server version mismatch; server version: 18; pg_dump version: 16", "older")]
    [InlineData("password authentication failed for user 'private'", "authentication")]
    [InlineData("no pg_hba.conf entry for host 'private'", "pg_hba")]
    public void PostgreSql_Dump_Errors_Are_Actionable_And_Safe(string raw,string expected)
    {
        var message = DumpFailureException.Explain(raw); Assert.Contains(expected,message); Assert.DoesNotContain("'private'",message);
    }
}
