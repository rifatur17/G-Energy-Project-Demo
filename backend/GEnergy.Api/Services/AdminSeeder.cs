using System.ComponentModel.DataAnnotations;
using GEnergy.Api.Data;
using GEnergy.Api.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace GEnergy.Api.Services;

public static class AdminSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration config, IHostEnvironment env)
    {
        if (!env.IsDevelopment()) throw new InvalidOperationException("Admin bootstrap is development-only.");
        var email = config["AdminSeed:Email"]?.Trim() ?? "";
        var password = config["AdminSeed:Password"] ?? "";
        if (!new EmailAddressAttribute().IsValid(email) || email.Length > 254 || password.Length is < 12 or > 128)
            throw new InvalidOperationException("Provide a valid AdminSeed:Email and 12-128 character AdminSeed:Password via secure configuration.");
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EnergyDbContext>();
        var existing = await db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == email.ToUpperInvariant());
        if (existing is not null)
        {
            if (existing.Role != Roles.Admin) throw new InvalidOperationException("Seed email already belongs to a non-Admin account.");
            return;
        }
        var user = new User
        {
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            FullName = "Demo Administrator",
            Phone = "",
            Role = Roles.Admin,
            UserCode = await scope.ServiceProvider.GetRequiredService<IUserCodeGenerator>().NextAsync(Roles.Admin, default)
        };
        user.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>().HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
    }
}
