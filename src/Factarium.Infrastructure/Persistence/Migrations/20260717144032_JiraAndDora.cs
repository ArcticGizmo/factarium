using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Factarium.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JiraAndDora : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BaseRef",
                table: "canonical_pull_requests",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "canonical_issues",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProjectKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IssueType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsResolved = table.Column<bool>(type: "boolean", nullable: false),
                    AssigneeIdentityId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssigneeLogin = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_canonical_issues", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_canonical_issues_AssigneeIdentityId",
                table: "canonical_issues",
                column: "AssigneeIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_canonical_issues_ResolvedAt",
                table: "canonical_issues",
                column: "ResolvedAt");

            migrationBuilder.CreateIndex(
                name: "IX_canonical_issues_Source_ExternalId",
                table: "canonical_issues",
                columns: new[] { "Source", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "canonical_issues");

            migrationBuilder.DropColumn(
                name: "BaseRef",
                table: "canonical_pull_requests");
        }
    }
}
