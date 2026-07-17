using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class AddMetalVariantDetail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CostUnitId",
                table: "AppMetals");

            migrationBuilder.DropColumn(
                name: "EntryLabor",
                table: "AppMetals");

            migrationBuilder.DropColumn(
                name: "EntryLaborChange",
                table: "AppMetals");

            migrationBuilder.DropColumn(
                name: "EntryLaborUnitId",
                table: "AppMetals");

            migrationBuilder.DropColumn(
                name: "ExitLabor",
                table: "AppMetals");

            migrationBuilder.DropColumn(
                name: "ExitLaborChange",
                table: "AppMetals");

            migrationBuilder.DropColumn(
                name: "ExitLaborUnitId",
                table: "AppMetals");

            migrationBuilder.DropColumn(
                name: "LaborType",
                table: "AppMetals");

            migrationBuilder.DropColumn(
                name: "LaborTypeChange",
                table: "AppMetals");

            migrationBuilder.CreateTable(
                name: "AppMetalVariantDetails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EntityVariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LaborType = table.Column<byte>(type: "tinyint", nullable: false),
                    LaborTypeChange = table.Column<bool>(type: "bit", nullable: false),
                    EntryLabor = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: false),
                    EntryLaborUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EntryLaborChange = table.Column<bool>(type: "bit", nullable: false),
                    ExitLabor = table.Column<decimal>(type: "decimal(18,5)", precision: 18, scale: 5, nullable: false),
                    ExitLaborUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExitLaborChange = table.Column<bool>(type: "bit", nullable: false),
                    CostUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_AppMetalVariantDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppMetalVariantDetails_AppEntityVariants_EntityVariantId",
                        column: x => x.EntityVariantId,
                        principalTable: "AppEntityVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppMetalVariantDetails_EntityVariantId",
                table: "AppMetalVariantDetails",
                column: "EntityVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_AppMetalVariantDetails_TenantId_EntityVariantId",
                table: "AppMetalVariantDetails",
                columns: new[] { "TenantId", "EntityVariantId" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppMetalVariantDetails");

            migrationBuilder.AddColumn<Guid>(
                name: "CostUnitId",
                table: "AppMetals",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EntryLabor",
                table: "AppMetals",
                type: "decimal(18,5)",
                precision: 18,
                scale: 5,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "EntryLaborChange",
                table: "AppMetals",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "EntryLaborUnitId",
                table: "AppMetals",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExitLabor",
                table: "AppMetals",
                type: "decimal(18,5)",
                precision: 18,
                scale: 5,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "ExitLaborChange",
                table: "AppMetals",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ExitLaborUnitId",
                table: "AppMetals",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "LaborType",
                table: "AppMetals",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<bool>(
                name: "LaborTypeChange",
                table: "AppMetals",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
