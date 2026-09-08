using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftFlow.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Hand-edited: the scaffolded migration used DropTable(Teams/TeamMembers) + CreateTable(Groups/
    // GroupMembers), which would have silently deleted every existing team and its membership rows
    // (EF's differ can't correlate a renamed CLR type on its own). Rewritten to use RenameTable/
    // RenameColumn/RenameIndex throughout so existing data survives the rename. Check constraints
    // still need an explicit drop+recreate since their CHECK expression is stored as literal SQL
    // text — sp_rename on a column does not rewrite text embedded inside a constraint definition.
    public partial class RenameTeamToGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InspectionOrders_Teams_AssignedToTeamId",
                table: "InspectionOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceOrders_Teams_AssignedToTeamId",
                table: "MaintenanceOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_RecurringOrders_Teams_AssignedToTeamId",
                table: "RecurringOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamMembers_Teams_TeamId",
                table: "TeamMembers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RecurringOrder_ExactlyOneAssignee",
                table: "RecurringOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MaintenanceOrder_ExactlyOneAssignee",
                table: "MaintenanceOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InspectionOrder_ExactlyOneAssignee",
                table: "InspectionOrders");

            // Table/column renames — preserve every existing row.
            migrationBuilder.RenameTable(name: "Teams", newName: "Groups");
            migrationBuilder.RenameTable(name: "TeamMembers", newName: "GroupMembers");
            migrationBuilder.RenameColumn(name: "TeamId", table: "GroupMembers", newName: "GroupId");
            migrationBuilder.RenameColumn(name: "AssignedToTeamId", table: "RecurringOrders", newName: "AssignedToGroupId");
            migrationBuilder.RenameColumn(name: "AssignedToTeamId", table: "MaintenanceOrders", newName: "AssignedToGroupId");
            migrationBuilder.RenameColumn(name: "AssignedToTeamId", table: "InspectionOrders", newName: "AssignedToGroupId");

            migrationBuilder.RenameIndex(name: "IX_Teams_CreatedByUserId", table: "Groups", newName: "IX_Groups_CreatedByUserId");
            migrationBuilder.RenameIndex(name: "IX_Teams_Name", table: "Groups", newName: "IX_Groups_Name");
            migrationBuilder.RenameIndex(name: "IX_TeamMembers_TeamId_UserId", table: "GroupMembers", newName: "IX_GroupMembers_GroupId_UserId");
            migrationBuilder.RenameIndex(name: "IX_TeamMembers_UserId", table: "GroupMembers", newName: "IX_GroupMembers_UserId");
            migrationBuilder.RenameIndex(name: "IX_RecurringOrders_AssignedToTeamId", table: "RecurringOrders", newName: "IX_RecurringOrders_AssignedToGroupId");
            migrationBuilder.RenameIndex(name: "IX_MaintenanceOrders_AssignedToTeamId", table: "MaintenanceOrders", newName: "IX_MaintenanceOrders_AssignedToGroupId");
            migrationBuilder.RenameIndex(name: "IX_InspectionOrders_AssignedToTeamId", table: "InspectionOrders", newName: "IX_InspectionOrders_AssignedToGroupId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RecurringOrder_ExactlyOneAssignee",
                table: "RecurringOrders",
                sql: "([AssignedToUserId] IS NOT NULL AND [AssignedToGroupId] IS NULL) OR ([AssignedToUserId] IS NULL AND [AssignedToGroupId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MaintenanceOrder_ExactlyOneAssignee",
                table: "MaintenanceOrders",
                sql: "([AssignedToUserId] IS NOT NULL AND [AssignedToGroupId] IS NULL) OR ([AssignedToUserId] IS NULL AND [AssignedToGroupId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InspectionOrder_ExactlyOneAssignee",
                table: "InspectionOrders",
                sql: "([AssignedToUserId] IS NOT NULL AND [AssignedToGroupId] IS NULL) OR ([AssignedToUserId] IS NULL AND [AssignedToGroupId] IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_GroupMembers_Groups_GroupId",
                table: "GroupMembers",
                column: "GroupId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_InspectionOrders_Groups_AssignedToGroupId",
                table: "InspectionOrders",
                column: "AssignedToGroupId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceOrders_Groups_AssignedToGroupId",
                table: "MaintenanceOrders",
                column: "AssignedToGroupId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringOrders_Groups_AssignedToGroupId",
                table: "RecurringOrders",
                column: "AssignedToGroupId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InspectionOrders_Groups_AssignedToGroupId",
                table: "InspectionOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceOrders_Groups_AssignedToGroupId",
                table: "MaintenanceOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_RecurringOrders_Groups_AssignedToGroupId",
                table: "RecurringOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_GroupMembers_Groups_GroupId",
                table: "GroupMembers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RecurringOrder_ExactlyOneAssignee",
                table: "RecurringOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MaintenanceOrder_ExactlyOneAssignee",
                table: "MaintenanceOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InspectionOrder_ExactlyOneAssignee",
                table: "InspectionOrders");

            migrationBuilder.RenameIndex(name: "IX_Groups_CreatedByUserId", table: "Groups", newName: "IX_Teams_CreatedByUserId");
            migrationBuilder.RenameIndex(name: "IX_Groups_Name", table: "Groups", newName: "IX_Teams_Name");
            migrationBuilder.RenameIndex(name: "IX_GroupMembers_GroupId_UserId", table: "GroupMembers", newName: "IX_TeamMembers_TeamId_UserId");
            migrationBuilder.RenameIndex(name: "IX_GroupMembers_UserId", table: "GroupMembers", newName: "IX_TeamMembers_UserId");
            migrationBuilder.RenameIndex(name: "IX_RecurringOrders_AssignedToGroupId", table: "RecurringOrders", newName: "IX_RecurringOrders_AssignedToTeamId");
            migrationBuilder.RenameIndex(name: "IX_MaintenanceOrders_AssignedToGroupId", table: "MaintenanceOrders", newName: "IX_MaintenanceOrders_AssignedToTeamId");
            migrationBuilder.RenameIndex(name: "IX_InspectionOrders_AssignedToGroupId", table: "InspectionOrders", newName: "IX_InspectionOrders_AssignedToTeamId");

            migrationBuilder.RenameColumn(name: "AssignedToGroupId", table: "RecurringOrders", newName: "AssignedToTeamId");
            migrationBuilder.RenameColumn(name: "AssignedToGroupId", table: "MaintenanceOrders", newName: "AssignedToTeamId");
            migrationBuilder.RenameColumn(name: "AssignedToGroupId", table: "InspectionOrders", newName: "AssignedToTeamId");
            migrationBuilder.RenameColumn(name: "GroupId", table: "GroupMembers", newName: "TeamId");
            migrationBuilder.RenameTable(name: "GroupMembers", newName: "TeamMembers");
            migrationBuilder.RenameTable(name: "Groups", newName: "Teams");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RecurringOrder_ExactlyOneAssignee",
                table: "RecurringOrders",
                sql: "([AssignedToUserId] IS NOT NULL AND [AssignedToTeamId] IS NULL) OR ([AssignedToUserId] IS NULL AND [AssignedToTeamId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MaintenanceOrder_ExactlyOneAssignee",
                table: "MaintenanceOrders",
                sql: "([AssignedToUserId] IS NOT NULL AND [AssignedToTeamId] IS NULL) OR ([AssignedToUserId] IS NULL AND [AssignedToTeamId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InspectionOrder_ExactlyOneAssignee",
                table: "InspectionOrders",
                sql: "([AssignedToUserId] IS NOT NULL AND [AssignedToTeamId] IS NULL) OR ([AssignedToUserId] IS NULL AND [AssignedToTeamId] IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_TeamMembers_Teams_TeamId",
                table: "TeamMembers",
                column: "TeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_InspectionOrders_Teams_AssignedToTeamId",
                table: "InspectionOrders",
                column: "AssignedToTeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceOrders_Teams_AssignedToTeamId",
                table: "MaintenanceOrders",
                column: "AssignedToTeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringOrders_Teams_AssignedToTeamId",
                table: "RecurringOrders",
                column: "AssignedToTeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
