using GEnergy.Api.Data;
using GEnergy.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
namespace GEnergy.Api.Services;
// SQL mutation lock order starts with Vehicles, shared with future booking/charging mutations.
public sealed class VehicleWriteScope : IAsyncDisposable
{
    private readonly IDbContextTransaction? transaction;
    public Vehicle? Vehicle { get; }
    private VehicleWriteScope(Vehicle? vehicle, IDbContextTransaction? transaction) { Vehicle = vehicle; this.transaction = transaction; }
    public static async Task<VehicleWriteScope> OpenAsync(EnergyDbContext db, long id, CancellationToken ct)
    {
        IDbContextTransaction? transaction = null;
        try
        {
            if (db.Database.IsSqlServer()) transaction = await db.Database.BeginTransactionAsync(ct);
            var vehicle = db.Database.IsSqlServer()
                ? await db.Vehicles.FromSqlInterpolated($"SELECT * FROM dbo.Vehicles WITH (UPDLOCK, HOLDLOCK) WHERE Id = {id}").SingleOrDefaultAsync(ct)
                : await db.Vehicles.SingleOrDefaultAsync(v => v.Id == id, ct);
            return new(vehicle, transaction);
        }
        catch { if (transaction is not null) await transaction.DisposeAsync(); throw; }
    }
    public async Task CommitAsync(CancellationToken ct) { if (transaction is not null) await transaction.CommitAsync(ct); }
    public async ValueTask DisposeAsync() { if (transaction is not null) await transaction.DisposeAsync(); }
}
