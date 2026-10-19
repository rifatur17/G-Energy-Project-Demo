using System.Net;
using System.Net.Http.Json;
using GEnergy.Api.Data;
using GEnergy.Api.DTOs;
using GEnergy.Api.Entities;
using GEnergy.Api.Services.Charging;
using GEnergy.Api.Services.Wallets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
namespace GEnergy.IntegrationTests;

public sealed class WalletFailureProbe : SaveChangesInterceptor
{
    public bool Fail;
    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
    {
        if (Fail && data.Context!.ChangeTracker.Entries<WalletTransaction>().Any()) throw new InvalidOperationException("Injected after wallet SQL writes, before commit.");
        return ValueTask.FromResult(result);
    }
}
public static class WalletFixture
{
    public static Task<ChargingScenario> Create(bool sql = false, WalletFailureProbe? probe = null) => ChargingScenario.Create(sql, configure: services =>
    {
        services.RemoveAll<IChargingSettlement>(); services.AddScoped<IChargingSettlement, WalletChargingSettlement>();
        if (probe is not null) services.AddDbContext<EnergyDbContext>(o => o.AddInterceptors(probe));
    }, label: "Member4");
    public static async Task<HttpResponseMessage> TopUp(ChargingScenario s, decimal amount = 1000, string outcome = "Success", string? key = null)
    { using var request = new HttpRequestMessage(HttpMethod.Post, "/api/wallet/top-up"); request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N")); request.Content = JsonContent.Create(new { amount, outcome, balance = 999999 }); return await s.Client.SendAsync(request); }
    public static Task<WalletResponse?> Balance(ChargingScenario s) => s.Client.GetFromJsonAsync<WalletResponse>("/api/wallet");
    public static async Task<SessionResponse> Completed(ChargingScenario s, long? vehicle = null, long? charger = null)
    { var active = await s.Start((await s.Book(vehicle, charger)).Id); s.Clock.Advance(10); return await s.Stop(active.Id); }
    public static async Task<WalletOperationResponse> Pay(ChargingScenario s, long id)
    { var response = await s.Client.PostAsJsonAsync($"/api/charging-sessions/{id}/pay", new { amount = 0.01, balance = 999999 }); Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await response.Content.ReadFromJsonAsync<WalletOperationResponse>())!; }
}
public sealed class WalletTests
{
    [Fact]
    public async Task Owner_initialization_is_stable_and_driver_anonymous_are_denied()
    {
        await using var s = await WalletFixture.Create(); var a = (await WalletFixture.Balance(s))!; Assert.Equal(0, a.Balance); Assert.Equal(a.Id, (await WalletFixture.Balance(s))!.Id); Assert.True(a.IsDemo);
        s.As(s.DriverToken); Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.GetAsync("/api/wallet")).StatusCode); s.Client.DefaultRequestHeaders.Authorization = null; Assert.Equal(HttpStatusCode.Unauthorized, (await s.Client.GetAsync("/api/wallet")).StatusCode);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.001)]
    [InlineData(100000.01)]
    public async Task Invalid_amounts_are_rejected(decimal amount)
    { await using var s = await WalletFixture.Create(); Assert.Equal(HttpStatusCode.BadRequest, (await WalletFixture.TopUp(s, amount)).StatusCode); Assert.Equal(0, (await WalletFixture.Balance(s))!.Balance); }
    [Fact]
    public async Task Topup_success_failure_retry_conflict_history_and_isolation()
    {
        await using var s = await WalletFixture.Create(); var r = await WalletFixture.TopUp(s, key: "stable"); Assert.Equal(HttpStatusCode.OK, r.StatusCode); var success = (await r.Content.ReadFromJsonAsync<WalletOperationResponse>())!; Assert.Equal(1000, success.Balance);
        Assert.Equal(HttpStatusCode.OK, (await WalletFixture.TopUp(s, key: "stable")).StatusCode); Assert.Equal(1000, (await WalletFixture.Balance(s))!.Balance);
        Assert.Equal(HttpStatusCode.Conflict, (await WalletFixture.TopUp(s, 500, key: "stable")).StatusCode);
        var failed = (await (await WalletFixture.TopUp(s, 200, "Failure", "failed")).Content.ReadFromJsonAsync<WalletOperationResponse>())!; Assert.Equal("Failed", failed.Transaction.Status); Assert.Equal(1000, failed.Balance); await WalletFixture.TopUp(s, 200, "Failure", "failed");
        var history = (await s.Client.GetFromJsonAsync<WalletTransactionResponse[]>("/api/wallet/transactions?take=1"))!; Assert.Single(history); Assert.Equal(failed.Transaction.Id, history[0].Id);
        Assert.Single((await s.Client.GetFromJsonAsync<WalletTransactionResponse[]>($"/api/wallet/transactions?before={history[0].Id}&take=1"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Client.GetAsync("/api/wallet/transactions?take=101")).StatusCode);
        s.As(s.StrangerToken); Assert.Empty((await s.Client.GetFromJsonAsync<WalletTransactionResponse[]>("/api/wallet/transactions"))!); Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/wallet/transactions/{success.Transaction.Id}")).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await s.Client.PostAsync($"/api/wallet/transactions/{success.Transaction.Id}/refund", null)).StatusCode);
    }
    [Fact]
    public async Task Active_foreign_and_driver_payment_denied_and_insufficient_stays_unpaid()
    {
        await using var s = await WalletFixture.Create(); var active = await s.Start((await s.Book()).Id); Assert.Equal(HttpStatusCode.Conflict, (await s.Client.PostAsync($"/api/charging-sessions/{active.Id}/pay", null)).StatusCode);
        s.As(s.StrangerToken); Assert.Equal(HttpStatusCode.NotFound, (await s.Client.PostAsync($"/api/charging-sessions/{active.Id}/pay", null)).StatusCode); s.As(s.DriverToken); Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.PostAsync($"/api/charging-sessions/{active.Id}/pay", null)).StatusCode);
        s.As(s.OwnerToken); s.Clock.Advance(10); var done = await s.Stop(active.Id); Assert.Equal("Unpaid", done.PaymentStatus); Assert.Contains("Insufficient", done.SettlementNote);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client.PostAsync($"/api/charging-sessions/{active.Id}/pay", null)).StatusCode); Assert.Equal(0, (await WalletFixture.Balance(s))!.Balance); await s.Db(async db => Assert.Equal("Unpaid", (await db.ChargingSessions.FindAsync(active.Id))!.PaymentStatus));
    }
    [Fact]
    public async Task Manual_payment_uses_final_cost_once_and_full_refund_retains_original()
    {
        await using var s = await WalletFixture.Create(); var done = await WalletFixture.Completed(s); await WalletFixture.TopUp(s);
        var paid = await WalletFixture.Pay(s, done.Id); Assert.Equal(250, paid.Transaction.Amount); Assert.Equal(750, paid.Balance); Assert.Equal(paid.Transaction.Id, (await WalletFixture.Pay(s, done.Id)).Transaction.Id);
        var refund = await s.Client.PostAsync($"/api/wallet/transactions/{paid.Transaction.Id}/refund", null); Assert.Equal(HttpStatusCode.OK, refund.StatusCode); var returned = (await refund.Content.ReadFromJsonAsync<WalletOperationResponse>())!; Assert.Equal(1000, returned.Balance); Assert.Equal(paid.Transaction.Id, returned.Transaction.OriginalTransactionId);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client.PostAsync($"/api/wallet/transactions/{paid.Transaction.Id}/refund", null)).StatusCode); Assert.Equal(1000, (await WalletFixture.Pay(s, done.Id)).Balance);
        var original = (await s.Client.GetFromJsonAsync<WalletTransactionResponse>($"/api/wallet/transactions/{paid.Transaction.Id}"))!; Assert.Equal("Completed", original.Status); Assert.False(original.CanRefund); Assert.Equal(returned.Transaction.Reference, original.RefundReference);
        await s.Db(async db => { Assert.Equal(3, await db.WalletTransactions.CountAsync(x => x.Wallet.UserId == s.Owner)); var session = (await db.ChargingSessions.FindAsync(done.Id))!; Assert.Equal("Paid", session.PaymentStatus); Assert.Equal(paid.Transaction.Reference, session.PaymentReference); });
    }
    [Fact]
    public async Task Automatic_finalization_debits_owner_wallet_and_blocks_topup_refunds()
    {
        await using var s = await WalletFixture.Create(); var topup = (await (await WalletFixture.TopUp(s)).Content.ReadFromJsonAsync<WalletOperationResponse>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client.PostAsync($"/api/wallet/transactions/{topup.Transaction.Id}/refund", null)).StatusCode);
        s.As(s.DriverToken); var done = await WalletFixture.Completed(s); Assert.Equal("Paid", done.PaymentStatus); Assert.NotNull(done.PaymentReference); s.As(s.OwnerToken); Assert.Equal(750, (await WalletFixture.Balance(s))!.Balance); Assert.Equal(750, (await WalletFixture.Pay(s, done.Id)).Balance);
    }
    [Fact]
    public async Task Completed_ledger_cannot_be_edited_or_deleted_by_application_context()
    {
        await using var s = await WalletFixture.Create(); await WalletFixture.TopUp(s);
        await s.Db(async db => { var t = await db.WalletTransactions.SingleAsync(); t.Amount = 42; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); });
        await s.Db(async db => { db.Remove(await db.WalletTransactions.SingleAsync()); await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); });
    }
}
[Collection("SharedSqlServer")]
public sealed class WalletSqlServerTests
{
 [SharedSqlServerFact] public async Task Sql_automatic_finalization_and_wallet_roll_back_together_then_retry_pays_once()
 {
  var probe=new WalletFailureProbe();await using var s=await WalletFixture.Create(true,probe);await WalletFixture.TopUp(s);var active=await s.Start((await s.Book()).Id);s.Clock.Advance(10);probe.Fail=true;
  Assert.Equal(HttpStatusCode.InternalServerError,(await s.Client.PostAsync($"/api/charging-sessions/{active.Id}/stop",null)).StatusCode);probe.Fail=false;
  await s.Db(async db=>{var session=(await db.ChargingSessions.FindAsync(active.Id))!;Assert.Equal("Active",session.Status);Assert.Equal("Unpaid",session.PaymentStatus);Assert.Null(session.EndedAtUtc);Assert.Null(session.PaymentReference);Assert.Equal(0,session.EnergyKWh);Assert.Equal(40,(await db.Vehicles.FindAsync(s.Vehicle))!.BatteryPercent);Assert.Equal("Charging",(await db.Chargers.FindAsync(s.Charger))!.Status);Assert.Equal(1000,(await db.Wallets.SingleAsync(w=>w.UserId==s.Owner)).Balance);Assert.False(await db.WalletTransactions.AnyAsync(t=>t.ChargingSessionId==active.Id));});
  var completed=await s.Stop(active.Id);Assert.Equal("Paid",completed.PaymentStatus);Assert.Equal(250,completed.Cost);Assert.Equal(750,(await WalletFixture.Balance(s))!.Balance);await s.Stop(active.Id);Assert.Equal(750,(await WalletFixture.Balance(s))!.Balance);
 }

