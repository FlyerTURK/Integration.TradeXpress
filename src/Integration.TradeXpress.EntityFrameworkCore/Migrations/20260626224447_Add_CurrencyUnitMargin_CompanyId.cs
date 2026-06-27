using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Add_CurrencyUnitMargin_CompanyId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Marj company-based oldu: eski (CompanyId'siz) tüm marj satırlarını SIFIRLA.
            // Append-only geçmiş; herkes Passthrough'tan başlar, ilk ayarda yeni satır oluşur.
            // Böylece "tenant ama CompanyId null" geçersiz durumu hiç doğmaz.
            migrationBuilder.Sql("DELETE FROM AppCurrencyUnitMargins;");

            migrationBuilder.DropIndex(
                name: "IX_AppCurrencyUnitMargins_TenantId_CurrencyUnitId_CreationTime",
                table: "AppCurrencyUnitMargins");

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "AppCurrencyUnitMargins",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppCurrencyUnitMargins_TenantId_CompanyId_CurrencyUnitId_CreationTime",
                table: "AppCurrencyUnitMargins",
                columns: new[] { "TenantId", "CompanyId", "CurrencyUnitId", "CreationTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppCurrencyUnitMargins_TenantId_CompanyId_CurrencyUnitId_CreationTime",
                table: "AppCurrencyUnitMargins");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "AppCurrencyUnitMargins");

            migrationBuilder.CreateIndex(
                name: "IX_AppCurrencyUnitMargins_TenantId_CurrencyUnitId_CreationTime",
                table: "AppCurrencyUnitMargins",
                columns: new[] { "TenantId", "CurrencyUnitId", "CreationTime" });
        }
    }
}
