using System.Net;
using GEnergy.Api.Services.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
namespace GEnergy.Api.Controllers;

// SSLCommerz posts the customer's browser back here. These endpoints are anonymous by design:
// nothing in the callback is trusted until the payment is confirmed with the gateway.
// tran_id "GW…" = wallet top-up (returns to /wallet); anything else = monthly statement (/billing).
[ApiController, AllowAnonymous, ApiExplorerSettings(IgnoreApi = true)]
public sealed class PaymentsController(BillingService billing, WalletTopUpService topUps, IOptions<SslCommerzOptions> ssl, IPaymentGateway gateway) : ControllerBase
{
    private string Field(string name) => (Request.HasFormContentType ? Request.Form[name].ToString() : "") is { Length: > 0 } f ? f : Request.Query[name].ToString();
    private static bool IsTopUp(string? tranId) => tranId is not null && tranId.StartsWith(WalletTopUpService.Prefix, StringComparison.Ordinal);
    private string Frontend => ssl.Value.FrontendBaseUrl.TrimEnd('/');
    private string WalletUrl(string result) => $"{Frontend}/wallet?topup={result}";
    private string BillingUrl(string result, long? statement) => $"{Frontend}/billing?payment={result}{(statement is null ? "" : $"&statement={statement}")}";

    [HttpPost("api/payments/sslcommerz/success"), HttpGet("api/payments/sslcommerz/success")]
    public async Task<IActionResult> Success(CancellationToken ct)
    {
        var tranId = Field("tran_id");
        if (IsTopUp(tranId)) { var t = await topUps.ConfirmSuccess(tranId, Field("val_id"), ct); return Redirect(WalletUrl(t.Paid ? "paid" : "unconfirmed")); }
        var outcome = await billing.ConfirmSuccess(tranId, Field("val_id"), ct);
        return Redirect(BillingUrl(outcome.Paid ? "paid" : "unconfirmed", outcome.StatementId));
    }
    [HttpPost("api/payments/sslcommerz/fail"), HttpGet("api/payments/sslcommerz/fail")]
    public async Task<IActionResult> Fail(CancellationToken ct)
    {
        var tranId = Field("tran_id"); var error = Field("error"); var reason = string.IsNullOrWhiteSpace(error) ? "Payment failed at the gateway." : error;
        if (IsTopUp(tranId)) { await topUps.MarkUnsuccessful(tranId, "Failed", reason, ct); return Redirect(WalletUrl("failed")); }
        var outcome = await billing.MarkUnsuccessful(tranId, "Failed", reason, ct);
        return Redirect(BillingUrl("failed", outcome.StatementId));
    }
    [HttpPost("api/payments/sslcommerz/cancel"), HttpGet("api/payments/sslcommerz/cancel")]
    public async Task<IActionResult> Cancel(CancellationToken ct)
    {
        var tranId = Field("tran_id");
        if (IsTopUp(tranId)) { await topUps.MarkUnsuccessful(tranId, "Cancelled", "Customer cancelled the checkout.", ct); return Redirect(WalletUrl("cancelled")); }
        var outcome = await billing.MarkUnsuccessful(tranId, "Cancelled", "Customer cancelled the checkout.", ct);
        return Redirect(BillingUrl("cancelled", outcome.StatementId));
    }
    // Server-to-server notification. Reachable only when the API has a public URL.
    [HttpPost("api/payments/sslcommerz/ipn")]
    public async Task<IActionResult> Ipn(CancellationToken ct)
    {
        var status = Field("status"); var tranId = Field("tran_id");
        if (status is "VALID" or "VALIDATED")
            return Ok(IsTopUp(tranId) ? (await topUps.ConfirmSuccess(tranId, Field("val_id"), ct)).Message : (await billing.ConfirmSuccess(tranId, Field("val_id"), ct)).Message);
        if (status is "FAILED" or "CANCELLED" or "UNATTEMPTED" or "EXPIRED")
        {
            var mapped = status == "CANCELLED" ? "Cancelled" : "Failed"; var reason = "Gateway reported " + status + ".";
            if (IsTopUp(tranId)) await topUps.MarkUnsuccessful(tranId, mapped, reason, ct); else await billing.MarkUnsuccessful(tranId, mapped, reason, ct);
        }
        return Ok("Received.");
    }

