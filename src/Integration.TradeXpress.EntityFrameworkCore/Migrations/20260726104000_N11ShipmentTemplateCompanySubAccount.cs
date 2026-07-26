using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class N11ShipmentTemplateCompanySubAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShipmentCompanyExternalIds",
                table: "AppN11ShipmentTemplates");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "AppN11ShipmentTemplates",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ShipmentCompanies",
                table: "AppN11ShipmentTemplates",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "AppN11ShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "ShipmentCompanies",
                table: "AppN11ShipmentTemplates");

            migrationBuilder.AddColumn<string>(
                name: "ShipmentCompanyExternalIds",
                table: "AppN11ShipmentTemplates",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }
    }
}
