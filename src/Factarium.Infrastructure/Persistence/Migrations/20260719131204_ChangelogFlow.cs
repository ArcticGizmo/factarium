using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Factarium.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChangelogFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BackflowCount",
                table: "canonical_issues",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReassignmentCount",
                table: "canonical_issues",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReopenCount",
                table: "canonical_issues",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "canonical_issue_segments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IssueKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IssueExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Value = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AssigneeIdentityId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssigneeLogin = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationSeconds = table.Column<double>(type: "double precision", nullable: false),
                    IsOpen = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_canonical_issue_segments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "canonical_issue_sprint_memberships",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IssueKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IssueExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SprintId = table.Column<long>(type: "bigint", nullable: false),
                    SprintName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_canonical_issue_sprint_memberships", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_canonical_issue_segments_AssigneeIdentityId",
                table: "canonical_issue_segments",
                column: "AssigneeIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_canonical_issue_segments_Kind_Category",
                table: "canonical_issue_segments",
                columns: new[] { "Kind", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_canonical_issue_segments_Source_IssueExternalId_Kind_Starte~",
                table: "canonical_issue_segments",
                columns: new[] { "Source", "IssueExternalId", "Kind", "StartedAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_canonical_issue_segments_Source_IssueKey",
                table: "canonical_issue_segments",
                columns: new[] { "Source", "IssueKey" });

            migrationBuilder.CreateIndex(
                name: "IX_canonical_issue_sprint_memberships_Source_IssueExternalId_S~",
                table: "canonical_issue_sprint_memberships",
                columns: new[] { "Source", "IssueExternalId", "SprintId", "AddedAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_canonical_issue_sprint_memberships_SprintId",
                table: "canonical_issue_sprint_memberships",
                column: "SprintId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "canonical_issue_segments");

            migrationBuilder.DropTable(
                name: "canonical_issue_sprint_memberships");

            migrationBuilder.DropColumn(
                name: "BackflowCount",
                table: "canonical_issues");

            migrationBuilder.DropColumn(
                name: "ReassignmentCount",
                table: "canonical_issues");

            migrationBuilder.DropColumn(
                name: "ReopenCount",
                table: "canonical_issues");
        }
    }
}
