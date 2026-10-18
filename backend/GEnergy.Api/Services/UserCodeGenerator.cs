using GEnergy.Api.Data;
using Microsoft.EntityFrameworkCore;
namespace GEnergy.Api.Services;

public interface IUserCodeGenerator { Task<string> NextAsync(string role, CancellationToken ct); }
public sealed class UserCodeGenerator(EnergyDbContext db) : IUserCodeGenerator
{
    public async Task<string> NextAsync(string role, CancellationToken ct)
    {
        var number = await db.Database.SqlQueryRaw<long>("SELECT NEXT VALUE FOR dbo.UserCodeNumbers AS Value").ToListAsync(ct);
        var prefix = role switch { "CarOwner" => "OWN", "CarDriver" => "DRV", "Admin" => "ADM", _ => throw new ArgumentException("Unknown role") };
        return $"GE-{prefix}-{number.Single():D6}";
    }
}
