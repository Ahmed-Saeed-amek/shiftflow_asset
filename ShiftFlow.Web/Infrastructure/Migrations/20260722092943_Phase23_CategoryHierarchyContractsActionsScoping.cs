using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftFlow.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase23_CategoryHierarchyContractsActionsScoping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assets_Vendors_VendorId",
                table: "Assets");

            migrationBuilder.DropIndex(
                name: "IX_Assets_VendorId",
                table: "Assets");

            migrationBuilder.DropIndex(
                name: "IX_AssetCategories_Name",
                table: "AssetCategories");

            migrationBuilder.DropColumn(
                name: "VendorId",
                table: "Assets");

            migrationBuilder.AddColumn<int>(
                name: "ActionTypeId",
                table: "WorkOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CauseId",
                table: "WorkOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParentCategoryId",
                table: "AssetCategories",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssetActionTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetActionTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetActionTypes_AssetCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "AssetCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Contracts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VendorId = table.Column<int>(type: "int", nullable: false),
                    ContractType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ContractNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Cost = table.Column<decimal>(type: "decimal(12,2)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contracts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contracts_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserAssetScopes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ScopeType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ScopeValueId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAssetScopes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAssetScopes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssetActionCauses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActionTypeId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetActionCauses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetActionCauses_AssetActionTypes_ActionTypeId",
                        column: x => x.ActionTypeId,
                        principalTable: "AssetActionTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContractAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    AssetId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractAssets_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContractAssets_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_ActionTypeId",
                table: "WorkOrders",
                column: "ActionTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_CauseId",
                table: "WorkOrders",
                column: "CauseId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetCategories_ParentCategoryId_Name",
                table: "AssetCategories",
                columns: new[] { "ParentCategoryId", "Name" },
                unique: true,
                filter: "[ParentCategoryId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AssetActionCauses_ActionTypeId_Name",
                table: "AssetActionCauses",
                columns: new[] { "ActionTypeId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetActionTypes_CategoryId_Name",
                table: "AssetActionTypes",
                columns: new[] { "CategoryId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractAssets_AssetId",
                table: "ContractAssets",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractAssets_ContractId_AssetId",
                table: "ContractAssets",
                columns: new[] { "ContractId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_VendorId",
                table: "Contracts",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAssetScopes_UserId",
                table: "UserAssetScopes",
                column: "UserId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AssetCategories_AssetCategories_ParentCategoryId",
                table: "AssetCategories",
                column: "ParentCategoryId",
                principalTable: "AssetCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_AssetActionCauses_CauseId",
                table: "WorkOrders",
                column: "CauseId",
                principalTable: "AssetActionCauses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_AssetActionTypes_ActionTypeId",
                table: "WorkOrders",
                column: "ActionTypeId",
                principalTable: "AssetActionTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AssetCategories_AssetCategories_ParentCategoryId",
                table: "AssetCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_AssetActionCauses_CauseId",
                table: "WorkOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_AssetActionTypes_ActionTypeId",
                table: "WorkOrders");

            migrationBuilder.DropTable(
                name: "AssetActionCauses");

            migrationBuilder.DropTable(
                name: "ContractAssets");

            migrationBuilder.DropTable(
                name: "UserAssetScopes");

            migrationBuilder.DropTable(
                name: "AssetActionTypes");

            migrationBuilder.DropTable(
                name: "Contracts");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_ActionTypeId",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_CauseId",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_AssetCategories_ParentCategoryId_Name",
                table: "AssetCategories");

            migrationBuilder.DropColumn(
                name: "ActionTypeId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "CauseId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "ParentCategoryId",
                table: "AssetCategories");

            migrationBuilder.AddColumn<int>(
                name: "VendorId",
                table: "Assets",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_VendorId",
                table: "Assets",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetCategories_Name",
                table: "AssetCategories",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_Vendors_VendorId",
                table: "Assets",
                column: "VendorId",
                principalTable: "Vendors",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