    [SharedSqlServerFact]
    public async Task Sql_initialization_and_topup_retries_credit_once_and_failures_remain_auditable()
    {
        await using var s = await WalletFixture.Create(true); var wallets = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => WalletFixture.Balance(s))); Assert.Single(wallets.Select(w => w!.Id).Distinct());
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => WalletFixture.TopUp(s, key: "sql-idempotent"))); Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode)); Assert.Equal(1000, (await WalletFixture.Balance(s))!.Balance);
        var failed = await WalletFixture.TopUp(s, 100, "Failure"); Assert.Equal("Failed", (await failed.Content.ReadFromJsonAsync<WalletOperationResponse>())!.Transaction.Status); Assert.Equal(1000, (await WalletFixture.Balance(s))!.Balance);
        await s.Db(async db => { Assert.Equal(1, await db.Wallets.CountAsync(w => w.UserId == s.Owner)); Assert.Equal(2, await db.WalletTransactions.CountAsync(t => t.Wallet.UserId == s.Owner)); Assert.Equal(8, (await db.Wallets.SingleAsync(w => w.UserId == s.Owner)).RowVersion.Length); });
    }
    [SharedSqlServerFact]
    public async Task Sql_settlement_races_debit_once_and_competing_bills_never_overdraw()
    {
        await using var s = await WalletFixture.Create(true); var first = await WalletFixture.Completed(s); var second = await WalletFixture.Completed(s, s.OtherVehicle, s.OtherCharger);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client.PostAsync($"/api/charging-sessions/{first.Id}/pay", null)).StatusCode);
        await WalletFixture.TopUp(s, 300);
        var results = await Task.WhenAll(s.Client.PostAsync($"/api/charging-sessions/{first.Id}/pay", null), s.Client.PostAsync($"/api/charging-sessions/{second.Id}/pay", null)); Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict); Assert.Equal(50, (await WalletFixture.Balance(s))!.Balance);
        var payment = (await s.Client.GetFromJsonAsync<WalletTransactionResponse[]>("/api/wallet/transactions"))!.Single(t => t.Type == "ChargingPayment"); var repeats = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => WalletFixture.Pay(s, payment.ChargingSessionId!.Value))); Assert.All(repeats, p => Assert.Equal(payment.Id, p.Transaction.Id)); Assert.Equal(50, (await WalletFixture.Balance(s))!.Balance);
        await s.Db(async db => { Assert.Equal(1, await db.ChargingSessions.CountAsync(x => x.PayerOwnerUserId == s.Owner && x.PaymentStatus == "Paid")); Assert.Equal(1, await db.WalletTransactions.CountAsync(t => t.Wallet.UserId == s.Owner && t.Type == "ChargingPayment")); });
    }
    [SharedSqlServerFact]
    public async Task Sql_refund_race_credits_once_keeps_payment_and_cannot_pay_again()
    {
        await using var s = await WalletFixture.Create(true); await WalletFixture.TopUp(s); var completed = await WalletFixture.Completed(s); Assert.Equal("Paid", completed.PaymentStatus); var p = await WalletFixture.Pay(s, completed.Id);
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => s.Client.PostAsync($"/api/wallet/transactions/{p.Transaction.Id}/refund", null))); Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK); Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict)); Assert.Equal(1000, (await WalletFixture.Balance(s))!.Balance); await WalletFixture.Pay(s, completed.Id); Assert.Equal(1000, (await WalletFixture.Balance(s))!.Balance);
        await s.Db(async db => { Assert.Equal(1, await db.WalletTransactions.CountAsync(x => x.OriginalTransactionId == p.Transaction.Id)); Assert.Equal("Completed", (await db.WalletTransactions.FindAsync(p.Transaction.Id))!.Status); });
    }
    [SharedSqlServerFact]
    public async Task Sql_after_write_failures_roll_back_topup_payment_and_refund_entirely()
    {
        var probe = new WalletFailureProbe(); await using var s = await WalletFixture.Create(true, probe); await WalletFixture.Balance(s);
        probe.Fail = true; Assert.Equal(HttpStatusCode.InternalServerError, (await WalletFixture.TopUp(s, key: "rollback-topup")).StatusCode); probe.Fail = false; Assert.Equal(0, (await WalletFixture.Balance(s))!.Balance);
        await s.Db(async db => Assert.Empty(await db.WalletTransactions.Where(t => t.Wallet.UserId == s.Owner).ToArrayAsync()));
        var done = await WalletFixture.Completed(s); await WalletFixture.TopUp(s, key: "rollback-topup"); probe.Fail = true; Assert.Equal(HttpStatusCode.InternalServerError, (await s.Client.PostAsync($"/api/charging-sessions/{done.Id}/pay", null)).StatusCode); probe.Fail = false;
        Assert.Equal(1000, (await WalletFixture.Balance(s))!.Balance); await s.Db(async db => { Assert.Equal("Unpaid", (await db.ChargingSessions.FindAsync(done.Id))!.PaymentStatus); Assert.False(await db.WalletTransactions.AnyAsync(t => t.ChargingSessionId == done.Id)); });
        var paid = await WalletFixture.Pay(s, done.Id); probe.Fail = true; Assert.Equal(HttpStatusCode.InternalServerError, (await s.Client.PostAsync($"/api/wallet/transactions/{paid.Transaction.Id}/refund", null)).StatusCode); probe.Fail = false;
        Assert.Equal(750, (await WalletFixture.Balance(s))!.Balance); await s.Db(async db => Assert.False(await db.WalletTransactions.AnyAsync(t => t.OriginalTransactionId == paid.Transaction.Id)));
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsync($"/api/wallet/transactions/{paid.Transaction.Id}/refund", null)).StatusCode); Assert.Equal(1000, (await WalletFixture.Balance(s))!.Balance);
    }
    [SharedSqlServerFact]
    public async Task Sql_constraints_reject_duplicate_payment_and_refund_and_new_host_reads_persistence()
    {
        await using var s = await WalletFixture.Create(true); await WalletFixture.TopUp(s); var done = await WalletFixture.Completed(s); var paid = await WalletFixture.Pay(s, done.Id);
        await s.Db(async db => { db.WalletTransactions.Add(new() { WalletId = (await db.Wallets.SingleAsync(w => w.UserId == s.Owner)).Id, Type = "ChargingPayment", Status = "Completed", Amount = 250, ChargingSessionId = done.Id, IdempotencyKey = Guid.NewGuid().ToString("N"), CreatedAtUtc = DateTime.UtcNow, CompletedAtUtc = DateTime.UtcNow }); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); });
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsync($"/api/wallet/transactions/{paid.Transaction.Id}/refund", null)).StatusCode);
        await s.Db(async db => { db.WalletTransactions.Add(new() { WalletId = (await db.Wallets.SingleAsync(w => w.UserId == s.Owner)).Id, Type = "Refund", Status = "Completed", Amount = 250, ChargingSessionId = done.Id, OriginalTransactionId = paid.Transaction.Id, IdempotencyKey = Guid.NewGuid().ToString("N"), CreatedAtUtc = DateTime.UtcNow, CompletedAtUtc = DateTime.UtcNow }); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); });
        await using var restarted = new SharedSqlServerFactory(); using var client = restarted.CreateClient(); using var scope = restarted.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EnergyDbContext>(); Assert.Equal(1000, (await db.Wallets.SingleAsync(w => w.UserId == s.Owner)).Balance); Assert.Equal(3, await db.WalletTransactions.CountAsync(t => t.Wallet.UserId == s.Owner)); Assert.Equal("Paid", (await db.ChargingSessions.FindAsync(done.Id))!.PaymentStatus);
    }
}
