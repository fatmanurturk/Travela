using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travela.API.Migrations
{
    /// <inheritdoc />
    public partial class HardenPaymentInvariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM PaymentOrders WHERE DATALENGTH(ProviderReference) > 256) THROW 51000, 'Saglayici referansi 128 karakteri asiyor; kisaltma yerine migration durduruldu.', 1;");
            migrationBuilder.AlterColumn<string>(
                name: "ProviderReference",
                table: "PaymentOrders",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Reservations_Kind",
                table: "Reservations",
                sql: "([Kind] = N'Legacy' AND [Pnr] IS NULL) OR ([Kind] = N'Hold' AND [Pnr] IS NULL AND [HoldExpiresAt] IS NOT NULL) OR ([Kind] = N'Ticket' AND [Pnr] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_Provider_ProviderReference",
                table: "PaymentOrders",
                columns: new[] { "Provider", "ProviderReference" },
                unique: true,
                filter: "[ProviderReference] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentOrders_Amount",
                table: "PaymentOrders",
                sql: "[Amount] > 0 AND [RefundAmount] >= 0 AND [RefundAmount] <= [Amount]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentOrders_Paid",
                table: "PaymentOrders",
                sql: "[PaymentStatus] <> N'Paid' OR ([PaidAt] IS NOT NULL AND [ProviderReference] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentOrders_Refund",
                table: "PaymentOrders",
                sql: "([RefundStatus] = N'None' AND [RefundAmount] = 0) OR ([PaymentStatus] = N'Paid' AND [RefundAmount] = [Amount] AND ([RefundStatus] IN (N'Pending', N'Failed') OR ([RefundStatus] = N'Succeeded' AND [RefundedAt] IS NOT NULL AND [RefundReference] IS NOT NULL)))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LocalPaymentRecords_RefundTransfers",
                table: "LocalPaymentRecords",
                sql: "[RefundTransfers] IN (0, 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Reservations_Kind",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_PaymentOrders_Provider_ProviderReference",
                table: "PaymentOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentOrders_Amount",
                table: "PaymentOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentOrders_Paid",
                table: "PaymentOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentOrders_Refund",
                table: "PaymentOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LocalPaymentRecords_RefundTransfers",
                table: "LocalPaymentRecords");

            migrationBuilder.AlterColumn<string>(
                name: "ProviderReference",
                table: "PaymentOrders",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldNullable: true);
        }
    }
}
