using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Rename_Purity_To_Factor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PurityChange",
                table: "AppScraps",
                newName: "FactorChange");

            migrationBuilder.RenameColumn(
                name: "Purity",
                table: "AppScraps",
                newName: "Factor");

            migrationBuilder.RenameColumn(
                name: "PurityChange",
                table: "AppMetals",
                newName: "FactorChange");

            migrationBuilder.RenameColumn(
                name: "Purity",
                table: "AppMetals",
                newName: "Factor");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FactorChange",
                table: "AppScraps",
                newName: "PurityChange");

            migrationBuilder.RenameColumn(
                name: "Factor",
                table: "AppScraps",
                newName: "Purity");

            migrationBuilder.RenameColumn(
                name: "FactorChange",
                table: "AppMetals",
                newName: "PurityChange");

            migrationBuilder.RenameColumn(
                name: "Factor",
                table: "AppMetals",
                newName: "Purity");
        }
    }
}
