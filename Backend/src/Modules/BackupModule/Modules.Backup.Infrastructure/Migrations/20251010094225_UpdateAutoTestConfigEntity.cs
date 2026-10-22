using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Backup.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateAutoTestConfigEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShouldTestEveryBackup",
                table: "AutomaticBackupTestConfigs");

            migrationBuilder.DropColumn(
                name: "TestFrequency",
                table: "AutomaticBackupTestConfigs");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "AutomaticBackupTestConfigs",
                newName: "ServerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ServerId",
                table: "AutomaticBackupTestConfigs",
                newName: "Name");

            migrationBuilder.AddColumn<bool>(
                name: "ShouldTestEveryBackup",
                table: "AutomaticBackupTestConfigs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<short>(
                name: "TestFrequency",
                table: "AutomaticBackupTestConfigs",
                type: "INTEGER",
                nullable: false,
                defaultValue: (short)0);
        }
    }
}
