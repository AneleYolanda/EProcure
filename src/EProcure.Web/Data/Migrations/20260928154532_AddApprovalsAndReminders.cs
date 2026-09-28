using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EProcure.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalsAndReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovalRequestedAtUtc",
                table: "Tenders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovalRequestedByUserId",
                table: "Tenders",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovalReturnNote",
                table: "Tenders",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAtUtc",
                table: "Tenders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedByUserId",
                table: "Tenders",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequireTenderApproval",
                table: "Organisations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "SentNotifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentNotifications", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "Organisations",
                keyColumn: "Id",
                keyValue: 1,
                column: "RequireTenderApproval",
                value: true);

            migrationBuilder.UpdateData(
                table: "Organisations",
                keyColumn: "Id",
                keyValue: 2,
                column: "RequireTenderApproval",
                value: false);

            migrationBuilder.CreateIndex(
                name: "IX_Tenders_ApprovalRequestedByUserId",
                table: "Tenders",
                column: "ApprovalRequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenders_ApprovedByUserId",
                table: "Tenders",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SentNotifications_Key",
                table: "SentNotifications",
                column: "Key",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Tenders_Users_ApprovalRequestedByUserId",
                table: "Tenders",
                column: "ApprovalRequestedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tenders_Users_ApprovedByUserId",
                table: "Tenders",
                column: "ApprovedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tenders_Users_ApprovalRequestedByUserId",
                table: "Tenders");

            migrationBuilder.DropForeignKey(
                name: "FK_Tenders_Users_ApprovedByUserId",
                table: "Tenders");

            migrationBuilder.DropTable(
                name: "SentNotifications");

            migrationBuilder.DropIndex(
                name: "IX_Tenders_ApprovalRequestedByUserId",
                table: "Tenders");

            migrationBuilder.DropIndex(
                name: "IX_Tenders_ApprovedByUserId",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "ApprovalRequestedAtUtc",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "ApprovalRequestedByUserId",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "ApprovalReturnNote",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "ApprovedAtUtc",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "RequireTenderApproval",
                table: "Organisations");
        }
    }
}
