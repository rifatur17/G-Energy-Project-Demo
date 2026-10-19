using GEnergy.Api.Data;
using GEnergy.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace GEnergy.IntegrationTests;

public sealed class AuthFactory : WebApplicationFactory<Program>
{
    private readonly string database = Guid.NewGuid().ToString();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Charging:WorkerEnabled"] = "false",
            ["Jwt:SigningKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)),
            ["Jwt:Issuer"] = "TestIssuer",
            ["Jwt:Audience"] = "TestAudience"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<EnergyDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<EnergyDbContext>>();
            services.AddDbContext<EnergyDbContext>(o => o.UseInMemoryDatabase(database));
            services.RemoveAll<IUserCodeGenerator>();
            services.AddSingleton<IUserCodeGenerator, TestCodes>();
            services.AddControllers().AddApplicationPart(typeof(PolicyProbeController).Assembly);
        });
    }
    private sealed class TestCodes : IUserCodeGenerator
    {
        private long value = 1000;
        public Task<string> NextAsync(string role, CancellationToken ct) => Task.FromResult($"GE-{(role == "CarOwner" ? "OWN" : role == "CarDriver" ? "DRV" : "ADM")}-{Interlocked.Increment(ref value):D6}");
    }
}
