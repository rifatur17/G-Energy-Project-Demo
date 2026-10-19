using System.Text;
using GEnergy.Api.Services.Admin;
using GEnergy.Api.Services.Wallets;
using GEnergy.Api.Services.Charging;
using System.Threading.RateLimiting;
using GEnergy.Api.Authorization;
using GEnergy.Api.Data;
using GEnergy.Api.Entities;
using GEnergy.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ChargingExceptionHandler>();
builder.Services.AddOptions<ChargingOptions>().BindConfiguration("Charging")
    .Validate(o => o.ReservationSeconds is >= 1 and <= 3600 && o.SimulationSpeed is >= 1 and <= 3600 && o.WorkerIntervalMilliseconds is >= 100 and <= 60000,
        "Invalid booking/charging demo configuration.").ValidateOnStart();
builder.Services.AddScoped<ChargingService>();
builder.Services.AddScoped<AdminQueries>();
builder.Services.AddScoped<AdminNetworkService>();
builder.Services.AddOptions<WalletOptions>().BindConfiguration("Wallet")
    .Validate(o => o.MaxTransactionAmount > 0 && o.MaxTransactionAmount <= 1000000 && o.MaxBalance >= o.MaxTransactionAmount && o.MaxBalance <= 10000000 && decimal.Round(o.MaxTransactionAmount,2)==o.MaxTransactionAmount && decimal.Round(o.MaxBalance,2)==o.MaxBalance,"Invalid demo wallet limits.").ValidateOnStart();
    builder.Services.AddScoped<IPaymentProvider, MockPaymentProvider>();
    builder.Services.AddScoped<WalletService>();
    builder.Services.AddScoped<IChargingSettlement, WalletChargingSettlement>();
builder.Services.AddHostedService<ChargingWorker>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", Description = "Paste the JWT access token from POST /api/auth/login." });
    o.AddSecurityRequirement(document => new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
});
builder.Services.AddDbContext<EnergyDbContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddOptions<JwtOptions>().BindConfiguration("Jwt")
    .Validate(o => Encoding.UTF8.GetByteCount(o.SigningKey) >= 32 && !string.IsNullOrWhiteSpace(o.Issuer) && !string.IsNullOrWhiteSpace(o.Audience) && o.ExpirationMinutes is >= 1 and <= 120,
        "Configure Jwt issuer, audience, a random signing key of at least 32 bytes, and expiry 1-120 minutes.").ValidateOnStart();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<IOptions<JwtOptions>>((o, configured) =>
{
    var jwt = configured.Value;
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
        NameClaimType = "userCode",
        RoleClaimType = "role",
        ClockSkew = TimeSpan.FromSeconds(5),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
    };
    o.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
    {
        var db = context.HttpContext.RequestServices.GetRequiredService<EnergyDbContext>();
        var role = context.Principal?.FindFirst("role")?.Value;
        var code = context.Principal?.FindFirst("userCode")?.Value;
        if (role is null || code is null || !long.TryParse(context.Principal?.FindFirst("sub")?.Value, out var id) ||
            !await db.Users.AsNoTracking().AnyAsync(u => u.Id == id && u.IsActive && u.Role == role && u.UserCode == code))
            context.Fail("Account is unavailable.");
    }
    };
});
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("VehicleUser", p => p.RequireAuthenticatedUser().RequireRole(Roles.CarOwner, Roles.CarDriver));
    foreach (var role in Roles.All) o.AddPolicy(role, p => p.RequireAuthenticatedUser().RequireRole(role));
    o.AddPolicy("ResourceOwner", p => p.RequireAuthenticatedUser().AddRequirements(new ResourceOwnerRequirement()));
});
builder.Services.AddSingleton<IAuthorizationHandler, ResourceOwnerHandler>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<VehicleAccess>();
builder.Services.AddScoped<IChargerAvailabilityProvider, BookingAvailabilityProvider>();
builder.Services.AddScoped<IVehicleActivityGuard, BookingActivityGuard>();
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<IUserCodeGenerator, UserCodeGenerator>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? []).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ =>
        new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
else app.UseHttpsRedirection();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
if (args.Contains("--seed-discovery"))
{
    await DiscoverySeeder.SeedAsync(app.Services, app.Environment);
    Console.WriteLine("Sample discovery locations seeded. These are not verified charging stations.");
    return;
}
if (args.Contains("--seed-admin"))
{
    var adminCode = await AdminSeeder.SeedAsync(app.Services, app.Configuration, app.Environment);
    Console.WriteLine($"Admin UserCode: {adminCode}");
    return;
}
app.Run();
public partial class Program;
