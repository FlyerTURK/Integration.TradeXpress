using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Added_Currency_Entities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppCurrencyUnitMargins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrencyUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MarginOnBuy_Type = table.Column<int>(type: "int", nullable: false),
                    MarginOnBuy_Value = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: false),
                    MarginOnSell_Type = table.Column<int>(type: "int", nullable: false),
                    MarginOnSell_Value = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: false),
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
                    table.PrimaryKey("PK_AppCurrencyUnitMargins", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppCurrencyUnits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    FollowingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FollowingMargin_Type = table.Column<int>(type: "int", nullable: true),
                    FollowingMargin_Value = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: true),
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
                    table.PrimaryKey("PK_AppCurrencyUnits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppExchangeRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CurrencyUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MarketPriceOnBuy = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: false),
                    MarketPriceOnSell = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: false),
                    AppliedMarginOnBuy_Type = table.Column<int>(type: "int", nullable: false),
                    AppliedMarginOnBuy_Value = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: false),
                    AppliedMarginOnSell_Type = table.Column<int>(type: "int", nullable: false),
                    AppliedMarginOnSell_Value = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RateDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GuardFired = table.Column<bool>(type: "bit", nullable: false),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppExchangeRates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppCurrencyUnitMargins_TenantId_CurrencyUnitId",
                table: "AppCurrencyUnitMargins",
                columns: new[] { "TenantId", "CurrencyUnitId" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppCurrencyUnits_TenantId_Code",
                table: "AppCurrencyUnits",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppExchangeRates_CurrencyUnitId_RateDate",
                table: "AppExchangeRates",
                columns: new[] { "CurrencyUnitId", "RateDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppCurrencyUnitMargins");

            migrationBuilder.DropTable(
                name: "AppCurrencyUnits");

            migrationBuilder.DropTable(
                name: "AppExchangeRates");
        }
    }
}
