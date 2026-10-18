using GEnergy.Api.Data;
using GEnergy.Api.Entities;
using Microsoft.EntityFrameworkCore;
namespace GEnergy.Api.Services;
// Reuse inside Member 3/4 transactions, after acquiring the vehicle write lock.
public sealed class VehicleAccess(EnergyDbContext db)
{
    public Task<bool> CanViewAsync(long vehicleId, long userId, CancellationToken ct = default) => db.Vehicles.AnyAsync(v => v.Id == vehicleId &&
        (v.OwnerUserId == userId || v.Drivers.Any(d => d.DriverUserId == userId && d.IsActive && d.Driver.IsActive && d.Driver.Role == Roles.CarDriver)), ct);
    public Task<bool> CanChargeAsync(long vehicleId, long userId, CancellationToken ct = default) => db.Vehicles.AnyAsync(v => v.Id == vehicleId &&
        ((v.OwnerUserId == userId && v.Owner.IsActive && v.Owner.Role == Roles.CarOwner) || v.Drivers.Any(d => d.DriverUserId == userId && d.IsActive && d.CanCharge && d.Driver.IsActive && d.Driver.Role == Roles.CarDriver)), ct);
}
public interface IVehicleActivityGuard
{
    // Called under the root vehicle lock; throw MobilityConflictException for an effective hold/session.
    Task EnsureMutableAsync(long vehicleId, CancellationToken ct);
}
// Booking/charging do not exist yet. Member 3 MUST replace this implementation when adding them.
public sealed class NoBookingModuleActivityGuard(IConfiguration config) : IVehicleActivityGuard
{
    public Task EnsureMutableAsync(long vehicleId, CancellationToken ct)
    {
        if (config.GetValue<bool>("Modules:BookingEnabled"))
            throw new MobilityConflictException("Booking integration requires an activity guard before vehicle or assignment changes are permitted.");
        return Task.CompletedTask;
    }
}
public sealed class MobilityConflictException(string message) : Exception(message);
