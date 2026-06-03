using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentOs.Modules.Workspaces.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReshapeWorkspaceToBoard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Repo",
                schema: "workspaces",
                table: "workspaces",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "RemoteUrl",
                schema: "workspaces",
                table: "workspaces",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048);

            migrationBuilder.AlterColumn<string>(
                name: "Owner",
                schema: "workspaces",
                table: "workspaces",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "DefaultBranch",
                schema: "workspaces",
                table: "workspaces",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AddColumn<string>(
                name: "ProjectNodeId",
                schema: "workspaces",
                table: "workspaces",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProjectNumber",
                schema: "workspaces",
                table: "workspaces",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectOwner",
                schema: "workspaces",
                table: "workspaces",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProjectScope",
                schema: "workspaces",
                table: "workspaces",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "workspace_repos",
                schema: "workspaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Owner = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Repo = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DefaultBranch = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RemoteUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Private = table.Column<bool>(type: "boolean", nullable: false),
                    AddedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_repos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workspace_repos_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalSchema: "workspaces",
                        principalTable: "workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_workspace_repos_TenantId_WorkspaceId",
                schema: "workspaces",
                table: "workspace_repos",
                columns: new[] { "TenantId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_workspace_repos_WorkspaceId",
                schema: "workspaces",
                table: "workspace_repos",
                column: "WorkspaceId");

            // ── Data fold — reshape each pre-board workspace (one repo) into a board + one repo row. ──
            // The old single repo moves into workspace_repos; ProjectOwner is seeded from the old Owner
            // so the row is a degenerate board (no Projects v2 number yet — the user attaches one in the
            // UI). CredentialRef / token are untouched, so the per-board PAT keeps working.
            migrationBuilder.Sql(
                """
                INSERT INTO "workspaces"."workspace_repos"
                    ("Id", "TenantId", "WorkspaceId", "Owner", "Repo", "DefaultBranch", "RemoteUrl", "Private", "AddedAtUtc")
                SELECT gen_random_uuid(), "TenantId", "Id", "Owner", "Repo",
                       COALESCE("DefaultBranch", 'main'), COALESCE("RemoteUrl", ''), false, "CreatedAtUtc"
                FROM "workspaces"."workspaces"
                WHERE "Owner" IS NOT NULL AND "Repo" IS NOT NULL;
                """);

            migrationBuilder.Sql(
                """
                UPDATE "workspaces"."workspaces"
                SET "ProjectOwner" = "Owner", "ProjectScope" = 'user'
                WHERE "Owner" IS NOT NULL AND ("ProjectOwner" = '' OR "ProjectOwner" IS NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workspace_repos",
                schema: "workspaces");

            migrationBuilder.DropColumn(
                name: "ProjectNodeId",
                schema: "workspaces",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "ProjectNumber",
                schema: "workspaces",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "ProjectOwner",
                schema: "workspaces",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "ProjectScope",
                schema: "workspaces",
                table: "workspaces");

            migrationBuilder.AlterColumn<string>(
                name: "Repo",
                schema: "workspaces",
                table: "workspaces",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "RemoteUrl",
                schema: "workspaces",
                table: "workspaces",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Owner",
                schema: "workspaces",
                table: "workspaces",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DefaultBranch",
                schema: "workspaces",
                table: "workspaces",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);
        }
    }
}
