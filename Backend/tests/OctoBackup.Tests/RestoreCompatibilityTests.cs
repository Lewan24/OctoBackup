using Microsoft.Extensions.Configuration;
using Modules.Backup.Application.Services;
using Modules.Backup.Core.Entities.DbContext;
using Modules.Backup.Shared.Enums;
using Xunit;

public sealed class RestoreCompatibilityTests
{
    [Theory]
    [InlineData("16.12", "16.4", "postgres:16-bookworm", 16)]
    [InlineData("17.11", "17.6", "postgres:17", 17)]
    [InlineData("16.12", "17.6", "postgres:17", 17)]
    [InlineData("17.11", "18.1", "postgres:18-alpine", 18)]
    public void PostgreSql_SelectsAtLeastBothServerAndDumpClientMajor(string source, string client, string image, int major)
    {
        var profile = RestoreArchiveProfile.FromHeader(new BackupTest { DatabaseType = DatabaseType.PostgreSql },
            $"-- PostgreSQL database dump\r\n-- Dumped from database version {source}\r\n-- Dumped by pg_dump version {client}\r\nSET transaction_timeout = 0;", new ConfigurationBuilder().Build());
        Assert.Equal(image, profile.Image); Assert.Equal(major, profile.PostgresMajor);
    }

    [Theory]
    [InlineData("5.7.44", "mysql:5.7")]
    [InlineData("8.0.43", "mysql:8.0")]
    [InlineData("8.4.8", "mysql:8.4")]
    [InlineData("10.11.14-MariaDB", "mariadb:10.11")]
    public void MySql_UsesSourceServerReleaseAndDatabase_NotDumpClientVersion(string version, string image)
    {
        var profile = RestoreArchiveProfile.FromHeader(new BackupTest { DatabaseType = DatabaseType.MySql, DatabaseName = "old_metadata" },
            $"-- MySQL dump 10.13 Distrib 8.0.43, for Win64\n-- Server version\t{version}\n-- Current Database: `actual_database`\n", new ConfigurationBuilder().Build());
        Assert.Equal(image, profile.Image); Assert.Equal("actual_database", profile.Database);
        Assert.Equal(version.Contains("MariaDB"), profile.MariaDb);
    }

    [Fact]
    public void VersionMappingsAreDeploymentOwned_AndUnknownMajorsFailBeforeContainerCreation()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["RestoreTests:PostgresImages:17"] = "private/approved@sha256:deployment" }).Build();
        var test = new BackupTest { DatabaseType = DatabaseType.PostgreSql };
        Assert.Equal("private/approved@sha256:deployment", RestoreArchiveProfile.FromHeader(test, "-- Dumped by pg_dump version 17.6\n", config).Image);
        Assert.Throws<RestoreTestException>(() => RestoreArchiveProfile.FromHeader(test, "-- Dumped by pg_dump version 99.1\n", config));
    }

    [Theory]
    [InlineData(DatabaseType.MySql, "-- PostgreSQL database dump\n")]
    [InlineData(DatabaseType.PostgreSql, "-- MySQL dump 10.13\n")]
    public void ARecognizedArchiveCannotBeSentToTheWrongEngine(DatabaseType type, string header)
        => Assert.Throws<RestoreTestException>(() => RestoreArchiveProfile.FromHeader(new BackupTest { DatabaseType = type }, header, new ConfigurationBuilder().Build()));

    [Theory]
    [InlineData("mysql")]
    [InlineData("information_schema")]
    [InlineData("sys")]
    [InlineData("performance_schema")]
    public void ProtectedMySqlDatabasesCannotBeSelected(string database)
        => Assert.Throws<RestoreTestException>(() => RestoreArchiveProfile.FromHeader(new BackupTest { DatabaseType = DatabaseType.MySql, DatabaseName = database }, "", new ConfigurationBuilder().Build()));
}
