using System.ComponentModel.DataAnnotations;
using GEnergy.Api.Data;
using GEnergy.Api.DTOs;
using GEnergy.Api.Entities;
using GEnergy.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace GEnergy.Api.Controllers;

[ApiController, Route("api/stations"), Authorize]
public sealed class StationsController(EnergyDbContext db, IChargerAvailabilityProvider availability) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<StationResponse>>> List(
        [FromQuery, RegularExpression("^(CCS2|Type2|CHAdeMO)$")] string? connectorType,
        [FromQuery, StringLength(120)] string? search, CancellationToken ct)
    {
        var query = db.Stations.AsNoTracking().Include(s => s.Chargers).Where(s => s.IsActive);
        if (connectorType is not null) query = query.Where(s => s.Chargers.Any(c => c.ConnectorType == connectorType));
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim().ToUpperInvariant(); query = query.Where(s => s.Name.ToUpper().Contains(term) || s.Address.ToUpper().Contains(term)); }
        var stations = await query.OrderBy(s => s.Id).ToListAsync(ct);
        return Ok(await Responses(stations, connectorType, ct));
    }
    [HttpGet("{id:long}")]
    public async Task<ActionResult<StationResponse>> Details(long id, CancellationToken ct)
    {
        var station = await db.Stations.AsNoTracking().Include(s => s.Chargers).SingleOrDefaultAsync(s => s.Id == id && s.IsActive, ct);
        return station is null ? NotFound() : Ok((await Responses([station], null, ct)).Single());
    }
    private async Task<IReadOnlyList<StationResponse>> Responses(IReadOnlyList<Station> stations, string? connector, CancellationToken ct)
    {
        var chargers = stations.SelectMany(s => s.Chargers).Where(c => connector is null || c.ConnectorType == connector).ToList();
        var effective = await availability.GetAsync(chargers, ct);
        return stations.Select(s => new StationResponse(s.Id, s.Name, s.Address, s.Latitude, s.Longitude, s.IsDemo,
            s.Chargers.Where(c => connector is null || c.ConnectorType == connector).OrderBy(c => c.Id).Select(c =>
            {
                var status = c.Status == "Offline" ? new ChargerAvailability("Offline", "ConfiguredStatus") :
                    effective.GetValueOrDefault(c.Id) ?? new ChargerAvailability("Unknown", "IntegrationRequired");
                return new ChargerResponse(c.Id, c.ChargerCode, c.ConnectorType, c.PowerKW, c.PricePerKWh, "BDT", c.Status, status.EffectiveStatus, status.EffectiveStatus == "Available", status.Source);
            }).ToList())).ToList();
    }
}
