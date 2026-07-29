using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class AddProductCategoryChannelAttributeValueMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppProductCategoryChannelAttributeValueMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<byte>(type: "tinyint", nullable: false),
                    ProductCategoryAttributeValueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelAttributeValueExternalId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ChannelAttributeValueName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
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
                    table.PrimaryKey("PK_AppProductCategoryChannelAttributeValueMappings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppProductCategoryChannelAttributeValueMappings_TenantId_CompanyId_ProductCategoryId_Channel",
                table: "AppProductCategoryChannelAttributeValueMappings",
                columns: new[] { "TenantId", "CompanyId", "ProductCategoryId", "Channel" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductCategoryChannelAttributeValueMapping_Unique",
                table: "AppProductCategoryChannelAttributeValueMappings",
                columns: new[] { "TenantId", "CompanyId", "ProductCategoryId", "Channel", "ProductCategoryAttributeValueId" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppProductCategoryChannelAttributeValueMappings");
        }
    }
}
