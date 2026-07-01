using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentOs.Modules.Pipeline.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelineRunTenantCreatedIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_pipeline_runs_CreatedAtUtc",
                schema: "pipeline",
                table: "pipeline_runs");

            migrationBuilder.DropIndex(
                name: "IX_pipeline_runs_TenantId",
                schema: "pipeline",
                table: "pipeline_runs");

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_runs_TenantId_CreatedAtUtc_Id",
                schema: "pipeline",
                table: "pipeline_runs",
                columns: new[] { "TenantId", "CreatedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_pipeline_runs_TenantId_CreatedAtUtc_Id",
                schema: "pipeline",
                table: "pipeline_runs");

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
        }
    }
}
