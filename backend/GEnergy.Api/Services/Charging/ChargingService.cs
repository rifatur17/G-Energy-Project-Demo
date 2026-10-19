using GEnergy.Api.Data;
using GEnergy.Api.DTOs;
using GEnergy.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace GEnergy.Api.Services.Charging;

public sealed class ChargingService(EnergyDbContext db, VehicleAccess access, TimeProvider clock, IOptions<ChargingOptions> options, IChargingSettlement settlement)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static ChargingException Missing() => new(404, "Booking or session is unavailable to your account.");
    private static ChargingException Conflict(string message) => new(409, message);
    private async Task<bool> Readable(Booking b, long user, CancellationToken ct) => b.BookedByUserId == user || await db.Vehicles.AnyAsync(v => v.Id == b.VehicleId && v.OwnerUserId == user, ct);
    // Every mutation takes the shared Member 2 vehicle lock first, then charger locks in ID order.
    // Include stale reservations for this vehicle so request-time cleanup frees their unique indexes.
    private async Task<Dictionary<long, Charger>> LockChargers(long vehicleId, long target, CancellationToken ct)
    {
        var ids = await db.Bookings.Where(b => b.VehicleId == vehicleId && b.Status == "Reserved").Select(b => b.ChargerId).ToListAsync(ct);
        ids.Add(target); var result = new Dictionary<long, Charger>();
        foreach (var id in ids.Distinct().Order())
        {
            var charger = db.Database.IsSqlServer()
                ? await db.Chargers.FromSqlInterpolated($"SELECT * FROM dbo.Chargers WITH (UPDLOCK, HOLDLOCK) WHERE Id={id}").Include(c => c.Station).SingleOrDefaultAsync(ct)
                : await db.Chargers.Include(c => c.Station).SingleOrDefaultAsync(c => c.Id == id, ct);
            if (charger is not null) result[id] = charger;
        }
        return result;
    }
    private async Task ExpireLocked(Dictionary<long, Charger> chargers, CancellationToken ct)
    {
        var ids = chargers.Keys.ToArray(); var now = Now;
        var expired = await db.Bookings.Where(b => ids.Contains(b.ChargerId) && b.Status == "Reserved" && b.ExpiresAtUtc <= now).ToListAsync(ct);
        foreach (var b in expired) b.Status = "Expired";
        if (expired.Count > 0) await db.SaveChangesAsync(ct); // Free filtered unique indexes before any new insert.
        foreach (var c in chargers.Values) await SyncCharger(c, ct);
    }
    private async Task SyncCharger(Charger c, CancellationToken ct)
    {
        if (c.Status == "Offline") return;
        var now = Now;
        c.Status = await db.ChargingSessions.AnyAsync(s => s.ChargerId == c.Id && s.Status == "Active", ct) ? "Charging" :
            await db.Bookings.AnyAsync(b => b.ChargerId == c.Id && b.Status == "Reserved" && b.ExpiresAtUtc > now, ct) ? "Reserved" : "Available";
    }
    private async Task Eligible(Vehicle vehicle, Charger charger, long user, CancellationToken ct)
    {
        if (!await access.CanChargeAsync(vehicle.Id, user, ct)) throw new ChargingException(403, "Vehicle charging permission is required.");
        if (!charger.Station.IsActive || charger.Status == "Offline") throw Conflict("The station or charger is offline.");
        if (vehicle.ConnectorType != charger.ConnectorType) throw new ChargingException(400, "Vehicle and charger connectors are incompatible.");
        if (vehicle.BatteryPercent >= 100) throw Conflict("Vehicle battery is already full.");
    }
    public async Task<BookingResponse> Reserve(long user, CreateBookingRequest request, string key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100) throw new ChargingException(400, "Provide an Idempotency-Key of 1-100 characters.");
        await using var write = await VehicleWriteScope.OpenAsync(db, request.VehicleId, ct);
        var vehicle = write.Vehicle ?? throw Missing();
        if (!await access.CanChargeAsync(vehicle.Id, user, ct)) throw new ChargingException(403, "Vehicle charging permission is required.");
        var chargers = await LockChargers(vehicle.Id, request.ChargerId, ct);
        if (!chargers.TryGetValue(request.ChargerId, out var charger) || charger.StationId != request.StationId) throw new ChargingException(400, "Charger does not belong to the selected station.");
        await ExpireLocked(chargers, ct);
        var existing = await db.Bookings.SingleOrDefaultAsync(b => b.BookedByUserId == user && b.IdempotencyKey == key, ct);
        if (existing is not null)
        {
            if (existing.VehicleId != vehicle.Id || existing.ChargerId != charger.Id) throw Conflict("Idempotency-Key was used with different booking details.");
            await db.SaveChangesAsync(ct); await write.CommitAsync(ct); return await BookingDto(existing.Id, ct);
        }
        await Eligible(vehicle, charger, user, ct);
        var now = Now;
        if (await db.Bookings.AnyAsync(b => b.Status == "Reserved" && (b.VehicleId == vehicle.Id || b.ChargerId == charger.Id), ct) ||
           await db.ChargingSessions.AnyAsync(s => s.Status == "Active" && (s.VehicleId == vehicle.Id || s.ChargerId == charger.Id), ct)) throw Conflict("Vehicle or charger already has a reservation or charging session.");
        await db.SaveChangesAsync(ct);
        var booking = new Booking { BookedByUserId = user, VehicleId = vehicle.Id, ChargerId = charger.Id, IdempotencyKey = key, CreatedAtUtc = now, ExpiresAtUtc = now.AddSeconds(options.Value.ReservationSeconds) };
        db.Bookings.Add(booking); charger.Status = "Reserved"; await db.SaveChangesAsync(ct); await write.CommitAsync(ct);
        return await BookingDto(booking.Id, ct);
    }
    public async Task<BookingResponse> GetBooking(long id, long user, CancellationToken ct)
    {
        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, ct);
        if (booking is null || !await Readable(booking, user, ct)) throw Missing();
        await RefreshBooking(id, ct); return await BookingDto(id, ct);
    }
    public async Task<IReadOnlyList<BookingResponse>> Bookings(long user, CancellationToken ct)
    {
        var ids = await db.Bookings.AsNoTracking().Where(b => b.BookedByUserId == user || b.Vehicle.OwnerUserId == user).OrderByDescending(b => b.Id).Take(100).Select(b => b.Id).ToListAsync(ct);
        var result = new List<BookingResponse>(); foreach (var id in ids) result.Add(await GetBooking(id, user, ct)); return result;
    }
    public async Task RefreshBooking(long id, CancellationToken ct)
    {
        var initial = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, ct);
        if (initial is null || initial.Status != "Reserved") return;
        await using var write = await VehicleWriteScope.OpenAsync(db, initial.VehicleId, ct);
        var chargers = await LockChargers(initial.VehicleId, initial.ChargerId, ct); await ExpireLocked(chargers, ct); await db.SaveChangesAsync(ct); await write.CommitAsync(ct);
    }
    public async Task<BookingResponse> Cancel(long id, long user, CancellationToken ct)
    {
        var initial = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, ct);
        if (initial is null || !await Readable(initial, user, ct)) throw Missing();
        await using var write = await VehicleWriteScope.OpenAsync(db, initial.VehicleId, ct);
        var chargers = await LockChargers(initial.VehicleId, initial.ChargerId, ct); await ExpireLocked(chargers, ct);
        var booking = await db.Bookings.SingleAsync(b => b.Id == id, ct);
        if (booking.Status == "Consumed") throw Conflict("Charging has started; stop the session instead.");
        if (booking.Status == "Reserved") { booking.Status = "Cancelled"; await db.SaveChangesAsync(ct); }
        if (chargers.TryGetValue(booking.ChargerId, out var charger)) await SyncCharger(charger, ct);
        await db.SaveChangesAsync(ct); await write.CommitAsync(ct); return await BookingDto(id, ct);
    }
    public async Task<SessionResponse> Start(long id, long user, CancellationToken ct)
    {
        var initial = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, ct);
        if (initial is null || !await Readable(initial, user, ct)) throw Missing();
        await using var write = await VehicleWriteScope.OpenAsync(db, initial.VehicleId, ct); var vehicle = write.Vehicle ?? throw Missing();
        var chargers = await LockChargers(vehicle.Id, initial.ChargerId, ct); await ExpireLocked(chargers, ct);
        var booking = await db.Bookings.SingleAsync(b => b.Id == id, ct);
        var previous = await db.ChargingSessions.SingleOrDefaultAsync(s => s.BookingId == id, ct);
        if (previous is not null) { await db.SaveChangesAsync(ct); await write.CommitAsync(ct); return SessionResponse.From(previous, Now); }
        if (booking.Status != "Reserved" || Now >= booking.ExpiresAtUtc)
        { await db.SaveChangesAsync(ct); await write.CommitAsync(ct); throw Conflict("Booking is expired or cancelled and cannot start charging."); }
        if (!chargers.TryGetValue(booking.ChargerId, out var charger)) throw Missing();
        await Eligible(vehicle, charger, user, ct);
        if (charger.Status != "Reserved" || await db.ChargingSessions.AnyAsync(s => s.Status == "Active" && (s.VehicleId == vehicle.Id || s.ChargerId == charger.Id), ct)) throw Conflict("Vehicle or charger already has an active session.");
        var session = new ChargingSession
        {
            BookingId = id,
            VehicleId = vehicle.Id,
            ChargerId = charger.Id,
            StartedByUserId = user,
            PayerOwnerUserId = vehicle.OwnerUserId,
            StartedAtUtc = Now,
            CapacityKWh = vehicle.BatteryCapacityKWh,
            InitialBatteryPercent = vehicle.BatteryPercent,
            BatteryPercent = vehicle.BatteryPercent,
            ConsumptionKWhPerKm = vehicle.ConsumptionKWhPerKm,
            PowerKW = charger.PowerKW,
            TariffPerKWh = charger.PricePerKWh,
            SimulationSpeed = options.Value.SimulationSpeed
        };
        booking.Status = "Consumed"; charger.Status = "Charging"; db.ChargingSessions.Add(session); await db.SaveChangesAsync(ct); await write.CommitAsync(ct); return SessionResponse.From(session, Now);
    }
    private async Task<bool> SessionReadable(ChargingSession s, long user, CancellationToken ct) => s.StartedByUserId == user || s.PayerOwnerUserId == user || await db.Bookings.AnyAsync(b => b.Id == s.BookingId && b.BookedByUserId == user, ct);
    public async Task<SessionResponse> GetSession(long id, long user, bool stop, CancellationToken ct)
    {
        var initial = await db.ChargingSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct);
        if (initial is null || !await SessionReadable(initial, user, ct)) throw Missing();
        return await Advance(id, stop, ct) ?? throw Missing();
    }
    public async Task<SessionResponse?> Advance(long id, bool stop, CancellationToken ct)
    {
        var initial = await db.ChargingSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct);
        if (initial is null) return null;
        await using var write = await VehicleWriteScope.OpenAsync(db, initial.VehicleId, ct); var vehicle = write.Vehicle ?? throw Missing();
        var chargers = await LockChargers(vehicle.Id, initial.ChargerId, ct); if (!chargers.TryGetValue(initial.ChargerId, out var charger)) throw Missing();
        var session = await db.ChargingSessions.SingleAsync(s => s.Id == id, ct);
        if (session.Status == "Active")
        {
            var progress = ChargingMath.Calculate(session, Now); session.EnergyKWh = progress.EnergyKWh; session.BatteryPercent = progress.BatteryPercent; session.Cost = progress.Cost; session.ElapsedSeconds = progress.ElapsedSeconds;
            vehicle.BatteryPercent = progress.BatteryPercent; vehicle.UpdatedAtUtc = Now;
            if (stop || progress.IsFull)
            {
                session.Status = "Completed"; session.EndedAtUtc = progress.EffectiveEndUtc;
                await db.SaveChangesAsync(ct);
                // This participant shares our scoped DbContext and SQL transaction. An exception rolls
                // back the battery, completion, reservation state and future wallet ledger together.
                var payment = await settlement.FinalizeAsync(new(session.Id, session.PayerOwnerUserId, session.EnergyKWh, session.Cost, session.EndedAtUtc.Value), ct);
                if (payment.PaymentStatus is not ("Paid" or "Unpaid") || payment.Note.Length > 200) throw new InvalidOperationException("Settlement contract returned invalid data.");
                session.PaymentStatus = payment.PaymentStatus; session.SettlementNote = payment.Note;
                await SyncCharger(charger, ct);
            }
            await db.SaveChangesAsync(ct);
        }
        await write.CommitAsync(ct); return SessionResponse.From(session, Now);
    }
    public async Task<IReadOnlyList<SessionResponse>> Sessions(long user, bool activeOnly, CancellationToken ct)
    {
        var query = db.ChargingSessions.AsNoTracking().Where(s => s.StartedByUserId == user || s.PayerOwnerUserId == user || s.Booking.BookedByUserId == user);
        if (activeOnly) query = query.Where(s => s.Status == "Active");
        var ids = await query.OrderByDescending(s => s.Id).Take(100).Select(s => s.Id).ToListAsync(ct); var result = new List<SessionResponse>();
        foreach (var id in ids) { var s = await GetSession(id, user, false, ct); if (!activeOnly || s.Status == "Active") result.Add(s); }
        return result;
    }
    private async Task<BookingResponse> BookingDto(long id, CancellationToken ct)
    {
        var b = await db.Bookings.AsNoTracking().Include(b => b.Charger).ThenInclude(c => c.Station).SingleAsync(b => b.Id == id, ct);
        var session = await db.ChargingSessions.Where(s => s.BookingId == id).Select(s => (long?)s.Id).SingleOrDefaultAsync(ct);
        return new(b.Id, b.BookingCode, b.VehicleId, b.Charger.StationId, b.ChargerId, b.Charger.Station.Name, b.Charger.ChargerCode, b.Status, b.CreatedAtUtc, b.ExpiresAtUtc, Now, session);
    }
}
