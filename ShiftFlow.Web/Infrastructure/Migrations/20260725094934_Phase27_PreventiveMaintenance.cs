using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftFlow.Web.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase27_PreventiveMaintenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledDate",
                table: "WorkOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceContractId",
                table: "WorkOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ContractType",
                table: "Contracts",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<string>(
                name: "PmCadence",
                table: "Contracts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_SourceContractId_AssetId_ScheduledDate",
                table: "WorkOrders",
                columns: new[] { "SourceContractId", "AssetId", "ScheduledDate" },
                unique: true,
                filter: "[SourceContractId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_Contracts_SourceContractId",
                table: "WorkOrders",
                column: "SourceContractId",
                principalTable: "Contracts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_Contracts_SourceContractId",
                table: "WorkOrders");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_SourceContractId_AssetId_ScheduledDate",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "ScheduledDate",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "SourceContractId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "PmCadence",
                table: "Contracts");

            migrationBuilder.AlterColumn<string>(
                name: "ContractType",
                table: "Contracts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(30)",
                oldMaxLength: 30);
        }
    }
}
