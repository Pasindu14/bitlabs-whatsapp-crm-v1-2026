using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace wa_api.Migrations
{
    /// <inheritdoc />
    public partial class AddContactWhatsAppValidity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Optimistic default: existing contacts haven't been proven invalid, so they stay sendable.
            // The flag only flips to false when Meta returns an undeliverable-recipient code (131026).
            migrationBuilder.AddColumn<bool>(
                name: "IsWhatsAppValid",
                table: "Contacts",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WhatsAppInvalidAt",
                table: "Contacts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsWhatsAppValid",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "WhatsAppInvalidAt",
                table: "Contacts");
        }
    }
}
