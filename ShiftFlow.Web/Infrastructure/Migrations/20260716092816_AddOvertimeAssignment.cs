using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftFlow.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOvertimeAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OvertimeAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DailyGroupShiftId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ShiftGroupId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ShiftType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AttendanceStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ClockInTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClockOutTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AttendanceNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequestedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ApprovedByChangeRequestId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OvertimeAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OvertimeAssignments_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OvertimeAssignments_DailyGroupShifts_DailyGroupShiftId",
                        column: x => x.DailyGroupShiftId,
                        principalTable: "DailyGroupShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OvertimeAssignments_ShiftChangeRequests_ApprovedByChangeRequestId",
                        column: x => x.ApprovedByChangeRequestId,
                        principalTable: "ShiftChangeRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OvertimeAssignments_ShiftGroups_ShiftGroupId",
                        column: x => x.ShiftGroupId,
                        principalTable: "ShiftGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OvertimeAssignments_ApprovedByChangeRequestId",
                table: "OvertimeAssignments",
                column: "ApprovedByChangeRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_OvertimeAssignments_DailyGroupShiftId",
                table: "OvertimeAssignments",
                column: "DailyGroupShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_OvertimeAssignments_ShiftGroupId",
                table: "OvertimeAssignments",
                column: "ShiftGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_OvertimeAssignments_UserId_DailyGroupShiftId",
                table: "OvertimeAssignments",
                columns: new[] { "UserId", "DailyGroupShiftId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OvertimeAssignments");
        }
    }
}
