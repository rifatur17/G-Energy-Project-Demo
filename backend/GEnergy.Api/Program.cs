using System.Text;
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
    foreach (var role in Roles.All) o.AddPolicy(role, p => p.RequireAuthenticatedUser().RequireRole(role));
    o.AddPolicy("ResourceOwner", p => p.RequireAuthenticatedUser().AddRequirements(new ResourceOwnerRequirement()));
});
builder.Services.AddSingleton<IAuthorizationHandler, ResourceOwnerHandler>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
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
if (args.Contains("--seed-admin"))
{
    await AdminSeeder.SeedAsync(app.Services, app.Configuration, app.Environment);
    return;
}
app.Run();
public partial class Program;
