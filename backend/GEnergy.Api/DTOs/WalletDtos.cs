using System.ComponentModel.DataAnnotations;
using GEnergy.Api.Entities;
namespace GEnergy.Api.DTOs;

public sealed record TopUpRequest(decimal Amount, [Required, RegularExpression("^(Success|Failure)$")] string Outcome);
public sealed record WalletResponse(long Id, decimal Balance, string Currency, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, decimal MaxTransactionAmount, decimal MaxBalance, bool IsDemo);
public sealed record WalletTransactionResponse(long Id, string Reference, string Type, string Status, decimal Amount, string Currency, string Description, long? ChargingSessionId, long? OriginalTransactionId, DateTime CreatedAtUtc, DateTime CompletedAtUtc, bool CanRefund, string? RefundReference);
public sealed record WalletOperationResponse(WalletTransactionResponse Transaction, decimal Balance, string Currency);
