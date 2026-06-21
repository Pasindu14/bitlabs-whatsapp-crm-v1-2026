using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace wa_api.Migrations
{
    /// <inheritdoc />
    public partial class AddWabaMessagingTier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill existing connections to the conservative Tier1K default. An empty string (EF's
            // generated default) would fail to convert back to the MessagingTier enum on read.
            migrationBuilder.AddColumn<string>(
                name: "MessagingTier",
                table: "WabaConnections",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Tier1K");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MessagingTier",
                table: "WabaConnections");
        }
    }
}
