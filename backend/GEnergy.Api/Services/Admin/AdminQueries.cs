using GEnergy.Api.Data;
using GEnergy.Api.DTOs;
using GEnergy.Api.Services.Wallets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace GEnergy.Api.Services.Admin;

public sealed class AdminQueries(EnergyDbContext db, TimeProvider clock, IOptions<WalletOptions> options)
{
    public async Task<AdminDashboardResponse> Dashboard(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var users = await db.Users.CountAsync(ct); var stations = await db.Stations.CountAsync(s => s.IsActive, ct); var chargers = await db.Chargers.CountAsync(ct);
        var available = await db.Chargers.CountAsync(c => c.Station.IsActive && c.Status != "Offline" && !db.Bookings.Any(b => b.ChargerId == c.Id && b.Status == "Reserved" && b.ExpiresAtUtc > now) && !db.ChargingSessions.Any(s => s.ChargerId == c.Id && s.Status == "Active"), ct);
        var holds = await db.Bookings.CountAsync(b => b.Status == "Reserved" && b.ExpiresAtUtc > now, ct); var active = await db.ChargingSessions.CountAsync(s => s.Status == "Active", ct); var completed = await db.ChargingSessions.CountAsync(s => s.Status == "Completed", ct);
        var ledger = await db.WalletTransactions.GroupBy(_ => 1).Select(g => new { Payments = g.Count(t => t.Type == "ChargingPayment" && t.Status == "Completed"), TopUps = g.Count(t => t.Type == "TopUp" && t.Status == "Completed"), Failed = g.Count(t => t.Type == "TopUp" && t.Status == "Failed"), RefundCount = g.Count(t => t.Type == "Refund" && t.Status == "Completed"), Gross = g.Sum(t => t.Type == "ChargingPayment" && t.Status == "Completed" ? t.Amount : 0), Refunds = g.Sum(t => t.Type == "Refund" && t.Status == "Completed" ? t.Amount : 0) }).SingleOrDefaultAsync(ct);
        var balances = await db.Wallets.SumAsync(w => (decimal?)w.Balance, ct) ?? 0;
        return new(now, users, stations, chargers, available, holds, active, completed, ledger?.Payments ?? 0, ledger?.TopUps ?? 0, ledger?.Failed ?? 0, ledger?.RefundCount ?? 0, ledger?.Gross ?? 0, ledger?.Refunds ?? 0, (ledger?.Gross ?? 0) - (ledger?.Refunds ?? 0), balances, "BDT", true);
    }
    public Task<List<AdminUserResponse>> Users(string? role, long? before, int take, CancellationToken ct) => db.Users.AsNoTracking().Where(u => (role == null || u.Role == role) && (!before.HasValue || u.Id < before)).OrderByDescending(u => u.Id).Take(take).Select(u => new AdminUserResponse(u.Id, u.UserCode, u.Role, u.IsActive, u.CreatedAtUtc)).ToListAsync(ct);
    public Task<List<AdminBookingResponse>> Bookings(string? status, long? stationId, DateTime? from, DateTime? to, long? before, int take, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var q = db.Bookings.AsNoTracking().Where(b => (!stationId.HasValue || b.Charger.StationId == stationId) && (!from.HasValue || b.CreatedAtUtc >= from) && (!to.HasValue || b.CreatedAtUtc <= to) && (!before.HasValue || b.Id < before));
        if (status is not null) q = status == "Expired" ? q.Where(b => b.Status == "Expired" || (b.Status == "Reserved" && b.ExpiresAtUtc <= now)) : status == "Reserved" ? q.Where(b => b.Status == "Reserved" && b.ExpiresAtUtc > now) : q.Where(b => b.Status == status);
        return q.OrderByDescending(b => b.Id).Take(take).Select(b => new AdminBookingResponse(b.Id, b.BookingCode, b.Charger.StationId, b.Charger.Station.Name, b.Charger.ChargerCode, b.Vehicle.RegistrationNo, b.BookedBy.UserCode, b.Status == "Reserved" && b.ExpiresAtUtc <= now ? "Expired" : b.Status, b.Status, b.CreatedAtUtc, b.ExpiresAtUtc, db.ChargingSessions.Where(s => s.BookingId == b.Id).Select(s => (long?)s.Id).SingleOrDefault())).ToListAsync(ct);
    }
    public Task<List<AdminSessionResponse>> Sessions(string? status, string? payment, long? stationId, DateTime? from, DateTime? to, long? before, int take, CancellationToken ct) => db.ChargingSessions.AsNoTracking().Where(s => (status == null || s.Status == status) && (payment == null || s.PaymentStatus == payment) && (!stationId.HasValue || s.Charger.StationId == stationId) && (!from.HasValue || s.StartedAtUtc >= from) && (!to.HasValue || s.StartedAtUtc <= to) && (!before.HasValue || s.Id < before)).OrderByDescending(s => s.Id).Take(take).Select(s => new AdminSessionResponse(s.Id, s.BookingId, s.Booking.BookingCode, s.Charger.StationId, s.Charger.Station.Name, s.Charger.ChargerCode, s.Vehicle.RegistrationNo, s.StartedBy.UserCode, s.PayerOwner.UserCode, s.Status, s.StartedAtUtc, s.EndedAtUtc, s.InitialBatteryPercent, s.BatteryPercent, s.EnergyKWh, s.Cost, s.PaymentStatus, s.PaymentReference, s.SettlementNote)).ToListAsync(ct);
    public async Task<IReadOnlyList<AdminTransactionResponse>> Transactions(string? type, string? status, DateTime? from, DateTime? to, long? before, int take, CancellationToken ct)
    {
        var rows = await db.WalletTransactions.AsNoTracking().Where(t => (type == null || t.Type == type) && (status == null || t.Status == status) && (!from.HasValue || t.CreatedAtUtc >= from) && (!to.HasValue || t.CreatedAtUtc <= to) && (!before.HasValue || t.Id < before)).OrderByDescending(t => t.Id).Take(take).Select(t => new AdminTransactionResponse(t.Id, t.Reference, t.Wallet.User.UserCode, t.Type, t.Status, t.Amount, t.Description, t.CreatedAtUtc, t.ChargingSessionId, t.OriginalTransaction == null ? null : t.OriginalTransaction.Reference, db.WalletTransactions.Where(r => r.OriginalTransactionId == t.Id).Select(r => r.Reference).FirstOrDefault(), false)).ToListAsync(ct);
        return rows.Select(t => t with { OwnerCanRefund = DemoRefundPolicy.CanRefund(t.Type, t.Status, t.RefundReference is not null, options.Value.AllowDemoRefunds) }).ToArray();
    }
    public Task<List<AdminWalletResponse>> Wallets(long? before, int take, CancellationToken ct) => db.Wallets.AsNoTracking().Where(w => !before.HasValue || w.Id < before).OrderByDescending(w => w.Id).Take(take).Select(w => new AdminWalletResponse(w.Id, w.User.UserCode, w.Balance, w.UpdatedAtUtc)).ToListAsync(ct);
}
