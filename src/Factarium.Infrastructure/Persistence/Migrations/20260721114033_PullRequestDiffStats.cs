using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Factarium.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PullRequestDiffStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Additions",
                table: "canonical_pull_requests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ChangedFiles",
                table: "canonical_pull_requests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Deletions",
                table: "canonical_pull_requests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReviewCommentCount",
                table: "canonical_pull_requests",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Additions",
                table: "canonical_pull_requests");

            migrationBuilder.DropColumn(
                name: "ChangedFiles",
                table: "canonical_pull_requests");

            migrationBuilder.DropColumn(
                name: "Deletions",
                table: "canonical_pull_requests");

            migrationBuilder.DropColumn(
                name: "ReviewCommentCount",
                table: "canonical_pull_requests");
        }
    }
}
