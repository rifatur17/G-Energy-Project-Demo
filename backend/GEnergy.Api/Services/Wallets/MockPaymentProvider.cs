using Microsoft.Extensions.Options;
namespace GEnergy.Api.Services.Wallets;

public sealed class WalletOptions
{
    public decimal MaxTransactionAmount { get; set; } = 100000;
    public decimal MaxBalance { get; set; } = 1000000;
    public bool ForceMockFailure { get; set; }
    public bool AllowDemoRefunds { get; set; } = true;
}
public sealed record MockPaymentRequest(string Reference, decimal Amount, string Outcome);
public sealed record MockPaymentResult(bool Success, string Description);
public interface IPaymentProvider { Task<MockPaymentResult> PayAsync(MockPaymentRequest request, CancellationToken ct); }
// This provider never contacts a bank or accepts payment credentials. The outcome is demo-only input.
public sealed class MockPaymentProvider(IOptions<WalletOptions> options) : IPaymentProvider
{
    public Task<MockPaymentResult> PayAsync(MockPaymentRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); var success = request.Outcome == "Success" && !options.Value.ForceMockFailure;
        return Task.FromResult(new MockPaymentResult(success, success ? "Simulated top-up completed; no real money transferred." : "Simulated payment failed; no credit applied."));
    }
}
