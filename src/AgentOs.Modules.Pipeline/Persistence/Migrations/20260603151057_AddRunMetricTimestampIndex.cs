using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentOs.Modules.Pipeline.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRunMetricTimestampIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_run_metrics_TenantId",
                schema: "pipeline",
                table: "run_metrics");

            migrationBuilder.CreateIndex(
                name: "IX_run_metrics_TenantId_TimestampUtc",
                schema: "pipeline",
                table: "run_metrics",
                columns: new[] { "TenantId", "TimestampUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_run_metrics_TenantId_TimestampUtc",
                schema: "pipeline",
                table: "run_metrics");

            migrationBuilder.CreateIndex(
                name: "IX_run_metrics_TenantId",
                schema: "pipeline",
                table: "run_metrics",
                column: "TenantId");
        }
    }
}
