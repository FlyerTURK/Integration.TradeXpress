using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Add_N11ShipmentTemplate_ConditionalShipping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ConditionalShippingThreshold",
                table: "AppN11ShipmentTemplates",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "ConditionalShippingUnit",
                table: "AppN11ShipmentTemplates",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConditionalShippingThreshold",
                table: "AppN11ShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "ConditionalShippingUnit",
                table: "AppN11ShipmentTemplates");
        }
    }
}
