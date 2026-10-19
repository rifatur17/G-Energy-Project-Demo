using System.ComponentModel.DataAnnotations;
using GEnergy.Api.Entities;
namespace GEnergy.Api.DTOs;

public sealed record CreateBookingRequest([Range(1, long.MaxValue)] long VehicleId, [Range(1, long.MaxValue)] long StationId, [Range(1, long.MaxValue)] long ChargerId);
public sealed record BookingResponse(long Id, string BookingCode, long VehicleId, long StationId, long ChargerId, string StationName, string ChargerCode, string Status, DateTime CreatedAtUtc, DateTime ExpiresAtUtc, DateTime ServerTimeUtc, long? SessionId);
public sealed record SessionResponse(long Id, long BookingId, long VehicleId, long ChargerId, string Status, string PaymentStatus, string SettlementNote, DateTime StartedAtUtc, DateTime? EndedAtUtc, DateTime ServerTimeUtc, decimal BatteryPercent, decimal EstimatedRangeKm, decimal EnergyKWh, decimal Cost, decimal ElapsedSeconds, decimal PowerKW, decimal TariffPerKWh, decimal SimulationSpeed, bool IsSimulated, string Currency)
{
    public static SessionResponse From(ChargingSession s, DateTime now) => new(s.Id, s.BookingId, s.VehicleId, s.ChargerId, s.Status, s.PaymentStatus, s.SettlementNote, s.StartedAtUtc, s.EndedAtUtc, now, s.BatteryPercent, decimal.Round(s.CapacityKWh * s.BatteryPercent / 100 / s.ConsumptionKWhPerKm, 2), s.EnergyKWh, s.Cost, s.ElapsedSeconds, s.PowerKW, s.TariffPerKWh, s.SimulationSpeed, true, "BDT");
}
public sealed record ChargerAvailabilityResponse(long ChargerId, string EffectiveStatus, bool IsAvailable, DateTime ServerTimeUtc);
