namespace GEnergy.Api.Entities;

// Durable delivery work only. No plaintext password, token or message body is stored.
public sealed class WelcomeEmail
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SentAtUtc { get; set; }
    public int Attempts { get; set; }
    public Guid LeaseToken { get; set; } = Guid.NewGuid();
    public DateTime? LeaseUntilUtc { get; set; }
}
