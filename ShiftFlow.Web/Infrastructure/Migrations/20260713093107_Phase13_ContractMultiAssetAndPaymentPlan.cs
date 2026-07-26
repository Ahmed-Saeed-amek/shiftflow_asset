using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftFlow.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase13_ContractMultiAssetAndPaymentPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssetContractAssets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssetContractId = table.Column<int>(type: "int", nullable: false),
                    AssetId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetContractAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetContractAssets_AssetContracts_AssetContractId",
                        column: x => x.AssetContractId,
                        principalTable: "AssetContracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssetContractAssets_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContractPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssetContractId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsPaid = table.Column<bool>(type: "bit", nullable: false),
                    PaidDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractPayments_AssetContracts_AssetContractId",
                        column: x => x.AssetContractId,
                        principalTable: "AssetContracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetContractAssets_AssetContractId_AssetId",
                table: "AssetContractAssets",
                columns: new[] { "AssetContractId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssetContractAssets_AssetId",
                table: "AssetContractAssets",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractPayments_AssetContractId",
                table: "ContractPayments",
                column: "AssetContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractPayments_DueDate",
                table: "ContractPayments",
                column: "DueDate");

            // Backfill: one join row per existing contract's current AssetId,
            // before the column is dropped below.
            migrationBuilder.Sql(
                "INSERT INTO AssetContractAssets (AssetContractId, AssetId) SELECT Id, AssetId FROM AssetContracts");

            migrationBuilder.DropForeignKey(
                name: "FK_AssetContracts_Assets_AssetId",
                table: "AssetContracts");

            migrationBuilder.DropIndex(
                name: "IX_AssetContracts_AssetId",
                table: "AssetContracts");

            migrationBuilder.DropColumn(
                name: "AssetId",
                table: "AssetContracts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssetId",
                table: "AssetContracts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Backfill: pick one linked asset (the lowest id) per contract before
            // the join table is dropped below — a contract linked to several
            // assets can only keep one under the old single-FK shape.
            migrationBuilder.Sql(@"
                UPDATE ac SET ac.AssetId = links.MinAssetId
                FROM AssetContracts ac
                CROSS APPLY (SELECT MIN(AssetId) AS MinAssetId FROM AssetContractAssets WHERE AssetContractId = ac.Id) links
                WHERE links.MinAssetId IS NOT NULL");

            migrationBuilder.DropTable(
                name: "AssetContractAssets");

            migrationBuilder.DropTable(
                name: "ContractPayments");

            migrationBuilder.CreateIndex(
                name: "IX_AssetContracts_AssetId",
                table: "AssetContracts",
                column: "AssetId");

            migrationBuilder.AddForeignKey(
                name: "FK_AssetContracts_Assets_AssetId",
                table: "AssetContracts",
                column: "AssetId",
                principalTable: "Assets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