    // ---- Built-in demo checkout (only when no SSLCommerz store is configured) ----
    private static readonly (string Value, string Label, string Group)[] Methods =
    [
        ("BKASH-BKash", "bKash", "Mobile banking"), ("NAGAD-Nagad", "Nagad", "Mobile banking"), ("DBBLMOBILEB-Dbbl Mobile Banking", "Rocket", "Mobile banking"),
        ("VISA-Dutch Bangla", "VISA", "Cards"), ("MASTER-City Bank", "Mastercard", "Cards"), ("AMEX-City Bank", "American Express", "Cards"),
        ("CITYTOUCH-City Bank", "City Touch", "Internet banking")
    ];
    private sealed record DemoCheckout(string TranId, decimal Amount, string Purpose, string Reference, string Status, string ReturnUrl);
    private async Task<DemoCheckout?> LoadDemo(string tranId, CancellationToken ct)
    {
        if (IsTopUp(tranId))
        {
            var t = await topUps.Find(tranId, ct);
            return t is null || t.Gateway != "SSLCommerzDemo" ? null : new(t.TranId, t.Amount, "Wallet top-up", t.Owner.UserCode, t.Status, WalletUrl(t.Status == "Initiated" ? "cancelled" : "unavailable"));
        }
        var p = await billing.DemoPayment(tranId, ct);
        return p is null ? null : new(p.TranId, p.Amount, "Statement", p.Statement.StatementNumber, p.Status, BillingUrl("failed", p.StatementId));
    }
    private static string Page(string body) => $$"""
<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>SSLCommerz demo checkout</title>
<style>
body{margin:0;font-family:"Segoe UI",Arial,sans-serif;background:#eef2f6;color:#1d2a35}
.top{background:#fff;border-bottom:1px solid #dde3ea;padding:14px 20px;display:flex;justify-content:space-between;align-items:center;gap:12px}
.logo{font-weight:700;font-size:17px;color:#1d2a35}.tag{font-size:11px;background:#fff4d6;color:#7a5600;border:1px solid #f1d38a;border-radius:4px;padding:3px 8px;white-space:nowrap}
main{max-width:560px;margin:24px auto;padding:0 16px}.card{background:#fff;border-radius:10px;box-shadow:0 2px 10px #0001;padding:20px}
.row{display:flex;justify-content:space-between;margin:4px 0;font-size:14px;gap:12px}.amt{font-size:26px;font-weight:700;margin:6px 0 2px}
fieldset{border:0;padding:0;margin:16px 0 0}legend{font-size:12px;font-weight:700;color:#5b6b78;text-transform:uppercase;letter-spacing:.5px;margin-bottom:8px}
.m{display:inline-flex;margin:0 8px 8px 0}.m input{position:absolute;opacity:0}.m span{border:1px solid #cfd8e1;border-radius:8px;padding:10px 14px;font-weight:600;cursor:pointer;min-width:90px;text-align:center}
.m input:checked+span{border-color:#0b5394;background:#e8f0fb;color:#0b5394}.m input:focus-visible+span{outline:3px solid #0b5394}
.btns{display:flex;gap:10px;margin-top:18px;flex-wrap:wrap}button,.btn{border:0;border-radius:8px;padding:12px 18px;font-weight:700;cursor:pointer;font-size:15px;text-decoration:none;display:inline-block;text-align:center}
.pay{background:#0b8f4d;color:#fff;flex:1}.cancel{background:#eef2f6;color:#334}
.unavailable{border:1px solid #f1c27d;background:#fff8ec;border-radius:10px;padding:16px;margin:14px 0}
.unavailable h1{font-size:19px;margin:0 0 6px;color:#8a4b00}.unavailable p{margin:4px 0;font-size:14px;line-height:1.6}
.note{font-size:12px;color:#5b6b78;margin-top:14px;line-height:1.5}
</style></head><body>
<div class="top"><span class="logo">Demo checkout · stands in for SSLCommerz</span><span class="tag">DEMO · no real money</span></div>
<main><div class="card">{{body}}</div></main></body></html>
""";
    private static string Summary(DemoCheckout d) =>
        $"<div class=\"row\"><span>Merchant</span><strong>G Energy</strong></div><div class=\"row\"><span>{WebUtility.HtmlEncode(d.Purpose)}</span><strong>{WebUtility.HtmlEncode(d.Reference)}</strong></div>" +
        $"<div class=\"row\"><span>Transaction</span><span>{WebUtility.HtmlEncode(d.TranId)}</span></div><div class=\"amt\">৳ {d.Amount:0.00} BDT</div>";

