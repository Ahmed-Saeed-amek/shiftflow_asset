using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftFlow.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase10_RemoveAssetMaintenanceResponsibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assets_ShiftGroups_MaintainedByGroupId",
                table: "Assets");

            migrationBuilder.DropForeignKey(
                name: "FK_Assets_WorkAreas_WorkAreaId",
                table: "Assets");

            migrationBuilder.DropIndex(
                name: "IX_Assets_MaintainedByGroupId",
                table: "Assets");

            migrationBuilder.DropIndex(
                name: "IX_Assets_WorkAreaId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "ExternalMaintainer",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "MaintainedByGroupId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "WorkAreaId",
                table: "Assets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalMaintainer",
                table: "Assets",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaintainedByGroupId",
                table: "Assets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkAreaId",
                table: "Assets",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_MaintainedByGroupId",
                table: "Assets",
                column: "MaintainedByGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_WorkAreaId",
                table: "Assets",
                column: "WorkAreaId");

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_ShiftGroups_MaintainedByGroupId",
                table: "Assets",
                column: "MaintainedByGroupId",
                principalTable: "ShiftGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_WorkAreas_WorkAreaId",
                table: "Assets",
                column: "WorkAreaId",
                principalTable: "WorkAreas",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
