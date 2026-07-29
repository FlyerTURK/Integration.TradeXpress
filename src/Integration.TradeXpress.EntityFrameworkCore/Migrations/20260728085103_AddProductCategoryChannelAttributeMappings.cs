using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class AddProductCategoryChannelAttributeMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppProductCategoryChannelAttributeMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<byte>(type: "tinyint", nullable: false),
                    ProductCategoryAttributeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelAttributeExternalId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ChannelAttributeName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
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
                    table.PrimaryKey("PK_AppProductCategoryChannelAttributeMappings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppProductCategoryChannelAttributeMappings_TenantId_CompanyId_ProductCategoryId_Channel",
                table: "AppProductCategoryChannelAttributeMappings",
                columns: new[] { "TenantId", "CompanyId", "ProductCategoryId", "Channel" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductCategoryChannelAttributeMapping_Unique",
                table: "AppProductCategoryChannelAttributeMappings",
                columns: new[] { "TenantId", "CompanyId", "ProductCategoryId", "Channel", "ProductCategoryAttributeId" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppProductCategoryChannelAttributeMappings");
        }
    }
}
