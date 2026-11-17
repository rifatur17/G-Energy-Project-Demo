using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
namespace GEnergy.Api.Services.Billing;

public sealed class SslCommerzOptions
{
    // Leave StoreId empty to use the built-in demo checkout. Put real sandbox credentials in
    // user secrets (SslCommerz:StoreId / SslCommerz:StorePassword); never commit them.
    public string StoreId { get; set; } = "";
    public string StorePassword { get; set; } = "";
    public bool Sandbox { get; set; } = true;
    public string PublicApiBaseUrl { get; set; } = "http://localhost:5080";
    public string FrontendBaseUrl { get; set; } = "http://localhost:5173";
    public bool UseLiveGateway => !string.IsNullOrWhiteSpace(StoreId) && !string.IsNullOrWhiteSpace(StorePassword);
    public string GatewayHost => Sandbox ? "https://sandbox.sslcommerz.com" : "https://securepay.sslcommerz.com";
}
public sealed record GatewayCustomer(string Name, string Email, string Phone);
public sealed record GatewayInitRequest(string TranId, decimal Amount, string StatementNumber, GatewayCustomer Customer, string SuccessUrl, string FailUrl, string CancelUrl, string IpnUrl);
public sealed record GatewayInitResult(bool Ok, string? RedirectUrl, string? Error);
public sealed record GatewayValidation(bool Valid, string? TranId, decimal Amount, string? Currency, string? BankTranId, string? CardType, string? Error);
public interface IPaymentGateway
{
    string Name { get; }
    Task<GatewayInitResult> InitiateAsync(GatewayInitRequest request, CancellationToken ct);
    // PRD PAY-02: a browser redirect is never trusted. Every success is confirmed with the provider first.
    Task<GatewayValidation> ValidateAsync(string valId, CancellationToken ct);
}

// SSLCommerz hosted checkout (v4). The customer chooses bKash, Nagad, cards or net banking on the
// SSLCommerz page; G Energy never sees card numbers or wallet PINs.
public sealed class SslCommerzGateway(HttpClient http, IOptions<SslCommerzOptions> options) : IPaymentGateway
{
    public string Name => "SSLCommerz";
    public async Task<GatewayInitResult> InitiateAsync(GatewayInitRequest r, CancellationToken ct)
    {
        var o = options.Value;
        var form = new Dictionary<string, string>
        {
            ["store_id"] = o.StoreId, ["store_passwd"] = o.StorePassword,
            ["total_amount"] = r.Amount.ToString("0.00", CultureInfo.InvariantCulture), ["currency"] = "BDT", ["tran_id"] = r.TranId,
            ["success_url"] = r.SuccessUrl, ["fail_url"] = r.FailUrl, ["cancel_url"] = r.CancelUrl, ["ipn_url"] = r.IpnUrl,
            ["cus_name"] = r.Customer.Name, ["cus_email"] = r.Customer.Email, ["cus_phone"] = string.IsNullOrWhiteSpace(r.Customer.Phone) ? "01700000000" : r.Customer.Phone,
            ["cus_add1"] = "Dhaka", ["cus_city"] = "Dhaka", ["cus_postcode"] = "1212", ["cus_country"] = "Bangladesh",
            ["shipping_method"] = "NO", ["num_of_item"] = "1", ["product_name"] = "EV charging statement " + r.StatementNumber,
            ["product_category"] = "EV charging", ["product_profile"] = "non-physical-goods", ["value_a"] = r.StatementNumber
        };
        try
        {
            using var response = await http.PostAsync(o.GatewayHost + "/gwprocess/v4/api.php", new FormUrlEncodedContent(form), ct);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            var status = root.TryGetProperty("status", out var s) ? s.GetString() : null;
            var url = root.TryGetProperty("GatewayPageURL", out var g) ? g.GetString() : null;
            if (status == "SUCCESS" && Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps) return new(true, url, null);
            var reason = root.TryGetProperty("failedreason", out var f) ? f.GetString() : null;
            return new(false, null, string.IsNullOrWhiteSpace(reason) ? "SSLCommerz did not open a checkout session." : "SSLCommerz: " + reason);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        { return new(false, null, "SSLCommerz is unreachable right now. Try again in a moment."); }
    }
    public async Task<GatewayValidation> ValidateAsync(string valId, CancellationToken ct)
    {
        var o = options.Value;
        var url = $"{o.GatewayHost}/validator/api/validationserverAPI.php?val_id={Uri.EscapeDataString(valId)}&store_id={Uri.EscapeDataString(o.StoreId)}&store_passwd={Uri.EscapeDataString(o.StorePassword)}&v=1&format=json";
        try
        {
            using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
            var root = doc.RootElement;
            string? Text(string name) => root.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString() : null;
            var status = Text("status");
            if (status is not ("VALID" or "VALIDATED")) return new(false, null, 0, null, null, null, "SSLCommerz did not confirm this payment.");
            if (!decimal.TryParse(Text("amount"), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)) return new(false, null, 0, null, null, null, "SSLCommerz returned an unreadable amount.");
            return new(true, Text("tran_id"), amount, Text("currency"), Text("bank_tran_id"), Text("card_type"), null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        { return new(false, null, 0, null, null, null, "SSLCommerz validation is unreachable; the payment stays pending."); }
    }
}

// Used when no SSLCommerz store is configured: a local checkout page that lets the customer pick a
// method and then reports that the payment service is not available. No money moves.
public sealed class DemoSslCommerzGateway(IOptions<SslCommerzOptions> options) : IPaymentGateway
{
    public string Name => "SSLCommerzDemo";
    public Task<GatewayInitResult> InitiateAsync(GatewayInitRequest r, CancellationToken ct) =>
        Task.FromResult(new GatewayInitResult(true, $"{options.Value.PublicApiBaseUrl.TrimEnd('/')}/api/payments/sslcommerz-demo/{Uri.EscapeDataString(r.TranId)}", null));
    // No real payment is connected in demo mode, so nothing can ever be confirmed (forged callbacks included).
    public Task<GatewayValidation> ValidateAsync(string valId, CancellationToken ct) =>
        Task.FromResult(new GatewayValidation(false, null, 0, null, null, null, "Demo checkout cannot confirm payments."));
}
