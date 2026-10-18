using GEnergy.Api.Entities;
using Microsoft.EntityFrameworkCore;
namespace GEnergy.Api.Data;

public static class MobilityConfiguration
{
    public static void ConfigureMobility(this ModelBuilder model)
    {
        var v = model.Entity<Vehicle>();
        v.ToTable("Vehicles", t =>
        {
            t.HasCheckConstraint("CK_Vehicles_Battery", "[BatteryPercent] >= 0 AND [BatteryPercent] <= 100 AND [BatteryCapacityKWh] > 0 AND [ConsumptionKWhPerKm] > 0");
            t.HasCheckConstraint("CK_Vehicles_Connector", "[ConnectorType] IN ('CCS2','Type2','CHAdeMO')");
        });
        v.Property(x => x.RegistrationNo).HasMaxLength(32).IsRequired();
        v.HasIndex(x => x.RegistrationNo).IsUnique();
        v.Property(x => x.Model).HasMaxLength(100).IsRequired();
        v.Property(x => x.ConnectorType).HasMaxLength(20).IsUnicode(false).IsRequired();
        v.Property(x => x.BatteryCapacityKWh).HasPrecision(8, 2);
        v.Property(x => x.BatteryPercent).HasPrecision(5, 2);
        v.Property(x => x.ConsumptionKWhPerKm).HasPrecision(8, 4);
        v.Property(x => x.RowVersion).IsRowVersion();
        v.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        v.Property(x => x.CreatedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        v.Property(x => x.UpdatedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        var d = model.Entity<VehicleDriver>();
        d.HasKey(x => new { x.VehicleId, x.DriverUserId });
        d.HasOne(x => x.Vehicle).WithMany(x => x.Drivers).HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
        d.HasOne(x => x.Driver).WithMany().HasForeignKey(x => x.DriverUserId).OnDelete(DeleteBehavior.Restrict);
        d.Property(x => x.UpdatedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        var s = model.Entity<Station>();
        s.ToTable("Stations", t => t.HasCheckConstraint("CK_Stations_Coordinates", "[Latitude] BETWEEN -90 AND 90 AND [Longitude] BETWEEN -180 AND 180"));
        s.Property(x => x.Name).HasMaxLength(120).IsRequired();
        s.Property(x => x.Address).HasMaxLength(250).IsRequired();
        s.Property(x => x.Latitude).HasPrecision(9, 6);
        s.Property(x => x.Longitude).HasPrecision(9, 6);
        var c = model.Entity<Charger>();
        c.ToTable("Chargers", t =>
        {
            t.HasCheckConstraint("CK_Chargers_Positive", "[PowerKW] > 0 AND [PricePerKWh] > 0");
            t.HasCheckConstraint("CK_Chargers_Connector", "[ConnectorType] IN ('CCS2','Type2','CHAdeMO')");
            t.HasCheckConstraint("CK_Chargers_Status", "[Status] IN ('Available','Reserved','Charging','Offline')");
        });
        c.Property(x => x.ChargerCode).HasMaxLength(30).IsRequired();
        c.HasIndex(x => new { x.StationId, x.ChargerCode }).IsUnique();
        c.Property(x => x.ConnectorType).HasMaxLength(20).IsUnicode(false).IsRequired();
        c.Property(x => x.PowerKW).HasPrecision(8, 2);
        c.Property(x => x.PricePerKWh).HasPrecision(10, 2);
        c.Property(x => x.Status).HasMaxLength(16).IsUnicode(false).IsRequired();
        c.Property(x => x.RowVersion).IsRowVersion();
        c.HasOne(x => x.Station).WithMany(x => x.Chargers).HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Restrict);
    }
}
