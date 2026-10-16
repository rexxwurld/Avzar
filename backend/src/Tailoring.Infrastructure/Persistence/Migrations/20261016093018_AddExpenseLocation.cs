using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                table: "Expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_BusinessId_LocationId",
                table: "Expenses",
                columns: new[] { "BusinessId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_LocationId_BusinessId",
                table: "Expenses",
                columns: new[] { "LocationId", "BusinessId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_Locations_LocationId_BusinessId",
                table: "Expenses",
                columns: new[] { "LocationId", "BusinessId" },
                principalTable: "Locations",
                principalColumns: new[] { "Id", "BusinessId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_Locations_LocationId_BusinessId",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_BusinessId_LocationId",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_LocationId_BusinessId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "Expenses");
        }
    }
}
