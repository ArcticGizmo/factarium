using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Factarium.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CanonicalAndMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "canonical_commits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Sha = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RepositoryFullName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    AuthorIdentityId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorLogin = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CommittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Message = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_canonical_commits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "canonical_pull_requests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    RepositoryFullName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Title = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsMerged = table.Column<bool>(type: "boolean", nullable: false),
                    AuthorIdentityId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorLogin = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MergedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_canonical_pull_requests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "canonical_repositories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FullName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Owner = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DefaultBranch = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_canonical_repositories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "canonical_reviews",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PullRequestNumber = table.Column<int>(type: "integer", nullable: false),
                    RepositoryFullName = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ReviewerIdentityId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewerLogin = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_canonical_reviews", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "daily_metrics",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MetricKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Dimension = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ActorKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ActorLabel = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Value = table.Column<double>(type: "double precision", nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_metrics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "people",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_people", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_steps",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LastRunAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastInputWatermark = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    LastItemsProcessed = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_steps", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "source_identities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Login = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: true),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_identities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_source_identities_people_PersonId",
                        column: x => x.PersonId,
                        principalTable: "people",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_canonical_commits_AuthorIdentityId",
                table: "canonical_commits",
                column: "AuthorIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_canonical_commits_CommittedAt",
                table: "canonical_commits",
                column: "CommittedAt");

            migrationBuilder.CreateIndex(
                name: "IX_canonical_commits_Source_RepositoryFullName_Sha",
                table: "canonical_commits",
                columns: new[] { "Source", "RepositoryFullName", "Sha" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_canonical_pull_requests_CreatedAt",
                table: "canonical_pull_requests",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_canonical_pull_requests_MergedAt",
                table: "canonical_pull_requests",
                column: "MergedAt");

            migrationBuilder.CreateIndex(
                name: "IX_canonical_pull_requests_RepositoryFullName_Number",
                table: "canonical_pull_requests",
                columns: new[] { "RepositoryFullName", "Number" });

            migrationBuilder.CreateIndex(
                name: "IX_canonical_pull_requests_Source_ExternalId",
                table: "canonical_pull_requests",
                columns: new[] { "Source", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_canonical_repositories_Source_ExternalId",
                table: "canonical_repositories",
                columns: new[] { "Source", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_canonical_reviews_RepositoryFullName_PullRequestNumber",
                table: "canonical_reviews",
                columns: new[] { "RepositoryFullName", "PullRequestNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_canonical_reviews_Source_ExternalId",
                table: "canonical_reviews",
                columns: new[] { "Source", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_daily_metrics_MetricKey_Day",
                table: "daily_metrics",
                columns: new[] { "MetricKey", "Day" });

            migrationBuilder.CreateIndex(
                name: "IX_daily_metrics_MetricKey_Day_Dimension_ActorKey",
                table: "daily_metrics",
                columns: new[] { "MetricKey", "Day", "Dimension", "ActorKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_source_identities_PersonId",
                table: "source_identities",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_source_identities_Source_Login",
                table: "source_identities",
                columns: new[] { "Source", "Login" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "canonical_commits");

            migrationBuilder.DropTable(
                name: "canonical_pull_requests");

            migrationBuilder.DropTable(
                name: "canonical_repositories");

            migrationBuilder.DropTable(
                name: "canonical_reviews");

            migrationBuilder.DropTable(
                name: "daily_metrics");

            migrationBuilder.DropTable(
                name: "pipeline_steps");

            migrationBuilder.DropTable(
                name: "source_identities");

            migrationBuilder.DropTable(
                name: "people");
        }
    }
}
