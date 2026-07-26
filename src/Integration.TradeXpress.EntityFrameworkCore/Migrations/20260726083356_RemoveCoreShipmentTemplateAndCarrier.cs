using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCoreShipmentTemplateAndCarrier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppShipmentTemplates");

            migrationBuilder.DropTable(
                name: "AppTrCarriers");

            migrationBuilder.DropIndex(
                name: "IX_AppProducts_ShipmentTemplateId",
                table: "AppProducts");

            migrationBuilder.DropIndex(
                name: "IX_AppN11ShipmentTemplates_ShipmentTemplateId",
                table: "AppN11ShipmentTemplates");

            migrationBuilder.DropColumn(
                name: "ShipmentTemplateId",
                table: "AppProducts");

            migrationBuilder.DropColumn(
                name: "ShipmentTemplateName",
                table: "AppProducts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ShipmentTemplateId",
                table: "AppProducts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipmentTemplateName",
                table: "AppProducts",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppShipmentTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CarrierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CarrierName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ConditionalThreshold = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ConditionalUnit = table.Column<byte>(type: "tinyint", nullable: true),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeleterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveryDaysMax = table.Column<int>(type: "int", nullable: true),
                    DeliveryDaysMin = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    DispatchBranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FeeModel = table.Column<byte>(type: "tinyint", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    LastModificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MaxPurchaseQuantity = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProcessingDaysMax = table.Column<int>(type: "int", nullable: false),
                    ProcessingDaysMin = table.Column<int>(type: "int", nullable: false),
                    ReturnAccepted = table.Column<bool>(type: "bit", nullable: false),
                    ReturnBranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReturnInfo = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    ReturnSameAsDispatch = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DispatchAddress_AdditionalStreetName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DispatchAddress_AdministrativeAreaId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DispatchAddress_AdministrativeAreaIsoCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    DispatchAddress_BuildingName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DispatchAddress_BuildingNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    DispatchAddress_City = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DispatchAddress_CityCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    DispatchAddress_CountryCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    DispatchAddress_District = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DispatchAddress_DistrictCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    DispatchAddress_Floor = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    DispatchAddress_Line = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    DispatchAddress_LocalityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DispatchAddress_Neighborhood = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DispatchAddress_PostalCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    DispatchAddress_Postbox = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    DispatchAddress_Room = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    DispatchAddress_Title = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ReturnAddress_AdditionalStreetName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ReturnAddress_AdministrativeAreaId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReturnAddress_AdministrativeAreaIsoCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    ReturnAddress_BuildingName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ReturnAddress_BuildingNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ReturnAddress_City = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ReturnAddress_CityCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    ReturnAddress_CountryCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    ReturnAddress_District = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ReturnAddress_DistrictCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    ReturnAddress_Floor = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    ReturnAddress_Line = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ReturnAddress_LocalityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReturnAddress_Neighborhood = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ReturnAddress_PostalCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    ReturnAddress_Postbox = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ReturnAddress_Room = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ReturnAddress_Title = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppShipmentTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppTrCarriers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeleterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    LastModificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    N11ShipmentCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SubAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppTrCarriers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppProducts_ShipmentTemplateId",
                table: "AppProducts",
                column: "ShipmentTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_AppN11ShipmentTemplates_ShipmentTemplateId",
                table: "AppN11ShipmentTemplates",
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

            migrationBuilder.CreateIndex(
                name: "IX_AppTrCarriers_TenantId_CompanyId",
                table: "AppTrCarriers",
                columns: new[] { "TenantId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppTrCarriers_TenantId_CompanyId_Code",
                table: "AppTrCarriers",
                columns: new[] { "TenantId", "CompanyId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AppTrCarriers_TenantId_CompanyId_N11ShipmentCompanyId",
                table: "AppTrCarriers",
                columns: new[] { "TenantId", "CompanyId", "N11ShipmentCompanyId" },
                unique: true,
                filter: "[TenantId] IS NOT NULL AND [IsDeleted] = 0 AND [N11ShipmentCompanyId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppTrCarriers_TenantId_SubAccountId",
                table: "AppTrCarriers",
                columns: new[] { "TenantId", "SubAccountId" });
        }
    }
}
