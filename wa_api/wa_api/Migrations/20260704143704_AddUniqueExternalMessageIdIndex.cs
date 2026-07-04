using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace wa_api.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueExternalMessageIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Messages_ExternalMessageId",
                table: "Messages");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ExternalMessageId",
                table: "Messages",
                column: "ExternalMessageId",
                unique: true,
                filter: "\"ExternalMessageId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Messages_ExternalMessageId",
                table: "Messages");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ExternalMessageId",
                table: "Messages",
                column: "ExternalMessageId");
        }
    }
}
