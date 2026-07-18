using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Factarium.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IntegrationTyping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add the typed per-type columns first, backfill them from the old
            // generic settings blob, then drop the blob — so no configuration is lost.
            migrationBuilder.AddColumn<string>(
                name: "BaseUrl",
                table: "integrations",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "integrations",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Jql",
                table: "integrations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Org",
                table: "integrations",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "ProjectKeys",
                table: "integrations",
                type: "text[]",
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "Repos",
                table: "integrations",
                type: "text[]",
                nullable: true);

            // Backfill from SettingsJson (keys: org, repos, baseUrl, email, projectKeys, jql).
            // csv lists become trimmed text[]; scalars copy straight across.
            migrationBuilder.Sql("""
                UPDATE integrations
                SET "Org" = "SettingsJson"->>'org',
                    "Repos" = (
                        SELECT array_agg(trim(elem))
                        FROM unnest(string_to_array("SettingsJson"->>'repos', ',')) AS elem
                        WHERE trim(elem) <> ''
                    )
                WHERE "Type" = 'github' AND "SettingsJson" IS NOT NULL;
                """);

            migrationBuilder.Sql("""
                UPDATE integrations
                SET "BaseUrl" = "SettingsJson"->>'baseUrl',
                    "Email" = "SettingsJson"->>'email',
                    "Jql" = "SettingsJson"->>'jql',
                    "ProjectKeys" = (
                        SELECT array_agg(trim(elem))
                        FROM unnest(string_to_array("SettingsJson"->>'projectKeys', ',')) AS elem
                        WHERE trim(elem) <> ''
                    )
                WHERE "Type" = 'jira' AND "SettingsJson" IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "SettingsJson",
                table: "integrations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SettingsJson",
                table: "integrations",
                type: "jsonb",
                nullable: true);

            // Best-effort reconstruction of the settings blob from the typed columns.
            migrationBuilder.Sql("""
                UPDATE integrations
                SET "SettingsJson" = jsonb_strip_nulls(jsonb_build_object(
                    'org', "Org",
                    'repos', array_to_string("Repos", ',')
                ))
                WHERE "Type" = 'github';
                """);

            migrationBuilder.Sql("""
                UPDATE integrations
                SET "SettingsJson" = jsonb_strip_nulls(jsonb_build_object(
                    'baseUrl', "BaseUrl",
                    'email', "Email",
                    'projectKeys', array_to_string("ProjectKeys", ','),
                    'jql', "Jql"
                ))
                WHERE "Type" = 'jira';
                """);

            migrationBuilder.DropColumn(
                name: "BaseUrl",
                table: "integrations");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "integrations");

            migrationBuilder.DropColumn(
                name: "Jql",
                table: "integrations");

            migrationBuilder.DropColumn(
                name: "Org",
                table: "integrations");

            migrationBuilder.DropColumn(
                name: "ProjectKeys",
                table: "integrations");

            migrationBuilder.DropColumn(
                name: "Repos",
                table: "integrations");
        }
    }
}
