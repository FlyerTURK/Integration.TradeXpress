using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class ExchangeRate_UniqueWindowIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppExchangeRates_CurrencyUnitId_RateDate",
                table: "AppExchangeRates");

            migrationBuilder.CreateIndex(
                name: "IX_AppExchangeRates_TenantId_CurrencyUnitId_RateDate",
                table: "AppExchangeRates",
                columns: new[] { "TenantId", "CurrencyUnitId", "RateDate" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppExchangeRates_TenantId_CurrencyUnitId_RateDate",
                table: "AppExchangeRates");

            migrationBuilder.CreateIndex(
                name: "IX_AppExchangeRates_CurrencyUnitId_RateDate",
                table: "AppExchangeRates",
                columns: new[] { "CurrencyUnitId", "RateDate" });
        }
    }
}
