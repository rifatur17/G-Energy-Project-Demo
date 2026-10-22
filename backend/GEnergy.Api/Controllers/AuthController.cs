using GEnergy.Api.Data;
using GEnergy.Api.DTOs;
using GEnergy.Api.Entities;
using GEnergy.Api.Services;
using GEnergy.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using GEnergy.Api.Services.Email;
using Microsoft.Extensions.Options;
namespace GEnergy.Api.Controllers;

[ApiController, Route("api/auth")]
public sealed class AuthController(EnergyDbContext db, IPasswordHasher<User> hasher, IUserCodeGenerator codes, TokenService tokens, CurrentUser current, IOptions<EmailOptions> emailOptions, TimeProvider clock) : ControllerBase
{
    private static readonly User DummyUser = new();
    private static readonly string DummyHash = new PasswordHasher<User>().HashPassword(DummyUser, Guid.NewGuid().ToString());
    [HttpPost("register"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<ActionResult<UserResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        var name = request.FullName.Trim();
        var email = request.Email.Trim();
        if (name.Length < 2 || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
            return Problem(statusCode: 400, title: "Invalid name or email.");
        var normalized = email.ToUpperInvariant();
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct))
            return Problem(statusCode: 409, title: "Email is already registered.");
        var user = new User
        {
            FullName = name,
            Email = email,
            NormalizedEmail = normalized,
            Phone = request.Phone.Trim(),
            Role = request.Role,
            UserCode = await codes.NextAsync(request.Role, ct)
        };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        // User and welcome delivery are saved in one SQL transaction. SMTP never blocks signup.
        var queuedAt = clock.GetUtcNow().UtcDateTime;
        db.WelcomeEmails.Add(new WelcomeEmail { User = user, CreatedAtUtc = queuedAt, NextAttemptAtUtc = queuedAt });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 2601 or 2627 })
        { return Problem(statusCode: 409, title: "Account conflicts with an existing registration."); }
        return Created("/api/auth/me", UserResponse.From(user) with { WelcomeEmailStatus = emailOptions.Value.Enabled ? "Queued" : "NotConfigured" });
    }
    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var code = request.UserCode.Trim().ToUpperInvariant();
        var user = await db.Users.SingleOrDefaultAsync(u => u.UserCode == code, ct);
        var result = hasher.VerifyHashedPassword(user ?? DummyUser, user?.PasswordHash ?? DummyHash, request.Password);
        if (user is null || !user.IsActive || result == PasswordVerificationResult.Failed)
            return Problem(statusCode: 401, title: "Invalid UserCode or password.");
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        { user.PasswordHash = hasher.HashPassword(user, request.Password); await db.SaveChangesAsync(ct); }
        return Ok(tokens.Create(user));
    }
    [HttpGet("me"), Authorize]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == current.Id && u.IsActive, ct);
        return user is null ? Unauthorized() : Ok(UserResponse.From(user));
    }
}
