using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Factarium.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClaudeCodeUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "canonical_usage_metrics",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MetricKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Value = table.Column<double>(type: "double precision", nullable: false),
                    ActorIdentityId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorLogin = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SessionId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_canonical_usage_metrics", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_canonical_usage_metrics_ActorIdentityId",
                table: "canonical_usage_metrics",
                column: "ActorIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_canonical_usage_metrics_MetricKey_OccurredAt",
                table: "canonical_usage_metrics",
                columns: new[] { "MetricKey", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_canonical_usage_metrics_Source_ExternalId",
                table: "canonical_usage_metrics",
                columns: new[] { "Source", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "canonical_usage_metrics");
        }
    }
}
