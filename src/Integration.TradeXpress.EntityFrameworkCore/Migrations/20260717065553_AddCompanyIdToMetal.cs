using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyIdToMetal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppMetals_TenantId_Code",
                table: "AppMetals");

            migrationBuilder.DropIndex(
                name: "IX_AppMetals_TenantId_FollowingUnitId",
                table: "AppMetals");

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "AppMetals",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppMetals_TenantId_CompanyId_Code",
                table: "AppMetals",
                columns: new[] { "TenantId", "CompanyId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL AND [CompanyId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppMetals_TenantId_CompanyId_FollowingUnitId",
                table: "AppMetals",
                columns: new[] { "TenantId", "CompanyId", "FollowingUnitId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppMetals_TenantId_CompanyId_Code",
                table: "AppMetals");

            migrationBuilder.DropIndex(
                name: "IX_AppMetals_TenantId_CompanyId_FollowingUnitId",
                table: "AppMetals");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "AppMetals");

            migrationBuilder.CreateIndex(
                name: "IX_AppMetals_TenantId_Code",
                table: "AppMetals",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppMetals_TenantId_FollowingUnitId",
                table: "AppMetals",
                columns: new[] { "TenantId", "FollowingUnitId" });
        }
    }
}
