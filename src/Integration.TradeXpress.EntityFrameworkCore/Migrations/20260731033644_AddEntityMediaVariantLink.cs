using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class AddEntityMediaVariantLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppEntityMediaLinks_EntityName_EntityId_DisplayOrder",
                table: "AppEntityMediaLinks");

            migrationBuilder.AddColumn<Guid>(
                name: "VariantId",
                table: "AppEntityMediaLinks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppEntityMediaLinks_EntityName_EntityId_VariantId_DisplayOrder",
                table: "AppEntityMediaLinks",
                columns: new[] { "EntityName", "EntityId", "VariantId", "DisplayOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppEntityMediaLinks_EntityName_EntityId_VariantId_DisplayOrder",
                table: "AppEntityMediaLinks");

            migrationBuilder.DropColumn(
                name: "VariantId",
                table: "AppEntityMediaLinks");

            migrationBuilder.CreateIndex(
                name: "IX_AppEntityMediaLinks_EntityName_EntityId_DisplayOrder",
                table: "AppEntityMediaLinks",
                columns: new[] { "EntityName", "EntityId", "DisplayOrder" });
        }
    }
}
