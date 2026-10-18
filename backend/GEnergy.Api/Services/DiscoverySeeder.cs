using System.Data;
using GEnergy.Api.Data;
using GEnergy.Api.Entities;
using Microsoft.EntityFrameworkCore;
namespace GEnergy.Api.Services;

public static class DiscoverySeeder
{
    public static async Task SeedAsync(IServiceProvider services, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment()) throw new InvalidOperationException("Sample discovery seeding is development-only.");
        using var scope = services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EnergyDbContext>();
        await using var transaction = db.Database.IsSqlServer() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;
        // A short table lock serializes only this explicit dev seed command, never normal requests.
        if (db.Database.IsSqlServer()) await db.Database.ExecuteSqlRawAsync("SELECT Id FROM dbo.Stations WITH (TABLOCKX, HOLDLOCK)");
        (string City, decimal Lat, decimal Lon)[] samples = [("Dhaka", 23.8103m, 90.4125m), ("Daudkandi", 23.5340m, 90.7112m), ("Cumilla", 23.4607m, 91.1809m), ("Feni", 23.0159m, 91.3976m), ("Chattogram", 22.3569m, 91.7832m)];
        foreach (var sample in samples)
        {
            var name = $"G Energy Demo — {sample.City}";
            var station = await db.Stations.Include(s => s.Chargers).SingleOrDefaultAsync(s => s.Name == name && s.IsDemo);
            if (station is null) { station = new Station { Name = name, Address = $"Sample location near {sample.City}; not a verified charging site", Latitude = sample.Lat, Longitude = sample.Lon, IsDemo = true }; db.Stations.Add(station); }
            if (!station.Chargers.Any(c => c.ChargerCode == "DEMO-DC")) station.Chargers.Add(new Charger { ChargerCode = "DEMO-DC", ConnectorType = "CCS2", PowerKW = 60, PricePerKWh = 25 });
            if (!station.Chargers.Any(c => c.ChargerCode == "DEMO-AC")) station.Chargers.Add(new Charger { ChargerCode = "DEMO-AC", ConnectorType = "Type2", PowerKW = 22, PricePerKWh = 20 });
        }
        await db.SaveChangesAsync(); if (transaction is not null) await transaction.CommitAsync();
    }
}
