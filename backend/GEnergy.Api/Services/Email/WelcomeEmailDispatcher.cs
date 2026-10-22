using GEnergy.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace GEnergy.Api.Services.Email;

public sealed class WelcomeEmailDispatcher(EnergyDbContext db, IEmailSender sender, IOptions<EmailOptions> configured, TimeProvider clock, ILogger<WelcomeEmailDispatcher> logger)
{
    public async Task<bool> DispatchOneAsync(CancellationToken ct, long? deliveryId = null)
    {
        if (!configured.Value.Enabled) return false;
        var now = clock.GetUtcNow().UtcDateTime;
        var delivery = await db.WelcomeEmails.Where(e => (!deliveryId.HasValue || e.Id == deliveryId) && e.SentAtUtc == null && e.Attempts < 10 && e.NextAttemptAtUtc <= now && (e.LeaseUntilUtc == null || e.LeaseUntilUtc <= now)).OrderBy(e => e.Id).FirstOrDefaultAsync(ct);
        if (delivery is null) return false;
        // Concurrency-token comparison makes claim acquisition atomic even across API hosts.
        delivery.LeaseToken = Guid.NewGuid();
        delivery.LeaseUntilUtc = now.AddMinutes(5);
        delivery.Attempts++;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); return false; }
        await db.Entry(delivery).Reference(e => e.User).LoadAsync(ct);
        try
        {
            await sender.SendAsync(WelcomeEmailMessage.Create(delivery, configured.Value), ct);
            delivery.SentAtUtc = clock.GetUtcNow().UtcDateTime;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // SMTP exceptions can contain addresses, authentication details or server replies.
            // Log only a work ID and exception type; retry state remains in SQL Server.
            logger.LogWarning("Welcome email {DeliveryId} attempt {Attempt} failed ({FailureType}); retained for retry.", delivery.Id, delivery.Attempts, ex.GetType().Name);
            delivery.NextAttemptAtUtc = clock.GetUtcNow().UtcDateTime.AddSeconds(Math.Min(3600, 30 * Math.Pow(2, delivery.Attempts - 1)));
        }
        // Keep a fresh token after releasing the lease, preventing a stale claim's ABA race.
        delivery.LeaseToken = Guid.NewGuid();
        delivery.LeaseUntilUtc = null;
        await db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class WelcomeEmailWorker(IServiceScopeFactory scopes, IOptions<EmailOptions> configured, ILogger<WelcomeEmailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configured.Value.Enabled || !configured.Value.WorkerEnabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                if (await scope.ServiceProvider.GetRequiredService<WelcomeEmailDispatcher>().DispatchOneAsync(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Welcome email worker could not complete a delivery ({FailureType}).", ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
