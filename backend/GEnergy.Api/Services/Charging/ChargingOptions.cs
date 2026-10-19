namespace GEnergy.Api.Services.Charging;

public sealed class ChargingOptions
{
    public int ReservationSeconds { get; set; } = 180;
    public decimal SimulationSpeed { get; set; } = 60;
    public int WorkerIntervalMilliseconds { get; set; } = 5000;
    public bool WorkerEnabled { get; set; } = true;
}
public sealed class ChargingException(int status, string message) : Exception(message) { public int Status { get; } = status; }
