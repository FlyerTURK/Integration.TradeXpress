using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Add_SalesChannelEtsy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSalesChannelEtsy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Keystring = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SharedSecret = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ShopId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ShopName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    EtsyUserId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    AccessToken = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    AccessTokenExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefreshToken = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    RefreshTokenExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSalesChannelEtsy", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppSalesChannelEtsy_AppSalesChannels_Id",
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
                name: "AppSalesChannelEtsy");
        }
    }
}
