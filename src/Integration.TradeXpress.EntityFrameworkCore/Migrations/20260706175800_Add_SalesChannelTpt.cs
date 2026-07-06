using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Add_SalesChannelTpt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppSalesChannels_TenantId_Code",
                table: "AppSalesChannels");

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "AppSalesChannels",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "AppSalesChannelTrN11",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    AppSecret = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSalesChannelTrN11", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppSalesChannelTrN11_AppSalesChannels_Id",
                        column: x => x.Id,
                        principalTable: "AppSalesChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannels_TenantId_CompanyId",
                table: "AppSalesChannels",
                columns: new[] { "TenantId", "CompanyId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannels_TenantId_CompanyId_Code",
                table: "AppSalesChannels",
                columns: new[] { "TenantId", "CompanyId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSalesChannelTrN11");

            migrationBuilder.DropIndex(
                name: "IX_AppSalesChannels_TenantId_CompanyId",
                table: "AppSalesChannels");

            migrationBuilder.DropIndex(
                name: "IX_AppSalesChannels_TenantId_CompanyId_Code",
                table: "AppSalesChannels");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "AppSalesChannels");

            migrationBuilder.CreateIndex(
                name: "IX_AppSalesChannels_TenantId_Code",
                table: "AppSalesChannels",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");
        }
    }
}
