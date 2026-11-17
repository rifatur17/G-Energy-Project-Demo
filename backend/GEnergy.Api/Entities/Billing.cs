namespace GEnergy.Api.Entities;

// Postpaid billing (PRD BILL-02, PAY-04). G Energy never holds an owner's money: charging is
// metered first, collected on a statement, and paid through a hosted gateway afterwards.
public sealed class BillingStatement
{
    public long Id { get; set; }
    public string StatementNumber { get; set; } = "";
    public long OwnerUserId { get; set; }
    public User Owner { get; set; } = null!;
    public string PeriodKey { get; set; } = ""; // yyyy-MM in Bangladesh time
    public int Sequence { get; set; }
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public DateTime IssuedAtUtc { get; set; }
    public DateTime DueAtUtc { get; set; }
    public int SessionCount { get; set; }
    public decimal ChargingTotal { get; set; }
    public int PlanVehicleCount { get; set; }
    public decimal PlanFee { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = "Issued"; // Issued | Paid
    public DateTime? PaidAtUtc { get; set; }
    public string? PaymentReference { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
// Adding money to the Owner wallet through the SSLCommerz hosted checkout. The wallet is credited
// only after the gateway validation confirms the payment (one ledger entry per transaction).
public sealed class WalletTopUpPayment
{
    public long Id { get; set; }
    public long OwnerUserId { get; set; }
    public User Owner { get; set; } = null!;
    public string TranId { get; set; } = "";
    public string Gateway { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "BDT";
    public string Status { get; set; } = "Initiated"; // Initiated | Paid | Failed | Cancelled
    public string? ValId { get; set; }
    public string? BankTranId { get; set; }
    public string? CardType { get; set; }
    public string? FailureReason { get; set; }
    public long? WalletTransactionId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
public sealed class StatementPayment
{
    public long Id { get; set; }
    public long StatementId { get; set; }
    public BillingStatement Statement { get; set; } = null!;
    public long OwnerUserId { get; set; }
    public string TranId { get; set; } = "";
    public string Gateway { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "BDT";
    public string Status { get; set; } = "Initiated"; // Initiated | Paid | Failed | Cancelled
    public string? ValId { get; set; }
    public string? BankTranId { get; set; }
    public string? CardType { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
