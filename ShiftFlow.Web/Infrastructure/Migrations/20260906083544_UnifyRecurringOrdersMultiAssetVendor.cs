using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftFlow.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UnifyRecurringOrdersMultiAssetVendor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Preserve each existing schedule's single AssetId into a temp table before the column is
            // dropped below, so it can be re-inserted into the new RecurringOrderAssets join table once
            // that table exists further down — otherwise every pre-existing RecurringOrder would
            // silently lose its only asset link.
            migrationBuilder.Sql("SELECT Id AS RecurringOrderId, AssetId INTO #RecurringOrderAssetBackup FROM RecurringOrders;");

            migrationBuilder.DropForeignKey(
                name: "FK_RecurringOrders_Assets_AssetId",
                table: "RecurringOrders");

            migrationBuilder.DropIndex(
                name: "IX_RecurringOrders_AssetId",
                table: "RecurringOrders");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceOrders_SourceRecurringOrderId_ScheduledDate",
                table: "MaintenanceOrders");

            migrationBuilder.DropIndex(
                name: "IX_InspectionOrders_SourceRecurringOrderId_ScheduledDate",
                table: "InspectionOrders");

            migrationBuilder.DropColumn(
                name: "AssetId",
                table: "RecurringOrders");

            migrationBuilder.AddColumn<int>(
                name: "SourceRecurringOrderId",
                table: "WorkOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VendorId",
                table: "RecurringOrders",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecurringOrderAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecurringOrderId = table.Column<int>(type: "int", nullable: false),
                    AssetId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringOrderAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringOrderAssets_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecurringOrderAssets_RecurringOrders_RecurringOrderId",
                        column: x => x.RecurringOrderId,
                        principalTable: "RecurringOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_SourceRecurringOrderId_AssetId_ScheduledDate",
                table: "WorkOrders",
                columns: new[] { "SourceRecurringOrderId", "AssetId", "ScheduledDate" },
                unique: true,
                filter: "[SourceRecurringOrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOrders_VendorId",
                table: "RecurringOrders",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceOrders_SourceRecurringOrderId_AssetId_ScheduledDate",
                table: "MaintenanceOrders",
                columns: new[] { "SourceRecurringOrderId", "AssetId", "ScheduledDate" },
                unique: true,
                filter: "[SourceRecurringOrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionOrders_SourceRecurringOrderId",
                table: "InspectionOrders",
                column: "SourceRecurringOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOrderAssets_AssetId",
                table: "RecurringOrderAssets",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOrderAssets_RecurringOrderId_AssetId",
                table: "RecurringOrderAssets",
                columns: new[] { "RecurringOrderId", "AssetId" },
                unique: true);

            // Restore each pre-existing schedule's asset link from the backup captured at the top of
            // this migration, then drop the now-unneeded temp table.
            migrationBuilder.Sql(
                "INSERT INTO RecurringOrderAssets (RecurringOrderId, AssetId) SELECT RecurringOrderId, AssetId FROM #RecurringOrderAssetBackup; " +
                "DROP TABLE #RecurringOrderAssetBackup;");

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringOrders_Vendors_VendorId",
                table: "RecurringOrders",
                column: "VendorId",
                principalTable: "Vendors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_RecurringOrders_SourceRecurringOrderId",
                table: "WorkOrders",
                column: "SourceRecurringOrderId",
                principalTable: "RecurringOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RecurringOrders_Vendors_VendorId",
                table: "RecurringOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_RecurringOrders_SourceRecurringOrderId",
                table: "WorkOrders");

            migrationBuilder.DropTable(
                name: "RecurringOrderAssets");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_SourceRecurringOrderId_AssetId_ScheduledDate",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_RecurringOrders_VendorId",
                table: "RecurringOrders");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceOrders_SourceRecurringOrderId_AssetId_ScheduledDate",
                table: "MaintenanceOrders");

            migrationBuilder.DropIndex(
                name: "IX_InspectionOrders_SourceRecurringOrderId",
                table: "InspectionOrders");

            migrationBuilder.DropColumn(
                name: "SourceRecurringOrderId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "VendorId",
                table: "RecurringOrders");

            migrationBuilder.AddColumn<int>(
                name: "AssetId",
                table: "RecurringOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOrders_AssetId",
                table: "RecurringOrders",
                column: "AssetId");

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

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringOrders_Assets_AssetId",
                table: "RecurringOrders",
                column: "AssetId",
                principalTable: "Assets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
