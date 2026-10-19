namespace GEnergy.Api.Services.Charging;
// Invoked only on Active -> Completed, within the same SQL transaction and scoped DbContext.
// Member 4 must use a unique SessionId debit and return Unpaid on insufficient funds. No external side effects here.
public sealed record ChargingCompletion(long SessionId, long PayerOwnerUserId, decimal EnergyKWh, decimal Cost, DateTime EndedAtUtc);
public sealed record SettlementResult(string PaymentStatus, string Note);
public interface IChargingSettlement { Task<SettlementResult> FinalizeAsync(ChargingCompletion completion, CancellationToken ct); }
public sealed class DeferredChargingSettlement : IChargingSettlement
{
    public Task<SettlementResult> FinalizeAsync(ChargingCompletion completion, CancellationToken ct) => Task.FromResult(new SettlementResult("Unpaid", "Wallet integration pending"));
}
