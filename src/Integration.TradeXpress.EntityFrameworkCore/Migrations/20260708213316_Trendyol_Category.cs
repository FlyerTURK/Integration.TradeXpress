using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Trendyol_Category : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppTrendyolCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ParentExternalId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    IsLeaf = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_AppTrendyolCategories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppTrendyolCategories_ExternalId",
                table: "AppTrendyolCategories",
                column: "ExternalId",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AppTrendyolCategories_ParentExternalId",
                table: "AppTrendyolCategories",
                column: "ParentExternalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppTrendyolCategories");
        }
    }
}
