using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentOs.Modules.Sessions.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionRunFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Error",
                schema: "sessions",
                table: "sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IssueNumber",
                schema: "sessions",
                table: "sessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrUrl",
                schema: "sessions",
                table: "sessions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Error",
                schema: "sessions",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "IssueNumber",
                schema: "sessions",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "PrUrl",
                schema: "sessions",
                table: "sessions");
        }
    }
}
