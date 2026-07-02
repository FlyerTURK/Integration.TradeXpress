using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Add_AssayOffice_CompanyId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppAssayOffices_TenantId_Code",
                table: "AppAssayOffices");

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "AppAssayOffices",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_AppAssayOffices_TenantId_CompanyId",
                table: "AppAssayOffices",
                columns: new[] { "TenantId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppAssayOffices_TenantId_CompanyId_Code",
                table: "AppAssayOffices",
                columns: new[] { "TenantId", "CompanyId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppAssayOffices_TenantId_CompanyId",
                table: "AppAssayOffices");

            migrationBuilder.DropIndex(
                name: "IX_AppAssayOffices_TenantId_CompanyId_Code",
                table: "AppAssayOffices");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "AppAssayOffices");

            migrationBuilder.CreateIndex(
                name: "IX_AppAssayOffices_TenantId_Code",
                table: "AppAssayOffices",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");
        }
    }
}
