using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgenticSdlc.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_app_config",
                table: "app_config");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "run_metrics",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "pipeline_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "orchestrations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "app_config",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddPrimaryKey(
                name: "PK_app_config",
                table: "app_config",
                columns: new[] { "TenantId", "Key" });

            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_run_metrics_TenantId",
                table: "run_metrics",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_runs_TenantId",
                table: "pipeline_runs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_orchestrations_TenantId",
                table: "orchestrations",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenants");

            migrationBuilder.DropIndex(
                name: "IX_run_metrics_TenantId",
                table: "run_metrics");

            migrationBuilder.DropIndex(
                name: "IX_pipeline_runs_TenantId",
                table: "pipeline_runs");

            migrationBuilder.DropIndex(
                name: "IX_orchestrations_TenantId",
                table: "orchestrations");

            migrationBuilder.DropPrimaryKey(
                name: "PK_app_config",
                table: "app_config");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "run_metrics");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "pipeline_runs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "orchestrations");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "app_config");

            migrationBuilder.AddPrimaryKey(
                name: "PK_app_config",
                table: "app_config",
                column: "Key");
        }
    }
}
