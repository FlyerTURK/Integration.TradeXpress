using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class AddN11ShipmentTemplateCoreRef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ShipmentTemplateId",
                table: "AppN11ShipmentTemplates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppN11ShipmentTemplates_ShipmentTemplateId",
                table: "AppN11ShipmentTemplates",
                column: "ShipmentTemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppN11ShipmentTemplates_ShipmentTemplateId",
                table: "AppN11ShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "ShipmentTemplateId",
                table: "AppN11ShipmentTemplates");
        }
    }
}
