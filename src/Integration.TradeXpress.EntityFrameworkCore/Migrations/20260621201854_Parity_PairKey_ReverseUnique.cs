using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Parity_PairKey_ReverseUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppParities_TenantId_BaseCurrencyUnitId_QuoteCurrencyUnitId",
                table: "AppParities");

            migrationBuilder.AddColumn<string>(
                name: "PairKey",
                table: "AppParities",
                type: "nvarchar(72)",
                maxLength: 72,
                nullable: false,
                defaultValue: "");

            // Mevcut satırlar için PairKey backfill — C# BuildPairKey ile birebir (küçük harf GUID metni,
            // ordinal sıralama = BIN2 collation, ayraçsız birleştirme). Unique index'ten ÖNCE şart.
            migrationBuilder.Sql(@"
UPDATE [AppParities]
SET [PairKey] =
    CASE WHEN LOWER(CONVERT(char(36), [BaseCurrencyUnitId])) COLLATE Latin1_General_BIN2
              <= LOWER(CONVERT(char(36), [QuoteCurrencyUnitId])) COLLATE Latin1_General_BIN2
         THEN LOWER(CONVERT(char(36), [BaseCurrencyUnitId])) + LOWER(CONVERT(char(36), [QuoteCurrencyUnitId]))
         ELSE LOWER(CONVERT(char(36), [QuoteCurrencyUnitId])) + LOWER(CONVERT(char(36), [BaseCurrencyUnitId])) END;");

            migrationBuilder.CreateIndex(
                name: "IX_AppParities_TenantId_PairKey",
                table: "AppParities",
                columns: new[] { "TenantId", "PairKey" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppParities_TenantId_PairKey",
                table: "AppParities");

            migrationBuilder.DropColumn(
                name: "PairKey",
                table: "AppParities");

            migrationBuilder.CreateIndex(
                name: "IX_AppParities_TenantId_BaseCurrencyUnitId_QuoteCurrencyUnitId",
                table: "AppParities",
                columns: new[] { "TenantId", "BaseCurrencyUnitId", "QuoteCurrencyUnitId" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");
        }
    }
}
