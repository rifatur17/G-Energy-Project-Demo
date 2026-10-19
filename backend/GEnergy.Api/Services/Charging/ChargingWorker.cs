using GEnergy.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace GEnergy.Api.Services.Charging;

public sealed class ChargingWorker(IServiceScopeFactory scopes, IOptions<ChargingOptions> options, TimeProvider clock, ILogger<ChargingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WorkerEnabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Tick(stoppingToken); } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; } catch (Exception e) { logger.LogError(e, "Charging worker iteration failed; will retry."); }
            try { await Task.Delay(options.Value.WorkerIntervalMilliseconds, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
    public async Task Tick(CancellationToken ct)
    {
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EnergyDbContext>(); var now = clock.GetUtcNow().UtcDateTime;
        var expired = await db.Bookings.AsNoTracking().Where(b => b.Status == "Reserved" && b.ExpiresAtUtc <= now).OrderBy(b => b.Id).Take(200).Select(b => b.Id).ToListAsync(ct);
        foreach (var id in expired) await Process(id, false, ct);
        // Page through the active set so long-running early sessions cannot starve later ones.
        var upper = await db.ChargingSessions.Select(s => (long?)s.Id).MaxAsync(ct) ?? 0;
        long after = 0;
        while (after < upper)
        {
            var sessions = await db.ChargingSessions.AsNoTracking().Where(s => s.Status == "Active" && s.Id > after && s.Id <= upper).OrderBy(s => s.Id).Take(200).Select(s => s.Id).ToListAsync(ct);
            if (sessions.Count == 0) break;
            foreach (var id in sessions) await Process(id, true, ct);
            after = sessions[^1];
        }
    }
    private async Task Process(long id, bool session, CancellationToken ct)
    {
        try { using var scope = scopes.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<ChargingService>(); if (session) await service.Advance(id, false, ct); else await service.RefreshBooking(id, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) { logger.LogError(e, "Charging worker could not process {Kind} {Id}; later iterations will retry.", session ? "session" : "booking", id); }
    }
}
