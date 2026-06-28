using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace wa_api.Migrations
{
    /// <inheritdoc />
    public partial class AddMessagePackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExtraMessageCredits",
                table: "Subscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "MessagePackages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ExtraMessages = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "USD"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessagePackages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PackagePurchases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    MessagesAdded = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "USD"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackagePurchases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackagePurchases_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PackagePurchases_MessagePackages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "MessagePackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PackagePurchases_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MessagePackages_Name",
                table: "MessagePackages",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackagePurchases_CompanyId",
                table: "PackagePurchases",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_PackagePurchases_CompanyId_CreatedAt",
                table: "PackagePurchases",
                columns: new[] { "CompanyId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PackagePurchases_PackageId",
                table: "PackagePurchases",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_PackagePurchases_SubscriptionId",
                table: "PackagePurchases",
                column: "SubscriptionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PackagePurchases");

            migrationBuilder.DropTable(
                name: "MessagePackages");

            migrationBuilder.DropColumn(
                name: "ExtraMessageCredits",
                table: "Subscriptions");
        }
    }
}
