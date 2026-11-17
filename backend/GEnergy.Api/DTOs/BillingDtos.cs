using GEnergy.Api.Entities;
namespace GEnergy.Api.DTOs;

public sealed record BillingSummaryResponse(decimal UnbilledAmount, int UnbilledSessions, int ActiveSessions, decimal PlanFeeDue, decimal OutstandingAmount, int OverdueStatements,
    int Vehicles, int VehiclesOnPlan, decimal PlanFeePerVehicle, int FreeMonths, DateTime? NextTrialEndsUtc, int DueDays, bool CanCloseMonth, string Gateway, string Currency);
public sealed record UnbilledSessionResponse(long SessionId, DateTime StartedAtUtc, DateTime? EndedAtUtc, string StationName, string ChargerCode, string VehicleRegistration, string ChargedBy, decimal EnergyKWh, decimal Cost);
public sealed record StatementResponse(long Id, string StatementNumber, string PeriodKey, DateTime PeriodStartUtc, DateTime PeriodEndUtc, DateTime IssuedAtUtc, DateTime DueAtUtc,
    int SessionCount, decimal ChargingTotal, int PlanVehicleCount, decimal PlanFee, decimal Total, string Status, DateTime? PaidAtUtc, string? PaymentReference, string Currency)
{
    public static StatementResponse From(BillingStatement s, string status) => new(s.Id, s.StatementNumber, s.PeriodKey, s.PeriodStartUtc, s.PeriodEndUtc, s.IssuedAtUtc, s.DueAtUtc,
        s.SessionCount, s.ChargingTotal, s.PlanVehicleCount, s.PlanFee, s.Total, status, s.PaidAtUtc, s.PaymentReference, "BDT");
}
public sealed record StatementPaymentResponse(long Id, string TranId, string Gateway, decimal Amount, string Status, string? Method, string? BankTranId, string? FailureReason, DateTime CreatedAtUtc, DateTime? CompletedAtUtc);
public sealed record StatementDetailResponse(StatementResponse Statement, IReadOnlyList<UnbilledSessionResponse> Lines, IReadOnlyList<StatementPaymentResponse> Payments);
public sealed record TopUpCheckoutRequest([System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0.01", "1000000")] decimal Amount);
public sealed record TopUpAttemptResponse(long Id, string TranId, string Gateway, decimal Amount, string Status, string? Method, string? FailureReason, DateTime CreatedAtUtc, DateTime? CompletedAtUtc);
public sealed record CheckoutResponse(string TranId, string RedirectUrl, string Gateway, decimal Amount, string Currency);
