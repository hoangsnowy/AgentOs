using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentOs.Modules.Sessions.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionBrain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Brain",
                schema: "sessions",
                table: "sessions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Quick");

            migrationBuilder.AddColumn<string>(
                name: "TicketType",
                schema: "sessions",
                table: "sessions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Brain",
                schema: "sessions",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "TicketType",
                schema: "sessions",
                table: "sessions");
        }
    }
}
