namespace GEnergy.Api.Entities;

public sealed class Wallet
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public User User { get; set; } = null!;
    public decimal Balance { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
public sealed class WalletTransaction
{
    public long Id { get; set; }
    public long WalletId { get; set; }
    public Wallet Wallet { get; set; } = null!;
    public string Reference { get; set; } = "WT-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
    public string Type { get; set; } = ""; public string Status { get; set; } = "";
    public decimal Amount { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public string RequestedOutcome { get; set; } = ""; public string Description { get; set; } = "";
    public long? ChargingSessionId { get; set; }
    public ChargingSession? ChargingSession { get; set; }
    public long? OriginalTransactionId { get; set; }
    public WalletTransaction? OriginalTransaction { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime CompletedAtUtc { get; set; }
}
