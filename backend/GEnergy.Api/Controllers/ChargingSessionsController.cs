using GEnergy.Api.Authorization;
using GEnergy.Api.DTOs;
using GEnergy.Api.Services.Charging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace GEnergy.Api.Controllers;

[ApiController, Route("api/charging-sessions"), Authorize(Policy = "VehicleUser")]
public sealed class ChargingSessionsController(ChargingService service, CurrentUser user) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<SessionResponse>> List(CancellationToken ct) => service.Sessions(user.Id, false, ct);
    [HttpGet("active")] public Task<IReadOnlyList<SessionResponse>> Active(CancellationToken ct) => service.Sessions(user.Id, true, ct);
    [HttpGet("{id:long}")] public Task<SessionResponse> Details(long id, CancellationToken ct) => service.GetSession(id, user.Id, false, ct);
    [HttpPost("{id:long}/stop")] public Task<SessionResponse> Stop(long id, CancellationToken ct) => service.GetSession(id, user.Id, true, ct);
}
