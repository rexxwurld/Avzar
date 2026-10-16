using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailDeliveryStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeliveryDetail",
                table: "EmailOutbox",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryStatus",
                table: "EmailOutbox",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveryUpdatedAtUtc",
                table: "EmailOutbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailOutbox_ProviderMessageId",
                table: "EmailOutbox",
                column: "ProviderMessageId",
                filter: "\"ProviderMessageId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmailOutbox_ProviderMessageId",
                table: "EmailOutbox");

            migrationBuilder.DropColumn(
                name: "DeliveryDetail",
                table: "EmailOutbox");

            migrationBuilder.DropColumn(
                name: "DeliveryStatus",
                table: "EmailOutbox");

            migrationBuilder.DropColumn(
                name: "DeliveryUpdatedAtUtc",
                table: "EmailOutbox");
        }
    }
}
