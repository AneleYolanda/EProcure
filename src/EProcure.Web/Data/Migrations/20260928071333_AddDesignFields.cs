using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EProcure.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDesignFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedValue",
                table: "Tenders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceNumber",
                table: "Submissions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EnterpriseSize",
                table: "Companies",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                // Hand-edited: existing companies get "Generic" (not "", which is not a valid enum name
                // and would make EF throw when reading the row). New rows always set the value in code.
                defaultValue: "Generic");

            migrationBuilder.UpdateData(
                table: "Organisations",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "AccentColour", "LogoPath", "PrimaryColour" },
                values: new object[] { "#1CA3EC", "/img/orgs/rbidz-logo.png", "#0F1B33" });

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_ReferenceNumber",
                table: "Submissions",
                column: "ReferenceNumber",
                unique: true,
                filter: "[ReferenceNumber] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Submissions_ReferenceNumber",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "EstimatedValue",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "ReferenceNumber",
                table: "Submissions");

            migrationBuilder.DropColumn(
                name: "EnterpriseSize",
                table: "Companies");

            migrationBuilder.UpdateData(
                table: "Organisations",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "AccentColour", "LogoPath", "PrimaryColour" },
                values: new object[] { "#C9A227", "/img/orgs/rbidz.svg", "#0B2545" });
        }
    }
}
