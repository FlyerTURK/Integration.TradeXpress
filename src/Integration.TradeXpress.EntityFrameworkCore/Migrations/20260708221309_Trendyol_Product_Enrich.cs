using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class Trendyol_Product_Enrich : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BrandName",
                table: "AppSalesChannelTrTrendyolProducts",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeliveryDuration",
                table: "AppSalesChannelTrTrendyolProducts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "AppSalesChannelTrTrendyolProducts",
                type: "nvarchar(max)",
                maxLength: 30000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailedItemCount",
                table: "AppSalesChannelTrTrendyolProducts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "FastDeliveryType",
                table: "AppSalesChannelTrTrendyolProducts",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastBatchRequestType",
                table: "AppSalesChannelTrTrendyolProducts",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductMainId",
                table: "AppSalesChannelTrTrendyolProducts",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "SequenceNo",
                table: "AppSalesChannelTrTrendyolProducts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Skus",
                table: "AppSalesChannelTrTrendyolProducts",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BrandName",
                table: "AppSalesChannelTrTrendyolProducts");

            migrationBuilder.DropColumn(
                name: "DeliveryDuration",
                table: "AppSalesChannelTrTrendyolProducts");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "AppSalesChannelTrTrendyolProducts");

            migrationBuilder.DropColumn(
                name: "FailedItemCount",
                table: "AppSalesChannelTrTrendyolProducts");

            migrationBuilder.DropColumn(
                name: "FastDeliveryType",
                table: "AppSalesChannelTrTrendyolProducts");

            migrationBuilder.DropColumn(
                name: "LastBatchRequestType",
                table: "AppSalesChannelTrTrendyolProducts");

            migrationBuilder.DropColumn(
                name: "ProductMainId",
                table: "AppSalesChannelTrTrendyolProducts");

            migrationBuilder.DropColumn(
                name: "SequenceNo",
                table: "AppSalesChannelTrTrendyolProducts");

            migrationBuilder.DropColumn(
                name: "Skus",
                table: "AppSalesChannelTrTrendyolProducts");
        }
    }
}
