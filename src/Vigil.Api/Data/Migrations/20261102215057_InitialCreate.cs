using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vigil.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "alerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<string>(type: "text", nullable: false),
                    RulesVersion = table.Column<string>(type: "text", nullable: false),
                    Hits = table.Column<string>(type: "jsonb", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Band = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsOpen = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alerts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "events",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<string>(type: "text", nullable: false),
                    HolderId = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeviceId = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "text", nullable: true),
                    Country = table.Column<string>(type: "text", nullable: true),
                    Channel = table.Column<string>(type: "text", nullable: false),
                    Attributes = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_events", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "cases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AlertId = table.Column<Guid>(type: "uuid", nullable: false),
                    Finding = table.Column<string>(type: "jsonb", nullable: true),
                    Model = table.Column<string>(type: "text", nullable: true),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    InvestigatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AnalystId = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Intervention = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    InterventionUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cases_alerts_AlertId",
                        column: x => x.AlertId,
                        principalTable: "alerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alerts_AccountId",
                table: "alerts",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_cases_AlertId",
                table: "cases",
                column: "AlertId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_events_AccountId_OccurredAt",
                table: "events",
                columns: new[] { "AccountId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cases");

            migrationBuilder.DropTable(
                name: "events");

            migrationBuilder.DropTable(
                name: "alerts");
        }
    }
}
