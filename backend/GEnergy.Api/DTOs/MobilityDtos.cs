using System.ComponentModel.DataAnnotations;
using GEnergy.Api.Entities;
namespace GEnergy.Api.DTOs;

public sealed record VehicleRequest(
    [Required, StringLength(32, MinimumLength = 2)] string RegistrationNo,
    [Required, StringLength(100, MinimumLength = 2)] string Model,
    [Required, RegularExpression("^(CCS2|Type2|CHAdeMO)$")] string ConnectorType,
    [Range(typeof(decimal), "0.01", "999999.99")] decimal BatteryCapacityKWh,
    [Range(typeof(decimal), "0", "100")] decimal BatteryPercent,
    [Range(typeof(decimal), "0.0001", "9999.9999")] decimal ConsumptionKWhPerKm);
public sealed record DriverAssignmentRequest([Required, StringLength(24)] string UserCode, bool CanCharge);
public sealed record DriverPermissionRequest(bool CanCharge);
public sealed record DriverResponse(long DriverUserId, string UserCode, string FullName, bool CanCharge, bool IsActive);
public sealed record VehicleResponse(long Id, string RegistrationNo, string Model, string ConnectorType, decimal BatteryCapacityKWh, decimal BatteryPercent, decimal ConsumptionKWhPerKm, decimal EstimatedRangeKm, bool IsSimulated, bool CanCharge, string RowVersion, DateTime CreatedAtUtc, DateTime UpdatedAtUtc)
{
    public static decimal Range(Vehicle v) => decimal.Round(v.BatteryCapacityKWh * v.BatteryPercent / 100m / v.ConsumptionKWhPerKm, 2);
    public static VehicleResponse From(Vehicle v, bool canCharge) => new(v.Id, v.RegistrationNo, v.Model, v.ConnectorType, v.BatteryCapacityKWh, v.BatteryPercent, v.ConsumptionKWhPerKm, Range(v), true, canCharge, Convert.ToBase64String(v.RowVersion), v.CreatedAtUtc, v.UpdatedAtUtc);
}
public sealed record VehicleStatusResponse(long VehicleId, decimal BatteryPercent, decimal EstimatedRangeKm, bool IsSimulated, bool CanCharge, DateTime UpdatedAtUtc);
public sealed record ChargerResponse(long Id, string ChargerCode, string ConnectorType, decimal PowerKW, decimal PricePerKWh, string Currency, string ConfiguredStatus, string EffectiveStatus, bool IsAvailable, string AvailabilitySource);
public sealed record StationResponse(long Id, string Name, string Address, decimal Latitude, decimal Longitude, bool IsDemo, IReadOnlyList<ChargerResponse> Chargers);
