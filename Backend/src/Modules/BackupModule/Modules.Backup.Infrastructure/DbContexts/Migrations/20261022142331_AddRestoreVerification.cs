using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Backup.Infrastructure.DbContexts.Migrations
{
    /// <inheritdoc />
    public partial class AddRestoreVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActiveBackupId",
                table: "BackupsTests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Automatic",
                table: "BackupsTests",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DatabaseName",
                table: "BackupsTests",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "DatabaseType",
                table: "BackupsTests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "FinishedOn",
                table: "BackupsTests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviewJson",
                table: "BackupsTests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestedById",
                table: "BackupsTests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DatabaseName",
                table: "Backups",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DatabaseType",
                table: "Backups",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BackupsTests_ActiveBackupId",
                table: "BackupsTests",
                column: "ActiveBackupId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BackupsTests_Status_TestedOn",
                table: "BackupsTests",
                columns: new[] { "Status", "TestedOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BackupsTests_ActiveBackupId",
                table: "BackupsTests");

            migrationBuilder.DropIndex(
                name: "IX_BackupsTests_Status_TestedOn",
                table: "BackupsTests");

            migrationBuilder.DropColumn(
                name: "ActiveBackupId",
                table: "BackupsTests");

            migrationBuilder.DropColumn(
                name: "Automatic",
                table: "BackupsTests");

            migrationBuilder.DropColumn(
                name: "DatabaseName",
                table: "BackupsTests");

            migrationBuilder.DropColumn(
                name: "DatabaseType",
                table: "BackupsTests");

            migrationBuilder.DropColumn(
                name: "FinishedOn",
                table: "BackupsTests");

            migrationBuilder.DropColumn(
                name: "PreviewJson",
                table: "BackupsTests");

            migrationBuilder.DropColumn(
                name: "RequestedById",
                table: "BackupsTests");

            migrationBuilder.DropColumn(
                name: "DatabaseName",
                table: "Backups");

            migrationBuilder.DropColumn(
                name: "DatabaseType",
                table: "Backups");
        }
    }
}
