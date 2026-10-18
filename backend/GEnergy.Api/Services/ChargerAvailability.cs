using GEnergy.Api.Entities;
namespace GEnergy.Api.Services;

public sealed record ChargerAvailability(string EffectiveStatus, string Source);
public interface IChargerAvailabilityProvider
{
    // Member 3 resolves expired holds from UTC timestamps, in one query batch.
    Task<IReadOnlyDictionary<long, ChargerAvailability>> GetAsync(IReadOnlyList<Charger> chargers, CancellationToken ct);
}
public sealed class ConfiguredChargerAvailability(IConfiguration config) : IChargerAvailabilityProvider
{
    public Task<IReadOnlyDictionary<long, ChargerAvailability>> GetAsync(IReadOnlyList<Charger> chargers, CancellationToken ct)
    {
        var integrationRequired = config.GetValue<bool>("Modules:BookingEnabled");
        return Task.FromResult<IReadOnlyDictionary<long, ChargerAvailability>>(chargers.ToDictionary(c => c.Id, c =>
            new ChargerAvailability(c.Status == "Offline" ? "Offline" : integrationRequired ? "Unknown" : c.Status, integrationRequired ? "IntegrationRequired" : "ConfiguredStatus")));
    }
}
