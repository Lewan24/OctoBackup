using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Backup.Infrastructure.DbContexts.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupEncryption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EncryptionKeyId",
                table: "Backups",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BackupEncryptionKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActiveScope = table.Column<string>(type: "TEXT", nullable: true),
                    Scope = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Salt = table.Column<string>(type: "TEXT", nullable: false),
                    ProtectedKey = table.Column<string>(type: "TEXT", nullable: false),
                    ProtectedRecoveryPassword = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupEncryptionKeys", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackupEncryptionKeys_ActiveScope",
                table: "BackupEncryptionKeys",
                column: "ActiveScope",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupEncryptionKeys");

            migrationBuilder.DropColumn(
                name: "EncryptionKeyId",
                table: "Backups");
        }
    }
}
