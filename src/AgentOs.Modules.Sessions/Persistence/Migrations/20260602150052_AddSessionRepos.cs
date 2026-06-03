using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentOs.Modules.Sessions.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionRepos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BoardItemNodeId",
                schema: "sessions",
                table: "sessions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TicketKind",
                schema: "sessions",
                table: "sessions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "session_repos",
                schema: "sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceRepoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Owner = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Repo = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DefaultBranch = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BranchName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PrUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session_repos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_session_repos_sessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "sessions",
                        principalTable: "sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_session_repos_SessionId",
                schema: "sessions",
                table: "session_repos",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_session_repos_TenantId_SessionId",
                schema: "sessions",
                table: "session_repos",
                columns: new[] { "TenantId", "SessionId" });

            // ── Data fold — give each existing single-repo session a session_repos row so it stays
            //    runnable under the multi-repo path (which reads the children). Status maps terminal
            //    states through; everything else resets to Pending. ──
            migrationBuilder.Sql(
                """
                INSERT INTO "sessions"."session_repos"
                    ("Id", "TenantId", "SessionId", "WorkspaceRepoId", "Owner", "Repo", "DefaultBranch", "Status", "BranchName", "PrUrl", "Error", "CompletedAtUtc")
                SELECT gen_random_uuid(), "TenantId", "Id", NULL, "RepoOwner", "RepoName",
                       COALESCE("RepoDefaultBranch", 'main'),
                       CASE WHEN "Status" IN ('Done', 'Failed') THEN "Status" ELSE 'Pending' END,
                       NULL, "PrUrl", "Error", NULL
                FROM "sessions"."sessions"
                WHERE "RepoOwner" IS NOT NULL AND "RepoName" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "session_repos",
                schema: "sessions");

            migrationBuilder.DropColumn(
                name: "BoardItemNodeId",
                schema: "sessions",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "TicketKind",
                schema: "sessions",
                table: "sessions");
        }
    }
}
