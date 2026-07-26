using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class TrCarrierCompanyOwned : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppTrCarriers_Code",
                table: "AppTrCarriers");

            migrationBuilder.DropIndex(
                name: "IX_AppN11ShipmentCompanies_CoreCarrierId",
                table: "AppN11ShipmentCompanies");

            migrationBuilder.DropColumn(
                name: "CoreCarrierId",
                table: "AppN11ShipmentCompanies");

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "AppTrCarriers",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "N11ShipmentCompanyId",
                table: "AppTrCarriers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "AppTrCarriers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppTrCarriers_TenantId_CompanyId",
                table: "AppTrCarriers",
                columns: new[] { "TenantId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppTrCarriers_TenantId_CompanyId_Code",
                table: "AppTrCarriers",
                columns: new[] { "TenantId", "CompanyId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AppTrCarriers_TenantId_CompanyId_N11ShipmentCompanyId",
                table: "AppTrCarriers",
                columns: new[] { "TenantId", "CompanyId", "N11ShipmentCompanyId" },
                unique: true,
                filter: "[TenantId] IS NOT NULL AND [IsDeleted] = 0 AND [N11ShipmentCompanyId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppTrCarriers_TenantId_CompanyId",
                table: "AppTrCarriers");

            migrationBuilder.DropIndex(
                name: "IX_AppTrCarriers_TenantId_CompanyId_Code",
                table: "AppTrCarriers");

            migrationBuilder.DropIndex(
                name: "IX_AppTrCarriers_TenantId_CompanyId_N11ShipmentCompanyId",
                table: "AppTrCarriers");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "AppTrCarriers");

            migrationBuilder.DropColumn(
                name: "N11ShipmentCompanyId",
                table: "AppTrCarriers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AppTrCarriers");

            migrationBuilder.AddColumn<Guid>(
                name: "CoreCarrierId",
                table: "AppN11ShipmentCompanies",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppTrCarriers_Code",
                table: "AppTrCarriers",
                column: "Code",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AppN11ShipmentCompanies_CoreCarrierId",
                table: "AppN11ShipmentCompanies",
                column: "CoreCarrierId");
        }
    }
}
