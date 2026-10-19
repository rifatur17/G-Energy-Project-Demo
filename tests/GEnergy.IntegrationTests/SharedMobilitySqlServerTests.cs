using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using GEnergy.Api.Data;
using GEnergy.Api.DTOs;
using GEnergy.Api.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace GEnergy.IntegrationTests;
[Collection("SharedSqlServer")]
public sealed class SharedMobilitySqlServerTests
{
    private static async Task<(UserResponse User,LoginResponse Session)> Account(HttpClient client,string email,string role,string password)
    {
        var registration=await client.PostAsJsonAsync("/api/auth/register",new RegisterRequest("SQL mobility verification",email,"01700000000",password,role));
        Assert.Equal(HttpStatusCode.Created,registration.StatusCode);var user=(await registration.Content.ReadFromJsonAsync<UserResponse>())!;
        var login=await client.PostAsJsonAsync("/api/auth/login",new LoginRequest(user.UserCode,password));Assert.Equal(HttpStatusCode.OK,login.StatusCode);
        var session=(await login.Content.ReadFromJsonAsync<LoginResponse>())!;client.DefaultRequestHeaders.Authorization=new("Bearer",session.AccessToken);return(user,session);
    }
    private static async Task<HttpResponseMessage> Mutation(HttpClient client,HttpMethod method,string path,VehicleResponse vehicle,object body)
    {
        using var request=new HttpRequestMessage(method,path){Content=JsonContent.Create(body)};
        request.Headers.TryAddWithoutValidation("If-Match",vehicle.RowVersion);return await client.SendAsync(request);
    }
    private static async Task Constraint(EnergyDbContext db,FormattableString sql,params int[] errorNumbers)
    {
        await using var transaction=await db.Database.BeginTransactionAsync();
        var exception=await Assert.ThrowsAsync<SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync(sql));
        Assert.Contains(exception.Number,errorNumbers);await transaction.RollbackAsync();
    }
    [SharedSqlServerFact]
    public async Task Vehicle_assignment_discovery_and_constraints_survive_host_restart_in_GEnergydB()
    {
        var tag=Guid.NewGuid().ToString("N");var password=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        VehicleResponse vehicle;UserResponse owner,driver;long[] previousIds;byte[] previousDigest;
        await using(var app=new SharedSqlServerFactory())
        {
            using(var scope=app.Services.CreateScope())
            {
                var db=scope.ServiceProvider.GetRequiredService<EnergyDbContext>();Assert.True(db.Database.IsSqlServer());Assert.Equal("GEnergyDb",db.Database.GetDbConnection().Database);
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
                var existing=await db.Users.AsNoTracking().OrderBy(u=>u.Id).ToListAsync();previousIds=existing.Select(u=>u.Id).ToArray();previousDigest=SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(existing));
            }
            using var client=app.CreateClient();
            var a=await Account(client,$"sql-mobility-{tag}-owner@example.test",Roles.CarOwner,password);owner=a.User;
            var input=new VehicleRequest("QA-"+tag[..20],"SQL persisted demo EV","CCS2",50,40,0.18m);
            var created=await client.PostAsJsonAsync("/api/vehicles",input);Assert.Equal(HttpStatusCode.Created,created.StatusCode);vehicle=(await created.Content.ReadFromJsonAsync<VehicleResponse>())!;
            Assert.Equal(111.11m,vehicle.EstimatedRangeKm);Assert.True(vehicle.IsSimulated);Assert.Equal(8,Convert.FromBase64String(vehicle.RowVersion).Length);
            var b=await Account(client,$"sql-mobility-{tag}-other@example.test",Roles.CarOwner,password);
            Assert.DoesNotContain((await client.GetFromJsonAsync<VehicleResponse[]>("/api/vehicles"))!,v=>v.Id==vehicle.Id);
            Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/vehicles/{vehicle.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await Mutation(client,HttpMethod.Put,$"/api/vehicles/{vehicle.Id}",vehicle,input)).StatusCode);
            var d=await Account(client,$"sql-mobility-{tag}-driver@example.test",Roles.CarDriver,password);driver=d.User;
            Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync("/api/vehicles",input)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await Mutation(client,HttpMethod.Put,$"/api/vehicles/{vehicle.Id}",vehicle,input)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/vehicles/{vehicle.Id}")).StatusCode);
            client.DefaultRequestHeaders.Authorization=new("Bearer",a.Session.AccessToken);
            foreach(var badCode in new[]{owner.UserCode,b.User.UserCode,"GE-DRV-NOT-REGISTERED"})
                Assert.Equal(HttpStatusCode.BadRequest,(await Mutation(client,HttpMethod.Post,$"/api/vehicles/{vehicle.Id}/drivers",vehicle,new DriverAssignmentRequest(badCode,true))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await Mutation(client,HttpMethod.Put,$"/api/vehicles/{vehicle.Id}/drivers/{b.User.Id}",vehicle,new DriverPermissionRequest(true))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,(await Mutation(client,HttpMethod.Post,$"/api/vehicles/{vehicle.Id}/drivers",vehicle,new DriverAssignmentRequest(driver.UserCode,false))).StatusCode);
            vehicle=(await client.GetFromJsonAsync<VehicleResponse>($"/api/vehicles/{vehicle.Id}"))!;
            client.DefaultRequestHeaders.Authorization=new("Bearer",d.Session.AccessToken);
            var driverView=(await client.GetFromJsonAsync<VehicleResponse>($"/api/vehicles/{vehicle.Id}"))!;Assert.False(driverView.CanCharge);
            client.DefaultRequestHeaders.Authorization=new("Bearer",a.Session.AccessToken);
            Assert.Equal(HttpStatusCode.NoContent,(await Mutation(client,HttpMethod.Put,$"/api/vehicles/{vehicle.Id}/drivers/{driver.Id}",vehicle,new DriverPermissionRequest(true))).StatusCode);
            vehicle=(await client.GetFromJsonAsync<VehicleResponse>($"/api/vehicles/{vehicle.Id}"))!;
            client.DefaultRequestHeaders.Authorization=new("Bearer",d.Session.AccessToken);
            var status=(await client.GetFromJsonAsync<VehicleStatusResponse>($"/api/vehicles/{vehicle.Id}/status"))!;Assert.True(status.CanCharge);Assert.Equal(111.11m,status.EstimatedRangeKm);
            var stations=(await client.GetFromJsonAsync<StationResponse[]>("/api/stations?connectorType=CCS2"))!;
            Assert.All(new[]{"Dhaka","Daudkandi","Cumilla","Feni","Chattogram"},city=>Assert.Contains(stations,s=>s.IsDemo && s.Name=="G Energy Demo — "+city));Assert.All(stations.SelectMany(s=>s.Chargers),c=>Assert.Equal("CCS2",c.ConnectorType));
            using(var scope=app.Services.CreateScope())
            {
                var db=scope.ServiceProvider.GetRequiredService<EnergyDbContext>();var saved=await db.Vehicles.AsNoTracking().SingleAsync(v=>v.Id==vehicle.Id);
                Assert.Equal(owner.Id,saved.OwnerUserId);Assert.Equal(40,saved.BatteryPercent);Assert.True((await db.VehicleDrivers.AsNoTracking().SingleAsync(x=>x.VehicleId==vehicle.Id && x.DriverUserId==driver.Id)).CanCharge);
                var charger=await db.Chargers.AsNoTracking().FirstAsync();Assert.Equal(8,charger.RowVersion.Length);
                await Constraint(db,$"INSERT INTO Vehicles (OwnerUserId,RegistrationNo,Model,ConnectorType,BatteryCapacityKWh,BatteryPercent,ConsumptionKWhPerKm,CreatedAtUtc,UpdatedAtUtc) SELECT OwnerUserId,RegistrationNo,Model,ConnectorType,BatteryCapacityKWh,BatteryPercent,ConsumptionKWhPerKm,SYSUTCDATETIME(),SYSUTCDATETIME() FROM Vehicles WHERE Id={vehicle.Id}",2601,2627);
                await Constraint(db,$"INSERT INTO Vehicles (OwnerUserId,RegistrationNo,Model,ConnectorType,BatteryCapacityKWh,BatteryPercent,ConsumptionKWhPerKm,CreatedAtUtc,UpdatedAtUtc) VALUES ({long.MinValue},{"QA-FK-"+tag[..18]},'Invalid FK','CCS2',50,40,0.18,SYSUTCDATETIME(),SYSUTCDATETIME())",547);
                await Constraint(db,$"INSERT INTO VehicleDrivers (VehicleId,DriverUserId,CanCharge,IsActive,UpdatedAtUtc) VALUES ({vehicle.Id},{long.MinValue},1,1,SYSUTCDATETIME())",547);
                await Constraint(db,$"INSERT INTO VehicleDrivers (VehicleId,DriverUserId,CanCharge,IsActive,UpdatedAtUtc) VALUES ({long.MinValue},{driver.Id},1,1,SYSUTCDATETIME())",547);
                await Constraint(db,$"INSERT INTO VehicleDrivers (VehicleId,DriverUserId,CanCharge,IsActive,UpdatedAtUtc) VALUES ({vehicle.Id},{driver.Id},1,1,SYSUTCDATETIME())",2601,2627);
                await Constraint(db,$"INSERT INTO Chargers (StationId,ChargerCode,ConnectorType,PowerKW,PricePerKWh,Status) SELECT StationId,ChargerCode,ConnectorType,PowerKW,PricePerKWh,Status FROM Chargers WHERE Id={charger.Id}",2601,2627);
                await Constraint(db,$"INSERT INTO Chargers (StationId,ChargerCode,ConnectorType,PowerKW,PricePerKWh,Status) VALUES ({long.MinValue},'QA-FK','CCS2',60,25,'Available')",547);
                await Constraint(db,$"INSERT INTO Chargers (StationId,ChargerCode,ConnectorType,PowerKW,PricePerKWh,Status) VALUES ({charger.StationId},{"QA-POWER-"+tag[..15]},'CCS2',0,25,'Available')",547);
                await Constraint(db,$"INSERT INTO Stations (Name,Address,Latitude,Longitude,IsActive,IsDemo) VALUES ({"QA-coordinate-"+tag},'Invalid coordinate',91,90,1,1)",547);
            }
        }
        await using(var restarted=new SharedSqlServerFactory())
        {
            using var client=restarted.CreateClient();
            var login=await client.PostAsJsonAsync("/api/auth/login",new LoginRequest(owner.UserCode,password));Assert.Equal(HttpStatusCode.OK,login.StatusCode);
            client.DefaultRequestHeaders.Authorization=new("Bearer",(await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
            var persisted=(await client.GetFromJsonAsync<VehicleResponse>($"/api/vehicles/{vehicle.Id}"))!;Assert.Equal(vehicle.RegistrationNo,persisted.RegistrationNo);Assert.Equal(111.11m,persisted.EstimatedRangeKm);
            Assert.Contains((await client.GetFromJsonAsync<DriverResponse[]>($"/api/vehicles/{vehicle.Id}/drivers"))!,d=>d.DriverUserId==driver.Id && d.CanCharge && d.IsActive);
            Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/api/auth/me")).StatusCode);
            var persistedStations=(await client.GetFromJsonAsync<StationResponse[]>("/api/stations"))!;Assert.All(new[]{"Dhaka","Daudkandi","Cumilla","Feni","Chattogram"},city=>Assert.Contains(persistedStations,s=>s.IsDemo && s.Name=="G Energy Demo — "+city));
            using var scope=restarted.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<EnergyDbContext>();
            var previous=await db.Users.AsNoTracking().Where(u=>previousIds.Contains(u.Id)).OrderBy(u=>u.Id).ToListAsync();
            Assert.True(CryptographicOperations.FixedTimeEquals(previousDigest,SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(previous))));
        }
    }
}
