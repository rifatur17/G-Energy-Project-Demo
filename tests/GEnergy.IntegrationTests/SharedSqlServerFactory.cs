using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace GEnergy.IntegrationTests;
public sealed class SharedSqlServerFactAttribute : FactAttribute
{
    public SharedSqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("G_ENERGY_SHARED_SQLSERVER")))
            Skip = "G_ENERGY_SHARED_SQLSERVER is unset; this additive verification retains labelled QA records in GEnergyDb.";
    }
}
// Never migrates, resets, creates or deletes the shared database. Setup is an explicit operator step.
public sealed class SharedSqlServerFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var connection = Environment.GetEnvironmentVariable("G_ENERGY_SHARED_SQLSERVER")
            ?? throw new InvalidOperationException("Configure G_ENERGY_SHARED_SQLSERVER explicitly.");
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connection,
            ["Charging:WorkerEnabled"] = "false",
            ["Jwt:SigningKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48))
        }));
    }
}
