using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class RetireLegacyImageColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Images",
                table: "AppProducts");

            migrationBuilder.DropColumn(
                name: "Image",
                table: "AppMetals");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Images",
                table: "AppProducts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Image",
                table: "AppMetals",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
