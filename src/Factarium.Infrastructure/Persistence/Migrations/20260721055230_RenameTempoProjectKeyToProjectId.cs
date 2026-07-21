using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Factarium.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameTempoProjectKeyToProjectId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TempoIntegration_ProjectKey",
                table: "integrations",
                newName: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ProjectId",
                table: "integrations",
                newName: "TempoIntegration_ProjectKey");
        }
    }
}
