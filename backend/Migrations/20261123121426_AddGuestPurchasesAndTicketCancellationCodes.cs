using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travela.API.Migrations
{
    /// <inheritdoc />
    public partial class AddGuestPurchasesAndTicketCancellationCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PurchaseGroups_PassengerId_RequestKey",
                table: "PurchaseGroups");

            migrationBuilder.DropIndex(
                name: "IX_PaymentOrders_PassengerId_RequestKey",
                table: "PaymentOrders");

            migrationBuilder.DropIndex(
                name: "IX_LegalAcceptances_UserId_Context_RequestKey_DocumentType",
                table: "LegalAcceptances");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Campaigns_Rules",
                table: "Campaigns");

            migrationBuilder.AlterColumn<int>(
                name: "PassengerId",
                table: "Reservations",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "PassengerId",
                table: "PurchaseGroups",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<Guid>(
                name: "GuestSessionId",
                table: "PurchaseGroups",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "PassengerId",
                table: "PaymentOrders",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "LegalAcceptances",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "PerUserLimit",
                table: "Campaigns",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.CreateTable(
                name: "GuestCheckoutSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuestCheckoutSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TicketCancellationChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrantHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    Delivered = table.Column<bool>(type: "bit", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketCancellationChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketCancellationChallenges_PaymentOrders_PaymentOrderId",
                        column: x => x.PaymentOrderId,
                        principalTable: "PaymentOrders",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseGroups_GuestSessionId_RequestKey",
                table: "PurchaseGroups",
                columns: new[] { "GuestSessionId", "RequestKey" },
                unique: true,
                filter: "[GuestSessionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseGroups_PassengerId_RequestKey",
                table: "PurchaseGroups",
                columns: new[] { "PassengerId", "RequestKey" },
                unique: true,
                filter: "[PassengerId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseGroups_Owner",
                table: "PurchaseGroups",
                sql: "([PassengerId] IS NOT NULL AND [GuestSessionId] IS NULL) OR ([PassengerId] IS NULL AND [GuestSessionId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_PassengerId_RequestKey",
                table: "PaymentOrders",
                columns: new[] { "PassengerId", "RequestKey" },
                unique: true,
                filter: "[PassengerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LegalAcceptances_UserId_Context_RequestKey_DocumentType",
                table: "LegalAcceptances",
                columns: new[] { "UserId", "Context", "RequestKey", "DocumentType" },
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Campaigns_Rules",
                table: "Campaigns",
                sql: "[EndsAt] > [StartsAt] AND [Value] > 0 AND ([DiscountType] = N'Fixed' OR ([DiscountType] = N'Percentage' AND [Value] <= 99)) AND [MinimumSubtotal] >= 0 AND ([MaximumDiscount] IS NULL OR [MaximumDiscount] > 0) AND ([UsageLimit] IS NULL OR [UsageLimit] > 0) AND ([PerUserLimit] IS NULL OR [PerUserLimit] > 0) AND ([DepartureFrom] IS NULL OR [DepartureUntil] IS NULL OR [DepartureUntil] > [DepartureFrom])");

            migrationBuilder.CreateIndex(
                name: "IX_GuestCheckoutSessions_ExpiresAt",
                table: "GuestCheckoutSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_TicketCancellationChallenges_PaymentOrderId_CreatedAt",
                table: "TicketCancellationChallenges",
                columns: new[] { "PaymentOrderId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseGroups_GuestCheckoutSessions_GuestSessionId",
                table: "PurchaseGroups",
                column: "GuestSessionId",
                principalTable: "GuestCheckoutSessions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Never fabricate account ownership or silently discard guest purchases on rollback.
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM PurchaseGroups WHERE PassengerId IS NULL) OR EXISTS (SELECT 1 FROM LegalAcceptances WHERE UserId IS NULL) OR EXISTS (SELECT 1 FROM Campaigns WHERE PerUserLimit IS NULL) THROW 51030, N'Misafir siparişleri veya sınırsız kampanyalar varken bu geçiş geri alınamaz; verileri koruyarak ileri geçiş uygulayın.', 1;");
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseGroups_GuestCheckoutSessions_GuestSessionId",
                table: "PurchaseGroups");

            migrationBuilder.DropTable(
                name: "GuestCheckoutSessions");

            migrationBuilder.DropTable(
                name: "TicketCancellationChallenges");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseGroups_GuestSessionId_RequestKey",
                table: "PurchaseGroups");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseGroups_PassengerId_RequestKey",
                table: "PurchaseGroups");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseGroups_Owner",
                table: "PurchaseGroups");

            migrationBuilder.DropIndex(
                name: "IX_PaymentOrders_PassengerId_RequestKey",
                table: "PaymentOrders");

            migrationBuilder.DropIndex(
                name: "IX_LegalAcceptances_UserId_Context_RequestKey_DocumentType",
                table: "LegalAcceptances");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Campaigns_Rules",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "GuestSessionId",
                table: "PurchaseGroups");

            migrationBuilder.AlterColumn<int>(
                name: "PassengerId",
                table: "Reservations",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "PassengerId",
                table: "PurchaseGroups",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "PassengerId",
                table: "PaymentOrders",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "LegalAcceptances",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "PerUserLimit",
                table: "Campaigns",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseGroups_PassengerId_RequestKey",
                table: "PurchaseGroups",
                columns: new[] { "PassengerId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_PassengerId_RequestKey",
                table: "PaymentOrders",
                columns: new[] { "PassengerId", "RequestKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegalAcceptances_UserId_Context_RequestKey_DocumentType",
                table: "LegalAcceptances",
                columns: new[] { "UserId", "Context", "RequestKey", "DocumentType" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Campaigns_Rules",
                table: "Campaigns",
                sql: "[EndsAt] > [StartsAt] AND [Value] > 0 AND ([DiscountType] = N'Fixed' OR ([DiscountType] = N'Percentage' AND [Value] <= 99)) AND [MinimumSubtotal] >= 0 AND ([MaximumDiscount] IS NULL OR [MaximumDiscount] > 0) AND ([UsageLimit] IS NULL OR [UsageLimit] > 0) AND [PerUserLimit] > 0 AND ([DepartureFrom] IS NULL OR [DepartureUntil] IS NULL OR [DepartureUntil] > [DepartureFrom])");
        }
    }
}
