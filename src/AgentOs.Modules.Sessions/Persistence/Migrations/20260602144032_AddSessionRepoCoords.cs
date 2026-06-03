using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentOs.Modules.Sessions.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionRepoCoords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RepoDefaultBranch",
                schema: "sessions",
                table: "sessions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepoName",
                schema: "sessions",
                table: "sessions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepoOwner",
                schema: "sessions",
                table: "sessions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RepoDefaultBranch",
                schema: "sessions",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "RepoName",
                schema: "sessions",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "RepoOwner",
                schema: "sessions",
                table: "sessions");
        }
    }
}