    [HttpGet("api/payments/sslcommerz-demo/{tranId}")]
    public async Task<IActionResult> DemoPage(string tranId, CancellationToken ct)
    {
        if (gateway is not DemoSslCommerzGateway) return NotFound();
        var d = await LoadDemo(tranId, ct);
        if (d is null) return NotFound();
        var e = (string s) => WebUtility.HtmlEncode(s);
        var options = string.Join("", Methods.GroupBy(m => m.Group).Select(g => $"<fieldset><legend>{e(g.Key)}</legend>{string.Join("", g.Select((m, i) => $"<label class=\"m\"><input type=\"radio\" name=\"method\" value=\"{e(m.Value)}\"{(g.Key == "Mobile banking" && i == 0 ? " checked" : "")}><span>{e(m.Label)}</span></label>"))}</fieldset>"));
        var body = Summary(d) + (d.Status != "Initiated"
            ? $"<p>This checkout is already <strong>{e(d.Status)}</strong>.</p><p><a class=\"btn cancel\" href=\"{e(d.ReturnUrl)}\">Return to G Energy</a></p>"
            : $"<form method=\"post\" action=\"/api/payments/sslcommerz-demo/{e(d.TranId)}\">{options}<div class=\"btns\"><button class=\"pay\" name=\"action\" value=\"pay\">Pay ৳ {d.Amount:0.00}</button><button class=\"cancel\" name=\"action\" value=\"cancel\">Cancel</button></div></form>")
            + "<p class=\"note\">This page stands in for the SSLCommerz hosted checkout while no SSLCommerz store is configured. Real payment is not connected yet, so no method can be charged here. Add SSLCommerz sandbox credentials (scripts\\Set-SslCommerzSandbox.ps1) to open the real SSLCommerz sandbox page instead.</p>";
        return Content(Page(body), "text/html; charset=utf-8");
    }
    [HttpPost("api/payments/sslcommerz-demo/{tranId}")]
    public async Task<IActionResult> DemoSubmit(string tranId, CancellationToken ct)
    {
        if (gateway is not DemoSslCommerzGateway) return NotFound();
        var api = ssl.Value.PublicApiBaseUrl.TrimEnd('/') + "/api/payments/sslcommerz";
        var action = Field("action"); var method = Field("method");
        if (action == "cancel") return Redirect($"{api}/cancel?tran_id={Uri.EscapeDataString(tranId)}");
        if (action != "pay" || !Methods.Any(m => m.Value == method)) return Redirect($"{api}/fail?tran_id={Uri.EscapeDataString(tranId)}&error={Uri.EscapeDataString("No payment method was chosen.")}");
        // No real payment is connected in demo mode: every method reports that the service is unavailable.
        DemoCheckout? d;
        if (IsTopUp(tranId)) { var t = await topUps.DemoUnavailable(tranId, method, ct); d = t is null ? null : new(t.TranId, t.Amount, "Wallet top-up", t.Owner.UserCode, t.Status, WalletUrl("unavailable")); }
        else { var p = await billing.DemoUnavailable(tranId, method, ct); d = p is null ? null : new(p.TranId, p.Amount, "Statement", p.Statement.StatementNumber, p.Status, BillingUrl("unavailable", p.StatementId)); }
        if (d is null) return NotFound();
        var label = Methods.First(m => m.Value == method).Label;
        var body = Summary(d) + $"<div class=\"unavailable\" role=\"alert\"><h1>This service is not available at the moment</h1><p>{WebUtility.HtmlEncode(label)} payment cannot be completed right now. Nothing was charged.</p><p lang=\"bn\">এই মুহূর্তে সেবাটি পাওয়া যাচ্ছে না। কোনো টাকা কাটা হয়নি।</p></div>" +
            $"<div class=\"btns\"><a class=\"btn pay\" href=\"{WebUtility.HtmlEncode(d.ReturnUrl)}\">Return to G Energy</a></div>";
        return Content(Page(body), "text/html; charset=utf-8");
    }
}
