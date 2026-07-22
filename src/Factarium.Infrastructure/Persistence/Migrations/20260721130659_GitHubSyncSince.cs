using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Factarium.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GitHubSyncSince : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "JiraIntegration_SyncSince",
                table: "integrations",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "JiraIntegration_SyncSince",
                table: "integrations");
        }
    }
}
