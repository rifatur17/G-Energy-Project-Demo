using System.Net;
using System.Net.Http.Json;
using System.Text;
using GEnergy.Api.DTOs;
using GEnergy.Api.Entities;
using GEnergy.Api.Services.Billing;
using GEnergy.Api.Services.Charging;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;
namespace GEnergy.IntegrationTests;

// Confirms whatever the test says, so tests can check that G Energy compares tran_id/amount itself.
public sealed class StubGateway : IPaymentGateway
{
    public decimal? AmountOverride;
    public string Name => "SSLCommerz";
    public Task<GatewayInitResult> InitiateAsync(GatewayInitRequest r, CancellationToken ct) => Task.FromResult(new GatewayInitResult(true, "https://sandbox.sslcommerz.com/EasyCheckOut/" + r.TranId, null));
    public Task<GatewayValidation> ValidateAsync(string valId, CancellationToken ct)
    {
        var parts = valId.Split(':'); // "<tranId>:<amount>"
        return Task.FromResult(new GatewayValidation(true, parts[0], AmountOverride ?? decimal.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), "BDT", "BANK-1", "BKASH-BKash", null));
    }
}
public static class BillingFixture
{
    public static Task<ChargingScenario> Create(Action<IServiceCollection>? extra = null) => ChargingScenario.Create(configure: services =>
    {
        services.RemoveAll<IChargingSettlement>(); services.AddScoped<IChargingSettlement, PostpaidChargingSettlement>();
        extra?.Invoke(services);
    }, label: "Billing");
    public static async Task<SessionResponse> Completed(ChargingScenario s, long? vehicle = null, long? charger = null)
    { var active = await s.Start((await s.Book(vehicle, charger)).Id); s.Clock.Advance(10); return await s.Stop(active.Id); }
    public static async Task<StatementResponse> Close(ChargingScenario s)
    { var r = await s.Client.PostAsync("/api/billing/statements/close", null); Assert.Equal(HttpStatusCode.OK, r.StatusCode); return (await r.Content.ReadFromJsonAsync<StatementResponse>())!; }
    public static async Task<CheckoutResponse> Checkout(ChargingScenario s, long id)
    { var r = await s.Client.PostAsync($"/api/billing/statements/{id}/checkout", null); Assert.Equal(HttpStatusCode.OK, r.StatusCode); return (await r.Content.ReadFromJsonAsync<CheckoutResponse>())!; }
    public static HttpClient Browser(ChargingScenario s) => s.App.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    public static Task<StatementDetailResponse?> Detail(ChargingScenario s, long id) => s.Client.GetFromJsonAsync<StatementDetailResponse>($"/api/billing/statements/{id}");
}
public sealed class BillingTests
{
    [Fact]
    public async Task Completed_charging_is_postpaid_and_one_statement_collects_it()
    {
        await using var s = await BillingFixture.Create();
        var done = await BillingFixture.Completed(s);
        Assert.Equal("Unpaid", done.PaymentStatus); Assert.Equal(PostpaidChargingSettlement.Note, done.SettlementNote); Assert.Equal(250m, done.Cost);
        var summary = (await s.Client.GetFromJsonAsync<BillingSummaryResponse>("/api/billing/summary"))!;
        Assert.Equal(250m, summary.UnbilledAmount); Assert.Equal(1, summary.UnbilledSessions); Assert.Equal(0m, summary.OutstandingAmount);
        Assert.Single((await s.Client.GetFromJsonAsync<List<UnbilledSessionResponse>>("/api/billing/unbilled"))!);

        var statement = await BillingFixture.Close(s);
        Assert.Equal(250m, statement.Total); Assert.Equal(1, statement.SessionCount); Assert.Equal("Due", statement.Status);
        Assert.Equal(7, (statement.DueAtUtc - statement.IssuedAtUtc).TotalDays);
        // Repeating the month close with nothing new issues nothing (PRD BILL-02).
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client.PostAsync("/api/billing/statements/close", null)).StatusCode);
        summary = (await s.Client.GetFromJsonAsync<BillingSummaryResponse>("/api/billing/summary"))!;
        Assert.Equal(0m, summary.UnbilledAmount); Assert.Equal(250m, summary.OutstandingAmount);
        Assert.Single((await BillingFixture.Detail(s, statement.Id))!.Lines);
        // A session billed on a statement can no longer be paid from the legacy wallet.
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client.PostAsync($"/api/charging-sessions/{done.Id}/pay", null)).StatusCode);
    }
    [Fact]
    public async Task Owner_plan_fee_applies_after_three_free_months_once_per_month()
    {
        await using var s = await BillingFixture.Create();
        await s.Db(async db => { foreach (var v in db.Vehicles.Where(v => v.OwnerUserId == s.Owner)) v.CreatedAtUtc = s.Clock.GetUtcNow().UtcDateTime.AddMonths(-4); await db.SaveChangesAsync(); });
        var first = await BillingFixture.Close(s);
        Assert.Equal(400m, first.PlanFee); Assert.Equal(2, first.PlanVehicleCount); Assert.Equal(400m, first.Total); Assert.Equal(0, first.SessionCount);
        await BillingFixture.Completed(s);
        var second = await BillingFixture.Close(s);
        Assert.Equal(0m, second.PlanFee); Assert.Equal(250m, second.Total); Assert.NotEqual(first.StatementNumber, second.StatementNumber);
    }
    [Fact]
    public async Task Demo_checkout_says_service_unavailable_and_charges_nothing()
    {
        await using var s = await BillingFixture.Create();
        var done = await BillingFixture.Completed(s); var statement = await BillingFixture.Close(s);
        var checkout = await BillingFixture.Checkout(s, statement.Id);
        Assert.Equal("SSLCommerzDemo", checkout.Gateway); Assert.Equal(250m, checkout.Amount);
        var browser = BillingFixture.Browser(s); var path = new Uri(checkout.RedirectUrl).PathAndQuery;
        var page = await browser.GetStringAsync(path);
        Assert.Contains("250.00", page); Assert.Contains("bKash", page); Assert.Contains("Nagad", page); Assert.Contains("VISA", page);

        foreach (var method in new[] { "BKASH-BKash", "VISA-Dutch Bangla" })
        {
            var attempt = method == "BKASH-BKash" ? checkout : await BillingFixture.Checkout(s, statement.Id);
            var submit = await browser.PostAsync(new Uri(attempt.RedirectUrl).PathAndQuery, new FormUrlEncodedContent(new Dictionary<string, string> { ["action"] = "pay", ["method"] = method }));
            Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
            var html = await submit.Content.ReadAsStringAsync();
            Assert.Contains("This service is not available at the moment", html); Assert.Contains("Nothing was charged", html);
            Assert.Contains($"http://localhost:5173/billing?payment=unavailable&amp;statement={statement.Id}", html);
        }
        // A demo validation can never confirm a payment, so a success callback cannot mark it paid either.
        Assert.Contains("payment=unconfirmed", (await browser.GetAsync($"/api/payments/sslcommerz/success?tran_id={checkout.TranId}&val_id=anything")).Headers.Location!.ToString());

        var detail = (await BillingFixture.Detail(s, statement.Id))!;
        Assert.Equal("Due", detail.Statement.Status); Assert.Null(detail.Statement.PaymentReference);
        Assert.All(detail.Payments, p => { Assert.Equal("Failed", p.Status); Assert.Contains("not available", p.FailureReason); });
        Assert.Equal(["VISA-Dutch Bangla", "BKASH-BKash"], detail.Payments.Select(p => p.Method ?? "").ToArray());
        Assert.Equal("Unpaid", (await s.Progress(done.Id))!.PaymentStatus);
        Assert.Equal(250m, (await s.Client.GetFromJsonAsync<BillingSummaryResponse>("/api/billing/summary"))!.OutstandingAmount);
        Assert.Contains("already", await browser.GetStringAsync(path));
    }
    [Fact]
    public async Task Forged_failed_and_cancelled_callbacks_never_mark_a_statement_paid()
    {
        await using var s = await BillingFixture.Create();
        await BillingFixture.Completed(s); var statement = await BillingFixture.Close(s); var browser = BillingFixture.Browser(s);
        var forged = await BillingFixture.Checkout(s, statement.Id);
        var r = await browser.PostAsync("/api/payments/sslcommerz/success", new FormUrlEncodedContent(new Dictionary<string, string> { ["tran_id"] = forged.TranId, ["val_id"] = "DEMO-VAL-FORGED", ["status"] = "VALID", ["amount"] = "250.00" }));
        Assert.Contains("payment=unconfirmed", r.Headers.Location!.ToString());
        Assert.Contains("payment=unconfirmed", (await browser.GetAsync("/api/payments/sslcommerz/success?tran_id=GEUNKNOWN&val_id=x")).Headers.Location!.ToString());

        var failed = await BillingFixture.Checkout(s, statement.Id);
        var fail = await browser.PostAsync(new Uri(failed.RedirectUrl).PathAndQuery, new FormUrlEncodedContent(new Dictionary<string, string> { ["action"] = "pay" })); // no method chosen
        Assert.Contains("payment=failed", (await browser.GetAsync(fail.Headers.Location!.PathAndQuery)).Headers.Location!.ToString());
        var cancelled = await BillingFixture.Checkout(s, statement.Id);
        Assert.Contains("payment=cancelled", (await browser.PostAsync("/api/payments/sslcommerz/cancel", new FormUrlEncodedContent(new Dictionary<string, string> { ["tran_id"] = cancelled.TranId }))).Headers.Location!.ToString());

        var detail = (await BillingFixture.Detail(s, statement.Id))!;
        Assert.Equal("Due", detail.Statement.Status);
        Assert.Equal(["Cancelled", "Failed", "Initiated"], detail.Payments.Select(p => p.Status).ToArray());
        // A cancelled checkout cannot later be approved on the demo page.
        Assert.Contains("already", await browser.GetStringAsync(new Uri(cancelled.RedirectUrl).PathAndQuery));
    }
    [Fact]
    public async Task Gateway_confirmation_must_match_amount_and_transaction()
    {
        var stub = new StubGateway();
        await using var s = await BillingFixture.Create(services => { services.RemoveAll<IPaymentGateway>(); services.AddSingleton<IPaymentGateway>(stub); });
        await BillingFixture.Completed(s); var statement = await BillingFixture.Close(s); var browser = BillingFixture.Browser(s);
        var first = await BillingFixture.Checkout(s, statement.Id);
        Assert.StartsWith("https://sandbox.sslcommerz.com/", first.RedirectUrl);
        stub.AmountOverride = 1m; // provider says a different amount was paid
        Assert.Contains("payment=unconfirmed", (await browser.GetAsync($"/api/payments/sslcommerz/success?tran_id={first.TranId}&val_id={first.TranId}:250")).Headers.Location!.ToString());
        stub.AmountOverride = null;
        var other = await BillingFixture.Checkout(s, statement.Id); // provider confirms someone else's transaction
        Assert.Contains("payment=unconfirmed", (await browser.GetAsync($"/api/payments/sslcommerz/success?tran_id={other.TranId}&val_id={first.TranId}:250")).Headers.Location!.ToString());
        var good = await BillingFixture.Checkout(s, statement.Id);
        Assert.Contains("payment=paid", (await browser.GetAsync($"/api/payments/sslcommerz/success?tran_id={good.TranId}&val_id={good.TranId}:250.00")).Headers.Location!.ToString());
        // IPN for an already paid statement from a second checkout is recorded, never applied twice.
        await s.Db(async db => { db.StatementPayments.Add(new StatementPayment { StatementId = statement.Id, OwnerUserId = s.Owner, TranId = "GELATE", Gateway = "SSLCommerz", Amount = 250m, CreatedAtUtc = DateTime.UtcNow }); await db.SaveChangesAsync(); });
        var ipn = await browser.PostAsync("/api/payments/sslcommerz/ipn", new FormUrlEncodedContent(new Dictionary<string, string> { ["tran_id"] = "GELATE", ["val_id"] = "GELATE:250", ["status"] = "VALID" }));
        Assert.Equal(HttpStatusCode.OK, ipn.StatusCode);
        var detail = (await BillingFixture.Detail(s, statement.Id))!;
        Assert.Single(detail.Payments, p => p.Status == "Paid"); Assert.Equal(good.TranId, detail.Statement.PaymentReference);
        Assert.Contains(detail.Payments, p => p.TranId == "GELATE" && p.Status == "Failed" && p.FailureReason!.Contains("already paid"));
    }
    [Fact]
    public async Task Statements_are_private_to_their_owner()
    {
        await using var s = await BillingFixture.Create();
        await BillingFixture.Completed(s); var statement = await BillingFixture.Close(s);
        s.As(s.StrangerToken);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/billing/statements/{statement.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.PostAsync($"/api/billing/statements/{statement.Id}/checkout", null)).StatusCode);
        Assert.Empty((await s.Client.GetFromJsonAsync<List<StatementResponse>>("/api/billing/statements"))!);
        s.As(s.DriverToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.GetAsync("/api/billing/summary")).StatusCode);
        s.As("");
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Client.GetAsync("/api/billing/summary")).StatusCode);
    }
}
public sealed class SslCommerzGatewayTests
{
    private sealed class Handler(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    {
        public readonly List<(HttpRequestMessage Request, string Body)> Seen = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respond(request), Encoding.UTF8, "application/json") };
        }
    }
    private static SslCommerzGateway Gateway(Handler h) => new(new HttpClient(h), Options.Create(new SslCommerzOptions { StoreId = "teststore", StorePassword = "testpass", Sandbox = true }));
    private static GatewayInitRequest Init => new("GE123", 250m, "GE-ST-1", new("Owner", "owner@example.test", "01700000000"), "http://localhost:5080/s", "http://localhost:5080/f", "http://localhost:5080/c", "http://localhost:5080/i");

    [Fact]
    public async Task Initiates_hosted_checkout_against_the_sandbox()
    {
        var h = new Handler(_ => """{"status":"SUCCESS","GatewayPageURL":"https://sandbox.sslcommerz.com/EasyCheckOut/testcde"}""");
        var result = await Gateway(h).InitiateAsync(Init, default);
        Assert.True(result.Ok); Assert.Equal("https://sandbox.sslcommerz.com/EasyCheckOut/testcde", result.RedirectUrl);
        var (request, body) = Assert.Single(h.Seen);
        Assert.Equal("https://sandbox.sslcommerz.com/gwprocess/v4/api.php", request.RequestUri!.ToString());
        Assert.Contains("total_amount=250.00", body); Assert.Contains("currency=BDT", body); Assert.Contains("tran_id=GE123", body); Assert.Contains("store_id=teststore", body);
    }
    [Fact]
    public async Task Reports_gateway_refusals_and_validates_payments()
    {
        var refused = await Gateway(new Handler(_ => """{"status":"FAILED","failedreason":"Store Credential Error Or Store is De-active"}""")).InitiateAsync(Init, default);
        Assert.False(refused.Ok); Assert.Contains("Store Credential Error", refused.Error);
        var h = new Handler(_ => """{"status":"VALID","tran_id":"GE123","amount":"250.00","currency":"BDT","bank_tran_id":"B1","card_type":"NAGAD-Nagad"}""");
        var valid = await Gateway(h).ValidateAsync("val 1", default);
        Assert.True(valid.Valid); Assert.Equal(250m, valid.Amount); Assert.Equal("GE123", valid.TranId); Assert.Equal("NAGAD-Nagad", valid.CardType);
        Assert.Contains("val_id=val%201", h.Seen[0].Request.RequestUri!.AbsoluteUri);
        Assert.False((await Gateway(new Handler(_ => """{"status":"INVALID_TRANSACTION"}""")).ValidateAsync("x", default)).Valid);
    }
}
