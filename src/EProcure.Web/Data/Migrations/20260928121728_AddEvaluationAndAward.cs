using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EProcure.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEvaluationAndAward : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BacReturnNote",
                table: "Tenders",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EvaluationSubmittedAtUtc",
                table: "Tenders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvaluationSubmittedByUserId",
                table: "Tenders",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecommendationReason",
                table: "Tenders",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BidEvaluations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubmissionId = table.Column<int>(type: "int", nullable: false),
                    IsResponsive = table.Column<bool>(type: "bit", nullable: false),
                    NonResponsiveReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    BidPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    EvaluatedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EvaluatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PricePoints = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    PreferencePoints = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    TotalPoints = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    Rank = table.Column<int>(type: "int", nullable: true),
                    IsRecommended = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BidEvaluations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BidEvaluations_Submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "Submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BidEvaluations_Users_EvaluatedByUserId",
                        column: x => x.EvaluatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tenders_EvaluationSubmittedByUserId",
                table: "Tenders",
                column: "EvaluationSubmittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BidEvaluations_EvaluatedByUserId",
                table: "BidEvaluations",
                column: "EvaluatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BidEvaluations_SubmissionId",
                table: "BidEvaluations",
                column: "SubmissionId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Tenders_Users_EvaluationSubmittedByUserId",
                table: "Tenders",
                column: "EvaluationSubmittedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tenders_Users_EvaluationSubmittedByUserId",
                table: "Tenders");

            migrationBuilder.DropTable(
                name: "BidEvaluations");

            migrationBuilder.DropIndex(
                name: "IX_Tenders_EvaluationSubmittedByUserId",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "BacReturnNote",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "EvaluationSubmittedAtUtc",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "EvaluationSubmittedByUserId",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "RecommendationReason",
                table: "Tenders");
        }
    }
}
