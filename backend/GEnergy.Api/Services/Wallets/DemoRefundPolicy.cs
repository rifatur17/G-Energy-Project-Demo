namespace GEnergy.Api.Services.Wallets;

public static class DemoRefundPolicy
{
    public static bool CanRefund(string type, string status, bool hasRefund, bool enabled) => enabled && type == "ChargingPayment" && status == "Completed" && !hasRefund;
}
