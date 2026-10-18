using System.ComponentModel.DataAnnotations;
using GEnergy.Api.Entities;
namespace GEnergy.Api.DTOs;

public sealed record RegisterRequest(
    [Required, StringLength(100, MinimumLength = 2)] string FullName,
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, RegularExpression(@"^\+?[0-9 ()-]{7,20}$")] string Phone,
    [Required, StringLength(128, MinimumLength = 12)] string Password,
    [Required, RegularExpression("^(CarOwner|CarDriver)$", ErrorMessage = "Public registration supports CarOwner or CarDriver only.")] string Role);
public sealed record LoginRequest(
    [Required, StringLength(24)] string UserCode,
    [Required, StringLength(128)] string Password);
public sealed record UserResponse(long Id, string UserCode, string FullName, string Email, string Phone, string Role, DateTime CreatedAtUtc)
{
    public static UserResponse From(User u) => new(u.Id, u.UserCode, u.FullName, u.Email, u.Phone, u.Role, u.CreatedAtUtc);
}
public sealed record LoginResponse(string AccessToken, string UserCode, string Role, DateTime ExpiresAtUtc);
