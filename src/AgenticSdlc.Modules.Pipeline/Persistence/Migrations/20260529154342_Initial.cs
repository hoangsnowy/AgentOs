using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AgenticSdlc.Modules.Pipeline.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "pipeline");

            migrationBuilder.CreateTable(
                name: "orchestrations",
                schema: "pipeline",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    DefinitionJson = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orchestrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_runs",
                schema: "pipeline",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserStoryText = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TotalCostUsd = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    TotalTokensIn = table.Column<int>(type: "integer", nullable: false),
                    TotalTokensOut = table.Column<int>(type: "integer", nullable: false),
                    IterationCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "run_metrics",
                schema: "pipeline",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    KcId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Iteration = table.Column<int>(type: "integer", nullable: false),
                    AgentName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TokensIn = table.Column<int>(type: "integer", nullable: false),
                    TokensOut = table.Column<int>(type: "integer", nullable: false),
                    LatencyMs = table.Column<double>(type: "double precision", nullable: false),
                    CostUsd = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    TimestampUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_run_metrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_run_metrics_pipeline_runs_RunId",
                        column: x => x.RunId,
                        principalSchema: "pipeline",
                        principalTable: "pipeline_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_orchestrations_TenantId",
                schema: "pipeline",
                table: "orchestrations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_runs_CreatedAtUtc",
                schema: "pipeline",
                table: "pipeline_runs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_runs_TenantId",
                schema: "pipeline",
                table: "pipeline_runs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_run_metrics_RunId",
                schema: "pipeline",
                table: "run_metrics",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_run_metrics_TenantId",
                schema: "pipeline",
                table: "run_metrics",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "orchestrations",
                schema: "pipeline");

            migrationBuilder.DropTable(
                name: "run_metrics",
                schema: "pipeline");

            migrationBuilder.DropTable(
                name: "pipeline_runs",
                schema: "pipeline");
        }
    }
}
