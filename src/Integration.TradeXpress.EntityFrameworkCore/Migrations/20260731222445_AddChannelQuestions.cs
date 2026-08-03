using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integration.TradeXpress.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppChannelQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesChannelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelType = table.Column<byte>(type: "tinyint", nullable: false),
                    RemoteQuestionId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RemoteProductId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProductTitle = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    QuestionText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    RemoteQuestionDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FirstSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NeutralStatus = table.Column<byte>(type: "tinyint", nullable: false),
                    RemoteStatus = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IsPublic = table.Column<bool>(type: "bit", nullable: true),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    AnswerText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    AnswerState = table.Column<byte>(type: "tinyint", nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AnswerPushedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AnswerPushError = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
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
                    table.PrimaryKey("PK_AppChannelQuestions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppChannelQuestions_TenantId_CompanyId_AnswerState",
                table: "AppChannelQuestions",
                columns: new[] { "TenantId", "CompanyId", "AnswerState" });

            migrationBuilder.CreateIndex(
                name: "IX_AppChannelQuestions_TenantId_CompanyId_NeutralStatus_FirstSeenAt",
                table: "AppChannelQuestions",
                columns: new[] { "TenantId", "CompanyId", "NeutralStatus", "FirstSeenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AppChannelQuestions_TenantId_SalesChannelId_RemoteQuestionId",
                table: "AppChannelQuestions",
                columns: new[] { "TenantId", "SalesChannelId", "RemoteQuestionId" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppChannelQuestions");
        }
    }
}
