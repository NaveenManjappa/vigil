using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vigil.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CaseLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Finding",
                table: "cases",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "InvestigationFailed",
                table: "cases",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InvestigationStartedAt",
                table: "cases",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotInvestigated",
                table: "cases",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PromptVersion",
                table: "cases",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PseudonymMap",
                table: "cases",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReviewFlagged",
                table: "cases",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_cases_InvestigationStartedAt",
                table: "cases",
                column: "InvestigationStartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_cases_Status",
                table: "cases",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_cases_InvestigationStartedAt",
                table: "cases");

            migrationBuilder.DropIndex(
                name: "IX_cases_Status",
                table: "cases");

            migrationBuilder.DropColumn(
                name: "Finding",
                table: "cases");

            migrationBuilder.DropColumn(
                name: "InvestigationFailed",
                table: "cases");

            migrationBuilder.DropColumn(
                name: "InvestigationStartedAt",
                table: "cases");

            migrationBuilder.DropColumn(
                name: "NotInvestigated",
                table: "cases");

            migrationBuilder.DropColumn(
                name: "PromptVersion",
                table: "cases");

            migrationBuilder.DropColumn(
                name: "PseudonymMap",
                table: "cases");

            migrationBuilder.DropColumn(
                name: "ReviewFlagged",
                table: "cases");
        }
    }
}
