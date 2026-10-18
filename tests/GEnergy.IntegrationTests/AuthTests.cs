using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using GEnergy.Api.Data;
using GEnergy.Api.DTOs;
using GEnergy.Api.Entities;
using GEnergy.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
namespace GEnergy.IntegrationTests;

public sealed class AuthTests
{
    private const string Password = "Test-only-long-passphrase";
    private static object Registration(string email = "owner@example.test", string role = "CarOwner") => new { fullName = "  Demo Owner  ", email, phone = "01700000000", password = Password, role };
    [Theory]
    [InlineData("CarOwner", "OWN", "owner")]
    [InlineData("CarDriver", "DRV", "driver")]
    public async Task Register_login_me_and_role_isolation(string role, string prefix, string route)
    {
        await using var app = new AuthFactory(); using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", Registration(role: role));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("password", raw, StringComparison.OrdinalIgnoreCase);
        var user = (await response.Content.ReadFromJsonAsync<UserResponse>())!;
        Assert.StartsWith($"GE-{prefix}-", user.UserCode); Assert.Equal("Demo Owner", user.FullName);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/auth/register", Registration("OWNER@EXAMPLE.TEST", role))).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var saved = await scope.ServiceProvider.GetRequiredService<EnergyDbContext>().Users.SingleAsync();
            Assert.NotEqual(Password, saved.PasswordHash);
            Assert.NotEqual(PasswordVerificationResult.Failed, new PasswordHasher<User>().VerifyHashedPassword(saved, saved.PasswordHash, Password));
        }
        var login = await client.PostAsJsonAsync("/api/auth/login", new { userCode = user.UserCode.ToLowerInvariant(), password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.Equal(role, token.Role); Assert.True(token.ExpiresAtUtc > DateTime.UtcNow);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var me = await client.GetAsync("/api/auth/me"); Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(user.Id, (await me.Content.ReadFromJsonAsync<UserResponse>())!.Id);
        Assert.DoesNotContain("password", await me.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/test/policies/{route}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/test/policies/admin")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/test/policies/{(route == "owner" ? "driver" : "owner")}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/test/policies/resource/{user.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/test/policies/resource/{user.Id + 1}")).StatusCode);
        using (var scope = app.Services.CreateScope())
        { var db = scope.ServiceProvider.GetRequiredService<EnergyDbContext>(); (await db.Users.SingleAsync()).IsActive = false; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }
    [Fact]
    public async Task Public_admin_invalid_fields_and_unknown_roles_are_rejected()
    {
        await using var app = new AuthFactory(); using var client = app.CreateClient();
        foreach (var role in new[] { "Admin", "admin", "SuperUser", "" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", Registration(role: role))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", new { fullName = " ", email = "invalid", phone = "abc", password = "short", role = "CarOwner" })).StatusCode);
    }
    [Theory]
    [InlineData("+12345678901234567890")]
    [InlineData("-------")]
    public async Task Invalid_phone_is_rejected_before_database_write(string phone)
    {
        await using var app = new AuthFactory(); using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new { fullName = "Demo Owner", email = "phone@example.test", phone, password = Password, role = "CarOwner" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
    [Fact]
    public async Task Login_failure_is_generic_and_client_role_is_ignored()
    {
        await using var app = new AuthFactory(); using var client = app.CreateClient();
        var registered = await client.PostAsJsonAsync("/api/auth/register", Registration()); var user = (await registered.Content.ReadFromJsonAsync<UserResponse>())!;
        var bad = await client.PostAsJsonAsync("/api/auth/login", new { userCode = user.UserCode, password = "wrong", role = "Admin" });
        var unknown = await client.PostAsJsonAsync("/api/auth/login", new { userCode = "GE-OWN-999999", password = "wrong" });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode); Assert.Equal(bad.StatusCode, unknown.StatusCode);
        var a = System.Text.Json.JsonDocument.Parse(await bad.Content.ReadAsStringAsync()); var b = System.Text.Json.JsonDocument.Parse(await unknown.Content.ReadAsStringAsync());
        Assert.Equal(a.RootElement.GetProperty("title").GetString(), b.RootElement.GetProperty("title").GetString());
        var valid = await client.PostAsJsonAsync("/api/auth/login", new { userCode = user.UserCode, password = Password, role = "Admin" });
        Assert.Equal("CarOwner", (await valid.Content.ReadFromJsonAsync<LoginResponse>())!.Role);
    }
    [Fact]
    public async Task Missing_tampered_expired_wrong_issuer_and_wrong_audience_tokens_fail()
    {
        await using var app = new AuthFactory(); using var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        var r = await client.PostAsJsonAsync("/api/auth/register", Registration()); var u = (await r.Content.ReadFromJsonAsync<UserResponse>())!;
        var token = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<JwtOptions>>().Value;
        foreach (var variant in new[] { "expired", "issuer", "audience", "signature", "role", "claims" })
        {
            var now = DateTime.UtcNow; var jwt = new JwtSecurityToken(variant == "issuer" ? "wrong" : token.Issuer, variant == "audience" ? "wrong" : token.Audience,
                [new("sub", u.Id.ToString()), new("role", variant == "role" ? "Admin" : u.Role), new("userCode", variant == "claims" ? "wrong" : u.UserCode)], now.AddHours(-2), variant == "expired" ? now.AddHours(-1) : now.AddHours(1),
                new Microsoft.IdentityModel.Tokens.SigningCredentials(new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(variant == "signature" ? new string('x', 48) : token.SigningKey)), "HS256"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        }
    }
    [Fact]
    public async Task Seeded_admin_can_access_admin_policy_and_seed_is_idempotent()
    {
        await using var app = new AuthFactory(); using var client = app.CreateClient();
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AdminSeed:Email"] = "admin@example.test", ["AdminSeed:Password"] = Password }).Build();
        var env = app.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>();
        await AdminSeeder.SeedAsync(app.Services, config, env); await AdminSeeder.SeedAsync(app.Services, config, env);
        using var scope = app.Services.CreateScope(); var users = await scope.ServiceProvider.GetRequiredService<EnergyDbContext>().Users.ToListAsync(); Assert.Single(users);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { userCode = users[0].UserCode, password = Password }); var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/test/policies/admin")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/test/policies/owner")).StatusCode);
    }
    [Fact]
    public async Task Auth_endpoints_are_rate_limited()
    {
        await using var app = new AuthFactory(); using var client = app.CreateClient();
        for (var i = 0; i < 20; i++) await client.PostAsJsonAsync("/api/auth/login", new { userCode = "x", password = "wrong" });
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/auth/login", new { userCode = "x", password = "wrong" })).StatusCode);
    }
    [Fact]
    public async Task Production_disables_swagger_and_admin_bootstrap()
    {
        await using var source = new AuthFactory();
        await using var app = source.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode);
        await Assert.ThrowsAsync<InvalidOperationException>(() => AdminSeeder.SeedAsync(app.Services,
            new ConfigurationBuilder().Build(), app.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>()));
    }
    [Fact]
    public async Task Swagger_contract_is_available_only_in_development()
    {
        await using var app = new AuthFactory(); using var client = app.CreateClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(); Assert.Contains("/api/auth/register", json); Assert.Contains("Bearer", json); Assert.DoesNotContain("passwordHash", json);
    }
}
