using System.ComponentModel.DataAnnotations;
using GEnergy.Api.DTOs;
using GEnergy.Api.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace GEnergy.Api.Controllers;

[ApiController, Route("api/admin"), Authorize(Policy = "Admin")]
public sealed class AdminController(AdminQueries queries, AdminNetworkService network) : ControllerBase
{
    private static void Dates(DateTime? from, DateTime? to) { if (from.HasValue && from.Value.Kind != DateTimeKind.Utc || to.HasValue && to.Value.Kind != DateTimeKind.Utc || from > to) throw new GEnergy.Api.Services.Charging.ChargingException(400, "Use UTC timestamps and an ordered date range."); }
    [HttpGet("dashboard")] public Task<AdminDashboardResponse> Dashboard(CancellationToken ct) => queries.Dashboard(ct);
    [HttpGet("users")] public Task<List<AdminUserResponse>> Users([RegularExpression("^(Admin|CarOwner|CarDriver)$")] string? role, [Range(1, long.MaxValue)] long? before, [Range(1, 100)] int take = 20, CancellationToken ct = default) => queries.Users(role, before, take, ct);
    [HttpGet("stations")] public Task<IReadOnlyList<AdminStationResponse>> Stations([StringLength(120)] string? search, bool? active, [Range(1, long.MaxValue)] long? before, [Range(1, 100)] int take = 20, CancellationToken ct = default) => network.Stations(search, active, before, take, ct);
    [HttpPost("stations")] public async Task<ActionResult<AdminStationResponse>> CreateStation(AdminStationRequest request, CancellationToken ct) { var result = await network.CreateStation(request, ct); return StatusCode(201, result); }
    [HttpPut("stations/{id:long}")] public Task<AdminStationResponse> UpdateStation(long id, AdminStationRequest request, [FromHeader(Name = "If-Match"), Required] string version, CancellationToken ct) => network.UpdateStation(id, request, version, ct);
    [HttpGet("stations/{id:long}/chargers")] public Task<IReadOnlyList<AdminChargerResponse>> Chargers(long id, [Range(1, long.MaxValue)] long? before, [Range(1, 100)] int take = 20, CancellationToken ct = default) => network.Chargers(id, before, take, ct);
    [HttpPost("stations/{id:long}/chargers")] public async Task<ActionResult<AdminChargerResponse>> CreateCharger(long id, AdminChargerRequest request, CancellationToken ct) { var result = await network.CreateCharger(id, request, ct); return StatusCode(201, result); }
    [HttpPut("chargers/{id:long}")] public Task<AdminChargerResponse> UpdateCharger(long id, AdminChargerRequest request, [FromHeader(Name = "If-Match"), Required] string version, CancellationToken ct) => network.UpdateCharger(id, request, version, ct);
    [HttpGet("bookings")] public Task<List<AdminBookingResponse>> Bookings([RegularExpression("^(Reserved|Expired|Cancelled|Consumed)$")] string? status, [Range(1, long.MaxValue)] long? stationId, DateTime? from, DateTime? to, [Range(1, long.MaxValue)] long? before, [Range(1, 100)] int take = 20, CancellationToken ct = default) { Dates(from, to); return queries.Bookings(status, stationId, from, to, before, take, ct); }
    [HttpGet("charging-sessions")] public Task<List<AdminSessionResponse>> Sessions([RegularExpression("^(Active|Completed)$")] string? status, [RegularExpression("^(Paid|Unpaid)$")] string? payment, [Range(1, long.MaxValue)] long? stationId, DateTime? from, DateTime? to, [Range(1, long.MaxValue)] long? before, [Range(1, 100)] int take = 20, CancellationToken ct = default) { Dates(from, to); return queries.Sessions(status, payment, stationId, from, to, before, take, ct); }
    [HttpGet("wallet-transactions")] public Task<IReadOnlyList<AdminTransactionResponse>> Transactions([RegularExpression("^(TopUp|ChargingPayment|Refund)$")] string? type, [RegularExpression("^(Completed|Failed)$")] string? status, DateTime? from, DateTime? to, [Range(1, long.MaxValue)] long? before, [Range(1, 100)] int take = 20, CancellationToken ct = default) { Dates(from, to); return queries.Transactions(type, status, from, to, before, take, ct); }
    [HttpGet("wallets")] public Task<List<AdminWalletResponse>> Wallets([Range(1, long.MaxValue)] long? before, [Range(1, 100)] int take = 20, CancellationToken ct = default) => queries.Wallets(before, take, ct);
}
