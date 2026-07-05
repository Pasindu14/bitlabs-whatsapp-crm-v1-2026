using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace wa_api.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookEventStatusUpdatedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WhatsAppWebhookEvents_Status",
                table: "WhatsAppWebhookEvents");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppWebhookEvents_Status_UpdatedAt",
                table: "WhatsAppWebhookEvents",
                columns: new[] { "Status", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WhatsAppWebhookEvents_Status_UpdatedAt",
                table: "WhatsAppWebhookEvents");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppWebhookEvents_Status",
                table: "WhatsAppWebhookEvents",
                column: "Status");
        }
    }
}
