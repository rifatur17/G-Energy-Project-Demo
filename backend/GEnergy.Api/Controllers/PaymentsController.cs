using System.Net;
using GEnergy.Api.Services.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
namespace GEnergy.Api.Controllers;

// SSLCommerz posts the customer's browser back here. These endpoints are anonymous by design:
// nothing in the callback is trusted until BillingService confirms it with the gateway.
[ApiController, AllowAnonymous, ApiExplorerSettings(IgnoreApi = true)]
public sealed class PaymentsController(BillingService billing, IOptions<SslCommerzOptions> ssl, IPaymentGateway gateway) : ControllerBase
{
    private string Field(string name) => (Request.HasFormContentType ? Request.Form[name].ToString() : "") is { Length: > 0 } f ? f : Request.Query[name].ToString();
    private RedirectResult Back(string result, long? statement) =>
        Redirect($"{ssl.Value.FrontendBaseUrl.TrimEnd('/')}/billing?payment={result}{(statement is null ? "" : $"&statement={statement}")}");

    [HttpPost("api/payments/sslcommerz/success"), HttpGet("api/payments/sslcommerz/success")]
    public async Task<IActionResult> Success(CancellationToken ct)
    {
        var outcome = await billing.ConfirmSuccess(Field("tran_id"), Field("val_id"), ct);
        return Back(outcome.Paid ? "paid" : "unconfirmed", outcome.StatementId);
    }
    [HttpPost("api/payments/sslcommerz/fail"), HttpGet("api/payments/sslcommerz/fail")]
    public async Task<IActionResult> Fail(CancellationToken ct)
    {
        var reason = Field("error"); var outcome = await billing.MarkUnsuccessful(Field("tran_id"), "Failed", string.IsNullOrWhiteSpace(reason) ? "Payment failed at the gateway." : reason, ct);
        return Back("failed", outcome.StatementId);
    }
    [HttpPost("api/payments/sslcommerz/cancel"), HttpGet("api/payments/sslcommerz/cancel")]
    public async Task<IActionResult> Cancel(CancellationToken ct)
    {
        var outcome = await billing.MarkUnsuccessful(Field("tran_id"), "Cancelled", "Customer cancelled the checkout.", ct);
        return Back("cancelled", outcome.StatementId);
    }
    // Server-to-server notification. Reachable only when the API has a public URL.
    [HttpPost("api/payments/sslcommerz/ipn")]
    public async Task<IActionResult> Ipn(CancellationToken ct)
    {
        var status = Field("status");
        if (status is "VALID" or "VALIDATED") { var o = await billing.ConfirmSuccess(Field("tran_id"), Field("val_id"), ct); return Ok(o.Message); }
        if (status is "FAILED" or "CANCELLED" or "UNATTEMPTED" or "EXPIRED") await billing.MarkUnsuccessful(Field("tran_id"), status == "CANCELLED" ? "Cancelled" : "Failed", "Gateway reported " + status + ".", ct);
        return Ok("Received.");
    }

