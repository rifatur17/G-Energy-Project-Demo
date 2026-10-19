using System.ComponentModel.DataAnnotations;
using GEnergy.Api.Authorization;
using GEnergy.Api.DTOs;
using GEnergy.Api.Services.Wallets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace GEnergy.Api.Controllers;

[ApiController, Route("api/wallet"), Authorize(Policy = "CarOwner")]
public sealed class WalletController(WalletService wallets, CurrentUser user) : ControllerBase
{
    [HttpGet] public Task<WalletResponse> Get(CancellationToken ct) => wallets.Get(user.Id, ct);
    [HttpGet("transactions")] public Task<IReadOnlyList<WalletTransactionResponse>> History([Range(1, long.MaxValue)] long? before, [Range(1, 100)] int take = 20, CancellationToken ct = default) => wallets.History(user.Id, before, take, ct);
    [HttpGet("transactions/{id:long}")] public Task<WalletTransactionResponse> Details(long id, CancellationToken ct) => wallets.Details(user.Id, id, ct);
    [HttpPost("top-up")] public Task<WalletOperationResponse> TopUp(TopUpRequest request, [FromHeader(Name = "Idempotency-Key"), Required, StringLength(100)] string key, CancellationToken ct) => wallets.TopUp(user.Id, request, key, ct);
    [HttpPost("transactions/{id:long}/refund")] public Task<WalletOperationResponse> Refund(long id, CancellationToken ct) => wallets.Refund(user.Id, id, ct);
    [HttpPost("/api/charging-sessions/{sessionId:long}/pay")] public Task<WalletOperationResponse> Pay(long sessionId, CancellationToken ct) => wallets.Pay(user.Id, sessionId, ct);
}
