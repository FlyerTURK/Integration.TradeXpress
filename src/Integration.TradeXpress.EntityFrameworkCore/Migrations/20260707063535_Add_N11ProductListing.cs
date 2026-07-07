using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Add_N11ProductListing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppN11ProductListings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesChannelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryExternalId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CategoryName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Condition = table.Column<byte>(type: "tinyint", nullable: false),
                    ShipmentTemplateName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Domestic = table.Column<bool>(type: "bit", nullable: false),
                    MaxPurchaseQuantity = table.Column<int>(type: "int", nullable: true),
                    N11ProductId = table.Column<long>(type: "bigint", nullable: true),
                    SaleStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ApprovalStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Attributes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SpecialInfo = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppN11ProductListings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppN11ProductListings_SalesChannelId_ProductId",
                table: "AppN11ProductListings",
                columns: new[] { "SalesChannelId", "ProductId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AppN11ProductListings_TenantId_CompanyId",
                table: "AppN11ProductListings",
                columns: new[] { "TenantId", "CompanyId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppN11ProductListings");
        }
    }
}
