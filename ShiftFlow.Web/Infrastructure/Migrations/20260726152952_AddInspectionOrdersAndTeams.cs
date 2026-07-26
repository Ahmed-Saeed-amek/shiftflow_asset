using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftFlow.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInspectionOrdersAndTeams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Role rename: ShiftManager -> OperationsManager. In-place rename (not a new role +
            // reassignment) so it preserves the role's Id, and therefore every existing
            // RolePermissions/AspNetUserRoles row pointing at it (e.g. manager@shiftflow.com).
            migrationBuilder.Sql(
                "UPDATE AspNetRoles SET Name='OperationsManager', NormalizedName='OPERATIONSMANAGER' WHERE NormalizedName='SHIFTMANAGER';");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_WorkAreas_WorkAreaId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_InspectionRuns_ShiftTasks_ShiftTaskId",
                table: "InspectionRuns");

            migrationBuilder.DropTable(
                name: "EmployeeShiftExceptions");

            migrationBuilder.DropTable(
                name: "OvertimeAssignments");

            migrationBuilder.DropTable(
                name: "RotationTemplateDays");

            migrationBuilder.DropTable(
                name: "ShiftAssignments");

            migrationBuilder.DropTable(
                name: "ShiftIncidents");

            migrationBuilder.DropTable(
                name: "ShiftOverrides");

            migrationBuilder.DropTable(
                name: "ShiftReportAttachments");

            migrationBuilder.DropTable(
                name: "ShiftTaskCompletions");

            migrationBuilder.DropTable(
                name: "UserGroupMemberships");

            migrationBuilder.DropTable(
                name: "ShiftChangeRequests");

            migrationBuilder.DropTable(
                name: "ShiftReports");

            migrationBuilder.DropTable(
                name: "ShiftTasks");

            migrationBuilder.DropTable(
                name: "DailyGroupShifts");

            migrationBuilder.DropTable(
                name: "ShiftGroups");

            migrationBuilder.DropTable(
                name: "ShiftSchedules");

            migrationBuilder.DropTable(
                name: "RotationTemplates");

            migrationBuilder.DropTable(
                name: "WorkAreas");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_WorkAreaId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "WorkAreaId",
                table: "AspNetUsers");

            migrationBuilder.RenameColumn(
                name: "ShiftTaskId",
                table: "InspectionRuns",
                newName: "InspectionOrderId");

            migrationBuilder.RenameIndex(
                name: "IX_InspectionRuns_ShiftTaskId",
                table: "InspectionRuns",
                newName: "IX_InspectionRuns_InspectionOrderId");

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Teams_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InspectionOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AssignedToUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    AssignedToTeamId = table.Column<int>(type: "int", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionOrders", x => x.Id);
                    table.CheckConstraint("CK_InspectionOrder_ExactlyOneAssignee", "([AssignedToUserId] IS NOT NULL AND [AssignedToTeamId] IS NULL) OR ([AssignedToUserId] IS NULL AND [AssignedToTeamId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_InspectionOrders_AspNetUsers_AssignedToUserId",
                        column: x => x.AssignedToUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InspectionOrders_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InspectionOrders_Teams_AssignedToTeamId",
                        column: x => x.AssignedToTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeamMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TeamId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamMembers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamMembers_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InspectionOrders_AssignedToTeamId",
                table: "InspectionOrders",
                column: "AssignedToTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionOrders_AssignedToUserId",
                table: "InspectionOrders",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionOrders_CreatedByUserId",
                table: "InspectionOrders",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionOrders_OrderNumber",
                table: "InspectionOrders",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InspectionOrders_Status",
                table: "InspectionOrders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_TeamId_UserId",
                table: "TeamMembers",
                columns: new[] { "TeamId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_UserId",
                table: "TeamMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_CreatedByUserId",
                table: "Teams",
                column: "CreatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_InspectionRuns_InspectionOrders_InspectionOrderId",
                table: "InspectionRuns",
                column: "InspectionOrderId",
                principalTable: "InspectionOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE AspNetRoles SET Name='ShiftManager', NormalizedName='SHIFTMANAGER' WHERE NormalizedName='OPERATIONSMANAGER';");

            migrationBuilder.DropForeignKey(
                name: "FK_InspectionRuns_InspectionOrders_InspectionOrderId",
                table: "InspectionRuns");

            migrationBuilder.DropTable(
                name: "InspectionOrders");

            migrationBuilder.DropTable(
                name: "TeamMembers");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.RenameColumn(
                name: "InspectionOrderId",
                table: "InspectionRuns",
                newName: "ShiftTaskId");

            migrationBuilder.RenameIndex(
                name: "IX_InspectionRuns_InspectionOrderId",
                table: "InspectionRuns",
                newName: "IX_InspectionRuns_ShiftTaskId");

            migrationBuilder.AddColumn<int>(
                name: "WorkAreaId",
                table: "AspNetUsers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RotationTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RotationTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RotationTemplates_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkAreas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Color = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Latitude = table.Column<double>(type: "float", nullable: true),
                    Longitude = table.Column<double>(type: "float", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkAreas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RotationTemplateDays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RotationTemplateId = table.Column<int>(type: "int", nullable: false),
                    DayNumber = table.Column<int>(type: "int", nullable: false),
                    EveningGroupName = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    MorningGroupName = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    NightGroupName = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RotationTemplateDays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RotationTemplateDays_RotationTemplates_RotationTemplateId",
                        column: x => x.RotationTemplateId,
                        principalTable: "RotationTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShiftGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkAreaId = table.Column<int>(type: "int", nullable: true),
                    Color = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftGroups_WorkAreas_WorkAreaId",
                        column: x => x.WorkAreaId,
                        principalTable: "WorkAreas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ShiftSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RotationTemplateId = table.Column<int>(type: "int", nullable: true),
                    WorkAreaId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartRotationDay = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftSchedules_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftSchedules_RotationTemplates_RotationTemplateId",
                        column: x => x.RotationTemplateId,
                        principalTable: "RotationTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftSchedules_WorkAreas_WorkAreaId",
                        column: x => x.WorkAreaId,
                        principalTable: "WorkAreas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "UserGroupMemberships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShiftGroupId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserGroupMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserGroupMemberships_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserGroupMemberships_ShiftGroups_ShiftGroupId",
                        column: x => x.ShiftGroupId,
                        principalTable: "ShiftGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DailyGroupShifts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShiftGroupId = table.Column<int>(type: "int", nullable: false),
                    ShiftScheduleId = table.Column<int>(type: "int", nullable: false),
                    ActivatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ShiftEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ShiftStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ShiftType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyGroupShifts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyGroupShifts_ShiftGroups_ShiftGroupId",
                        column: x => x.ShiftGroupId,
                        principalTable: "ShiftGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyGroupShifts_ShiftSchedules_ShiftScheduleId",
                        column: x => x.ShiftScheduleId,
                        principalTable: "ShiftSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShiftOverrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ShiftGroupId = table.Column<int>(type: "int", nullable: false),
                    ShiftScheduleId = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NewShiftType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OriginalShiftType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftOverrides_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftOverrides_ShiftGroups_ShiftGroupId",
                        column: x => x.ShiftGroupId,
                        principalTable: "ShiftGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftOverrides_ShiftSchedules_ShiftScheduleId",
                        column: x => x.ShiftScheduleId,
                        principalTable: "ShiftSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShiftAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DailyGroupShiftId = table.Column<int>(type: "int", nullable: false),
                    ShiftGroupId = table.Column<int>(type: "int", nullable: false),
                    ShiftScheduleId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    AttendanceNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AttendanceStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ClockInTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClockOutTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ShiftType = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftAssignments_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftAssignments_DailyGroupShifts_DailyGroupShiftId",
                        column: x => x.DailyGroupShiftId,
                        principalTable: "DailyGroupShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftAssignments_ShiftGroups_ShiftGroupId",
                        column: x => x.ShiftGroupId,
                        principalTable: "ShiftGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftAssignments_ShiftSchedules_ShiftScheduleId",
                        column: x => x.ShiftScheduleId,
                        principalTable: "ShiftSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShiftChangeRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AffectedUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    DailyGroupShiftId = table.Column<int>(type: "int", nullable: false),
                    RequestedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ReviewedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    SwapWithUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    TargetGroupId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequestType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReviewNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftChangeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftChangeRequests_AspNetUsers_AffectedUserId",
                        column: x => x.AffectedUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftChangeRequests_AspNetUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftChangeRequests_AspNetUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftChangeRequests_AspNetUsers_SwapWithUserId",
                        column: x => x.SwapWithUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftChangeRequests_DailyGroupShifts_DailyGroupShiftId",
                        column: x => x.DailyGroupShiftId,
                        principalTable: "DailyGroupShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShiftChangeRequests_ShiftGroups_TargetGroupId",
                        column: x => x.TargetGroupId,
                        principalTable: "ShiftGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ShiftReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DailyGroupShiftId = table.Column<int>(type: "int", nullable: false),
                    AbsentCount = table.Column<int>(type: "int", nullable: false),
                    AttendeeCount = table.Column<int>(type: "int", nullable: false),
                    EquipmentStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftReports_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftReports_DailyGroupShifts_DailyGroupShiftId",
                        column: x => x.DailyGroupShiftId,
                        principalTable: "DailyGroupShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShiftTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssignedToUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DailyGroupShiftId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsMandatoryForHandover = table.Column<bool>(type: "bit", nullable: false),
                    RolledOverFromTaskId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftTasks_AspNetUsers_AssignedToUserId",
                        column: x => x.AssignedToUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ShiftTasks_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftTasks_DailyGroupShifts_DailyGroupShiftId",
                        column: x => x.DailyGroupShiftId,
                        principalTable: "DailyGroupShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeShiftExceptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AffectedUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ApprovedByChangeRequestId = table.Column<int>(type: "int", nullable: true),
                    DailyGroupShiftId = table.Column<int>(type: "int", nullable: false),
                    ReplacedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    SwapWithUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    TemporaryGroupId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExceptionType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OvertimeHours = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequestedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeShiftExceptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeShiftExceptions_AspNetUsers_AffectedUserId",
                        column: x => x.AffectedUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeShiftExceptions_AspNetUsers_ReplacedByUserId",
                        column: x => x.ReplacedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeShiftExceptions_AspNetUsers_SwapWithUserId",
                        column: x => x.SwapWithUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeShiftExceptions_DailyGroupShifts_DailyGroupShiftId",
                        column: x => x.DailyGroupShiftId,
                        principalTable: "DailyGroupShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeShiftExceptions_ShiftChangeRequests_ApprovedByChangeRequestId",
                        column: x => x.ApprovedByChangeRequestId,
                        principalTable: "ShiftChangeRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeShiftExceptions_ShiftGroups_TemporaryGroupId",
                        column: x => x.TemporaryGroupId,
                        principalTable: "ShiftGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OvertimeAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApprovedByChangeRequestId = table.Column<int>(type: "int", nullable: true),
                    DailyGroupShiftId = table.Column<int>(type: "int", nullable: false),
                    ShiftGroupId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    AttendanceNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AttendanceStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ClockInTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClockOutTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequestedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ShiftType = table.Column<string>(type: "nvarchar(max)", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "ShiftIncidents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DailyGroupShiftId = table.Column<int>(type: "int", nullable: false),
                    ReportedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ShiftReportId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReportedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Resolution = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftIncidents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftIncidents_AspNetUsers_ReportedByUserId",
                        column: x => x.ReportedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftIncidents_DailyGroupShifts_DailyGroupShiftId",
                        column: x => x.DailyGroupShiftId,
                        principalTable: "DailyGroupShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftIncidents_ShiftReports_ShiftReportId",
                        column: x => x.ShiftReportId,
                        principalTable: "ShiftReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShiftReportAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShiftReportId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    FileType = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    UploadedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UploadedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftReportAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftReportAttachments_ShiftReports_ShiftReportId",
                        column: x => x.ShiftReportId,
                        principalTable: "ShiftReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShiftTaskCompletions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShiftTaskId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NewStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PreviousStatus = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftTaskCompletions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftTaskCompletions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftTaskCompletions_ShiftTasks_ShiftTaskId",
                        column: x => x.ShiftTaskId,
                        principalTable: "ShiftTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_WorkAreaId",
                table: "AspNetUsers",
                column: "WorkAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyGroupShifts_Date_Status",
                table: "DailyGroupShifts",
                columns: new[] { "Date", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyGroupShifts_ShiftGroupId",
                table: "DailyGroupShifts",
                column: "ShiftGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyGroupShifts_ShiftScheduleId_ShiftGroupId_Date",
                table: "DailyGroupShifts",
                columns: new[] { "ShiftScheduleId", "ShiftGroupId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeShiftExceptions_AffectedUserId_DailyGroupShiftId",
                table: "EmployeeShiftExceptions",
                columns: new[] { "AffectedUserId", "DailyGroupShiftId" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeShiftExceptions_ApprovedByChangeRequestId",
                table: "EmployeeShiftExceptions",
                column: "ApprovedByChangeRequestId",
                unique: true,
                filter: "[ApprovedByChangeRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeShiftExceptions_DailyGroupShiftId",
                table: "EmployeeShiftExceptions",
                column: "DailyGroupShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeShiftExceptions_ReplacedByUserId",
                table: "EmployeeShiftExceptions",
                column: "ReplacedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeShiftExceptions_SwapWithUserId",
                table: "EmployeeShiftExceptions",
                column: "SwapWithUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeShiftExceptions_TemporaryGroupId",
                table: "EmployeeShiftExceptions",
                column: "TemporaryGroupId");

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

            migrationBuilder.CreateIndex(
                name: "IX_RotationTemplateDays_RotationTemplateId_DayNumber",
                table: "RotationTemplateDays",
                columns: new[] { "RotationTemplateId", "DayNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RotationTemplates_CreatedByUserId",
                table: "RotationTemplates",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RotationTemplates_IsDefault",
                table: "RotationTemplates",
                column: "IsDefault",
                filter: "[IsDefault] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignments_DailyGroupShiftId",
                table: "ShiftAssignments",
                column: "DailyGroupShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignments_ShiftGroupId",
                table: "ShiftAssignments",
                column: "ShiftGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignments_ShiftScheduleId_UserId_Date",
                table: "ShiftAssignments",
                columns: new[] { "ShiftScheduleId", "UserId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftAssignments_UserId_Date",
                table: "ShiftAssignments",
                columns: new[] { "UserId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftChangeRequests_AffectedUserId",
                table: "ShiftChangeRequests",
                column: "AffectedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftChangeRequests_DailyGroupShiftId",
                table: "ShiftChangeRequests",
                column: "DailyGroupShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftChangeRequests_RequestedByUserId",
                table: "ShiftChangeRequests",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftChangeRequests_ReviewedByUserId",
                table: "ShiftChangeRequests",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftChangeRequests_Status_CreatedAt",
                table: "ShiftChangeRequests",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftChangeRequests_SwapWithUserId",
                table: "ShiftChangeRequests",
                column: "SwapWithUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftChangeRequests_TargetGroupId",
                table: "ShiftChangeRequests",
                column: "TargetGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftGroups_Name_WorkAreaId",
                table: "ShiftGroups",
                columns: new[] { "Name", "WorkAreaId" },
                unique: true,
                filter: "[WorkAreaId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftGroups_WorkAreaId",
                table: "ShiftGroups",
                column: "WorkAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftIncidents_DailyGroupShiftId",
                table: "ShiftIncidents",
                column: "DailyGroupShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftIncidents_ReportedByUserId",
                table: "ShiftIncidents",
                column: "ReportedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftIncidents_ShiftReportId",
                table: "ShiftIncidents",
                column: "ShiftReportId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftOverrides_CreatedByUserId",
                table: "ShiftOverrides",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftOverrides_ShiftGroupId",
                table: "ShiftOverrides",
                column: "ShiftGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftOverrides_ShiftScheduleId_ShiftGroupId_Date",
                table: "ShiftOverrides",
                columns: new[] { "ShiftScheduleId", "ShiftGroupId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftReportAttachments_ShiftReportId",
                table: "ShiftReportAttachments",
                column: "ShiftReportId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftReports_CreatedByUserId",
                table: "ShiftReports",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftReports_DailyGroupShiftId",
                table: "ShiftReports",
                column: "DailyGroupShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftReports_SubmittedAt",
                table: "ShiftReports",
                column: "SubmittedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftSchedules_CreatedByUserId",
                table: "ShiftSchedules",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftSchedules_RotationTemplateId",
                table: "ShiftSchedules",
                column: "RotationTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftSchedules_WorkAreaId",
                table: "ShiftSchedules",
                column: "WorkAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTaskCompletions_ShiftTaskId",
                table: "ShiftTaskCompletions",
                column: "ShiftTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTaskCompletions_UserId",
                table: "ShiftTaskCompletions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTasks_AssignedToUserId",
                table: "ShiftTasks",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTasks_CreatedByUserId",
                table: "ShiftTasks",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTasks_DailyGroupShiftId_DisplayOrder",
                table: "ShiftTasks",
                columns: new[] { "DailyGroupShiftId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_UserGroupMemberships_ShiftGroupId_EffectiveTo",
                table: "UserGroupMemberships",
                columns: new[] { "ShiftGroupId", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_UserGroupMemberships_UserId_EffectiveTo",
                table: "UserGroupMemberships",
                columns: new[] { "UserId", "EffectiveTo" });

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_WorkAreas_WorkAreaId",
                table: "AspNetUsers",
                column: "WorkAreaId",
                principalTable: "WorkAreas",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_InspectionRuns_ShiftTasks_ShiftTaskId",
                table: "InspectionRuns",
                column: "ShiftTaskId",
                principalTable: "ShiftTasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
