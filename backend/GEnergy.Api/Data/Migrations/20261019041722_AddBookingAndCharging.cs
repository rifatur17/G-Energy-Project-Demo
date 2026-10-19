using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GEnergy.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingAndCharging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bookings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingCode = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    BookedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    VehicleId = table.Column<long>(type: "bigint", nullable: false),
                    ChargerId = table.Column<long>(type: "bigint", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookings", x => x.Id);
                    table.CheckConstraint("CK_Bookings_Expiry", "[ExpiresAtUtc] > [CreatedAtUtc]");
                    table.CheckConstraint("CK_Bookings_Status", "[Status] IN ('Reserved','Expired','Cancelled','Consumed')");
                    table.ForeignKey(
                        name: "FK_Bookings_Chargers_ChargerId",
                        column: x => x.ChargerId,
                        principalTable: "Chargers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Bookings_Users_BookedByUserId",
                        column: x => x.BookedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Bookings_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChargingSessions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingId = table.Column<long>(type: "bigint", nullable: false),
                    VehicleId = table.Column<long>(type: "bigint", nullable: false),
                    ChargerId = table.Column<long>(type: "bigint", nullable: false),
                    StartedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    PayerOwnerUserId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    PaymentStatus = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    SettlementNote = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CapacityKWh = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    InitialBatteryPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    BatteryPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    ConsumptionKWhPerKm = table.Column<decimal>(type: "decimal(8,4)", precision: 8, scale: 4, nullable: false),
                    PowerKW = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    TariffPerKWh = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    SimulationSpeed = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    EnergyKWh = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ElapsedSeconds = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChargingSessions", x => x.Id);
                    table.CheckConstraint("CK_Sessions_Numbers", "[CapacityKWh] > 0 AND [PowerKW] > 0 AND [TariffPerKWh] > 0 AND [SimulationSpeed] > 0 AND [ConsumptionKWhPerKm] > 0 AND [InitialBatteryPercent] BETWEEN 0 AND 100 AND [BatteryPercent] BETWEEN 0 AND 100 AND [EnergyKWh] >= 0 AND [EnergyKWh] <= [CapacityKWh] AND [Cost] >= 0 AND [ElapsedSeconds] >= 0");
                    table.CheckConstraint("CK_Sessions_Status", "[Status] IN ('Active','Completed') AND [PaymentStatus] IN ('Unpaid','Paid') AND (([Status] = 'Active' AND [EndedAtUtc] IS NULL) OR ([Status] = 'Completed' AND [EndedAtUtc] IS NOT NULL AND [EndedAtUtc] >= [StartedAtUtc]))");
                    table.ForeignKey(
                        name: "FK_ChargingSessions_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChargingSessions_Chargers_ChargerId",
                        column: x => x.ChargerId,
                        principalTable: "Chargers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChargingSessions_Users_PayerOwnerUserId",
                        column: x => x.PayerOwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChargingSessions_Users_StartedByUserId",
                        column: x => x.StartedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChargingSessions_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_BookedByUserId_CreatedAtUtc",
                table: "Bookings",
                columns: new[] { "BookedByUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_BookedByUserId_IdempotencyKey",
                table: "Bookings",
                columns: new[] { "BookedByUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_BookingCode",
                table: "Bookings",
                column: "BookingCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_Status_ExpiresAtUtc",
                table: "Bookings",
                columns: new[] { "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Bookings_ReservedCharger",
                table: "Bookings",
                column: "ChargerId",
                unique: true,
                filter: "[Status] = 'Reserved'");

            migrationBuilder.CreateIndex(
                name: "UX_Bookings_ReservedVehicle",
                table: "Bookings",
                column: "VehicleId",
                unique: true,
                filter: "[Status] = 'Reserved'");

            migrationBuilder.CreateIndex(
                name: "IX_ChargingSessions_BookingId",
                table: "ChargingSessions",
                column: "BookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChargingSessions_PayerOwnerUserId",
                table: "ChargingSessions",
                column: "PayerOwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChargingSessions_StartedByUserId_StartedAtUtc",
                table: "ChargingSessions",
                columns: new[] { "StartedByUserId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Sessions_ActiveCharger",
                table: "ChargingSessions",
                column: "ChargerId",
                unique: true,
                filter: "[Status] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "UX_Sessions_ActiveVehicle",
                table: "ChargingSessions",
                column: "VehicleId",
                unique: true,
                filter: "[Status] = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChargingSessions");

            migrationBuilder.DropTable(
                name: "Bookings");
        }
    }
}
