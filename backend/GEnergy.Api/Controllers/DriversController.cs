using System.ComponentModel.DataAnnotations;
using GEnergy.Api.Data;
using GEnergy.Api.DTOs;
using GEnergy.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace GEnergy.Api.Controllers;

[ApiController, Route("api/drivers"), Authorize(Policy = Roles.CarOwner)]
public sealed class DriversController(EnergyDbContext db) : ControllerBase
{
    [HttpGet("lookup")]
    public async Task<ActionResult<DriverResponse>> Lookup([FromQuery, Required, StringLength(24)] string userCode, CancellationToken ct)
    {
        var code = userCode.Trim().ToUpperInvariant();
        var driver = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.UserCode == code && u.IsActive && u.Role == Roles.CarDriver, ct);
        return driver is null ? NotFound() : Ok(new DriverResponse(driver.Id, driver.UserCode, driver.FullName, false, false));
    }
}
