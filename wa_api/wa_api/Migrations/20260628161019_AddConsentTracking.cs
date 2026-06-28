using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace wa_api.Migrations
{
    /// <inheritdoc />
    public partial class AddConsentTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConsentSource",
                table: "Contacts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                // "None" (not "") so existing rows deserialize back to ConsentSource.None on read.
                defaultValue: "None");

            migrationBuilder.AddColumn<int>(
                name: "NoConsentSentCount",
                table: "Campaigns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "OverrideConsentGate",
                table: "Campaigns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SentWithoutConsent",
                table: "CampaignRecipients",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConsentSource",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "NoConsentSentCount",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "OverrideConsentGate",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "SentWithoutConsent",
                table: "CampaignRecipients");
        }
    }
}
