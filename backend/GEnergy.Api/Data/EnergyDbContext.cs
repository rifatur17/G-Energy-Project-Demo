using GEnergy.Api.Entities;
using Microsoft.EntityFrameworkCore;
namespace GEnergy.Api.Data;

public sealed class EnergyDbContext(DbContextOptions<EnergyDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<VehicleDriver> VehicleDrivers => Set<VehicleDriver>();
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<Charger> Chargers => Set<Charger>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<ChargingSession> ChargingSessions => Set<ChargingSession>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<WelcomeEmail> WelcomeEmails => Set<WelcomeEmail>();
    private void ProtectLedger()
    {
        if (ChangeTracker.Entries<WalletTransaction>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Wallet ledger entries are immutable.");
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess) { ProtectLedger();return base.SaveChanges(acceptAllChangesOnSuccess); }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default) {ProtectLedger();return base.SaveChangesAsync(acceptAllChangesOnSuccess,ct);}
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ConfigureMobility();
        model.ConfigureCharging();
        model.ConfigureWallet();
        var mail = model.Entity<WelcomeEmail>();
        mail.HasKey(e => e.Id);
        mail.HasIndex(e => e.UserId).IsUnique();
        mail.HasIndex(e => new { e.SentAtUtc, e.NextAttemptAtUtc });
        mail.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
        mail.Property(e => e.LeaseToken).IsConcurrencyToken();
        mail.ToTable("WelcomeEmails", t => t.HasCheckConstraint("CK_WelcomeEmails_Attempts", "[Attempts] >= 0 AND [Attempts] <= 10"));
        mail.Property(e => e.CreatedAtUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        mail.Property(e => e.NextAttemptAtUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        model.HasSequence<long>("UserCodeNumbers").StartsAt(1001);
        var u = model.Entity<User>();
        u.ToTable("Users", t => t.HasCheckConstraint("CK_Users_Role", "[Role] IN ('Admin','CarOwner','CarDriver')"));
        u.HasKey(x => x.Id);
        u.Property(x => x.UserCode).HasMaxLength(24).IsRequired();
        u.Property(x => x.FullName).HasMaxLength(100).IsRequired();
        u.Property(x => x.Email).HasMaxLength(254).IsRequired();
        u.Property(x => x.NormalizedEmail).HasMaxLength(254).IsRequired();
        u.Property(x => x.Phone).HasMaxLength(20).IsRequired();
        u.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
        u.Property(x => x.Role).HasMaxLength(16).IsUnicode(false).IsRequired();
        u.Property(x => x.CreatedAtUtc).HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        u.HasIndex(x => x.UserCode).IsUnique();
        u.HasIndex(x => x.NormalizedEmail).IsUnique();
    }
}
