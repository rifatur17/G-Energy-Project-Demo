namespace GEnergy.Api.Entities;

public sealed class Booking
{
    public long Id { get; set; }
    public string BookingCode { get; set; } = "GE-BKG-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
    public long BookedByUserId { get; set; }
    public User BookedBy { get; set; } = null!;
    public long VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;
    public long ChargerId { get; set; }
    public Charger Charger { get; set; } = null!;
    public string IdempotencyKey { get; set; } = "";
    public string Status { get; set; } = "Reserved";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
