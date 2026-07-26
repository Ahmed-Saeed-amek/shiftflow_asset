using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftFlow.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase2a_AddAssetCategoryFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Assets_Category",
                table: "Assets");

            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "Assets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParentCategoryId",
                table: "AssetCategories",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_CategoryId",
                table: "Assets",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetCategories_ParentCategoryId",
                table: "AssetCategories",
                column: "ParentCategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_AssetCategories_AssetCategories_ParentCategoryId",
                table: "AssetCategories",
                column: "ParentCategoryId",
                principalTable: "AssetCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_AssetCategories_CategoryId",
                table: "Assets",
                column: "CategoryId",
                principalTable: "AssetCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Data backfill: create a real AssetCategory row for every distinct legacy
            // Category string an Asset actually uses (that doesn't already have one),
            // fall back blank/unmatched values to a new "Uncategorized" category, then
            // point every Asset.CategoryId at the matching row. This must leave zero
            // NULL CategoryId rows so the follow-up migration can safely require it.
            migrationBuilder.Sql(@"
                INSERT INTO AssetCategories (Name, IsActive, SortOrder)
                SELECT DISTINCT a.Category, 1, 0
                FROM Assets a
                WHERE a.Category IS NOT NULL AND a.Category <> ''
                  AND NOT EXISTS (SELECT 1 FROM AssetCategories c WHERE c.Name = a.Category);

                IF NOT EXISTS (SELECT 1 FROM AssetCategories WHERE Name = 'Uncategorized')
                    INSERT INTO AssetCategories (Name, IsActive, SortOrder) VALUES ('Uncategorized', 1, 999);

                UPDATE a
                SET a.CategoryId = c.Id
                FROM Assets a
                JOIN AssetCategories c ON c.Name = a.Category
                WHERE a.CategoryId IS NULL;

                UPDATE a
                SET a.CategoryId = (SELECT Id FROM AssetCategories WHERE Name = 'Uncategorized')
                FROM Assets a
                WHERE a.CategoryId IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AssetCategories_AssetCategories_ParentCategoryId",
                table: "AssetCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_Assets_AssetCategories_CategoryId",
                table: "Assets");

            migrationBuilder.DropIndex(
                name: "IX_Assets_CategoryId",
                table: "Assets");

            migrationBuilder.DropIndex(
                name: "IX_AssetCategories_ParentCategoryId",
                table: "AssetCategories");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "ParentCategoryId",
                table: "AssetCategories");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_Category",
                table: "Assets",
                column: "Category");
        }
    }
}
