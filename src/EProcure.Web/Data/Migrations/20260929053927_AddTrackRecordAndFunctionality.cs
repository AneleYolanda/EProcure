using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EProcure.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackRecordAndFunctionality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FunctionalityThreshold",
                table: "Tenders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FunctionalityScore",
                table: "BidEvaluations",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CompanyDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ClientName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    YearCompleted = table.Column<int>(type: "int", nullable: true),
                    ContractValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    UploadedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RemovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyDocuments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyDocuments_Users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TenderFunctionalityCriteria",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Weight = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenderFunctionalityCriteria", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenderFunctionalityCriteria_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "Tenders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FunctionalityRatings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BidEvaluationId = table.Column<int>(type: "int", nullable: false),
                    TenderFunctionalityCriterionId = table.Column<int>(type: "int", nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FunctionalityRatings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FunctionalityRatings_BidEvaluations_BidEvaluationId",
                        column: x => x.BidEvaluationId,
                        principalTable: "BidEvaluations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FunctionalityRatings_TenderFunctionalityCriteria_TenderFunctionalityCriterionId",
                        column: x => x.TenderFunctionalityCriterionId,
                        principalTable: "TenderFunctionalityCriteria",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyDocuments_CompanyId",
                table: "CompanyDocuments",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyDocuments_StorageKey",
                table: "CompanyDocuments",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyDocuments_UploadedByUserId",
                table: "CompanyDocuments",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FunctionalityRatings_BidEvaluationId_TenderFunctionalityCriterionId",
                table: "FunctionalityRatings",
                columns: new[] { "BidEvaluationId", "TenderFunctionalityCriterionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FunctionalityRatings_TenderFunctionalityCriterionId",
                table: "FunctionalityRatings",
                column: "TenderFunctionalityCriterionId");

            migrationBuilder.CreateIndex(
                name: "IX_TenderFunctionalityCriteria_TenderId",
                table: "TenderFunctionalityCriteria",
                column: "TenderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyDocuments");

            migrationBuilder.DropTable(
                name: "FunctionalityRatings");

            migrationBuilder.DropTable(
                name: "TenderFunctionalityCriteria");

            migrationBuilder.DropColumn(
                name: "FunctionalityThreshold",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "FunctionalityScore",
                table: "BidEvaluations");
        }
    }
}
