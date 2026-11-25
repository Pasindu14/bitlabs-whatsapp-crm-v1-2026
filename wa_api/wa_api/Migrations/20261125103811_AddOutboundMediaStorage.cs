using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace wa_api.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboundMediaStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "MediaPurgedAt",
                table: "Messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoragePath",
                table: "MessageMedia",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MessageMedia_CreatedAt",
                table: "MessageMedia",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MessageMedia_CreatedAt",
                table: "MessageMedia");

            migrationBuilder.DropColumn(
                name: "MediaPurgedAt",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "StoragePath",
                table: "MessageMedia");
        }
    }
}
