using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftFlow.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase46_RecurringOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledDate",
                table: "MaintenanceOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceRecurringOrderId",
                table: "MaintenanceOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledDate",
                table: "InspectionOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceRecurringOrderId",
                table: "InspectionOrders",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecurringOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderTypeId = table.Column<int>(type: "int", nullable: false),
                    AssetId = table.Column<int>(type: "int", nullable: false),
                    AssignedToUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    AssignedToTeamId = table.Column<int>(type: "int", nullable: true),
                    Cadence = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringOrders", x => x.Id);
                    table.CheckConstraint("CK_RecurringOrder_ExactlyOneAssignee", "([AssignedToUserId] IS NOT NULL AND [AssignedToTeamId] IS NULL) OR ([AssignedToUserId] IS NULL AND [AssignedToTeamId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_RecurringOrders_AspNetUsers_AssignedToUserId",
                        column: x => x.AssignedToUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringOrders_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringOrders_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringOrders_OrderTypes_OrderTypeId",
                        column: x => x.OrderTypeId,
                        principalTable: "OrderTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurringOrders_Teams_AssignedToTeamId",
                        column: x => x.AssignedToTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceOrders_SourceRecurringOrderId_ScheduledDate",
                table: "MaintenanceOrders",
                columns: new[] { "SourceRecurringOrderId", "ScheduledDate" },
                unique: true,
                filter: "[SourceRecurringOrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionOrders_SourceRecurringOrderId_ScheduledDate",
                table: "InspectionOrders",
                columns: new[] { "SourceRecurringOrderId", "ScheduledDate" },
                unique: true,
                filter: "[SourceRecurringOrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOrders_AssetId",
                table: "RecurringOrders",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOrders_AssignedToTeamId",
                table: "RecurringOrders",
                column: "AssignedToTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOrders_AssignedToUserId",
                table: "RecurringOrders",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOrders_CreatedByUserId",
                table: "RecurringOrders",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOrders_IsActive",
                table: "RecurringOrders",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOrders_OrderTypeId",
                table: "RecurringOrders",
                column: "OrderTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_InspectionOrders_RecurringOrders_SourceRecurringOrderId",
                table: "InspectionOrders",
                column: "SourceRecurringOrderId",
                principalTable: "RecurringOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceOrders_RecurringOrders_SourceRecurringOrderId",
                table: "MaintenanceOrders",
                column: "SourceRecurringOrderId",
                principalTable: "RecurringOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InspectionOrders_RecurringOrders_SourceRecurringOrderId",
                table: "InspectionOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceOrders_RecurringOrders_SourceRecurringOrderId",
                table: "MaintenanceOrders");

            migrationBuilder.DropTable(
                name: "RecurringOrders");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceOrders_SourceRecurringOrderId_ScheduledDate",
                table: "MaintenanceOrders");

            migrationBuilder.DropIndex(
                name: "IX_InspectionOrders_SourceRecurringOrderId_ScheduledDate",
                table: "InspectionOrders");

            migrationBuilder.DropColumn(
                name: "ScheduledDate",
                table: "MaintenanceOrders");

            migrationBuilder.DropColumn(
                name: "SourceRecurringOrderId",
                table: "MaintenanceOrders");

            migrationBuilder.DropColumn(
                name: "ScheduledDate",
                table: "InspectionOrders");

            migrationBuilder.DropColumn(
                name: "SourceRecurringOrderId",
                table: "InspectionOrders");
        }
    }
}
