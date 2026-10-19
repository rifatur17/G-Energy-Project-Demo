using System.ComponentModel.DataAnnotations;
using GEnergy.Api.Authorization;
using GEnergy.Api.DTOs;
using GEnergy.Api.Services.Charging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace GEnergy.Api.Controllers;

[ApiController, Route("api/bookings"), Authorize(Policy = "VehicleUser")]
public sealed class BookingsController(ChargingService service, CurrentUser user) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<BookingResponse>> Create(CreateBookingRequest request, [FromHeader(Name = "Idempotency-Key"), Required, StringLength(100)] string key, CancellationToken ct)
    { var result = await service.Reserve(user.Id, request, key, ct); return CreatedAtAction(nameof(Details), new { id = result.Id }, result); }
    [HttpGet] public Task<IReadOnlyList<BookingResponse>> List(CancellationToken ct) => service.Bookings(user.Id, ct);
    [HttpGet("{id:long}")] public Task<BookingResponse> Details(long id, CancellationToken ct) => service.GetBooking(id, user.Id, ct);
    [HttpPost("{id:long}/cancel")] public Task<BookingResponse> Cancel(long id, CancellationToken ct) => service.Cancel(id, user.Id, ct);
    [HttpPost("{id:long}/start")] public Task<SessionResponse> Start(long id, CancellationToken ct) => service.Start(id, user.Id, ct);
}
