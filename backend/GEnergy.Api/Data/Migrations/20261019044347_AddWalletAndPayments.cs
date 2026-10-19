using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GEnergy.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWalletAndPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaymentReference",
                table: "ChargingSessions",
                type: "nvarchar(48)",
                maxLength: 48,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Wallets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wallets", x => x.Id);
                    table.CheckConstraint("CK_Wallets_Balance", "[Balance] >= 0");
                    table.ForeignKey(
                        name: "FK_Wallets_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WalletTransactions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WalletId = table.Column<long>(type: "bigint", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    Type = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    RequestedOutcome = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ChargingSessionId = table.Column<long>(type: "bigint", nullable: true),
                    OriginalTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletTransactions", x => x.Id);
                    table.UniqueConstraint("AK_WalletTransactions_Id_WalletId", x => new { x.Id, x.WalletId });
                    table.CheckConstraint("CK_WalletTransactions_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_WalletTransactions_State", "([Type] = 'TopUp' AND [Status] IN ('Completed','Failed') AND [ChargingSessionId] IS NULL AND [OriginalTransactionId] IS NULL) OR ([Type] = 'ChargingPayment' AND [Status] = 'Completed' AND [ChargingSessionId] IS NOT NULL AND [OriginalTransactionId] IS NULL) OR ([Type] = 'Refund' AND [Status] = 'Completed' AND [ChargingSessionId] IS NOT NULL AND [OriginalTransactionId] IS NOT NULL)");
                    table.CheckConstraint("CK_WalletTransactions_Time", "[CompletedAtUtc] >= [CreatedAtUtc]");
                    table.ForeignKey(
                        name: "FK_WalletTransactions_ChargingSessions_ChargingSessionId",
                        column: x => x.ChargingSessionId,
                        principalTable: "ChargingSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WalletTransactions_WalletTransactions_OriginalTransactionId_WalletId",
                        columns: x => new { x.OriginalTransactionId, x.WalletId },
                        principalTable: "WalletTransactions",
                        principalColumns: new[] { "Id", "WalletId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WalletTransactions_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_UserId",
                table: "Wallets",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_OriginalTransactionId_WalletId",
                table: "WalletTransactions",
                columns: new[] { "OriginalTransactionId", "WalletId" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_Reference",
                table: "WalletTransactions",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_WalletId_Id",
                table: "WalletTransactions",
                columns: new[] { "WalletId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_WalletId_IdempotencyKey",
                table: "WalletTransactions",
                columns: new[] { "WalletId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_WalletTransactions_Refund",
                table: "WalletTransactions",
                column: "OriginalTransactionId",
                unique: true,
                filter: "[OriginalTransactionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_WalletTransactions_SessionPayment",
                table: "WalletTransactions",
                column: "ChargingSessionId",
                unique: true,
                filter: "[Type] = 'ChargingPayment' AND [Status] = 'Completed'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WalletTransactions");

            migrationBuilder.DropTable(
                name: "Wallets");

            migrationBuilder.DropColumn(
                name: "PaymentReference",
                table: "ChargingSessions");
        }
    }
}
