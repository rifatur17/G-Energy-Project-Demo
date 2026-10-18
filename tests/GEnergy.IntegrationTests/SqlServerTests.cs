using System.Net;
using System.Net.Http.Json;
using GEnergy.Api.Data;
using GEnergy.Api.DTOs;
using GEnergy.Api.Entities;
using GEnergy.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace GEnergy.IntegrationTests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("G_ENERGY_TEST_SQLSERVER")))
            Skip = "G_ENERGY_TEST_SQLSERVER is unset; requires SQL Server and permission to create an isolated test database.";
    }
}
public sealed class SqlServerTests
{
    [SqlServerFact]
    public async Task Migration_sequence_constraints_and_concurrent_registration()
    {
        var database = "GEnergy_AuthTests_" + Guid.NewGuid().ToString("N");
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("G_ENERGY_TEST_SQLSERVER")) { InitialCatalog = database };
        await using var app = new SqlFactory(connection.ConnectionString);
        using var client = app.CreateClient();
        try
        {
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<EnergyDbContext>();
                await db.Database.MigrateAsync();
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            }
            var calls = Enumerable.Range(0, 8).Select(i => client.PostAsJsonAsync("/api/auth/register", new { fullName = "Concurrent User", email = $"user{i}@example.test", phone = "01700000000", password = "Test-only-long-passphrase", role = i % 2 == 0 ? "CarOwner" : "CarDriver" }));
            var results = await Task.WhenAll(calls);
            Assert.All(results, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
            var users = await Task.WhenAll(results.Select(async r => (await r.Content.ReadFromJsonAsync<UserResponse>())!));
            Assert.Equal(8, users.Select(u => u.UserCode).Distinct().Count());
            var duplicates = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync("/api/auth/register", new { fullName = "Duplicate Race", email = "race@example.test", phone = "01700000000", password = "Test-only-long-passphrase", role = "CarOwner" })));
            Assert.Single(duplicates, r => r.StatusCode == HttpStatusCode.Created);
            Assert.Single(duplicates, r => r.StatusCode == HttpStatusCode.Conflict);
            var login = await client.PostAsJsonAsync("/api/auth/login", new { userCode = users[0].UserCode, password = "Test-only-long-passphrase" });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
            client.DefaultRequestHeaders.Authorization = new("Bearer", token.AccessToken);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<EnergyDbContext>();
                var original = await db.Users.AsNoTracking().FirstAsync();
                original.Id = 0; original.NormalizedEmail = "ANOTHER@EXAMPLE.TEST";
                db.Users.Add(original);
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }
        }
        finally
        {
            // The generated catalog is the only database this test may delete.
            Assert.StartsWith("GEnergy_AuthTests_", connection.InitialCatalog);
            using var scope = app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<EnergyDbContext>().Database.EnsureDeletedAsync();
        }
    }
    [SqlServerFact]
    public async Task Mobility_migration_constraints_rowversion_and_serialized_writes()
    {
        var database = "GEnergy_MobilityTests_" + Guid.NewGuid().ToString("N");
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("G_ENERGY_TEST_SQLSERVER")) { InitialCatalog = database };
        await using var app = new SqlFactory(connection.ConnectionString); using var client = app.CreateClient();
        try
        {
            using (var scope = app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<EnergyDbContext>().Database.MigrateAsync();
            var register = await client.PostAsJsonAsync("/api/auth/register", new { fullName = "SQL Owner", email = "sql-owner@example.test", phone = "01700000000", password = "SQL-test-only-passphrase", role = "CarOwner" });
            var user = (await register.Content.ReadFromJsonAsync<UserResponse>())!;
            var login = await client.PostAsJsonAsync("/api/auth/login", new { userCode = user.UserCode, password = "SQL-test-only-passphrase" });
            client.DefaultRequestHeaders.Authorization = new("Bearer", (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
            var input = new VehicleRequest("SQL-VEHICLE", "Demo EV", "CCS2", 50, 40, 0.18m);
            var created = await client.PostAsJsonAsync("/api/vehicles", input); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var vehicle = (await created.Content.ReadFromJsonAsync<VehicleResponse>())!; Assert.Equal(8, Convert.FromBase64String(vehicle.RowVersion).Length); Assert.Equal(DateTimeKind.Utc, vehicle.CreatedAtUtc.Kind);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/vehicles", input)).StatusCode);
            async Task<HttpResponseMessage> Update(decimal battery)
            { using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/vehicles/{vehicle.Id}") { Content = JsonContent.Create(input with { BatteryPercent = battery }) }; request.Headers.TryAddWithoutValidation("If-Match", vehicle.RowVersion); return await client.SendAsync(request); }
            var writes = await Task.WhenAll(Update(50), Update(60)); Assert.Single(writes, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(writes, r => r.StatusCode == HttpStatusCode.Conflict);
            var after = (await client.GetFromJsonAsync<VehicleResponse>($"/api/vehicles/{vehicle.Id}"))!; Assert.NotEqual(vehicle.RowVersion, after.RowVersion);
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<EnergyDbContext>();
                db.Vehicles.Add(new Vehicle { OwnerUserId = user.Id, RegistrationNo = "INVALID", Model = "Invalid", ConnectorType = "CCS2", BatteryCapacityKWh = 50, BatteryPercent = 101, ConsumptionKWhPerKm = 0.18m });
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }
            await DiscoverySeeder.SeedAsync(app.Services, app.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>());
            Assert.Equal(5, (await client.GetFromJsonAsync<StationResponse[]>("/api/stations"))!.Length);
        }
        finally
        {
            Assert.StartsWith("GEnergy_MobilityTests_", connection.InitialCatalog);
            using var scope = app.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<EnergyDbContext>().Database.EnsureDeletedAsync();
        }
    }
    private sealed class SqlFactory(string connection) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connection,
                ["Jwt:SigningKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48))
            }));
        }
    }
}
