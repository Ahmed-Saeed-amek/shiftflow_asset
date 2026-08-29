using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftFlow.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase36_OrderTypesVendorFlagAssetScopeRework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. WorkOrder.RequiresVendorResponse ────────────────────────────────
            // Default false (vendor not required) matches the new column going forward.
            // Backfill existing rows to true wherever a vendor is already engaged, or the
            // work order is PM-generated (always vendor-driven) — everything else keeps
            // today's only actual behavior (VendorId==null implies no vendor involvement).
            migrationBuilder.AddColumn<bool>(
                name: "RequiresVendorResponse",
                table: "WorkOrders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                "UPDATE WorkOrders SET RequiresVendorResponse = 1 WHERE VendorId IS NOT NULL OR SourceContractId IS NOT NULL");

            // ── 2. OrderTypes catalog ───────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "OrderTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Prefix = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    AppliesTo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TracksDefectOutcome = table.Column<bool>(type: "bit", nullable: false),
                    RequiresVendor = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderTypes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderTypes_AppliesTo_Name",
                table: "OrderTypes",
                columns: new[] { "AppliesTo", "Name" },
                unique: true);

            // Explicit fixed ids so the InspectionOrders/MaintenanceOrders backfill below can
            // reference them deterministically, without depending on identity auto-numbering.
            migrationBuilder.InsertData(
                table: "OrderTypes",
                columns: new[] { "Id", "Name", "NameAr", "Prefix", "AppliesTo", "TracksDefectOutcome", "RequiresVendor", "IsActive", "SortOrder" },
                values: new object[,]
                {
                    { 1, "Inspection", null, "INS", "Inspection", true, true, true, 0 },
                    { 2, "Quick Check", null, "QC", "Inspection", false, true, true, 1 },
                    { 3, "Standard", null, "MO", "Maintenance", false, false, true, 0 },
                });

            // ── 3. InspectionOrder.OrderKind (string) -> OrderTypeId (FK) ──────────
            // Nullable first so the backfill can run, then tightened to NOT NULL once every
            // row has a value — every existing row's OrderKind is one of exactly two known
            // strings, both migrated above, so the CASE below always matches.
            migrationBuilder.AddColumn<int>(
                name: "OrderTypeId",
                table: "InspectionOrders",
                type: "int",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE InspectionOrders SET OrderTypeId = CASE WHEN OrderKind = 'Inspection' THEN 1 WHEN OrderKind = 'QuickCheck' THEN 2 ELSE 1 END");

            migrationBuilder.AlterColumn<int>(
                name: "OrderTypeId",
                table: "InspectionOrders",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "OrderKind",
                table: "InspectionOrders");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionOrders_OrderTypeId",
                table: "InspectionOrders",
                column: "OrderTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_InspectionOrders_OrderTypes_OrderTypeId",
                table: "InspectionOrders",
                column: "OrderTypeId",
                principalTable: "OrderTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ── 4. MaintenanceOrder.OrderTypeId (nullable FK) ──────────────────────
            // Every existing MaintenanceOrder was created under today's only behavior — no
            // vendor, no type concept — so backfill them all to the seeded "Standard" type
            // for reporting consistency, even though null would also behave correctly.
            migrationBuilder.AddColumn<int>(
                name: "OrderTypeId",
                table: "MaintenanceOrders",
                type: "int",
                nullable: true);

            migrationBuilder.Sql("UPDATE MaintenanceOrders SET OrderTypeId = 3");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceOrders_OrderTypeId",
                table: "MaintenanceOrders",
                column: "OrderTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceOrders_OrderTypes_OrderTypeId",
                table: "MaintenanceOrders",
                column: "OrderTypeId",
                principalTable: "OrderTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ── 5. UserAssetScope: polymorphic ScopeType/ScopeValueId -> explicit,
            // independently-optional Zone/LocationCategory/Category FKs ────────────
            migrationBuilder.AddColumn<int>(
                name: "ZoneId",
                table: "UserAssetScopes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LocationCategoryId",
                table: "UserAssetScopes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "UserAssetScopes",
                type: "int",
                nullable: true);

            migrationBuilder.Sql("UPDATE UserAssetScopes SET ZoneId = ScopeValueId WHERE ScopeType = 'Zone'");
            migrationBuilder.Sql("UPDATE UserAssetScopes SET LocationCategoryId = ScopeValueId WHERE ScopeType = 'LocationCategory'");
            migrationBuilder.Sql("UPDATE UserAssetScopes SET CategoryId = ScopeValueId WHERE ScopeType = 'Category'");

            migrationBuilder.DropColumn(
                name: "ScopeType",
                table: "UserAssetScopes");

            migrationBuilder.DropColumn(
                name: "ScopeValueId",
                table: "UserAssetScopes");

            migrationBuilder.CreateIndex(
                name: "IX_UserAssetScopes_ZoneId",
                table: "UserAssetScopes",
                column: "ZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAssetScopes_LocationCategoryId",
                table: "UserAssetScopes",
                column: "LocationCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAssetScopes_CategoryId",
                table: "UserAssetScopes",
                column: "CategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserAssetScopes_Zones_ZoneId",
                table: "UserAssetScopes",
                column: "ZoneId",
                principalTable: "Zones",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserAssetScopes_LocationCategories_LocationCategoryId",
                table: "UserAssetScopes",
                column: "LocationCategoryId",
                principalTable: "LocationCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserAssetScopes_AssetCategories_CategoryId",
                table: "UserAssetScopes",
                column: "CategoryId",
                principalTable: "AssetCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InspectionOrders_OrderTypes_OrderTypeId",
                table: "InspectionOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceOrders_OrderTypes_OrderTypeId",
                table: "MaintenanceOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_UserAssetScopes_AssetCategories_CategoryId",
                table: "UserAssetScopes");

            migrationBuilder.DropForeignKey(
                name: "FK_UserAssetScopes_LocationCategories_LocationCategoryId",
                table: "UserAssetScopes");

            migrationBuilder.DropForeignKey(
                name: "FK_UserAssetScopes_Zones_ZoneId",
                table: "UserAssetScopes");

            migrationBuilder.DropTable(
                name: "OrderTypes");

            migrationBuilder.DropIndex(
                name: "IX_UserAssetScopes_CategoryId",
                table: "UserAssetScopes");

            migrationBuilder.DropIndex(
                name: "IX_UserAssetScopes_LocationCategoryId",
                table: "UserAssetScopes");

            migrationBuilder.DropIndex(
                name: "IX_UserAssetScopes_ZoneId",
                table: "UserAssetScopes");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceOrders_OrderTypeId",
                table: "MaintenanceOrders");

            migrationBuilder.DropIndex(
                name: "IX_InspectionOrders_OrderTypeId",
                table: "InspectionOrders");

            migrationBuilder.DropColumn(
                name: "RequiresVendorResponse",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "UserAssetScopes");

            migrationBuilder.DropColumn(
                name: "LocationCategoryId",
                table: "UserAssetScopes");

            migrationBuilder.DropColumn(
                name: "ZoneId",
                table: "UserAssetScopes");

            migrationBuilder.DropColumn(
                name: "OrderTypeId",
                table: "MaintenanceOrders");

            migrationBuilder.DropColumn(
                name: "OrderTypeId",
                table: "InspectionOrders");

            migrationBuilder.AddColumn<string>(
                name: "ScopeType",
                table: "UserAssetScopes",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ScopeValueId",
                table: "UserAssetScopes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "OrderKind",
                table: "InspectionOrders",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }
    }
}
