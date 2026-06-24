using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Add_Metal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppMetals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Barcode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FollowingUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purity = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: false),
                    PurityChange = table.Column<bool>(type: "bit", nullable: false),
                    IsQuantity = table.Column<bool>(type: "bit", nullable: false),
                    StableQuantity = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: false),
                    LaborType = table.Column<byte>(type: "tinyint", nullable: false),
                    LaborTypeChange = table.Column<bool>(type: "bit", nullable: false),
                    EntryLabor = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: false),
                    EntryLaborUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EntryLaborChange = table.Column<bool>(type: "bit", nullable: false),
                    ExitLabor = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: false),
                    ExitLaborUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExitLaborChange = table.Column<bool>(type: "bit", nullable: false),
                    CostUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_AppMetals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppMetals_AppCurrencyUnits_FollowingUnitId",
                        column: x => x.FollowingUnitId,
                        principalTable: "AppCurrencyUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppMetals_FollowingUnitId",
                table: "AppMetals",
                column: "FollowingUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_AppMetals_TenantId_Code",
                table: "AppMetals",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppMetals_TenantId_FollowingUnitId",
                table: "AppMetals",
                columns: new[] { "TenantId", "FollowingUnitId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppMetals");
        }
    }
}
