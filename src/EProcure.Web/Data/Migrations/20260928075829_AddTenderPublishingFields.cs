using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EProcure.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenderPublishingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "Tenders",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "Tenders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PointSystem",
                table: "Tenders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                // Hand-edited: existing tenders get "EightyTwenty" (not "", which is not a valid enum name).
                // New tenders always set the value explicitly (pre-filled from the organisation's default).
                defaultValue: "EightyTwenty");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "PointSystem",
                table: "Tenders");
        }
    }
}
