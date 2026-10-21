using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Backup.Infrastructure.DbContexts.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutomaticBackupTestConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShouldTestEveryBackup = table.Column<bool>(type: "INTEGER", nullable: false),
                    TestFrequency = table.Column<short>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomaticBackupTestConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Backups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ServerConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Backups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BackupsTests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BackupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TestedOn = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsSuccess = table.Column<bool>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupsTests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DbConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsDisabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConnectionName = table.Column<string>(type: "TEXT", nullable: false),
                    ServerHost = table.Column<string>(type: "TEXT", nullable: false),
                    ServerPort = table.Column<short>(type: "INTEGER", nullable: false),
                    DbName = table.Column<string>(type: "TEXT", nullable: false),
                    DbType = table.Column<int>(type: "INTEGER", nullable: false),
                    DbUser = table.Column<string>(type: "TEXT", nullable: false),
                    DbPasswd = table.Column<string>(type: "TEXT", nullable: false),
                    IsTunnelRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    TunnelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BackupEncryptionKeyHas = table.Column<string>(type: "TEXT", nullable: true),
                    AutoTestBackupsConfigId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DbConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DbServerTunnels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ServerHost = table.Column<string>(type: "TEXT", nullable: false),
                    SshPort = table.Column<int>(type: "INTEGER", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    UsePasswordAuth = table.Column<bool>(type: "INTEGER", nullable: false),
                    Password = table.Column<string>(type: "TEXT", nullable: true),
                    PrivateKeyContent = table.Column<string>(type: "TEXT", nullable: true),
                    LocalPort = table.Column<int>(type: "INTEGER", nullable: false),
                    RemoteHost = table.Column<string>(type: "TEXT", nullable: false),
                    RemotePort = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DbServerTunnels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Schedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DbConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConfigurationJson = table.Column<string>(type: "TEXT", nullable: false),
                    NextBackupDate = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Schedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsersNotificationsSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    NotifyOnSuccessfulBackup = table.Column<bool>(type: "INTEGER", nullable: false),
                    NotifyOnErrors = table.Column<bool>(type: "INTEGER", nullable: false),
                    NotifyOnUpdates = table.Column<bool>(type: "INTEGER", nullable: false),
                    NotifyOnChanges = table.Column<bool>(type: "INTEGER", nullable: false),
                    NotifyOnFailedTests = table.Column<bool>(type: "INTEGER", nullable: false),
                    NotifyOnFailedBackups = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsersNotificationsSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsersServers",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ServerId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomaticBackupTestConfigs");

            migrationBuilder.DropTable(
                name: "Backups");

            migrationBuilder.DropTable(
                name: "BackupsTests");

            migrationBuilder.DropTable(
                name: "DbConnections");

            migrationBuilder.DropTable(
                name: "DbServerTunnels");

            migrationBuilder.DropTable(
                name: "Schedules");

            migrationBuilder.DropTable(
                name: "UsersNotificationsSettings");

            migrationBuilder.DropTable(
                name: "UsersServers");
        }
    }
}