    // ---- Built-in SSLCommerz demo checkout (only when no SSLCommerz store is configured) ----
    private static readonly (string Value, string Label, string Group)[] Methods =
    [
        ("BKASH-BKash", "bKash", "Mobile banking"), ("NAGAD-Nagad", "Nagad", "Mobile banking"), ("DBBLMOBILEB-Dbbl Mobile Banking", "Rocket", "Mobile banking"),
        ("VISA-Dutch Bangla", "VISA", "Cards"), ("MASTER-City Bank", "Mastercard", "Cards"), ("AMEX-City Bank", "American Express", "Cards"),
        ("CITYTOUCH-City Bank", "City Touch", "Internet banking")
    ];
    [HttpGet("api/payments/sslcommerz-demo/{tranId}")]
    public async Task<IActionResult> DemoPage(string tranId, CancellationToken ct)
    {
        if (gateway is not DemoSslCommerzGateway) return NotFound();
        var p = await billing.DemoPayment(tranId, ct);
        if (p is null) return NotFound();
        var e = (string s) => WebUtility.HtmlEncode(s);
        var options = string.Join("", Methods.GroupBy(m => m.Group).Select(g => $"<fieldset><legend>{e(g.Key)}</legend>{string.Join("", g.Select((m, i) => $"<label class=\"m\"><input type=\"radio\" name=\"method\" value=\"{e(m.Value)}\"{(g.Key == "Mobile banking" && i == 0 ? " checked" : "")}><span>{e(m.Label)}</span></label>"))}</fieldset>"));
        var done = p.Status != "Initiated";
        var html = $$"""
<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>SSLCommerz demo checkout</title>
<style>
body{margin:0;font-family:"Segoe UI",Arial,sans-serif;background:#eef2f6;color:#1d2a35}
.top{background:#fff;border-bottom:1px solid #dde3ea;padding:14px 20px;display:flex;justify-content:space-between;align-items:center}
.logo{font-weight:700;font-size:17px;color:#1d2a35}.tag{font-size:11px;background:#fff4d6;color:#7a5600;border:1px solid #f1d38a;border-radius:4px;padding:3px 8px}
main{max-width:560px;margin:24px auto;padding:0 16px}.card{background:#fff;border-radius:10px;box-shadow:0 2px 10px #0001;padding:20px}
.row{display:flex;justify-content:space-between;margin:4px 0;font-size:14px}.amt{font-size:26px;font-weight:700;margin:6px 0 2px}
fieldset{border:0;padding:0;margin:16px 0 0}legend{font-size:12px;font-weight:700;color:#5b6b78;text-transform:uppercase;letter-spacing:.5px;margin-bottom:8px}
.m{display:inline-flex;margin:0 8px 8px 0}.m input{position:absolute;opacity:0}.m span{border:1px solid #cfd8e1;border-radius:8px;padding:10px 14px;font-weight:600;cursor:pointer;min-width:90px;text-align:center}
.m input:checked+span{border-color:#0b5394;background:#e8f0fb;color:#0b5394}.m input:focus-visible+span{outline:3px solid #0b5394}
.btns{display:flex;gap:10px;margin-top:18px;flex-wrap:wrap}button{border:0;border-radius:8px;padding:12px 18px;font-weight:700;cursor:pointer;font-size:15px}
.pay{background:#0b8f4d;color:#fff;flex:1}.fail{background:#fde8e8;color:#9b1c1c}.cancel{background:#eef2f6;color:#334}
.note{font-size:12px;color:#5b6b78;margin-top:14px;line-height:1.5}
</style></head><body>
<div class="top"><span class="logo">Demo checkout · stands in for SSLCommerz</span><span class="tag">DEMO · no real money</span></div>
<main><div class="card">
<div class="row"><span>Merchant</span><strong>G Energy</strong></div>
<div class="row"><span>Statement</span><strong>{{e(p.Statement.StatementNumber)}}</strong></div>
<div class="row"><span>Transaction</span><span>{{e(p.TranId)}}</span></div>
<div class="amt">৳ {{p.Amount:0.00}} BDT</div>
{{(done ? $"<p>This checkout is already <strong>{e(p.Status)}</strong>.</p>" : $"""
<form method="post" action="/api/payments/sslcommerz-demo/{e(p.TranId)}">{options}
<div class="btns"><button class="pay" name="action" value="pay">Pay ৳ {p.Amount:0.00}</button><button class="fail" name="action" value="fail">Simulate failure</button><button class="cancel" name="action" value="cancel">Cancel</button></div></form>
""")}}
<p class="note">This page stands in for the SSLCommerz hosted checkout while no SSLCommerz store is configured. It follows the same steps: redirect, choose a method, callback to G Energy, then server-side validation before the statement is marked paid. Add SslCommerz:StoreId and SslCommerz:StorePassword in user secrets to use the real SSLCommerz sandbox.</p>
</div></main></body></html>
""";
        return Content(html, "text/html; charset=utf-8");
    }
    [HttpPost("api/payments/sslcommerz-demo/{tranId}")]
    public async Task<IActionResult> DemoSubmit(string tranId, CancellationToken ct)
    {
        if (gateway is not DemoSslCommerzGateway) return NotFound();
        var api = ssl.Value.PublicApiBaseUrl.TrimEnd('/') + "/api/payments/sslcommerz";
        var action = Field("action"); var method = Field("method");
        if (action == "cancel") return Redirect($"{api}/cancel?tran_id={Uri.EscapeDataString(tranId)}");
        if (action != "pay" || !Methods.Any(m => m.Value == method)) return Redirect($"{api}/fail?tran_id={Uri.EscapeDataString(tranId)}&error={Uri.EscapeDataString("Simulated payment failure at the gateway.")}");
        var valId = await billing.DemoApprove(tranId, method, ct);
        if (valId is null) return NotFound();
        return Redirect($"{api}/success?tran_id={Uri.EscapeDataString(tranId)}&val_id={Uri.EscapeDataString(valId)}");
    }
}
