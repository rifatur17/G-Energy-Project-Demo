using System.Security.Cryptography;
namespace GEnergy.Api.Entities;

public sealed class Vehicle
{
    public long Id { get; set; }
    public long OwnerUserId { get; set; }
    public User Owner { get; set; } = null!;
    public string RegistrationNo { get; set; } = "";
    public string Model { get; set; } = "";
    public string ConnectorType { get; set; } = "";
    public decimal BatteryCapacityKWh { get; set; }
    public decimal BatteryPercent { get; set; }
    public decimal ConsumptionKWhPerKm { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = RandomNumberGenerator.GetBytes(8);
    public List<VehicleDriver> Drivers { get; set; } = [];
}
public sealed class VehicleDriver
{
    public long VehicleId { get; set; }
    public Vehicle Vehicle { get; set; } = null!;
    public long DriverUserId { get; set; }
    public User Driver { get; set; } = null!;
    public bool CanCharge { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
public static class Connectors
{
    public static readonly string[] All = ["CCS2", "Type2", "CHAdeMO"];
}
