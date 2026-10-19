using GEnergy.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace GEnergy.Api.Data;

public static class ChargingConfiguration
{
    private static void Utc(PropertyBuilder<DateTime> p) => p.HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
    public static void ConfigureCharging(this ModelBuilder model)
    {
        var b = model.Entity<Booking>();
        b.ToTable("Bookings", t => { t.HasCheckConstraint("CK_Bookings_Status", "[Status] IN ('Reserved','Expired','Cancelled','Consumed')"); t.HasCheckConstraint("CK_Bookings_Expiry", "[ExpiresAtUtc] > [CreatedAtUtc]"); });
        b.Property(x => x.BookingCode).HasMaxLength(48).IsRequired(); b.HasIndex(x => x.BookingCode).IsUnique();
        b.Property(x => x.Status).HasMaxLength(16).IsUnicode(false).IsRequired(); b.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion(); Utc(b.Property(x => x.CreatedAtUtc)); Utc(b.Property(x => x.ExpiresAtUtc));
        b.HasIndex(x => new { x.BookedByUserId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => x.ChargerId).IsUnique().HasFilter("[Status] = 'Reserved'").HasDatabaseName("UX_Bookings_ReservedCharger");
        b.HasIndex(x => x.VehicleId).IsUnique().HasFilter("[Status] = 'Reserved'").HasDatabaseName("UX_Bookings_ReservedVehicle");
        b.HasIndex(x => new { x.Status, x.ExpiresAtUtc }); b.HasIndex(x => new { x.BookedByUserId, x.CreatedAtUtc });
        b.HasOne(x => x.BookedBy).WithMany().HasForeignKey(x => x.BookedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Vehicle).WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Charger).WithMany().HasForeignKey(x => x.ChargerId).OnDelete(DeleteBehavior.Restrict);
        var s = model.Entity<ChargingSession>();
        s.ToTable("ChargingSessions", t =>
        {
            t.HasCheckConstraint("CK_Sessions_Status", "[Status] IN ('Active','Completed') AND [PaymentStatus] IN ('Unpaid','Paid') AND (([Status] = 'Active' AND [EndedAtUtc] IS NULL) OR ([Status] = 'Completed' AND [EndedAtUtc] IS NOT NULL AND [EndedAtUtc] >= [StartedAtUtc]))");
            t.HasCheckConstraint("CK_Sessions_Numbers", "[CapacityKWh] > 0 AND [PowerKW] > 0 AND [TariffPerKWh] > 0 AND [SimulationSpeed] > 0 AND [ConsumptionKWhPerKm] > 0 AND [InitialBatteryPercent] BETWEEN 0 AND 100 AND [BatteryPercent] BETWEEN 0 AND 100 AND [EnergyKWh] >= 0 AND [EnergyKWh] <= [CapacityKWh] AND [Cost] >= 0 AND [ElapsedSeconds] >= 0");
        });
        s.Property(x => x.Status).HasMaxLength(16).IsUnicode(false).IsRequired(); s.Property(x => x.PaymentStatus).HasMaxLength(16).IsUnicode(false).IsRequired(); s.Property(x => x.SettlementNote).HasMaxLength(200).IsRequired();
        s.Property(x => x.RowVersion).IsRowVersion(); Utc(s.Property(x => x.StartedAtUtc));
        s.Property(x => x.EndedAtUtc).HasConversion(x => x, x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Utc) : null);
        s.Property(x => x.CapacityKWh).HasPrecision(8, 2); s.Property(x => x.InitialBatteryPercent).HasPrecision(5, 2); s.Property(x => x.BatteryPercent).HasPrecision(5, 2);
        s.Property(x => x.ConsumptionKWhPerKm).HasPrecision(8, 4); s.Property(x => x.PowerKW).HasPrecision(8, 2); s.Property(x => x.TariffPerKWh).HasPrecision(10, 2); s.Property(x => x.SimulationSpeed).HasPrecision(8, 2);
        s.Property(x => x.EnergyKWh).HasPrecision(18, 6); s.Property(x => x.Cost).HasPrecision(18, 2); s.Property(x => x.ElapsedSeconds).HasPrecision(18, 3);
        s.HasIndex(x => x.BookingId).IsUnique(); s.HasIndex(x => x.ChargerId).IsUnique().HasFilter("[Status] = 'Active'").HasDatabaseName("UX_Sessions_ActiveCharger");
        s.HasIndex(x => x.VehicleId).IsUnique().HasFilter("[Status] = 'Active'").HasDatabaseName("UX_Sessions_ActiveVehicle"); s.HasIndex(x => new { x.StartedByUserId, x.StartedAtUtc });
        s.HasOne(x => x.Booking).WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
        s.HasOne(x => x.Vehicle).WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
        s.HasOne(x => x.Charger).WithMany().HasForeignKey(x => x.ChargerId).OnDelete(DeleteBehavior.Restrict);
        s.HasOne(x => x.StartedBy).WithMany().HasForeignKey(x => x.StartedByUserId).OnDelete(DeleteBehavior.Restrict);
        s.HasOne(x => x.PayerOwner).WithMany().HasForeignKey(x => x.PayerOwnerUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
