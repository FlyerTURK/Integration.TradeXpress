using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class N11ShipmentTemplateEstimatedCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedCost",
                table: "AppN11ShipmentTemplates",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EstimatedCostCurrencyUnitId",
                table: "AppN11ShipmentTemplates",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EstimatedCost",
                table: "AppN11ShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "EstimatedCostCurrencyUnitId",
                table: "AppN11ShipmentTemplates");
        }
    }
}
