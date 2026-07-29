using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class DropLegacyPersonalizationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPersonalizable",
                table: "AppSalesChannelEtsyProducts");

            migrationBuilder.DropColumn(
                name: "PersonalizationCharCountMax",
                table: "AppSalesChannelEtsyProducts");

            migrationBuilder.DropColumn(
                name: "PersonalizationInstructions",
                table: "AppSalesChannelEtsyProducts");

            migrationBuilder.DropColumn(
                name: "PersonalizationIsRequired",
                table: "AppSalesChannelEtsyProducts");

            migrationBuilder.DropColumn(
                name: "IsPersonalizable",
                table: "AppProducts");

            migrationBuilder.DropColumn(
                name: "PersonalizationCharCountMax",
                table: "AppProducts");

            migrationBuilder.DropColumn(
                name: "PersonalizationInstructions",
                table: "AppProducts");

            migrationBuilder.DropColumn(
                name: "PersonalizationIsRequired",
                table: "AppProducts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPersonalizable",
                table: "AppSalesChannelEtsyProducts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PersonalizationCharCountMax",
                table: "AppSalesChannelEtsyProducts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PersonalizationInstructions",
                table: "AppSalesChannelEtsyProducts",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PersonalizationIsRequired",
                table: "AppSalesChannelEtsyProducts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPersonalizable",
                table: "AppProducts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PersonalizationCharCountMax",
                table: "AppProducts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PersonalizationInstructions",
                table: "AppProducts",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PersonalizationIsRequired",
                table: "AppProducts",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
