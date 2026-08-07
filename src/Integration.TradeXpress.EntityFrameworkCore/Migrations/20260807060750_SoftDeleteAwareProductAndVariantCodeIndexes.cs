using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class SoftDeleteAwareProductAndVariantCodeIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppProducts_TenantId_CompanyId_Code",
                table: "AppProducts");

            migrationBuilder.DropIndex(
                name: "IX_AppEntityVariants_TenantId_EntityName_EntityId_Code",
                table: "AppEntityVariants");

            migrationBuilder.CreateIndex(
                name: "IX_AppProducts_TenantId_CompanyId_Code",
                table: "AppProducts",
                columns: new[] { "TenantId", "CompanyId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AppEntityVariants_TenantId_EntityName_EntityId_Code",
                table: "AppEntityVariants",
                columns: new[] { "TenantId", "EntityName", "EntityId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppProducts_TenantId_CompanyId_Code",
                table: "AppProducts");

            migrationBuilder.DropIndex(
                name: "IX_AppEntityVariants_TenantId_EntityName_EntityId_Code",
                table: "AppEntityVariants");

            migrationBuilder.CreateIndex(
                name: "IX_AppProducts_TenantId_CompanyId_Code",
                table: "AppProducts",
                columns: new[] { "TenantId", "CompanyId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppEntityVariants_TenantId_EntityName_EntityId_Code",
                table: "AppEntityVariants",
                columns: new[] { "TenantId", "EntityName", "EntityId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");
        }
    }
}
