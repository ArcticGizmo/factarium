using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Factarium.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JiraSingleProject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Jql",
                table: "integrations");

            migrationBuilder.DropColumn(
                name: "ProjectKeys",
                table: "integrations");

            migrationBuilder.AddColumn<string>(
                name: "ProjectKey",
                table: "integrations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SyncSince",
                table: "integrations",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProjectKey",
                table: "integrations");

            migrationBuilder.DropColumn(
                name: "SyncSince",
                table: "integrations");

            migrationBuilder.AddColumn<string>(
                name: "Jql",
                table: "integrations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "ProjectKeys",
                table: "integrations",
                type: "text[]",
                nullable: true);
        }
    }
}
