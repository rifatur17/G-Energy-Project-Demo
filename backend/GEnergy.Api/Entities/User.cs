namespace GEnergy.Api.Entities;

public static class Roles
{
    public const string Admin = "Admin", CarOwner = "CarOwner", CarDriver = "CarDriver";
    public static readonly string[] All = [Admin, CarOwner, CarDriver];
}
public sealed class User
{
    public long Id { get; set; }
    public string UserCode { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string Phone { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
