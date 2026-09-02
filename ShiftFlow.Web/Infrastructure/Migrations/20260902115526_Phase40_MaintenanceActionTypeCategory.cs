using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftFlow.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase40_MaintenanceActionTypeCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "MaintenanceActionTypes",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceActionTypes_CategoryId",
                table: "MaintenanceActionTypes",
                column: "CategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceActionTypes_AssetCategories_CategoryId",
                table: "MaintenanceActionTypes",
                column: "CategoryId",
                principalTable: "AssetCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceActionTypes_AssetCategories_CategoryId",
                table: "MaintenanceActionTypes");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceActionTypes_CategoryId",
                table: "MaintenanceActionTypes");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "MaintenanceActionTypes");
        }
    }
}
