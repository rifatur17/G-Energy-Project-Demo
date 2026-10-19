using System.Net;
using System.Net.Http.Json;
using GEnergy.Api.Data;
using GEnergy.Api.DTOs;
using GEnergy.Api.Entities;
using GEnergy.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Xunit;
namespace GEnergy.IntegrationTests;

public sealed class MobilityTests
{
    private const string Password = "Module-tests-only-passphrase";
    private static object VehicleBody(decimal capacity = 50, decimal battery = 40, decimal consumption = 0.18m, string registration = "DHAKA AB-1234", string connector = "CCS2") => new { registrationNo = registration, model = "Demo EV", connectorType = connector, batteryCapacityKWh = capacity, batteryPercent = battery, consumptionKWhPerKm = consumption };
    private static async Task<(long Id, string Code, string Token)> Account(HttpClient client, string email, string role = "CarOwner")
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new { fullName = "Module Test User", email, phone = "01700000000", password = Password, role });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); var user = (await response.Content.ReadFromJsonAsync<UserResponse>())!;
        var login = await client.PostAsJsonAsync("/api/auth/login", new { userCode = user.UserCode, password = Password }); var session = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken); return (user.Id, user.UserCode, session.AccessToken);
    }
    private static void Token(HttpClient client, string token) => client.DefaultRequestHeaders.Authorization = new("Bearer", token);
    private static async Task<VehicleResponse> Create(HttpClient client)
    { var response = await client.PostAsJsonAsync("/api/vehicles", VehicleBody()); Assert.Equal(HttpStatusCode.Created, response.StatusCode); return (await response.Content.ReadFromJsonAsync<VehicleResponse>())!; }
    private static async Task<HttpResponseMessage> Mutate(HttpClient client, HttpMethod method, string route, string version, object? body = null)
    { using var request = new HttpRequestMessage(method, route); request.Headers.TryAddWithoutValidation("If-Match", version); if (body is not null) request.Content = JsonContent.Create(body); return await client.SendAsync(request); }
    [Fact]
    public async Task Owners_are_isolated_and_range_is_explicitly_simulated()
    {
        await using var app = new AuthFactory(); using var client = app.CreateClient(); var owner = await Account(client, "owner@example.test"); var vehicle = await Create(client);
        Assert.Equal(111.11m, vehicle.EstimatedRangeKm); Assert.True(vehicle.IsSimulated);
        var status = (await client.GetFromJsonAsync<VehicleStatusResponse>($"/api/vehicles/{vehicle.Id}/status"))!; Assert.Equal(111.11m, status.EstimatedRangeKm);
        Assert.Single((await client.GetFromJsonAsync<VehicleResponse[]>("/api/vehicles"))!);
        await Account(client, "foreign@example.test");
        Assert.Empty((await client.GetFromJsonAsync<VehicleResponse[]>("/api/vehicles"))!);
        foreach (var suffix in new[] { "", "/status", "/drivers" }) Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/vehicles/{vehicle.Id}{suffix}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Mutate(client, HttpMethod.Put, $"/api/vehicles/{vehicle.Id}", vehicle.RowVersion, VehicleBody())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Mutate(client, HttpMethod.Post, $"/api/vehicles/{vehicle.Id}/drivers", vehicle.RowVersion, new { userCode = owner.Code, canCharge = true })).StatusCode);
        Token(client, owner.Token);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/vehicles", VehicleBody(registration: "  dhaka  ab-1234  "))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/vehicles/{vehicle.Id}", VehicleBody())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Mutate(client, HttpMethod.Put, $"/api/vehicles/{vehicle.Id}", "AQ==", VehicleBody())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Mutate(client, HttpMethod.Put, $"/api/vehicles/{vehicle.Id}", vehicle.RowVersion, VehicleBody(battery: 60))).StatusCode);
    }
    [Theory]
    [InlineData(0, 40, 0.18)]
    [InlineData(50, -1, 0.18)]
    [InlineData(50, 101, 0.18)]
    [InlineData(50, 40, 0)]
    [InlineData(50, 40, 0.00001)]
    public async Task Invalid_vehicle_numbers_are_rejected(decimal capacity, decimal battery, decimal consumption)
    {
        await using var app = new AuthFactory(); using var client = app.CreateClient(); await Account(client, "owner@example.test");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/vehicles", VehicleBody(capacity, battery, consumption))).StatusCode);
    }
    [Fact]
    public async Task Drivers_only_view_active_assignments_and_CanCharge_is_enforced()
    {
        await using var app = new AuthFactory(); using var client = app.CreateClient(); var owner = await Account(client, "owner@example.test"); var v = await Create(client); var driver = await Account(client, "driver@example.test", "CarDriver");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/vehicles", VehicleBody())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Mutate(client, HttpMethod.Put, $"/api/vehicles/{v.Id}", v.RowVersion, VehicleBody())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/vehicles/{v.Id}")).StatusCode);
        Token(client, owner.Token);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/drivers/lookup?userCode={owner.Code}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/drivers/lookup?userCode={driver.Code}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Mutate(client, HttpMethod.Post, $"/api/vehicles/{v.Id}/drivers", v.RowVersion, new { userCode = owner.Code, canCharge = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Mutate(client, HttpMethod.Post, $"/api/vehicles/{v.Id}/drivers", v.RowVersion, new { userCode = driver.Code, canCharge = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Mutate(client, HttpMethod.Post, $"/api/vehicles/{v.Id}/drivers", v.RowVersion, new { userCode = driver.Code, canCharge = true })).StatusCode);
        using (var scope = app.Services.CreateScope()) Assert.False(await scope.ServiceProvider.GetRequiredService<VehicleAccess>().CanChargeAsync(v.Id, driver.Id));
        Token(client, driver.Token); var view = await client.GetFromJsonAsync<VehicleResponse>($"/api/vehicles/{v.Id}"); Assert.False(view!.CanCharge);
        Assert.Single((await client.GetFromJsonAsync<VehicleResponse[]>("/api/vehicles"))!);
        Assert.Equal(HttpStatusCode.Forbidden, (await Mutate(client, HttpMethod.Put, $"/api/vehicles/{v.Id}/drivers/{driver.Id}", v.RowVersion, new { canCharge = true })).StatusCode);
        Token(client, owner.Token);
        Assert.Equal(HttpStatusCode.NoContent, (await Mutate(client, HttpMethod.Put, $"/api/vehicles/{v.Id}/drivers/{driver.Id}", v.RowVersion, new { canCharge = true })).StatusCode);
        using (var scope = app.Services.CreateScope()) Assert.True(await scope.ServiceProvider.GetRequiredService<VehicleAccess>().CanChargeAsync(v.Id, driver.Id));
        Assert.Equal(HttpStatusCode.NoContent, (await Mutate(client, HttpMethod.Delete, $"/api/vehicles/{v.Id}/drivers/{driver.Id}", v.RowVersion)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<DriverResponse[]>($"/api/vehicles/{v.Id}/drivers"))!);
        Token(client, driver.Token); Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/vehicles/{v.Id}")).StatusCode);
        using (var scope = app.Services.CreateScope()) Assert.False(await scope.ServiceProvider.GetRequiredService<VehicleAccess>().CanChargeAsync(v.Id, driver.Id));
    }
    [Fact]
    public async Task Charging_permission_requires_active_owner_and_driver_accounts()
    {
        await using var app=new AuthFactory();using var client=app.CreateClient();
        var owner=await Account(client,"owner@example.test");var v=await Create(client);var driver=await Account(client,"driver@example.test","CarDriver");
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<EnergyDbContext>();var access=scope.ServiceProvider.GetRequiredService<VehicleAccess>();
        db.VehicleDrivers.Add(new VehicleDriver {VehicleId=v.Id,DriverUserId=driver.Id,CanCharge=true});await db.SaveChangesAsync();
        Assert.True(await access.CanChargeAsync(v.Id,driver.Id));
        var ownerAccount=(await db.Users.FindAsync(owner.Id))!;ownerAccount.IsActive=false;await db.SaveChangesAsync();
        Assert.False(await access.CanChargeAsync(v.Id,driver.Id));
        ownerAccount.IsActive=true;var driverAccount=(await db.Users.FindAsync(driver.Id))!;driverAccount.IsActive=false;await db.SaveChangesAsync();
        Assert.False(await access.CanChargeAsync(v.Id,driver.Id));
    }
    private sealed class BusyGuard : IVehicleActivityGuard
    { public Task EnsureMutableAsync(long id, CancellationToken ct) => throw new MobilityConflictException("Vehicle has an active reservation or charging session."); }
    [Fact]
    public async Task Activity_guard_blocks_vehicle_and_assignment_mutations()
    {
        await using var original = new AuthFactory(); await using var app = original.WithWebHostBuilder(builder => builder.ConfigureServices(services => { services.RemoveAll<IVehicleActivityGuard>(); services.AddScoped<IVehicleActivityGuard, BusyGuard>(); }));
        using var client = app.CreateClient(); await Account(client, "owner@example.test"); var v = await Create(client);
        Assert.Equal(HttpStatusCode.Conflict, (await Mutate(client, HttpMethod.Put, $"/api/vehicles/{v.Id}", v.RowVersion, VehicleBody())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Mutate(client, HttpMethod.Post, $"/api/vehicles/{v.Id}/drivers", v.RowVersion, new { userCode = "GE-DRV-001010", canCharge = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Mutate(client, HttpMethod.Put, $"/api/vehicles/{v.Id}/drivers/2", v.RowVersion, new { canCharge = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Mutate(client, HttpMethod.Delete, $"/api/vehicles/{v.Id}/drivers/2", v.RowVersion)).StatusCode);
    }
    private sealed class ExpiredHoldAvailability : IChargerAvailabilityProvider
    {
        public Task<IReadOnlyDictionary<long,ChargerAvailability>> GetAsync(IReadOnlyList<Charger> chargers,CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<long,ChargerAvailability>>(chargers.ToDictionary(c=>c.Id,_=>new ChargerAvailability("Available","TestBookingAdapter")));
    }
    [Fact]
    public async Task Availability_adapter_can_resolve_stale_holds_but_cannot_override_offline_chargers()
    {
        await using var source=new AuthFactory();
        await using var app=source.WithWebHostBuilder(builder=>builder.ConfigureServices(services=>{services.RemoveAll<IChargerAvailabilityProvider>();services.AddScoped<IChargerAvailabilityProvider,ExpiredHoldAvailability>();}));
        using var client=app.CreateClient();await Account(client,"owner@example.test");
        await DiscoverySeeder.SeedAsync(app.Services,app.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>());
        using(var scope=app.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<EnergyDbContext>();var chargers=await db.Chargers.OrderBy(c=>c.Id).Take(2).ToListAsync();chargers[0].Status="Reserved";chargers[1].Status="Offline";await db.SaveChangesAsync();
        }
        var stations=(await client.GetFromJsonAsync<StationResponse[]>("/api/stations"))!;
        Assert.Contains(stations.SelectMany(s=>s.Chargers),c=>c.ConfiguredStatus=="Reserved" && c.IsAvailable && c.AvailabilitySource=="TestBookingAdapter");
        Assert.Contains(stations.SelectMany(s=>s.Chargers),c=>c.ConfiguredStatus=="Offline" && !c.IsAvailable && c.EffectiveStatus=="Offline");
    }
    [Fact]
    public async Task Enabling_booking_without_adapters_fails_closed()
    {
        await using var source = new AuthFactory();
        await using var app = source.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Modules:BookingEnabled"] = "true" })).ConfigureServices(services => { services.RemoveAll<IVehicleActivityGuard>(); services.AddScoped<IVehicleActivityGuard,NoBookingModuleActivityGuard>(); services.RemoveAll<IChargerAvailabilityProvider>(); services.AddScoped<IChargerAvailabilityProvider,ConfiguredChargerAvailability>(); }));
        using var client = app.CreateClient(); await Account(client, "owner@example.test"); var v = await Create(client);
        Assert.Equal(HttpStatusCode.Conflict, (await Mutate(client, HttpMethod.Put, $"/api/vehicles/{v.Id}", v.RowVersion, VehicleBody())).StatusCode);
        await DiscoverySeeder.SeedAsync(app.Services, app.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>());
        var stations = (await client.GetFromJsonAsync<StationResponse[]>("/api/stations"))!;
        Assert.All(stations.SelectMany(s => s.Chargers), c => { Assert.False(c.IsAvailable); Assert.Equal("Unknown", c.EffectiveStatus); Assert.Equal("IntegrationRequired", c.AvailabilitySource); });
    }
    [Fact]
    public async Task Station_seed_is_repeatable_filters_are_compatible_and_inactive_stations_are_hidden()
    {
        await using var app = new AuthFactory(); using var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/stations")).StatusCode);
        await Account(client, "owner@example.test");
        await DiscoverySeeder.SeedAsync(app.Services, app.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>());
        await DiscoverySeeder.SeedAsync(app.Services, app.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>());
        var stations = (await client.GetFromJsonAsync<StationResponse[]>("/api/stations"))!; Assert.Equal(5, stations.Length); Assert.All(stations, s => { Assert.True(s.IsDemo); Assert.Equal(2, s.Chargers.Count); });
        var filtered = (await client.GetFromJsonAsync<StationResponse[]>("/api/stations?connectorType=CCS2&search=cumilla"))!; Assert.Single(filtered); Assert.Single(filtered[0].Chargers); Assert.True(filtered[0].Chargers[0].IsAvailable);
        Assert.Empty((await client.GetFromJsonAsync<StationResponse[]>("/api/stations?connectorType=CHAdeMO"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/stations?connectorType=Unknown")).StatusCode);
        var first = stations[0]; using (var scope = app.Services.CreateScope())
        { var db = scope.ServiceProvider.GetRequiredService<EnergyDbContext>(); var station = await db.Stations.Include(s => s.Chargers).SingleAsync(s => s.Id == first.Id); station.Chargers[0].Status = "Offline"; await db.SaveChangesAsync(); }
        var detail = (await client.GetFromJsonAsync<StationResponse>($"/api/stations/{first.Id}"))!; Assert.Contains(detail.Chargers, c => !c.IsAvailable && c.EffectiveStatus == "Offline");
        using (var scope = app.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<EnergyDbContext>(); (await db.Stations.FindAsync(first.Id))!.IsActive = false; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/stations/{first.Id}")).StatusCode);
        Assert.Equal(4, (await client.GetFromJsonAsync<StationResponse[]>("/api/stations"))!.Length);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsJsonAsync("/api/stations", new { })).StatusCode);
    }
}
