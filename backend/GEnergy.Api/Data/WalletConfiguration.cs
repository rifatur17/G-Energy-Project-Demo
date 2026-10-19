using GEnergy.Api.Entities;
using Microsoft.EntityFrameworkCore;
namespace GEnergy.Api.Data;

public static class WalletConfiguration
{
    public static void ConfigureWallet(this ModelBuilder model)
    {
        var w = model.Entity<Wallet>();
        w.ToTable("Wallets", t => t.HasCheckConstraint("CK_Wallets_Balance", "[Balance] >= 0"));
        w.Property(x => x.Balance).HasPrecision(18, 2); w.Property(x => x.RowVersion).IsRowVersion();
        w.HasIndex(x => x.UserId).IsUnique(); w.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        w.Property(x => x.CreatedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc)); w.Property(x => x.UpdatedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        var t = model.Entity<WalletTransaction>();
        t.ToTable("WalletTransactions", b =>
        {
            b.HasCheckConstraint("CK_WalletTransactions_Amount", "[Amount] > 0");
            b.HasCheckConstraint("CK_WalletTransactions_State", "([Type] = 'TopUp' AND [Status] IN ('Completed','Failed') AND [ChargingSessionId] IS NULL AND [OriginalTransactionId] IS NULL) OR ([Type] = 'ChargingPayment' AND [Status] = 'Completed' AND [ChargingSessionId] IS NOT NULL AND [OriginalTransactionId] IS NULL) OR ([Type] = 'Refund' AND [Status] = 'Completed' AND [ChargingSessionId] IS NOT NULL AND [OriginalTransactionId] IS NOT NULL)");
            b.HasCheckConstraint("CK_WalletTransactions_Time", "[CompletedAtUtc] >= [CreatedAtUtc]");
        });
        t.Property(x => x.Amount).HasPrecision(18, 2); t.Property(x => x.Reference).HasMaxLength(48).IsRequired(); t.HasIndex(x => x.Reference).IsUnique();
        t.Property(x => x.Type).HasMaxLength(24).IsUnicode(false).IsRequired(); t.Property(x => x.Status).HasMaxLength(16).IsUnicode(false).IsRequired();
        t.Property(x => x.IdempotencyKey).HasMaxLength(120).IsRequired(); t.HasIndex(x => new { x.WalletId, x.IdempotencyKey }).IsUnique();
        t.Property(x => x.RequestedOutcome).HasMaxLength(16).IsRequired(); t.Property(x => x.Description).HasMaxLength(200).IsRequired();
        t.Property(x => x.CreatedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc)); t.Property(x => x.CompletedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        t.HasIndex(x => new { x.WalletId, x.Id });
        t.HasIndex(x => x.ChargingSessionId).IsUnique().HasFilter("[Type] = 'ChargingPayment' AND [Status] = 'Completed'").HasDatabaseName("UX_WalletTransactions_SessionPayment");
        t.HasIndex(x => x.OriginalTransactionId).IsUnique().HasFilter("[OriginalTransactionId] IS NOT NULL").HasDatabaseName("UX_WalletTransactions_Refund");
        t.HasOne(x => x.Wallet).WithMany().HasForeignKey(x => x.WalletId).OnDelete(DeleteBehavior.Restrict);
        t.HasOne(x => x.ChargingSession).WithMany().HasForeignKey(x => x.ChargingSessionId).OnDelete(DeleteBehavior.Restrict);
        t.HasAlternateKey(x => new { x.Id, x.WalletId });
        t.HasOne(x => x.OriginalTransaction).WithMany().HasForeignKey(x => new { x.OriginalTransactionId, x.WalletId }).HasPrincipalKey(x => new { x.Id, x.WalletId }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ChargingSession>().Property(x => x.PaymentReference).HasMaxLength(48);
    }
}
