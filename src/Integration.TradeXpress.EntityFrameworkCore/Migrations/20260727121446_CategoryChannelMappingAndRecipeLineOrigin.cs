using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class CategoryChannelMappingAndRecipeLineOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "Origin",
                table: "AppProductVariantRecipeLines",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.CreateTable(
                name: "AppProductCategoryChannelMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<byte>(type: "tinyint", nullable: false),
                    ChannelCategoryExternalId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ChannelCategoryName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
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
                    table.PrimaryKey("PK_AppProductCategoryChannelMappings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppProductVariantRecipeLines_TenantId_ProductVariantId_Origin",
                table: "AppProductVariantRecipeLines",
                columns: new[] { "TenantId", "ProductVariantId", "Origin" });

            migrationBuilder.CreateIndex(
                name: "IX_AppProductCategoryChannelMappings_TenantId_CompanyId_Channel_ProductCategoryId",
                table: "AppProductCategoryChannelMappings",
                columns: new[] { "TenantId", "CompanyId", "Channel", "ProductCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppProductCategoryChannelMappings_TenantId_CompanyId_ProductCategoryId_Channel",
                table: "AppProductCategoryChannelMappings",
                columns: new[] { "TenantId", "CompanyId", "ProductCategoryId", "Channel" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppProductCategoryChannelMappings");

            migrationBuilder.DropIndex(
                name: "IX_AppProductVariantRecipeLines_TenantId_ProductVariantId_Origin",
                table: "AppProductVariantRecipeLines");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "AppProductVariantRecipeLines");
        }
    }
}
