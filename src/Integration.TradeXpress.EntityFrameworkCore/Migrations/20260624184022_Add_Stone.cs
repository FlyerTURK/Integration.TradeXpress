using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Add_Stone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppStones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    StoneKind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    StoneType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Color = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Cut = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Clarity = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Sieve = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    GroupCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IsQuantity = table.Column<bool>(type: "bit", nullable: false),
                    PriceByQuantity = table.Column<bool>(type: "bit", nullable: false),
                    PriceTypeChange = table.Column<bool>(type: "bit", nullable: false),
                    EntryPrice = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: false),
                    EntryPriceUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExitPrice = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: false),
                    ExitPriceUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_AppStones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppStones_AppCurrencyUnits_EntryPriceUnitId",
                        column: x => x.EntryPriceUnitId,
                        principalTable: "AppCurrencyUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppStones_AppCurrencyUnits_ExitPriceUnitId",
                        column: x => x.ExitPriceUnitId,
                        principalTable: "AppCurrencyUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppStones_EntryPriceUnitId",
                table: "AppStones",
                column: "EntryPriceUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_AppStones_ExitPriceUnitId",
                table: "AppStones",
                column: "ExitPriceUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_AppStones_TenantId_Code",
                table: "AppStones",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppStones");
        }
    }
}
