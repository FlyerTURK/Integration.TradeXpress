using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class ScopeProductBarcodeUniquenessToCompany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppEntityVariants_TenantId_Barcode",
                table: "AppEntityVariants");

            migrationBuilder.CreateIndex(
                name: "IX_AppEntityVariants_TenantId_CompanyId_Barcode",
                table: "AppEntityVariants",
                columns: new[] { "TenantId", "CompanyId", "Barcode" },
                unique: true,
                filter: "[EntityName] = 'Product' AND [Barcode] IS NOT NULL AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppEntityVariants_TenantId_CompanyId_Barcode",
                table: "AppEntityVariants");

            migrationBuilder.CreateIndex(
                name: "IX_AppEntityVariants_TenantId_Barcode",
                table: "AppEntityVariants",
                columns: new[] { "TenantId", "Barcode" },
                unique: true,
                filter: "[EntityName] = 'Product' AND [Barcode] IS NOT NULL");
        }
    }
}
