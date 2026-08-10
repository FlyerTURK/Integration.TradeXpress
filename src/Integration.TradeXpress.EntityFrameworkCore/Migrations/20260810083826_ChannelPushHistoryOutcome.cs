using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class ChannelPushHistoryOutcome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ErrorMessage",
                table: "AppSalesChannelTrTrendyolProductPushHistories",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "Outcome",
                table: "AppSalesChannelTrTrendyolProductPushHistories",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<string>(
                name: "ErrorMessage",
                table: "AppSalesChannelTrN11ProductPushHistories",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "Outcome",
                table: "AppSalesChannelTrN11ProductPushHistories",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ErrorMessage",
                table: "AppSalesChannelTrTrendyolProductPushHistories");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "AppSalesChannelTrTrendyolProductPushHistories");

            migrationBuilder.DropColumn(
                name: "ErrorMessage",
                table: "AppSalesChannelTrN11ProductPushHistories");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "AppSalesChannelTrN11ProductPushHistories");
        }
    }
}
