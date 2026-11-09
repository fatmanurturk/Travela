using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Travela.API.Migrations
{
    /// <inheritdoc />
    public partial class AddLegalAcceptances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LegalAcceptances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DocumentVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DocumentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DocumentSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: false),
                    Context = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PurchaseGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PaymentOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TransactionSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalAcceptances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegalAcceptances_PaymentOrders_PaymentOrderId",
                        column: x => x.PaymentOrderId,
                        principalTable: "PaymentOrders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LegalAcceptances_PurchaseGroups_PurchaseGroupId",
                        column: x => x.PurchaseGroupId,
                        principalTable: "PurchaseGroups",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LegalAcceptances_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_LegalAcceptances_PaymentOrderId",
                table: "LegalAcceptances",
                column: "PaymentOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_LegalAcceptances_PurchaseGroupId",
                table: "LegalAcceptances",
                column: "PurchaseGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_LegalAcceptances_UserId_Context_RequestKey_DocumentType",
                table: "LegalAcceptances",
                columns: new[] { "UserId", "Context", "RequestKey", "DocumentType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LegalAcceptances");
        }
    }
}
