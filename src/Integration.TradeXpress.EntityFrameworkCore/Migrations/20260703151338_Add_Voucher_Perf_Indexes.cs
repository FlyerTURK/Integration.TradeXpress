using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Add_Voucher_Perf_Indexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AppVouchers_TenantId_CompanyId_SubAccountId_VoucherDate",
                table: "AppVouchers",
                columns: new[] { "TenantId", "CompanyId", "SubAccountId", "VoucherDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AppVouchers_TenantId_CompanyId_VoucherDate",
                table: "AppVouchers",
                columns: new[] { "TenantId", "CompanyId", "VoucherDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppVouchers_TenantId_CompanyId_SubAccountId_VoucherDate",
                table: "AppVouchers");

            migrationBuilder.DropIndex(
                name: "IX_AppVouchers_TenantId_CompanyId_VoucherDate",
                table: "AppVouchers");
        }
    }
}
