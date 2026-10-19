using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using GEnergy.Api.Data;
using GEnergy.Api.DTOs;
using GEnergy.Api.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace GEnergy.IntegrationTests;
[Collection("SharedSqlServer")]
public sealed class SharedIdentitySqlServerTests
{
    [SharedSqlServerFact]
    public async Task Registration_login_and_concurrent_uniqueness_persist_in_GEnergydB()
    {
        var prefix="sql-identity-"+Guid.NewGuid().ToString("N");
        var password=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        UserResponse first;
        await using (var app=new SharedSqlServerFactory())
        {
            using(var scope=app.Services.CreateScope())
            {
                var db=scope.ServiceProvider.GetRequiredService<EnergyDbContext>();
                Assert.True(db.Database.IsSqlServer());Assert.Equal("GEnergyDb",db.Database.GetDbConnection().Database);
                Assert.Contains("20261018185818_InitialIdentity",await db.Database.GetAppliedMigrationsAsync());
            }
            using var client=app.CreateClient();
            var responses=await Task.WhenAll(Enumerable.Range(0,8).Select(i=>client.PostAsJsonAsync("/api/auth/register",
                new RegisterRequest("SQL verification identity",$"{prefix}-{i}@example.test","01700000000",password,i%2==0?Roles.CarOwner:Roles.CarDriver))));
            Assert.All(responses,r=>Assert.Equal(HttpStatusCode.Created,r.StatusCode));
            var users=await Task.WhenAll(responses.Select(async r=>(await r.Content.ReadFromJsonAsync<UserResponse>())!));first=users[0];
            Assert.Equal(8,users.Select(u=>u.UserCode).Distinct().Count());
            foreach(var response in responses)Assert.DoesNotContain("password",await response.Content.ReadAsStringAsync(),StringComparison.OrdinalIgnoreCase);
            var duplicateEmail=$"{prefix}-duplicate@example.test";
            var duplicates=await Task.WhenAll(Enumerable.Range(0,2).Select(i=>client.PostAsJsonAsync("/api/auth/register",
                new RegisterRequest("SQL verification duplicate",i==0?duplicateEmail:duplicateEmail.ToUpperInvariant(),"01700000000",password,Roles.CarOwner))));
            Assert.Single(duplicates,r=>r.StatusCode==HttpStatusCode.Created);Assert.Single(duplicates,r=>r.StatusCode==HttpStatusCode.Conflict);
            using(var scope=app.Services.CreateScope())
            {
                var db=scope.ServiceProvider.GetRequiredService<EnergyDbContext>();
                var stored=await db.Users.AsNoTracking().Where(u=>u.Email.StartsWith(prefix)).ToListAsync();Assert.Equal(9,stored.Count);
                Assert.All(stored,u=>{
                    Assert.False(u.PasswordHash==password);Assert.NotEqual(PasswordVerificationResult.Failed,new PasswordHasher<User>().VerifyHashedPassword(u,u.PasswordHash,password));
                    Assert.Equal(u.Email.ToUpperInvariant(),u.NormalizedEmail);Assert.Equal(DateTimeKind.Utc,u.CreatedAtUtc.Kind);
                });
            }
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/auth/register",new RegisterRequest("SQL rejected Admin",$"{prefix}-admin@example.test","01700000000",password,Roles.Admin))).StatusCode);
            var bad=await client.PostAsJsonAsync("/api/auth/login",new LoginRequest(first.UserCode,"incorrect-passphrase"));Assert.Equal(HttpStatusCode.Unauthorized,bad.StatusCode);
        }
        // A fresh host/context must authenticate the same persisted account, with no in-memory provider.
        await using(var restarted=new SharedSqlServerFactory())
        {
            using var client=restarted.CreateClient();
            var login=await client.PostAsJsonAsync("/api/auth/login",new LoginRequest(first.UserCode,password));Assert.Equal(HttpStatusCode.OK,login.StatusCode);
            var session=(await login.Content.ReadFromJsonAsync<LoginResponse>())!;client.DefaultRequestHeaders.Authorization=new("Bearer",session.AccessToken);
            var me=await client.GetAsync("/api/auth/me");Assert.Equal(HttpStatusCode.OK,me.StatusCode);
            Assert.Equal(first.Id,(await me.Content.ReadFromJsonAsync<UserResponse>())!.Id);Assert.DoesNotContain("password",await me.Content.ReadAsStringAsync(),StringComparison.OrdinalIgnoreCase);
        }
    }
}
