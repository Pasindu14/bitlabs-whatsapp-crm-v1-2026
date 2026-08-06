using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace wa_api.Migrations
{
    /// <inheritdoc />
    public partial class AddPayHere : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PayHereOrderId",
                table: "SubscriptionPurchases",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PayHereOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "USD"),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PayHerePaymentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StatusMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayHereOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayHereOrders_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayHereOrders_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPurchases_PayHereOrderId",
                table: "SubscriptionPurchases",
                column: "PayHereOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PayHereOrders_CompanyId",
                table: "PayHereOrders",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_PayHereOrders_CompanyId_CreatedAt",
                table: "PayHereOrders",
                columns: new[] { "CompanyId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PayHereOrders_PlanId",
                table: "PayHereOrders",
                column: "PlanId");

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriptionPurchases_PayHereOrders_PayHereOrderId",
                table: "SubscriptionPurchases",
                column: "PayHereOrderId",
                principalTable: "PayHereOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SubscriptionPurchases_PayHereOrders_PayHereOrderId",
                table: "SubscriptionPurchases");

            migrationBuilder.DropTable(
                name: "PayHereOrders");

            migrationBuilder.DropIndex(
                name: "IX_SubscriptionPurchases_PayHereOrderId",
                table: "SubscriptionPurchases");

            migrationBuilder.DropColumn(
                name: "PayHereOrderId",
                table: "SubscriptionPurchases");
        }
    }
}
