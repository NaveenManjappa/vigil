using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vigil.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConfirmedFraud : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "confirmed_frauds",
                columns: table => new
                {
                    AccountId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_confirmed_frauds", x => x.AccountId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "confirmed_frauds");
        }
    }
}
