using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Factarium.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TempoIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TempoIntegration_ProjectKey",
                table: "integrations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TempoIntegration_SyncSince",
                table: "integrations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "canonical_worklogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IssueKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IssueExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AuthorIdentityId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorLogin = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TimeSpentSeconds = table.Column<int>(type: "integer", nullable: false),
                    BillableSeconds = table.Column<int>(type: "integer", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_canonical_worklogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_canonical_worklogs_AuthorIdentityId",
                table: "canonical_worklogs",
                column: "AuthorIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_canonical_worklogs_IssueKey",
                table: "canonical_worklogs",
                column: "IssueKey");

            migrationBuilder.CreateIndex(
                name: "IX_canonical_worklogs_Source_ExternalId",
                table: "canonical_worklogs",
                columns: new[] { "Source", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_canonical_worklogs_WorkDate",
                table: "canonical_worklogs",
                column: "WorkDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "canonical_worklogs");

            migrationBuilder.DropColumn(
                name: "TempoIntegration_ProjectKey",
                table: "integrations");

            migrationBuilder.DropColumn(
                name: "TempoIntegration_SyncSince",
                table: "integrations");
        }
    }
}
