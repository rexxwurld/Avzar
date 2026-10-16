using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailoring.Infrastructure.Persistence.Migrations
{
    public partial class AddEmailDeduplication : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "DeduplicationKey", table: "EmailOutbox", type: "character varying(200)", maxLength: 200, nullable: true);
            migrationBuilder.CreateIndex(name: "IX_EmailOutbox_DeduplicationKey", table: "EmailOutbox", column: "DeduplicationKey", unique: true, filter: "\"DeduplicationKey\" IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_EmailOutbox_DeduplicationKey", table: "EmailOutbox");
            migrationBuilder.DropColumn(name: "DeduplicationKey", table: "EmailOutbox");
        }
    }
}
