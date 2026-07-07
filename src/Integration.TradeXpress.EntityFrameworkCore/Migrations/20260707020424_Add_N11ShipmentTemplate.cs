using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Add_N11ShipmentTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppN11ShipmentTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesChannelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DeliveryFeeType = table.Column<byte>(type: "tinyint", nullable: false),
                    ShipmentMethod = table.Column<byte>(type: "tinyint", nullable: false),
                    SpecialDelivery = table.Column<bool>(type: "bit", nullable: false),
                    CombinedShipmentAllowed = table.Column<bool>(type: "bit", nullable: false),
                    UseDmallCargo = table.Column<bool>(type: "bit", nullable: false),
                    ShippingInfo = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    ExchangeInfo = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    InstallmentInfo = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CargoAccountNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ClaimShipmentCompanyExternalId = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    WarehouseAddress_City = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WarehouseAddress_Line = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    WarehouseAddress_CountryCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    WarehouseAddress_District = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    WarehouseAddress_Neighborhood = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    WarehouseAddress_PostalCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    WarehouseAddress_Title = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    WarehouseAddress_CityCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    WarehouseAddress_DistrictCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    ExchangeAddress_City = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ExchangeAddress_Line = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ExchangeAddress_CountryCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    ExchangeAddress_District = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ExchangeAddress_Neighborhood = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ExchangeAddress_PostalCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    ExchangeAddress_Title = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ExchangeAddress_CityCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    ExchangeAddress_DistrictCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    ShipmentCompanyExternalIds = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DeliverableCityCodes = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    table.PrimaryKey("PK_AppN11ShipmentTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppN11ShipmentTemplates_SalesChannelId_TemplateName",
                table: "AppN11ShipmentTemplates",
                columns: new[] { "SalesChannelId", "TemplateName" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppN11ShipmentTemplates");
        }
    }
}
