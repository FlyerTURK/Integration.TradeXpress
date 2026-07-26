using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class RenameCarriersTableToTrCarriers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_AppCarriers",
                table: "AppCarriers");

            migrationBuilder.RenameTable(
                name: "AppCarriers",
                newName: "AppTrCarriers");

            migrationBuilder.RenameIndex(
                name: "IX_AppCarriers_Code",
                table: "AppTrCarriers",
                newName: "IX_AppTrCarriers_Code");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AppTrCarriers",
                table: "AppTrCarriers",
                column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_AppTrCarriers",
                table: "AppTrCarriers");

            migrationBuilder.RenameTable(
                name: "AppTrCarriers",
                newName: "AppCarriers");

            migrationBuilder.RenameIndex(
                name: "IX_AppTrCarriers_Code",
                table: "AppCarriers",
                newName: "IX_AppCarriers_Code");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AppCarriers",
                table: "AppCarriers",
                column: "Id");
        }
    }
}
