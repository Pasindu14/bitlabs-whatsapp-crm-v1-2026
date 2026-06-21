using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace wa_api.Migrations
{
    /// <inheritdoc />
    public partial class AddWabaQualityRating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QualityRating",
                table: "WabaConnections",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QualityRating",
                table: "WabaConnections");
        }
    }
}
