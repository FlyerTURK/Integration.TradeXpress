using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ShipmentTemplateId",
                table: "AppProducts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppShipmentTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    OriginAddress_City = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OriginAddress_Line = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    OriginAddress_CountryCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    OriginAddress_District = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    OriginAddress_Neighborhood = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    OriginAddress_PostalCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    OriginAddress_Title = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    OriginAddress_CityCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    OriginAddress_DistrictCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    ProcessingDaysMin = table.Column<int>(type: "int", nullable: false),
                    ProcessingDaysMax = table.Column<int>(type: "int", nullable: false),
                    FeeModel = table.Column<byte>(type: "tinyint", nullable: false),
                    ConditionalThreshold = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ConditionalUnit = table.Column<byte>(type: "tinyint", nullable: true),
                    DeliveryDaysMin = table.Column<int>(type: "int", nullable: true),
                    DeliveryDaysMax = table.Column<int>(type: "int", nullable: true),
                    CarrierName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ReturnAccepted = table.Column<bool>(type: "bit", nullable: false),
                    ReturnAddress_City = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ReturnAddress_Line = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ReturnAddress_CountryCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    ReturnAddress_District = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ReturnAddress_Neighborhood = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ReturnAddress_PostalCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    ReturnAddress_Title = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ReturnAddress_CityCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    ReturnAddress_DistrictCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    ReturnInfo = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    MaxPurchaseQuantity = table.Column<int>(type: "int", nullable: true),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
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
                    table.PrimaryKey("PK_AppShipmentTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppProducts_ShipmentTemplateId",
                table: "AppProducts",
                column: "ShipmentTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_AppShipmentTemplates_TenantId_CompanyId",
                table: "AppShipmentTemplates",
                columns: new[] { "TenantId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppShipmentTemplates_TenantId_CompanyId_Code",
                table: "AppShipmentTemplates",
                columns: new[] { "TenantId", "CompanyId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppShipmentTemplates");

            migrationBuilder.DropIndex(
                name: "IX_AppProducts_ShipmentTemplateId",
                table: "AppProducts");

            migrationBuilder.DropColumn(
                name: "ShipmentTemplateId",
                table: "AppProducts");
        }
    }
}
