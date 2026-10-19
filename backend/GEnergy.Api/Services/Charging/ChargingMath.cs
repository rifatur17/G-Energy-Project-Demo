using GEnergy.Api.Entities;
namespace GEnergy.Api.Services.Charging;

public sealed record ChargingProgress(decimal EnergyKWh, decimal BatteryPercent, decimal Cost, decimal ElapsedSeconds, bool IsFull, DateTime EffectiveEndUtc);
public static class ChargingMath
{
    public static ChargingProgress Calculate(ChargingSession s, DateTime now)
    {
        var remaining = s.CapacityKWh * (100 - s.InitialBatteryPercent) / 100;
        var elapsed = Math.Max(0, (decimal)(now.Ticks - s.StartedAtUtc.Ticks) / TimeSpan.TicksPerSecond);
        var fullSeconds = remaining * 3600 / (s.PowerKW * s.SimulationSpeed);
        var effective = Math.Min(elapsed, fullSeconds);
        var raw = Math.Min(remaining, s.PowerKW * s.SimulationSpeed * effective / 3600);
        var energy = elapsed >= fullSeconds ? remaining : decimal.Round(raw, 6, MidpointRounding.ToZero);
        var battery = Math.Clamp(decimal.Round(s.InitialBatteryPercent + energy / s.CapacityKWh * 100, 2), 0, 100);
        var end = s.StartedAtUtc.AddTicks((long)(effective * TimeSpan.TicksPerSecond));
        return new(energy, battery, decimal.Round(energy * s.TariffPerKWh, 2, MidpointRounding.AwayFromZero), decimal.Round(effective, 3), elapsed >= fullSeconds, end);
    }
}
