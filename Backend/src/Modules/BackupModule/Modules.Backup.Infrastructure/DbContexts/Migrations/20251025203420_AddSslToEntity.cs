using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Backup.Infrastructure.DbContexts.Migrations
{
    /// <inheritdoc />
    public partial class AddSslToEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SsL",
                table: "DbConnections",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SsL",
                table: "DbConnections");
        }
    }
}
