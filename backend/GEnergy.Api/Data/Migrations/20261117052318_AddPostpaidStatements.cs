using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GEnergy.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPostpaidStatements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChargingSessions_PayerOwnerUserId",
                table: "ChargingSessions");

            migrationBuilder.AddColumn<long>(
                name: "StatementId",
                table: "ChargingSessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BillingStatements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StatementNumber = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    OwnerUserId = table.Column<long>(type: "bigint", nullable: false),
                    PeriodKey = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    PeriodStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PeriodEndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SessionCount = table.Column<int>(type: "int", nullable: false),
                    ChargingTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PlanVehicleCount = table.Column<int>(type: "int", nullable: false),
                    PlanFee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PaymentReference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingStatements", x => x.Id);
                    table.CheckConstraint("CK_Statements_Amounts", "[ChargingTotal] >= 0 AND [PlanFee] >= 0 AND [Total] = [ChargingTotal] + [PlanFee] AND [Total] > 0 AND [SessionCount] >= 0 AND [PlanVehicleCount] >= 0 AND [Sequence] >= 1");
                    table.CheckConstraint("CK_Statements_Dates", "[PeriodEndUtc] >= [PeriodStartUtc] AND [DueAtUtc] >= [IssuedAtUtc]");
                    table.CheckConstraint("CK_Statements_Status", "[Status] IN ('Issued','Paid') AND (([Status] = 'Paid' AND [PaidAtUtc] IS NOT NULL) OR ([Status] = 'Issued' AND [PaidAtUtc] IS NULL))");
                    table.ForeignKey(
                        name: "FK_BillingStatements_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StatementPayments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StatementId = table.Column<long>(type: "bigint", nullable: false),
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
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatementPayments", x => x.Id);
                    table.CheckConstraint("CK_StatementPayments_Amount", "[Amount] > 0 AND [Currency] = 'BDT'");
                    table.CheckConstraint("CK_StatementPayments_Status", "[Status] IN ('Initiated','Paid','Failed','Cancelled')");
                    table.ForeignKey(
                        name: "FK_StatementPayments_BillingStatements_StatementId",
                        column: x => x.StatementId,
                        principalTable: "BillingStatements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChargingSessions_PayerOwnerUserId_StatementId_Status",
                table: "ChargingSessions",
                columns: new[] { "PayerOwnerUserId", "StatementId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ChargingSessions_StatementId",
                table: "ChargingSessions",
                column: "StatementId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingStatements_OwnerUserId_PeriodKey_Sequence",
                table: "BillingStatements",
                columns: new[] { "OwnerUserId", "PeriodKey", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingStatements_StatementNumber",
                table: "BillingStatements",
                column: "StatementNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatementPayments_OwnerUserId_CreatedAtUtc",
                table: "StatementPayments",
                columns: new[] { "OwnerUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StatementPayments_TranId",
                table: "StatementPayments",
                column: "TranId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_StatementPayments_PaidStatement",
                table: "StatementPayments",
                column: "StatementId",
                unique: true,
                filter: "[Status] = 'Paid'");

            migrationBuilder.AddForeignKey(
                name: "FK_ChargingSessions_BillingStatements_StatementId",
                table: "ChargingSessions",
                column: "StatementId",
                principalTable: "BillingStatements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChargingSessions_BillingStatements_StatementId",
                table: "ChargingSessions");

            migrationBuilder.DropTable(
                name: "StatementPayments");

            migrationBuilder.DropTable(
                name: "BillingStatements");

            migrationBuilder.DropIndex(
                name: "IX_ChargingSessions_PayerOwnerUserId_StatementId_Status",
                table: "ChargingSessions");

            migrationBuilder.DropIndex(
                name: "IX_ChargingSessions_StatementId",
                table: "ChargingSessions");

            migrationBuilder.DropColumn(
                name: "StatementId",
                table: "ChargingSessions");

            migrationBuilder.CreateIndex(
                name: "IX_ChargingSessions_PayerOwnerUserId",
                table: "ChargingSessions",
                column: "PayerOwnerUserId");
        }
    }
}
