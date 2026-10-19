using GEnergy.Api.Data;
using GEnergy.Api.Entities;
using Microsoft.EntityFrameworkCore;
namespace GEnergy.Api.Services.Charging;

public sealed class BookingActivityGuard(EnergyDbContext db, TimeProvider clock) : IVehicleActivityGuard
{
    public async Task EnsureMutableAsync(long vehicleId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (await db.Bookings.AnyAsync(b => b.VehicleId == vehicleId && b.Status == "Reserved" && b.ExpiresAtUtc > now, ct) || await db.ChargingSessions.AnyAsync(s => s.VehicleId == vehicleId && s.Status == "Active", ct))
            throw new MobilityConflictException("Vehicle has an active reservation or charging session.");
    }
}
public sealed class BookingAvailabilityProvider(EnergyDbContext db, TimeProvider clock) : IChargerAvailabilityProvider
{
    public async Task<IReadOnlyDictionary<long, ChargerAvailability>> GetAsync(IReadOnlyList<Charger> chargers, CancellationToken ct)
    {
        var ids = chargers.Select(c => c.Id).ToArray(); var now = clock.GetUtcNow().UtcDateTime;
        var held = await db.Bookings.AsNoTracking().Where(b => ids.Contains(b.ChargerId) && b.Status == "Reserved" && b.ExpiresAtUtc > now).Select(b => b.ChargerId).ToListAsync(ct);
        var active = await db.ChargingSessions.AsNoTracking().Where(s => ids.Contains(s.ChargerId) && s.Status == "Active").Select(s => s.ChargerId).ToListAsync(ct);
        return chargers.ToDictionary(c => c.Id, c => new ChargerAvailability(c.Status == "Offline" ? "Offline" : active.Contains(c.Id) ? "Charging" : held.Contains(c.Id) ? "Reserved" : "Available", "ReservationState"));
    }
}
