using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectMarvin.Migrations.ApplicationDbContextLogDataMigrations
{
    /// <inheritdoc />
    public partial class newLogdata2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK__logEntries",
                table: "_logEntries");

            migrationBuilder.RenameTable(
                name: "_logEntries",
                newName: "LogEntries");

            migrationBuilder.AddPrimaryKey(
                name: "PK_LogEntries",
                table: "LogEntries",
                column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_LogEntries",
                table: "LogEntries");

            migrationBuilder.RenameTable(
                name: "LogEntries",
                newName: "_logEntries");

            migrationBuilder.AddPrimaryKey(
                name: "PK__logEntries",
                table: "_logEntries",
                column: "Id");
        }
    }
}
