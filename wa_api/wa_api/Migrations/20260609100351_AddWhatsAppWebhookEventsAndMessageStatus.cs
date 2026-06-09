using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace wa_api.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppWebhookEventsAndMessageStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Billable",
                table: "Messages",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Messages",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErrorCode",
                table: "Messages",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StatusAt",
                table: "Messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WhatsAppWebhookEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EventSignature = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppWebhookEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Templates_MetaTemplateId",
                table: "Templates",
                column: "MetaTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ExternalMessageId",
                table: "Messages",
                column: "ExternalMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppWebhookEvents_PhoneNumberId",
                table: "WhatsAppWebhookEvents",
                column: "PhoneNumberId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppWebhookEvents_Status",
                table: "WhatsAppWebhookEvents",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UX_WhatsAppWebhookEvents_EventSignature",
                table: "WhatsAppWebhookEvents",
                column: "EventSignature",
                unique: true,
                filter: "\"EventSignature\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppWebhookEvents");

            migrationBuilder.DropIndex(
                name: "IX_Templates_MetaTemplateId",
                table: "Templates");

            migrationBuilder.DropIndex(
                name: "IX_Messages_ExternalMessageId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "Billable",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ErrorCode",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "StatusAt",
                table: "Messages");
        }
    }
}
