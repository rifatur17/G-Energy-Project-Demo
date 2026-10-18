using GEnergy.Api.Entities;
using Microsoft.EntityFrameworkCore;
namespace GEnergy.Api.Data;

public sealed class EnergyDbContext(DbContextOptions<EnergyDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    protected override void OnModelCreating(ModelBuilder model)
    {
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
