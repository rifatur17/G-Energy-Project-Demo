using System.Globalization;
using GEnergy.Api.Data;
using GEnergy.Api.DTOs;
using GEnergy.Api.Entities;
using GEnergy.Api.Services.Charging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
namespace GEnergy.Api.Services.Billing;

public sealed class BillingOptions
{
    public int DueDays { get; set; } = 7;               // PRD BILL-02: personal due date 7 days after issue
    public decimal PlanFeePerVehicle { get; set; } = 200; // PRD BILL-03: Tk 200 per car per month
    public int FreeMonths { get; set; } = 3;              // after three free months
    public bool AllowDemoMonthClose { get; set; } = true;  // demo button; production closes on the 1st
}

// Charging is metered, then billed later (PRD ALW-01/BILL-02). Nothing is debited at completion.
public sealed class PostpaidChargingSettlement : IChargingSettlement
{
    public const string Note = "Postpaid: added to your next monthly statement.";
    public Task<SettlementResult> FinalizeAsync(ChargingCompletion completion, CancellationToken ct) =>
        Task.FromResult(new SettlementResult("Unpaid", completion.Cost > 0 ? Note : "No charge for this session."));
}

public sealed class BillingService(EnergyDbContext db, TimeProvider clock, IOptions<BillingOptions> options, IOptions<SslCommerzOptions> ssl, IPaymentGateway gateway)
{
    private static readonly TimeSpan Dhaka = TimeSpan.FromHours(6);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static ChargingException Missing() => new(404, "Statement is unavailable to your account.");
    private static ChargingException Conflict(string message) => new(409, message);
    private Task<IDbContextTransaction?> Begin(CancellationToken ct) => db.Database.IsSqlServer() ? SqlBegin(ct) : Task.FromResult<IDbContextTransaction?>(null);
    private async Task<IDbContextTransaction?> SqlBegin(CancellationToken ct) => await db.Database.BeginTransactionAsync(ct);
    private static async Task Commit(IDbContextTransaction? tx, CancellationToken ct) { if (tx is not null) await tx.CommitAsync(ct); }
    private static (string Key, DateTime StartUtc) Month(DateTime utc)
    {
        var local = utc + Dhaka;
        return (local.ToString("yyyy-MM", CultureInfo.InvariantCulture), new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Utc) - Dhaka);
    }
    private IQueryable<ChargingSession> Unbilled(long owner) =>
        db.ChargingSessions.Where(s => s.PayerOwnerUserId == owner && s.Status == "Completed" && s.PaymentStatus == "Unpaid" && s.StatementId == null && s.Cost > 0);
    private async Task<List<Vehicle>> PlanVehicles(long owner, DateTime now, CancellationToken ct)
    {
        var vehicles = await db.Vehicles.AsNoTracking().Where(v => v.OwnerUserId == owner).ToListAsync(ct);
        return vehicles.Where(v => v.CreatedAtUtc.AddMonths(options.Value.FreeMonths) <= now).ToList();
    }
    private static string StatusOf(BillingStatement s, DateTime now) => s.Status == "Paid" ? "Paid" : s.DueAtUtc < now ? "Overdue" : "Due";

    public async Task<BillingSummaryResponse> Summary(long owner, CancellationToken ct)
    {
        var now = Now; var month = Month(now);
        var unbilled = await Unbilled(owner).AsNoTracking().Select(s => s.Cost).ToListAsync(ct);
        var active = await db.ChargingSessions.AsNoTracking().CountAsync(s => s.PayerOwnerUserId == owner && s.Status == "Active", ct);
        var open = await db.BillingStatements.AsNoTracking().Where(s => s.OwnerUserId == owner && s.Status == "Issued").ToListAsync(ct);
        var vehicles = await db.Vehicles.AsNoTracking().Where(v => v.OwnerUserId == owner).ToListAsync(ct);
        var planCars = vehicles.Count(v => v.CreatedAtUtc.AddMonths(options.Value.FreeMonths) <= now);
        var feeBilled = await db.BillingStatements.AsNoTracking().AnyAsync(s => s.OwnerUserId == owner && s.PeriodKey == month.Key && s.PlanFee > 0, ct);
        var trialEnds = vehicles.Where(v => v.CreatedAtUtc.AddMonths(options.Value.FreeMonths) > now).Select(v => (DateTime?)v.CreatedAtUtc.AddMonths(options.Value.FreeMonths)).Min();
        return new(unbilled.Sum(), unbilled.Count, active, feeBilled ? 0 : planCars * options.Value.PlanFeePerVehicle, open.Sum(s => s.Total), open.Count(s => s.DueAtUtc < now),
            vehicles.Count, planCars, options.Value.PlanFeePerVehicle, options.Value.FreeMonths, trialEnds, options.Value.DueDays,
            options.Value.AllowDemoMonthClose, ssl.Value.UseLiveGateway ? "SSLCommerz sandbox" : "SSLCommerz demo checkout", "BDT");
    }
    public async Task<IReadOnlyList<UnbilledSessionResponse>> UnbilledSessions(long owner, CancellationToken ct) =>
        await Unbilled(owner).AsNoTracking().OrderByDescending(s => s.Id).Take(100)
            .Select(s => new UnbilledSessionResponse(s.Id, s.StartedAtUtc, s.EndedAtUtc, s.Charger.Station.Name, s.Charger.ChargerCode, s.Vehicle.RegistrationNo, s.StartedBy.FullName, s.EnergyKWh, s.Cost))
            .ToListAsync(ct);
    public async Task<IReadOnlyList<StatementResponse>> Statements(long owner, CancellationToken ct)
    {
        var now = Now;
        var rows = await db.BillingStatements.AsNoTracking().Where(s => s.OwnerUserId == owner).OrderByDescending(s => s.Id).Take(60).ToListAsync(ct);
        return rows.Select(s => StatementResponse.From(s, StatusOf(s, now))).ToList();
    }
    public async Task<StatementDetailResponse> Details(long owner, long id, CancellationToken ct)
    {
        var s = await db.BillingStatements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == owner, ct) ?? throw Missing();
        var lines = await db.ChargingSessions.AsNoTracking().Where(x => x.StatementId == id).OrderBy(x => x.StartedAtUtc)
            .Select(x => new UnbilledSessionResponse(x.Id, x.StartedAtUtc, x.EndedAtUtc, x.Charger.Station.Name, x.Charger.ChargerCode, x.Vehicle.RegistrationNo, x.StartedBy.FullName, x.EnergyKWh, x.Cost)).ToListAsync(ct);
        var payments = await db.StatementPayments.AsNoTracking().Where(p => p.StatementId == id).OrderByDescending(p => p.Id)
            .Select(p => new StatementPaymentResponse(p.Id, p.TranId, p.Gateway, p.Amount, p.Status, p.CardType, p.BankTranId, p.FailureReason, p.CreatedAtUtc, p.CompletedAtUtc)).ToListAsync(ct);
        return new(StatementResponse.From(s, StatusOf(s, Now)), lines, payments);
    }

    // Demo month close. Repeating it with nothing new to bill issues nothing (PRD BILL-02 acceptance).
    public async Task<StatementResponse> CloseNow(long owner, CancellationToken ct)
    {
        if (!options.Value.AllowDemoMonthClose) throw Conflict("Statements are issued automatically on the 1st of each month.");
        await using var tx = await Begin(ct);
        var account = db.Database.IsSqlServer()
            ? await db.Users.FromSqlInterpolated($"SELECT * FROM dbo.Users WITH (UPDLOCK, HOLDLOCK) WHERE Id = {owner}").SingleOrDefaultAsync(ct)
            : await db.Users.SingleOrDefaultAsync(u => u.Id == owner, ct);
        if (account is null || !account.IsActive || account.Role != Roles.CarOwner) throw new ChargingException(403, "An active Car Owner account is required.");
        var now = Now; var month = Month(now);
        var sessions = await Unbilled(owner).ToListAsync(ct);
        var feeBilled = await db.BillingStatements.AnyAsync(s => s.OwnerUserId == owner && s.PeriodKey == month.Key && s.PlanFee > 0, ct);
        var planCars = feeBilled ? 0 : (await PlanVehicles(owner, now, ct)).Count;
        var charging = sessions.Sum(s => s.Cost); var fee = planCars * options.Value.PlanFeePerVehicle;
        if (charging + fee <= 0) throw Conflict("Nothing to bill yet. Completed charging appears here before the statement is issued.");
        var sequence = await db.BillingStatements.CountAsync(s => s.OwnerUserId == owner && s.PeriodKey == month.Key, ct) + 1;
        var start = sessions.Count > 0 ? new[] { month.StartUtc, sessions.Min(s => s.StartedAtUtc) }.Min() : month.StartUtc;
        var statement = new BillingStatement
        {
            OwnerUserId = owner, PeriodKey = month.Key, Sequence = sequence,
            StatementNumber = $"GE-ST-{month.Key.Replace("-", "")}-{account.UserCode.Split('-')[^1]}-{sequence:D2}",
            PeriodStartUtc = start, PeriodEndUtc = now, IssuedAtUtc = now, DueAtUtc = now.AddDays(options.Value.DueDays),
            SessionCount = sessions.Count, ChargingTotal = charging, PlanVehicleCount = planCars, PlanFee = fee, Total = charging + fee
        };
        db.BillingStatements.Add(statement); await db.SaveChangesAsync(ct);
        foreach (var s in sessions) { s.StatementId = statement.Id; s.SettlementNote = $"Billed on statement {statement.StatementNumber}."; }
        await db.SaveChangesAsync(ct); await Commit(tx, ct);
        return StatementResponse.From(statement, StatusOf(statement, now));
    }

    // PRD PAY-02/PAY-04: open a hosted SSLCommerz checkout for an issued statement.
    public async Task<CheckoutResponse> Checkout(long owner, long id, CancellationToken ct)
    {
        var s = await db.BillingStatements.SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == owner, ct) ?? throw Missing();
        if (s.Status == "Paid") throw Conflict("This statement is already paid.");
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == owner, ct);
        var payment = new StatementPayment { StatementId = s.Id, OwnerUserId = owner, TranId = "GE" + Guid.NewGuid().ToString("N")[..22].ToUpperInvariant(), Gateway = gateway.Name, Amount = s.Total, CreatedAtUtc = Now };
        db.StatementPayments.Add(payment); await db.SaveChangesAsync(ct);
        var api = ssl.Value.PublicApiBaseUrl.TrimEnd('/') + "/api/payments/sslcommerz";
        var result = await gateway.InitiateAsync(new(payment.TranId, payment.Amount, s.StatementNumber, new(user.FullName, user.Email, user.Phone),
            api + "/success", api + "/fail", api + "/cancel", api + "/ipn"), ct);
        if (!result.Ok)
        {
            payment.Status = "Failed"; payment.FailureReason = Trim(result.Error); payment.CompletedAtUtc = Now; await db.SaveChangesAsync(ct);
            throw new ChargingException(502, result.Error ?? "Payment gateway is unavailable.");
        }
        return new(payment.TranId, result.RedirectUrl!, gateway.Name, payment.Amount, "BDT");
    }

    // Success callback or IPN. Confirms with the gateway, matches tran_id/amount/currency, and
    // applies the payment once even if the gateway calls back several times.
    public async Task<PaymentOutcome> ConfirmSuccess(string? tranId, string? valId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tranId) || string.IsNullOrWhiteSpace(valId)) return new(false, null, "Missing payment reference.");
        var known = await db.StatementPayments.AsNoTracking().SingleOrDefaultAsync(p => p.TranId == tranId, ct);
        if (known is null) return new(false, null, "Unknown payment reference.");
        var check = await gateway.ValidateAsync(valId, ct);
        await using var tx = await Begin(ct);
        var p = await LockPayment(tranId, ct);
        if (p.Status == "Paid") { await Commit(tx, ct); return new(true, p.StatementId, "Payment already confirmed."); }
        if (!check.Valid) { await Commit(tx, ct); return new(false, p.StatementId, check.Error ?? "Payment could not be confirmed."); }
        if (check.TranId != p.TranId || check.Amount != p.Amount || !string.Equals(check.Currency, p.Currency, StringComparison.OrdinalIgnoreCase))
        {
            p.Status = "Failed"; p.FailureReason = "Gateway confirmation did not match this checkout."; p.CompletedAtUtc = Now;
            await db.SaveChangesAsync(ct); await Commit(tx, ct); return new(false, p.StatementId, p.FailureReason);
        }
        var s = await db.BillingStatements.SingleAsync(x => x.Id == p.StatementId, ct);
        if (s.Status == "Paid")
        {
            // Another checkout already paid this statement. Keep evidence for a manual refund; never double-apply.
            p.Status = "Failed"; p.ValId = valId; p.FailureReason = "Statement was already paid by another payment; refer to support for refund."; p.CompletedAtUtc = Now;
            await db.SaveChangesAsync(ct); await Commit(tx, ct); return new(false, s.Id, p.FailureReason);
        }
        p.Status = "Paid"; p.ValId = valId; p.BankTranId = Trim(check.BankTranId, 80); p.CardType = Trim(check.CardType, 50); p.CompletedAtUtc = Now;
        s.Status = "Paid"; s.PaidAtUtc = Now; s.PaymentReference = p.TranId;
        var sessions = await db.ChargingSessions.Where(x => x.StatementId == s.Id).ToListAsync(ct);
        foreach (var x in sessions) { x.PaymentStatus = "Paid"; x.PaymentReference = p.TranId; x.SettlementNote = $"Paid via {gateway.Name} on statement {s.StatementNumber}."; }
        await db.SaveChangesAsync(ct); await Commit(tx, ct);
        return new(true, s.Id, "Payment confirmed.");
    }
    public async Task<PaymentOutcome> MarkUnsuccessful(string? tranId, string status, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tranId)) return new(false, null, "Missing payment reference.");
        await using var tx = await Begin(ct);
        var p = await db.StatementPayments.SingleOrDefaultAsync(x => x.TranId == tranId, ct);
        if (p is null) return new(false, null, "Unknown payment reference.");
        if (p.Status == "Initiated") { p.Status = status; p.FailureReason = Trim(reason); p.CompletedAtUtc = Now; await db.SaveChangesAsync(ct); }
        await Commit(tx, ct); return new(false, p.StatementId, reason);
    }
    // Demo checkout page (no real payment connected).
    public async Task<StatementPayment?> DemoPayment(string tranId, CancellationToken ct) =>
        await db.StatementPayments.AsNoTracking().Include(p => p.Statement).SingleOrDefaultAsync(p => p.TranId == tranId && p.Gateway == "SSLCommerzDemo", ct);
    public async Task<StatementPayment?> DemoUnavailable(string tranId, string method, CancellationToken ct)
    {
        var p = await db.StatementPayments.Include(x => x.Statement).SingleOrDefaultAsync(x => x.TranId == tranId && x.Gateway == "SSLCommerzDemo", ct);
        if (p is null) return null;
        if (p.Status == "Initiated")
        {
            p.Status = "Failed"; p.CardType = method; p.FailureReason = "Payment service is not available at the moment (demo checkout)."; p.CompletedAtUtc = Now;
            await db.SaveChangesAsync(ct);
        }
        return p;
    }
    private async Task<StatementPayment> LockPayment(string tranId, CancellationToken ct)
    {
        var p = db.Database.IsSqlServer()
            ? await db.StatementPayments.FromSqlInterpolated($"SELECT * FROM dbo.StatementPayments WITH (UPDLOCK, HOLDLOCK) WHERE TranId = {tranId}").SingleOrDefaultAsync(ct)
            : await db.StatementPayments.SingleOrDefaultAsync(x => x.TranId == tranId, ct);
        if (p is null) throw Missing();
        if (db.Database.IsSqlServer()) await db.BillingStatements.FromSqlInterpolated($"SELECT * FROM dbo.BillingStatements WITH (UPDLOCK, HOLDLOCK) WHERE Id = {p.StatementId}").SingleAsync(ct);
        return p;
    }
    private static string? Trim(string? value, int max = 200) => value is null ? null : value.Length <= max ? value : value[..max];
}
public sealed record PaymentOutcome(bool Paid, long? StatementId, string Message);
