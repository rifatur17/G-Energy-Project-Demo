using GEnergy.Api.Data;
using GEnergy.Api.DTOs;
using GEnergy.Api.Entities;
using GEnergy.Api.Services.Charging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
namespace GEnergy.Api.Services.Wallets;

public sealed class WalletService(EnergyDbContext db, TimeProvider clock, IOptions<WalletOptions> options, IPaymentProvider provider)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static ChargingException Missing() => new(404, "Wallet transaction or payable session is unavailable to your account.");
    private static ChargingException Conflict(string message) => new(409, message);
    private Task<IDbContextTransaction?> Begin(CancellationToken ct) => db.Database.IsSqlServer() ? SqlBegin(ct) : Task.FromResult<IDbContextTransaction?>(null);
    private async Task<IDbContextTransaction?> SqlBegin(CancellationToken ct) => await db.Database.BeginTransactionAsync(ct);
    private static async Task Commit(IDbContextTransaction? tx, CancellationToken ct) { if (tx is not null) await tx.CommitAsync(ct); }
    // User lock serializes first initialization before a Wallet row exists. All writers use
    // vehicle -> charger -> payer User -> Wallet, or just User -> Wallet. Never reverse that order.
    private async Task<Wallet> LockWallet(long user, CancellationToken ct)
    {
        var account = db.Database.IsSqlServer() ? await db.Users.FromSqlInterpolated($"SELECT * FROM dbo.Users WITH (UPDLOCK,HOLDLOCK) WHERE Id={user}").SingleOrDefaultAsync(ct) : await db.Users.SingleOrDefaultAsync(x => x.Id == user, ct);
        if (account is null || !account.IsActive || account.Role != Roles.CarOwner) throw new ChargingException(403, "An active Car Owner wallet is required.");
        var wallet = db.Database.IsSqlServer() ? await db.Wallets.FromSqlInterpolated($"SELECT * FROM dbo.Wallets WITH (UPDLOCK,HOLDLOCK) WHERE UserId={user}").SingleOrDefaultAsync(ct) : await db.Wallets.SingleOrDefaultAsync(x => x.UserId == user, ct);
        if (wallet is null) { wallet = new() { UserId = user, CreatedAtUtc = Now, UpdatedAtUtc = Now }; db.Wallets.Add(wallet); await db.SaveChangesAsync(ct); }
        return wallet;
    }
    private void Amount(decimal amount)
    { if (amount <= 0 || amount > options.Value.MaxTransactionAmount || decimal.Round(amount, 2) != amount) throw new ChargingException(400, $"Amount must be positive, at most {options.Value.MaxTransactionAmount:0.00} BDT, with at most two decimal places."); }
    private void Credit(Wallet wallet, decimal amount)
    { if (wallet.Balance + amount > options.Value.MaxBalance) throw Conflict("The demo wallet balance limit would be exceeded."); wallet.Balance += amount; wallet.UpdatedAtUtc = Now; }
    public async Task<WalletResponse> Get(long user, CancellationToken ct)
    { await using var tx = await Begin(ct); var w = await LockWallet(user, ct); await Commit(tx, ct); return new(w.Id, w.Balance, "BDT", w.CreatedAtUtc, w.UpdatedAtUtc, options.Value.MaxTransactionAmount, options.Value.MaxBalance, true); }
    private async Task<WalletTransactionResponse> Dto(WalletTransaction t, CancellationToken ct)
    {
        var refund = await db.WalletTransactions.AsNoTracking().Where(x => x.OriginalTransactionId == t.Id).Select(x => x.Reference).SingleOrDefaultAsync(ct);
        return new(t.Id, t.Reference, t.Type, t.Status, t.Amount, "BDT", t.Description, t.ChargingSessionId, t.OriginalTransactionId, t.CreatedAtUtc, t.CompletedAtUtc, options.Value.AllowDemoRefunds && t.Type == "ChargingPayment" && t.Status == "Completed" && refund is null, refund);
    }
    public async Task<IReadOnlyList<WalletTransactionResponse>> History(long user, long? before, int take, CancellationToken ct)
    {
        var rows = await db.WalletTransactions.AsNoTracking().Where(t => t.Wallet.UserId == user && (!before.HasValue || t.Id < before)).OrderByDescending(t => t.Id).Take(take).ToListAsync(ct);
        var result = new List<WalletTransactionResponse>(); foreach (var t in rows) result.Add(await Dto(t, ct)); return result;
    }
    public async Task<WalletTransactionResponse> Details(long user, long id, CancellationToken ct)
    { var t = await db.WalletTransactions.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && t.Wallet.UserId == user, ct) ?? throw Missing(); return await Dto(t, ct); }
    public async Task<WalletOperationResponse> TopUp(long user, TopUpRequest request, string key, CancellationToken ct)
    {
        Amount(request.Amount); if (string.IsNullOrWhiteSpace(key) || key.Length > 100) throw new ChargingException(400, "Provide an Idempotency-Key of 1–100 characters.");
        if (request.Outcome is not ("Success" or "Failure")) throw new ChargingException(400, "Choose a demo Success or Failure outcome.");
        await using var tx = await Begin(ct); var w = await LockWallet(user, ct); var scopedKey = "topup:" + key;
        var previous = await db.WalletTransactions.SingleOrDefaultAsync(t => t.WalletId == w.Id && t.IdempotencyKey == scopedKey, ct);
        if (previous is not null) { if (previous.Amount != request.Amount || previous.RequestedOutcome != request.Outcome) throw Conflict("Idempotency-Key was used with different top-up details."); await Commit(tx, ct); return new(await Dto(previous, ct), w.Balance, "BDT"); }
        if (w.Balance + request.Amount > options.Value.MaxBalance) throw Conflict("The demo wallet balance limit would be exceeded.");
        var t = new WalletTransaction { WalletId = w.Id, Type = "TopUp", Amount = request.Amount, IdempotencyKey = scopedKey, RequestedOutcome = request.Outcome, CreatedAtUtc = Now };
        var payment = await provider.PayAsync(new(t.Reference, t.Amount, request.Outcome), ct);
        t.Status = payment.Success ? "Completed" : "Failed"; t.Description = payment.Description; t.CompletedAtUtc = Now;
        if (payment.Success) Credit(w, t.Amount); db.WalletTransactions.Add(t); await db.SaveChangesAsync(ct); await Commit(tx, ct); return new(await Dto(t, ct), w.Balance, "BDT");
    }
    // Member 3 calls this AFTER flushing Completed, inside its existing transaction.
    public async Task<SettlementResult> SettleWithinTransaction(long sessionId, CancellationToken ct)
    {
        if (db.Database.IsSqlServer() && db.Database.CurrentTransaction is null) throw new InvalidOperationException("Settlement requires the shared SQL transaction.");
        var s = await db.ChargingSessions.SingleAsync(x => x.Id == sessionId, ct);
        if (s.Status != "Completed" || !s.EndedAtUtc.HasValue) throw Conflict("Complete the charging session before payment.");
        if (!await db.Users.AnyAsync(u => u.Id == s.PayerOwnerUserId && u.IsActive && u.Role == Roles.CarOwner, ct)) return new("Unpaid", "Payer wallet is unavailable; charging completion is retained.");
        var w = await LockWallet(s.PayerOwnerUserId, ct);
        var prior = await db.WalletTransactions.SingleOrDefaultAsync(t => t.ChargingSessionId == s.Id && t.Type == "ChargingPayment" && t.Status == "Completed", ct);
        if (prior is not null) { s.PaymentReference = prior.Reference; s.PaymentStatus = "Paid"; return new("Paid", s.SettlementNote); }
        if (s.PaymentStatus == "Paid") throw Conflict("Session payment requires reconciliation; no additional debit was made.");
        if (s.Cost <= 0) return new("Unpaid", "No positive charging bill is payable.");
        if (s.Cost > options.Value.MaxTransactionAmount) return new("Unpaid", "Charging bill exceeds the configured demo payment limit.");
        if (w.Balance < s.Cost) return new("Unpaid", "Insufficient wallet balance. Top up and pay this completed session.");
        var t = new WalletTransaction { WalletId = w.Id, Type = "ChargingPayment", Status = "Completed", Amount = s.Cost, IdempotencyKey = $"pay:{s.Id}", ChargingSessionId = s.Id, Description = "Simulated charging payment from Owner wallet.", CreatedAtUtc = Now, CompletedAtUtc = Now };
        w.Balance -= s.Cost; w.UpdatedAtUtc = Now; db.WalletTransactions.Add(t); s.PaymentReference = t.Reference; s.PaymentStatus = "Paid"; s.SettlementNote = "Paid from demo wallet; no real money transferred.";
        await db.SaveChangesAsync(ct); return new("Paid", s.SettlementNote);
    }
    private async Task<(VehicleWriteScope Scope, ChargingSession Session)> LockSession(long sessionId, long user, CancellationToken ct)
    {
        var initial = await db.ChargingSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId && s.PayerOwnerUserId == user, ct) ?? throw Missing();
        var scope = await VehicleWriteScope.OpenAsync(db, initial.VehicleId, ct);
        try
        {
            if (db.Database.IsSqlServer()) await db.Chargers.FromSqlInterpolated($"SELECT * FROM dbo.Chargers WITH (UPDLOCK,HOLDLOCK) WHERE Id={initial.ChargerId}").SingleAsync(ct);
            var session = await db.ChargingSessions.SingleAsync(s => s.Id == sessionId, ct); return (scope, session);
        }
        catch { await scope.DisposeAsync(); throw; }
    }
    public async Task<WalletOperationResponse> Pay(long user, long sessionId, CancellationToken ct)
    {
        var locked = await LockSession(sessionId, user, ct); await using var scope = locked.Scope;
        var result = await SettleWithinTransaction(sessionId, ct); if (result.PaymentStatus != "Paid") throw Conflict(result.Note);
        var t = await db.WalletTransactions.SingleAsync(t => t.ChargingSessionId == sessionId && t.Type == "ChargingPayment", ct); var w = await db.Wallets.SingleAsync(w => w.Id == t.WalletId, ct);
        await db.SaveChangesAsync(ct); await scope.CommitAsync(ct); return new(await Dto(t, ct), w.Balance, "BDT");
    }
    public async Task<WalletOperationResponse> Refund(long user, long transactionId, CancellationToken ct)
    {
        if (!options.Value.AllowDemoRefunds) throw Conflict("Demo refunds are disabled.");
        var original = await db.WalletTransactions.AsNoTracking().SingleOrDefaultAsync(t => t.Id == transactionId && t.Wallet.UserId == user, ct) ?? throw Missing();
        if (original.Type != "ChargingPayment" || original.Status != "Completed" || !original.ChargingSessionId.HasValue) throw Conflict("Only completed demo charging payments are eligible for a full refund.");
        var locked = await LockSession(original.ChargingSessionId.Value, user, ct); await using var scope = locked.Scope;
        if (locked.Session.Status != "Completed" || locked.Session.PaymentStatus != "Paid") throw Conflict("The session is not an eligible paid completion.");
        var w = await LockWallet(user, ct);
        if (await db.WalletTransactions.AnyAsync(t => t.OriginalTransactionId == original.Id, ct)) throw Conflict("This payment has already been refunded.");
        Credit(w, original.Amount);
        var refund = new WalletTransaction { WalletId = w.Id, Type = "Refund", Status = "Completed", Amount = original.Amount, OriginalTransactionId = original.Id, ChargingSessionId = original.ChargingSessionId, IdempotencyKey = $"refund:{original.Id}", Description = "Full demo refund; original payment retained. Session remains settled.", CreatedAtUtc = Now, CompletedAtUtc = Now };
        db.WalletTransactions.Add(refund); locked.Session.SettlementNote = "Demo charging payment refunded; session remains settled and cannot be charged again.";
        await db.SaveChangesAsync(ct); await scope.CommitAsync(ct); return new(await Dto(refund, ct), w.Balance, "BDT");
    }
}
