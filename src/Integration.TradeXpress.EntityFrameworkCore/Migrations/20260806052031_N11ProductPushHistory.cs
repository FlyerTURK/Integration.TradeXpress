using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class N11ProductPushHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSalesChannelTrN11ProductPushHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesChannelTrN11ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SellerStockCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PushedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PushKind = table.Column<byte>(type: "tinyint", nullable: false),
                    SalePrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CurrencyType = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    VariantOptions = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Images = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: true),
                    RemoteReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSalesChannelTrN11ProductPushHistories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelTrN11ProductPushHistories_TenantId_CompanyId",
                table: "AppSalesChannelTrN11ProductPushHistories",
                columns: new[] { "TenantId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannelTrN11ProductPushHistories_TenantId_SalesChannelTrN11ProductId_SellerStockCode_PushedAtUtc",
                table: "AppSalesChannelTrN11ProductPushHistories",
                columns: new[] { "TenantId", "SalesChannelTrN11ProductId", "SellerStockCode", "PushedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSalesChannelTrN11ProductPushHistories");
        }
    }
}
