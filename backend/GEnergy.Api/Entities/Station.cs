namespace GEnergy.Api.Entities;

public sealed class Station
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDemo { get; set; } = true;
    public List<Charger> Chargers { get; set; } = [];
}
public sealed class Charger
{
    public long Id { get; set; }
    public long StationId { get; set; }
    public Station Station { get; set; } = null!;
    public string ChargerCode { get; set; } = "";
    public string ConnectorType { get; set; } = "";
    public decimal PowerKW { get; set; }
    public decimal PricePerKWh { get; set; }
    public string Status { get; set; } = "Available";
    public byte[] RowVersion { get; set; } = [];
}
