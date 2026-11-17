using GEnergy.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace GEnergy.Api.Data;

public static class BillingConfiguration
{
    private static void Utc(PropertyBuilder<DateTime> p) => p.HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
    private static void Utc(PropertyBuilder<DateTime?> p) => p.HasConversion(x => x, x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Utc) : null);
    public static void ConfigureBilling(this ModelBuilder model)
    {
        var st = model.Entity<BillingStatement>();
        st.ToTable("BillingStatements", t =>
        {
            t.HasCheckConstraint("CK_Statements_Status", "[Status] IN ('Issued','Paid') AND (([Status] = 'Paid' AND [PaidAtUtc] IS NOT NULL) OR ([Status] = 'Issued' AND [PaidAtUtc] IS NULL))");
            t.HasCheckConstraint("CK_Statements_Amounts", "[ChargingTotal] >= 0 AND [PlanFee] >= 0 AND [Total] = [ChargingTotal] + [PlanFee] AND [Total] > 0 AND [SessionCount] >= 0 AND [PlanVehicleCount] >= 0 AND [Sequence] >= 1");
            t.HasCheckConstraint("CK_Statements_Dates", "[PeriodEndUtc] >= [PeriodStartUtc] AND [DueAtUtc] >= [IssuedAtUtc]");
        });
        st.Property(x => x.StatementNumber).HasMaxLength(48).IsRequired(); st.HasIndex(x => x.StatementNumber).IsUnique();
        st.Property(x => x.PeriodKey).HasMaxLength(7).IsUnicode(false).IsRequired();
        st.HasIndex(x => new { x.OwnerUserId, x.PeriodKey, x.Sequence }).IsUnique();
        st.Property(x => x.Status).HasMaxLength(16).IsUnicode(false).IsRequired();
        st.Property(x => x.ChargingTotal).HasPrecision(18, 2); st.Property(x => x.PlanFee).HasPrecision(18, 2); st.Property(x => x.Total).HasPrecision(18, 2);
        st.Property(x => x.PaymentReference).HasMaxLength(64);
        st.Property(x => x.RowVersion).IsRowVersion();
        Utc(st.Property(x => x.PeriodStartUtc)); Utc(st.Property(x => x.PeriodEndUtc)); Utc(st.Property(x => x.IssuedAtUtc)); Utc(st.Property(x => x.DueAtUtc)); Utc(st.Property(x => x.PaidAtUtc));
        st.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);

        var p = model.Entity<StatementPayment>();
        p.ToTable("StatementPayments", t =>
        {
            t.HasCheckConstraint("CK_StatementPayments_Status", "[Status] IN ('Initiated','Paid','Failed','Cancelled')");
            t.HasCheckConstraint("CK_StatementPayments_Amount", "[Amount] > 0 AND [Currency] = 'BDT'");
        });
        p.Property(x => x.TranId).HasMaxLength(48).IsUnicode(false).IsRequired(); p.HasIndex(x => x.TranId).IsUnique();
        p.Property(x => x.Gateway).HasMaxLength(24).IsUnicode(false).IsRequired();
        p.Property(x => x.Currency).HasMaxLength(3).IsUnicode(false).IsRequired();
        p.Property(x => x.Status).HasMaxLength(16).IsUnicode(false).IsRequired();
        p.Property(x => x.Amount).HasPrecision(18, 2);
        p.Property(x => x.ValId).HasMaxLength(64); p.Property(x => x.BankTranId).HasMaxLength(80); p.Property(x => x.CardType).HasMaxLength(50); p.Property(x => x.FailureReason).HasMaxLength(200);
        p.Property(x => x.RowVersion).IsRowVersion();
        Utc(p.Property(x => x.CreatedAtUtc)); Utc(p.Property(x => x.CompletedAtUtc));
        // At most one successful payment per statement, whatever the number of gateway callbacks.
        p.HasIndex(x => x.StatementId).IsUnique().HasFilter("[Status] = 'Paid'").HasDatabaseName("UX_StatementPayments_PaidStatement");
        p.HasIndex(x => new { x.OwnerUserId, x.CreatedAtUtc });
        p.HasOne(x => x.Statement).WithMany().HasForeignKey(x => x.StatementId).OnDelete(DeleteBehavior.Restrict);

        var w = model.Entity<WalletTopUpPayment>();
        w.ToTable("WalletTopUpPayments", t =>
        {
            t.HasCheckConstraint("CK_WalletTopUpPayments_Status", "[Status] IN ('Initiated','Paid','Failed','Cancelled') AND ([Status] <> 'Paid' OR [WalletTransactionId] IS NOT NULL)");
            t.HasCheckConstraint("CK_WalletTopUpPayments_Amount", "[Amount] > 0 AND [Currency] = 'BDT'");
        });
        w.Property(x => x.TranId).HasMaxLength(48).IsUnicode(false).IsRequired(); w.HasIndex(x => x.TranId).IsUnique();
        w.Property(x => x.Gateway).HasMaxLength(24).IsUnicode(false).IsRequired();
        w.Property(x => x.Currency).HasMaxLength(3).IsUnicode(false).IsRequired();
        w.Property(x => x.Status).HasMaxLength(16).IsUnicode(false).IsRequired();
        w.Property(x => x.Amount).HasPrecision(18, 2);
        w.Property(x => x.ValId).HasMaxLength(64); w.Property(x => x.BankTranId).HasMaxLength(80); w.Property(x => x.CardType).HasMaxLength(50); w.Property(x => x.FailureReason).HasMaxLength(200);
        w.Property(x => x.RowVersion).IsRowVersion();
        Utc(w.Property(x => x.CreatedAtUtc)); Utc(w.Property(x => x.CompletedAtUtc));
        w.HasIndex(x => x.WalletTransactionId).IsUnique().HasFilter("[WalletTransactionId] IS NOT NULL");
        w.HasIndex(x => new { x.OwnerUserId, x.CreatedAtUtc });
        w.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        w.HasOne<WalletTransaction>().WithMany().HasForeignKey(x => x.WalletTransactionId).OnDelete(DeleteBehavior.Restrict);

        var s = model.Entity<ChargingSession>();
        s.HasOne(x => x.Statement).WithMany().HasForeignKey(x => x.StatementId).OnDelete(DeleteBehavior.Restrict);
        s.HasIndex(x => new { x.PayerOwnerUserId, x.StatementId, x.Status });
    }
}
