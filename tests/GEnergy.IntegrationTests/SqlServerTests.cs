using System.Net;
using System.Net.Http.Json;
using GEnergy.Api.Data;
using GEnergy.Api.DTOs;
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
