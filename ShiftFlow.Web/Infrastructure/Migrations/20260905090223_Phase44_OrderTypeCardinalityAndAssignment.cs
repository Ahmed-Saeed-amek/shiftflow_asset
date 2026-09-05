using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftFlow.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase44_OrderTypeCardinalityAndAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowsMultipleAssets",
                table: "OrderTypes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AssignmentMode",
                table: "OrderTypes",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "Either");

            migrationBuilder.AlterColumn<string>(
                name: "AssignedToUserId",
                table: "MaintenanceOrders",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AddColumn<int>(
                name: "AssignedToTeamId",
                table: "MaintenanceOrders",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceOrders_AssignedToTeamId",
                table: "MaintenanceOrders",
                column: "AssignedToTeamId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MaintenanceOrder_ExactlyOneAssignee",
                table: "MaintenanceOrders",
                sql: "([AssignedToUserId] IS NOT NULL AND [AssignedToTeamId] IS NULL) OR ([AssignedToUserId] IS NULL AND [AssignedToTeamId] IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceOrders_Teams_AssignedToTeamId",
                table: "MaintenanceOrders",
                column: "AssignedToTeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceOrders_Teams_AssignedToTeamId",
                table: "MaintenanceOrders");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceOrders_AssignedToTeamId",
                table: "MaintenanceOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MaintenanceOrder_ExactlyOneAssignee",
                table: "MaintenanceOrders");

            migrationBuilder.DropColumn(
                name: "AllowsMultipleAssets",
                table: "OrderTypes");

            migrationBuilder.DropColumn(
                name: "AssignmentMode",
                table: "OrderTypes");

            migrationBuilder.DropColumn(
                name: "AssignedToTeamId",
                table: "MaintenanceOrders");

            migrationBuilder.AlterColumn<string>(
                name: "AssignedToUserId",
                table: "MaintenanceOrders",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);
        }
    }
}
