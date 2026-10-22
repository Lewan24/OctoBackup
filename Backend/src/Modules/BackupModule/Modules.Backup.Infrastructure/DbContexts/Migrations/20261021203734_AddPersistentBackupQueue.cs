using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Backup.Infrastructure.DbContexts.Migrations
{
    /// <inheritdoc />
    public partial class AddPersistentBackupQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BackupRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ServerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ServerName = table.Column<string>(type: "TEXT", nullable: false),
                    DatabaseName = table.Column<string>(type: "TEXT", nullable: false),
                    TargetKey = table.Column<string>(type: "TEXT", nullable: false),
                    ActiveDatabaseKey = table.Column<string>(type: "TEXT", nullable: true),
                    RequestedById = table.Column<string>(type: "TEXT", nullable: true),
                    RequestedByName = table.Column<string>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    ScheduleIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    EligibleAt = table.Column<long>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    FinishedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Message = table.Column<string>(type: "TEXT", nullable: true),
                    BackupId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsHidden = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackupRequests_ActiveDatabaseKey",
                table: "BackupRequests",
                column: "ActiveDatabaseKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BackupRequests_FinishedAt",
                table: "BackupRequests",
                column: "FinishedAt");

            migrationBuilder.CreateIndex(
                name: "IX_BackupRequests_RequestedById",
                table: "BackupRequests",
                column: "RequestedById");

            migrationBuilder.CreateIndex(
                name: "IX_BackupRequests_ServerId",
                table: "BackupRequests",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_BackupRequests_Status_EligibleAt_CreatedAt",
                table: "BackupRequests",
                columns: new[] { "Status", "EligibleAt", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupRequests");
        }
    }
}
