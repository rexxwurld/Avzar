using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPdfBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BrandColor",
                table: "Businesses",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                table: "Businesses",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPhone",
                table: "Businesses",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LogoMediaId",
                table: "Businesses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PdfFooterNote",
                table: "Businesses",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxNumber",
                table: "Businesses",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BrandColor",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "ContactEmail",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "ContactPhone",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LogoMediaId",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "PdfFooterNote",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "TaxNumber",
                table: "Businesses");
        }
    }
}
