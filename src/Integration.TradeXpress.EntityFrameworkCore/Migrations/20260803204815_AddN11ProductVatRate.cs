using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class AddN11ProductVatRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VatRate",
                table: "AppSalesChannelTrN11Products",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppChannelQuestionSyncStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesChannelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelType = table.Column<byte>(type: "tinyint", nullable: false),
                    LastRefreshedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefreshPageIndex = table.Column<int>(type: "int", nullable: false),
                    SeedCompleted = table.Column<bool>(type: "bit", nullable: false),
                    SeedMonthStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SeedPageIndex = table.Column<int>(type: "int", nullable: false),
                    SeedMonthsProcessed = table.Column<int>(type: "int", nullable: false),
                    ConsecutiveEmptyMonths = table.Column<int>(type: "int", nullable: false),
                    ExtraProperties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppChannelQuestionSyncStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppChannelQuestionSyncStates_TenantId_SalesChannelId",
                table: "AppChannelQuestionSyncStates",
                columns: new[] { "TenantId", "SalesChannelId" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppChannelQuestionSyncStates");

            migrationBuilder.DropColumn(
                name: "VatRate",
                table: "AppSalesChannelTrN11Products");
        }
    }
}
