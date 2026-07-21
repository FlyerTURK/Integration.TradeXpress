using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class AddCountryAddressFormat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AdministrativeAreaType",
                table: "AppCountries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LocalityType",
                table: "AppCountries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PostalCodeType",
                table: "AppCountries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SubLocalityType",
                table: "AppCountries",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdministrativeAreaType",
                table: "AppCountries");

            migrationBuilder.DropColumn(
                name: "LocalityType",
                table: "AppCountries");

            migrationBuilder.DropColumn(
                name: "PostalCodeType",
                table: "AppCountries");

            migrationBuilder.DropColumn(
                name: "SubLocalityType",
                table: "AppCountries");
        }
    }
}
