using GEnergy.Api.Data;
using GEnergy.Api.DTOs;
using GEnergy.Api.Services;
using GEnergy.Api.Services.Charging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace GEnergy.Api.Controllers;

[ApiController, Route("api/chargers"), Authorize]
public sealed class ChargerAvailabilityController(EnergyDbContext db, IChargerAvailabilityProvider availability, ChargingService service, TimeProvider clock) : ControllerBase
{
    [HttpGet("{id:long}/availability")]
    public async Task<ActionResult<ChargerAvailabilityResponse>> Get(long id, CancellationToken ct)
    {
        var charger = await db.Chargers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id && c.Station.IsActive, ct); if (charger is null) return NotFound();
        var now = clock.GetUtcNow().UtcDateTime;
        var expired = await db.Bookings.Where(b => b.ChargerId == id && b.Status == "Reserved" && b.ExpiresAtUtc <= now).Select(b => b.Id).ToListAsync(ct);
        foreach (var booking in expired) await service.RefreshBooking(booking, ct);
        var state = (await availability.GetAsync([charger], ct))[id]; return Ok(new ChargerAvailabilityResponse(id, state.EffectiveStatus, state.EffectiveStatus == "Available", clock.GetUtcNow().UtcDateTime));
    }
}
