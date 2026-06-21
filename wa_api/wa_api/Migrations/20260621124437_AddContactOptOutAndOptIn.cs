using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace wa_api.Migrations
{
    /// <inheritdoc />
    public partial class AddContactOptOutAndOptIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasOptedIn",
                table: "Contacts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsOptedOut",
                table: "Contacts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "OptedInAt",
                table: "Contacts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OptedOutAt",
                table: "Contacts",
                type: "timestamp with time zone",
                nullable: true);

            // Grandfather all existing contacts — they were in the CRM before consent tracking
            // was introduced, so prior engagement implies consent. New contacts get HasOptedIn
            // stamped individually (true when created via inbound message, per-request otherwise).
            migrationBuilder.Sql("UPDATE \"Contacts\" SET \"HasOptedIn\" = true WHERE \"HasOptedIn\" = false;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasOptedIn",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "IsOptedOut",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "OptedInAt",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "OptedOutAt",
                table: "Contacts");
        }
    }
}
