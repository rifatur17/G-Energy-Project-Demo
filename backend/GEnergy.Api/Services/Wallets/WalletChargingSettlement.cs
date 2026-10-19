using GEnergy.Api.Services.Charging;
namespace GEnergy.Api.Services.Wallets;

public sealed class WalletChargingSettlement(WalletService wallets) : IChargingSettlement
{
    public Task<SettlementResult> FinalizeAsync(ChargingCompletion completion, CancellationToken ct) => wallets.SettleWithinTransaction(completion.SessionId, ct);
}
