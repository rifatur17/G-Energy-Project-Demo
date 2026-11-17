using GEnergy.Api.Authorization;
using GEnergy.Api.DTOs;
using GEnergy.Api.Services.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace GEnergy.Api.Controllers;

[ApiController, Route("api/billing"), Authorize(Policy = "CarOwner")]
public sealed class BillingController(BillingService billing, CurrentUser user) : ControllerBase
{
    [HttpGet("summary")] public Task<BillingSummaryResponse> Summary(CancellationToken ct) => billing.Summary(user.Id, ct);
    [HttpGet("unbilled")] public Task<IReadOnlyList<UnbilledSessionResponse>> Unbilled(CancellationToken ct) => billing.UnbilledSessions(user.Id, ct);
    [HttpGet("statements")] public Task<IReadOnlyList<StatementResponse>> Statements(CancellationToken ct) => billing.Statements(user.Id, ct);
    [HttpGet("statements/{id:long}")] public Task<StatementDetailResponse> Details(long id, CancellationToken ct) => billing.Details(user.Id, id, ct);
    [HttpPost("statements/close")] public Task<StatementResponse> Close(CancellationToken ct) => billing.CloseNow(user.Id, ct);
    [HttpPost("statements/{id:long}/checkout")] public Task<CheckoutResponse> Checkout(long id, CancellationToken ct) => billing.Checkout(user.Id, id, ct);
}
