using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travela.API.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignDiscounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CampaignId",
                table: "PurchaseGroups",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CampaignName",
                table: "PurchaseGroups",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CouponCode",
                table: "PurchaseGroups",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "PurchaseGroups",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalAmount",
                table: "PurchaseGroups",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "CampaignName",
                table: "PaymentOrders",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CouponCode",
                table: "PaymentOrders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "PaymentOrders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalAmount",
                table: "PaymentOrders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "Campaigns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    RouteId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    DiscountType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MinimumSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MaximumDiscount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    UsageLimit = table.Column<int>(type: "int", nullable: true),
                    PerUserLimit = table.Column<int>(type: "int", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DepartureFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DepartureUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Campaigns", x => x.Id);
                    table.CheckConstraint("CK_Campaigns_Rules", "[EndsAt] > [StartsAt] AND [Value] > 0 AND ([DiscountType] = N'Fixed' OR ([DiscountType] = N'Percentage' AND [Value] <= 99)) AND [MinimumSubtotal] >= 0 AND ([MaximumDiscount] IS NULL OR [MaximumDiscount] > 0) AND ([UsageLimit] IS NULL OR [UsageLimit] > 0) AND [PerUserLimit] > 0 AND ([DepartureFrom] IS NULL OR [DepartureUntil] IS NULL OR [DepartureUntil] > [DepartureFrom])");
                    table.ForeignKey(
                        name: "FK_Campaigns_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Campaigns_Routes_RouteId",
                        column: x => x.RouteId,
                        principalTable: "Routes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseGroups_CampaignId_PassengerId_PaymentStatus_HoldExpiresAt",
                table: "PurchaseGroups",
                columns: new[] { "CampaignId", "PassengerId", "PaymentStatus", "HoldExpiresAt" });

            // Only new snapshot columns are populated; historical charged/refunded amounts stay intact.
            migrationBuilder.Sql("UPDATE [PurchaseGroups] SET [OriginalAmount] = [Amount]; UPDATE [PaymentOrders] SET [OriginalAmount] = [Amount];");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseGroups_Discount",
                table: "PurchaseGroups",
                sql: "[DiscountAmount] >= 0 AND [OriginalAmount] = [Amount] + [DiscountAmount]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentOrders_Discount",
                table: "PaymentOrders",
                sql: "[DiscountAmount] >= 0 AND [OriginalAmount] = [Amount] + [DiscountAmount]");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_CompanyId_Code",
                table: "Campaigns",
                columns: new[] { "CompanyId", "Code" },
                unique: true,
                filter: "[Code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_CompanyId_IsActive_StartsAt_EndsAt",
                table: "Campaigns",
                columns: new[] { "CompanyId", "IsActive", "StartsAt", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_RouteId",
                table: "Campaigns",
                column: "RouteId");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseGroups_Campaigns_CampaignId",
                table: "PurchaseGroups",
                column: "CampaignId",
                principalTable: "Campaigns",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [Campaigns]) OR EXISTS (SELECT 1 FROM [PurchaseGroups] WHERE [DiscountAmount] > 0)
                    OR EXISTS (SELECT 1 FROM [PaymentOrders] WHERE [DiscountAmount] > 0)
                    THROW 51000, N'Kampanya veya indirim geçmişi kaybedileceği için geri migration engellendi.', 1;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseGroups_Campaigns_CampaignId",
                table: "PurchaseGroups");

            migrationBuilder.DropTable(
                name: "Campaigns");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseGroups_CampaignId_PassengerId_PaymentStatus_HoldExpiresAt",
                table: "PurchaseGroups");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseGroups_Discount",
                table: "PurchaseGroups");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentOrders_Discount",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "CampaignId",
                table: "PurchaseGroups");

            migrationBuilder.DropColumn(
                name: "CampaignName",
                table: "PurchaseGroups");

            migrationBuilder.DropColumn(
                name: "CouponCode",
                table: "PurchaseGroups");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "PurchaseGroups");

            migrationBuilder.DropColumn(
                name: "OriginalAmount",
                table: "PurchaseGroups");

            migrationBuilder.DropColumn(
                name: "CampaignName",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "CouponCode",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "OriginalAmount",
                table: "PaymentOrders");
        }
    }
}
