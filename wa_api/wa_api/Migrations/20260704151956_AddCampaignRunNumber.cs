using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace wa_api.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignRunNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CampaignRunNumber",
                table: "Messages",
                type: "integer",
                nullable: true);

            // Existing campaigns have already sent their run 1; default new rows and backfill existing
            // rows to 1 so the run counter is consistent with the app default (Campaign.CurrentRunNumber = 1).
            migrationBuilder.AddColumn<int>(
                name: "CurrentRunNumber",
                table: "Campaigns",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // Backfill every existing campaign message as run 1 so the batch's per-run duplicate guard
            // (CampaignRunNumber == CurrentRunNumber) still recognises them — otherwise an in-flight
            // OneTime campaign at deploy time would treat its already-sent messages as un-sent and resend.
            migrationBuilder.Sql(
                "UPDATE \"Messages\" SET \"CampaignRunNumber\" = 1 WHERE \"CampaignId\" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CampaignRunNumber",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "CurrentRunNumber",
                table: "Campaigns");
        }
    }
}
