using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class SalesChannel_UniqueCode_FilterSoftDeleted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppSalesChannels_TenantId_CompanyId_Code",
                table: "AppSalesChannels");

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannels_TenantId_CompanyId_Code",
                table: "AppSalesChannels",
                columns: new[] { "TenantId", "CompanyId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppSalesChannels_TenantId_CompanyId_Code",
                table: "AppSalesChannels");

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannels_TenantId_CompanyId_Code",
                table: "AppSalesChannels",
                columns: new[] { "TenantId", "CompanyId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");
        }
    }
}
