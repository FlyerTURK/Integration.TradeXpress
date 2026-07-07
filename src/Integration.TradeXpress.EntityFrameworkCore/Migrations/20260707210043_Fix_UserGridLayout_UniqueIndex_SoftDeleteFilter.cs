using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Fix_UserGridLayout_UniqueIndex_SoftDeleteFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppUserGridLayouts_TenantId_UserId_GridKey",
                table: "AppUserGridLayouts");

            migrationBuilder.CreateIndex(
                name: "IX_AppUserGridLayouts_TenantId_UserId_GridKey",
                table: "AppUserGridLayouts",
                columns: new[] { "TenantId", "UserId", "GridKey" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppUserGridLayouts_TenantId_UserId_GridKey",
                table: "AppUserGridLayouts");

            migrationBuilder.CreateIndex(
                name: "IX_AppUserGridLayouts_TenantId_UserId_GridKey",
                table: "AppUserGridLayouts",
                columns: new[] { "TenantId", "UserId", "GridKey" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");
        }
    }
}
