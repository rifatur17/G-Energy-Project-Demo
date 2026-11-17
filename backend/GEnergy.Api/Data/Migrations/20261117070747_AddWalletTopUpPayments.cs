using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GEnergy.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWalletTopUpPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WalletTopUpPayments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerUserId = table.Column<long>(type: "bigint", nullable: false),
                    TranId = table.Column<string>(type: "varchar(48)", unicode: false, maxLength: 48, nullable: false),
                    Gateway = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    ValId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    BankTranId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    CardType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    WalletTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletTopUpPayments", x => x.Id);
                    table.CheckConstraint("CK_WalletTopUpPayments_Amount", "[Amount] > 0 AND [Currency] = 'BDT'");
                    table.CheckConstraint("CK_WalletTopUpPayments_Status", "[Status] IN ('Initiated','Paid','Failed','Cancelled') AND ([Status] <> 'Paid' OR [WalletTransactionId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_WalletTopUpPayments_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WalletTopUpPayments_WalletTransactions_WalletTransactionId",
                        column: x => x.WalletTransactionId,
                        principalTable: "WalletTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WalletTopUpPayments_OwnerUserId_CreatedAtUtc",
                table: "WalletTopUpPayments",
                columns: new[] { "OwnerUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletTopUpPayments_TranId",
                table: "WalletTopUpPayments",
                column: "TranId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletTopUpPayments_WalletTransactionId",
                table: "WalletTopUpPayments",
                column: "WalletTransactionId",
                unique: true,
                filter: "[WalletTransactionId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WalletTopUpPayments");
        }
    }
}
