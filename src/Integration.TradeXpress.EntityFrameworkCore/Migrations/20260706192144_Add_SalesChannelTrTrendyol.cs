using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Add_SalesChannelTrTrendyol : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSalesChannelTrTrendyol",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    AppSecret = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSalesChannelTrTrendyol", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppSalesChannelTrTrendyol_AppSalesChannels_Id",
                        column: x => x.Id,
                        principalTable: "AppSalesChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSalesChannelTrTrendyol");
        }
    }
}
