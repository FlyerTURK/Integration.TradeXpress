using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Order_OperationalData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppOrderLineOperationalData",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RemoteLineId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProductVariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProductSnapshotName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ProductSnapshotImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MatchedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CustomTextCorrections = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppOrderLineOperationalData", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppOrderOperationalData",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuyerCorrection = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BillingAddressCorrection = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ShippingAddressCorrection = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CargoProviderOverride = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CargoTrackingNumberOverride = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppOrderOperationalData", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppOrderLineOperationalData_TenantId_CompanyId",
                table: "AppOrderLineOperationalData",
                columns: new[] { "TenantId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppOrderLineOperationalData_TenantId_OrderId_RemoteLineId",
                table: "AppOrderLineOperationalData",
                columns: new[] { "TenantId", "OrderId", "RemoteLineId" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppOrderOperationalData_TenantId_CompanyId",
                table: "AppOrderOperationalData",
                columns: new[] { "TenantId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppOrderOperationalData_TenantId_OrderId",
                table: "AppOrderOperationalData",
                columns: new[] { "TenantId", "OrderId" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppOrderLineOperationalData");

            migrationBuilder.DropTable(
                name: "AppOrderOperationalData");
        }
    }
}
