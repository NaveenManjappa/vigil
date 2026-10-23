using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Vigil.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowCheckpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EvidenceRefs",
                table: "cases",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "workflow_checkpoints",
                columns: table => new
                {
                    Position = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CheckpointId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ParentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Payload = table.Column<string>(type: "json", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_checkpoints", x => x.Position);
                });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_checkpoints_SessionId_CheckpointId",
                table: "workflow_checkpoints",
                columns: new[] { "SessionId", "CheckpointId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workflow_checkpoints");

            migrationBuilder.DropColumn(
                name: "EvidenceRefs",
                table: "cases");
        }
    }
}
