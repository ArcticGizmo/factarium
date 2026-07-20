using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Factarium.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JiraIssueSimplify : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ResolvedAt",
                table: "canonical_issues",
                newName: "ClosedAt");

            migrationBuilder.RenameColumn(
                name: "IsResolved",
                table: "canonical_issues",
                newName: "IsClosed");

            migrationBuilder.RenameIndex(
                name: "IX_canonical_issues_ResolvedAt",
                table: "canonical_issues",
                newName: "IX_canonical_issues_ClosedAt");

            migrationBuilder.AddColumn<Guid>(
                name: "ClosedByIdentityId",
                table: "canonical_issues",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosedByLogin",
                table: "canonical_issues",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CommentCount",
                table: "canonical_issues",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "IssueTypeId",
                table: "canonical_issues",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PrimaryDeveloperIdentityId",
                table: "canonical_issues",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryDeveloperLogin",
                table: "canonical_issues",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReporterIdentityId",
                table: "canonical_issues",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReporterLogin",
                table: "canonical_issues",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SprintId",
                table: "canonical_issues",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SprintName",
                table: "canonical_issues",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StatusCategory",
                table: "canonical_issues",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "StoryPoints",
                table: "canonical_issues",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "canonical_issues",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_canonical_issues_PrimaryDeveloperIdentityId",
                table: "canonical_issues",
                column: "PrimaryDeveloperIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_canonical_issues_ReporterIdentityId",
                table: "canonical_issues",
                column: "ReporterIdentityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_canonical_issues_PrimaryDeveloperIdentityId",
                table: "canonical_issues");

            migrationBuilder.DropIndex(
                name: "IX_canonical_issues_ReporterIdentityId",
                table: "canonical_issues");

            migrationBuilder.DropColumn(
                name: "ClosedByIdentityId",
                table: "canonical_issues");

            migrationBuilder.DropColumn(
                name: "ClosedByLogin",
                table: "canonical_issues");

            migrationBuilder.DropColumn(
                name: "CommentCount",
                table: "canonical_issues");

            migrationBuilder.DropColumn(
                name: "IssueTypeId",
                table: "canonical_issues");

            migrationBuilder.DropColumn(
                name: "PrimaryDeveloperIdentityId",
                table: "canonical_issues");

            migrationBuilder.DropColumn(
                name: "PrimaryDeveloperLogin",
                table: "canonical_issues");

            migrationBuilder.DropColumn(
                name: "ReporterIdentityId",
                table: "canonical_issues");

            migrationBuilder.DropColumn(
                name: "ReporterLogin",
                table: "canonical_issues");

            migrationBuilder.DropColumn(
                name: "SprintId",
                table: "canonical_issues");

            migrationBuilder.DropColumn(
                name: "SprintName",
                table: "canonical_issues");

            migrationBuilder.DropColumn(
                name: "StatusCategory",
                table: "canonical_issues");

            migrationBuilder.DropColumn(
                name: "StoryPoints",
                table: "canonical_issues");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "canonical_issues");

            migrationBuilder.RenameColumn(
                name: "IsClosed",
                table: "canonical_issues",
                newName: "IsResolved");

            migrationBuilder.RenameColumn(
                name: "ClosedAt",
                table: "canonical_issues",
                newName: "ResolvedAt");

            migrationBuilder.RenameIndex(
                name: "IX_canonical_issues_ClosedAt",
                table: "canonical_issues",
                newName: "IX_canonical_issues_ResolvedAt");
        }
    }
}
