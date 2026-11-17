using GEnergy.Api.Data;
using GEnergy.Api.DTOs;
using GEnergy.Api.Entities;
using GEnergy.Api.Services.Charging;
using GEnergy.Api.Services.Wallets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
namespace GEnergy.Api.Services.Billing;

// Owner wallet top-up through the SSLCommerz hosted checkout (bKash, Nagad, cards, internet banking).
// Transaction ids start with "GW" so gateway callbacks can be routed to top-ups ("GE" = statements).
public sealed class WalletTopUpService(EnergyDbContext db, TimeProvider clock, WalletService wallets, IOptions<SslCommerzOptions> ssl, IPaymentGateway gateway)
{
    public const string Prefix = "GW";
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private Task<IDbContextTransaction?> Begin(CancellationToken ct) => db.Database.IsSqlServer() ? SqlBegin(ct) : Task.FromResult<IDbContextTransaction?>(null);
    private async Task<IDbContextTransaction?> SqlBegin(CancellationToken ct) => await db.Database.BeginTransactionAsync(ct);
    private static async Task Commit(IDbContextTransaction? tx, CancellationToken ct) { if (tx is not null) await tx.CommitAsync(ct); }
    private static string? Trim(string? value, int max = 200) => value is null ? null : value.Length <= max ? value : value[..max];

    public async Task<CheckoutResponse> Checkout(long owner, decimal amount, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == owner, ct);
        if (user is null || !user.IsActive || user.Role != Roles.CarOwner) throw new ChargingException(403, "An active Car Owner wallet is required.");
        await wallets.ValidateTopUp(owner, amount, ct);
        var payment = new WalletTopUpPayment { OwnerUserId = owner, TranId = Prefix + Guid.NewGuid().ToString("N")[..22].ToUpperInvariant(), Gateway = gateway.Name, Amount = amount, CreatedAtUtc = Now };
        db.WalletTopUpPayments.Add(payment); await db.SaveChangesAsync(ct);
        var api = ssl.Value.PublicApiBaseUrl.TrimEnd('/') + "/api/payments/sslcommerz";
        var result = await gateway.InitiateAsync(new(payment.TranId, amount, "Wallet top-up " + user.UserCode, new(user.FullName, user.Email, user.Phone),
            api + "/success", api + "/fail", api + "/cancel", api + "/ipn"), ct);
        if (!result.Ok)
        {
            payment.Status = "Failed"; payment.FailureReason = Trim(result.Error); payment.CompletedAtUtc = Now; await db.SaveChangesAsync(ct);
            throw new ChargingException(502, result.Error ?? "Payment gateway is unavailable.");
        }
        return new(payment.TranId, result.RedirectUrl!, gateway.Name, amount, "BDT");
    }

    public async Task<PaymentOutcome> ConfirmSuccess(string tranId, string? valId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(valId)) return new(false, null, "Missing payment reference.");
        if (!await db.WalletTopUpPayments.AsNoTracking().AnyAsync(p => p.TranId == tranId, ct)) return new(false, null, "Unknown payment reference.");
        var check = await gateway.ValidateAsync(valId, ct);
        await using var tx = await Begin(ct);
        var p = db.Database.IsSqlServer()
            ? await db.WalletTopUpPayments.FromSqlInterpolated($"SELECT * FROM dbo.WalletTopUpPayments WITH (UPDLOCK, HOLDLOCK) WHERE TranId = {tranId}").SingleAsync(ct)
            : await db.WalletTopUpPayments.SingleAsync(x => x.TranId == tranId, ct);
        if (p.Status == "Paid") { await Commit(tx, ct); return new(true, null, "Top-up already confirmed."); }
        if (!check.Valid) { await Commit(tx, ct); return new(false, null, check.Error ?? "Payment could not be confirmed."); }
        if (check.TranId != p.TranId || check.Amount != p.Amount || !string.Equals(check.Currency, p.Currency, StringComparison.OrdinalIgnoreCase))
        {
            p.Status = "Failed"; p.FailureReason = "Gateway confirmation did not match this checkout."; p.CompletedAtUtc = Now;
            await db.SaveChangesAsync(ct); await Commit(tx, ct); return new(false, null, p.FailureReason);
        }
        try
        {
            var credit = await wallets.CreditGatewayTopUp(p.OwnerUserId, p.Amount, p.TranId, p.Gateway, ct);
            p.Status = "Paid"; p.WalletTransactionId = credit.Id; p.ValId = valId; p.BankTranId = Trim(check.BankTranId, 80); p.CardType = Trim(check.CardType, 50); p.CompletedAtUtc = Now;
        }
        catch (ChargingException e)
        {
            // Paid at the gateway but the wallet cannot accept it (e.g. balance limit): keep evidence for support.
            p.Status = "Failed"; p.ValId = valId; p.FailureReason = Trim("Paid at gateway but not credited: " + e.Message + " Refer to support for a refund."); p.CompletedAtUtc = Now;
        }
        await db.SaveChangesAsync(ct); await Commit(tx, ct);
        return new(p.Status == "Paid", null, p.Status == "Paid" ? "Top-up confirmed." : p.FailureReason!);
    }
    public async Task MarkUnsuccessful(string tranId, string status, string reason, CancellationToken ct)
    {
        var p = await db.WalletTopUpPayments.SingleOrDefaultAsync(x => x.TranId == tranId, ct);
        if (p is not null && p.Status == "Initiated") { p.Status = status; p.FailureReason = Trim(reason); p.CompletedAtUtc = Now; await db.SaveChangesAsync(ct); }
    }
    public Task<WalletTopUpPayment?> Find(string tranId, CancellationToken ct) => db.WalletTopUpPayments.AsNoTracking().Include(p => p.Owner).SingleOrDefaultAsync(p => p.TranId == tranId, ct);
    public async Task<WalletTopUpPayment?> DemoUnavailable(string tranId, string method, CancellationToken ct)
    {
        var p = await db.WalletTopUpPayments.Include(x => x.Owner).SingleOrDefaultAsync(x => x.TranId == tranId && x.Gateway == "SSLCommerzDemo", ct);
        if (p is null) return null;
        if (p.Status == "Initiated") { p.Status = "Failed"; p.CardType = method; p.FailureReason = "Payment service is not available at the moment (demo checkout)."; p.CompletedAtUtc = Now; await db.SaveChangesAsync(ct); }
        return p;
    }
    public async Task<IReadOnlyList<TopUpAttemptResponse>> Recent(long owner, CancellationToken ct) =>
        await db.WalletTopUpPayments.AsNoTracking().Where(p => p.OwnerUserId == owner).OrderByDescending(p => p.Id).Take(10)
            .Select(p => new TopUpAttemptResponse(p.Id, p.TranId, p.Gateway, p.Amount, p.Status, p.CardType, p.FailureReason, p.CreatedAtUtc, p.CompletedAtUtc)).ToListAsync(ct);
}
