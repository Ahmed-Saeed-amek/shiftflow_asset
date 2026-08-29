using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftFlow.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase30_AddLocationCategoriesAndZoneCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the old FK first — the column is renamed (not recreated) below, so it briefly
            // carries stale Block ids until the backfill UPDATE overwrites them.
            migrationBuilder.DropForeignKey(
                name: "FK_Zones_Blocks_BlockId",
                table: "Zones");

            migrationBuilder.RenameColumn(
                name: "BlockId",
                table: "Zones",
                newName: "LocationCategoryId");

            migrationBuilder.RenameIndex(
                name: "IX_Zones_BlockId_Name",
                table: "Zones",
                newName: "IX_Zones_LocationCategoryId_Name");

            migrationBuilder.CreateTable(
                name: "LocationCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationCategories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LocationCategories_Name",
                table: "LocationCategories",
                column: "Name",
                unique: true);

            // Seed the 3 fixed categories before the backfill/FK below need them to exist.
            migrationBuilder.Sql(
                "INSERT INTO LocationCategories (Name, NameAr) VALUES " +
                "(N'Main Locations', N'المواقع الرئيسية'), " +
                "(N'Side Locations', N'المواقع الفرعية'), " +
                "(N'Governmental Locations', N'المواقع الحكومية')");

            // Every existing Zone still holds its old (now meaningless) BlockId value in the
            // renamed column — there's no principled automatic mapping from Block to one of the
            // 3 new categories, so every zone defaults to Main Locations; admins recategorize by
            // hand afterward via the Zone edit form. This must run before the FK below, since the
            // stale values would otherwise violate it.
            migrationBuilder.Sql(
                "UPDATE Zones SET LocationCategoryId = (SELECT Id FROM LocationCategories WHERE Name = N'Main Locations')");

            migrationBuilder.AddForeignKey(
                name: "FK_Zones_LocationCategories_LocationCategoryId",
                table: "Zones",
                column: "LocationCategoryId",
                principalTable: "LocationCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Now safe to drop the old hierarchy tables — Zones no longer references Blocks.
            migrationBuilder.DropTable(
                name: "Blocks");

            migrationBuilder.DropTable(
                name: "Areas");

            migrationBuilder.DropTable(
                name: "Governorates");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Zones_LocationCategories_LocationCategoryId",
                table: "Zones");

            migrationBuilder.DropTable(
                name: "LocationCategories");

            migrationBuilder.RenameColumn(
                name: "LocationCategoryId",
                table: "Zones",
                newName: "BlockId");

            migrationBuilder.RenameIndex(
                name: "IX_Zones_LocationCategoryId_Name",
                table: "Zones",
                newName: "IX_Zones_BlockId_Name");

            migrationBuilder.CreateTable(
                name: "Governorates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CenterLat = table.Column<double>(type: "float", nullable: false),
                    CenterLng = table.Column<double>(type: "float", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Governorates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Areas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GovernorateId = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Areas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Areas_Governorates_GovernorateId",
                        column: x => x.GovernorateId,
                        principalTable: "Governorates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Blocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Blocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Blocks_Areas_AreaId",
                        column: x => x.AreaId,
                        principalTable: "Areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Areas_GovernorateId_Name",
                table: "Areas",
                columns: new[] { "GovernorateId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Blocks_AreaId_Number",
                table: "Blocks",
                columns: new[] { "AreaId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Governorates_Name",
                table: "Governorates",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Zones_Blocks_BlockId",
                table: "Zones",
                column: "BlockId",
                principalTable: "Blocks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
