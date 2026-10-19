namespace GEnergy.Api.Entities;

public sealed class ChargingSession
{
    public long Id { get; set; }
    public long BookingId { get; set; }
    public Booking Booking { get; set; } = null!;
    public long VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;
    public long ChargerId { get; set; }
    public Charger Charger { get; set; } = null!;
    public long StartedByUserId { get; set; }
    public User StartedBy { get; set; } = null!;
    public long PayerOwnerUserId { get; set; }
    public User PayerOwner { get; set; } = null!;
    public string Status { get; set; } = "Active";
    public string PaymentStatus { get; set; } = "Unpaid";
    public string? PaymentReference { get; set; }
    public string SettlementNote { get; set; } = "Wallet integration pending";
    public DateTime StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public decimal CapacityKWh { get; set; }
    public decimal InitialBatteryPercent { get; set; }
    public decimal BatteryPercent { get; set; }
    public decimal ConsumptionKWhPerKm { get; set; }
    public decimal PowerKW { get; set; }
    public decimal TariffPerKWh { get; set; }
    public decimal SimulationSpeed { get; set; }
    public decimal EnergyKWh { get; set; }
    public decimal Cost { get; set; }
    public decimal ElapsedSeconds { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
