using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travela.API.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupPurchasesAndIyzicoSandbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseGroupId",
                table: "PaymentOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProviderRefundAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RefundKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderRefundAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProviderRefundAttempts_PaymentOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "PaymentOrders",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PurchaseGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PassengerId = table.Column<int>(type: "int", nullable: false),
                    TripId = table.Column<int>(type: "int", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsTest = table.Column<bool>(type: "bit", nullable: false),
                    PaymentStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CheckoutState = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CheckoutUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProviderToken = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ProviderPaymentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ProtectedBuyerJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    NeedsAttention = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    HoldExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CheckoutLeaseUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NextReconciliationAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReconciliationAttempts = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseGroups_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PurchaseGroups_Users_PassengerId",
                        column: x => x.PassengerId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_PurchaseGroupId",
                table: "PaymentOrders",
                column: "PurchaseGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderRefundAttempts_OrderId",
                table: "ProviderRefundAttempts",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseGroups_PassengerId_RequestKey",
                table: "PurchaseGroups",
                columns: new[] { "PassengerId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseGroups_PaymentStatus_NextReconciliationAt",
                table: "PurchaseGroups",
                columns: new[] { "PaymentStatus", "NextReconciliationAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseGroups_Provider_ProviderToken",
                table: "PurchaseGroups",
                columns: new[] { "Provider", "ProviderToken" },
                unique: true,
                filter: "[ProviderToken] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseGroups_TripId",
                table: "PurchaseGroups",
                column: "TripId");

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentOrders_PurchaseGroups_PurchaseGroupId",
                table: "PaymentOrders",
                column: "PurchaseGroupId",
                principalTable: "PurchaseGroups",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [PurchaseGroups]) OR EXISTS (SELECT 1 FROM [ProviderRefundAttempts])
                    THROW 51000, N'Ortak sipariş veya sağlayıcı iade kayıtları kaybedileceği için geri migration engellendi.', 1;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentOrders_PurchaseGroups_PurchaseGroupId",
                table: "PaymentOrders");

            migrationBuilder.DropTable(
                name: "ProviderRefundAttempts");

            migrationBuilder.DropTable(
                name: "PurchaseGroups");

            migrationBuilder.DropIndex(
                name: "IX_PaymentOrders_PurchaseGroupId",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "PurchaseGroupId",
                table: "PaymentOrders");
        }
    }
}
