using System.Net;
using System.Net.Http.Json;
using GEnergy.Api.Data;
using GEnergy.Api.DTOs;
using GEnergy.Api.Entities;
using GEnergy.Api.Services.Charging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;
namespace GEnergy.IntegrationTests;

public sealed class ManualChargingClock : TimeProvider
{
    private long ticks = DateTime.UtcNow.Ticks;
    public override DateTimeOffset GetUtcNow() => new(new DateTime(Interlocked.Read(ref ticks), DateTimeKind.Utc));
    public void Advance(double seconds) => Interlocked.Add(ref ticks, (long)(seconds * TimeSpan.TicksPerSecond));
}
public sealed class SettlementProbe : IChargingSettlement
{
    public bool Fail;
    public int Calls;
    public Task<SettlementResult> FinalizeAsync(ChargingCompletion completion, CancellationToken ct)
    {
        Interlocked.Increment(ref Calls);
        if (Fail) throw new InvalidOperationException("Injected settlement failure for transaction verification.");
        return Task.FromResult(new SettlementResult("Unpaid", "Wallet integration pending"));
    }
}
// Retains only uniquely labelled QA additions in the shared catalog; never resets or migrates it.
public sealed class ChargingScenario : IAsyncDisposable
{
    public readonly ManualChargingClock Clock = new();
    public readonly SettlementProbe Settlement = new();
    public WebApplicationFactory<Program> App = null!;
    private WebApplicationFactory<Program> parent = null!;
    public HttpClient Client = null!;
    public long Owner, Driver, Stranger, Vehicle, OtherVehicle, Station, Charger, OtherCharger;
    public string OwnerToken = "", DriverToken = "", StrangerToken = "";
    public static async Task<ChargingScenario> Create(bool sql = false, bool worker = false)
    {
        var s = new ChargingScenario();
        s.parent = sql ? new SharedSqlServerFactory() : new AuthFactory();
        s.App = s.parent.WithWebHostBuilder(b =>
        {
            if (worker) b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Charging:WorkerEnabled"] = "true", ["Charging:WorkerIntervalMilliseconds"] = "100" }));
            b.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(s.Clock);
                services.RemoveAll<IChargingSettlement>(); services.AddSingleton<IChargingSettlement>(s.Settlement);
            });
        });
        s.Client = s.App.CreateClient();
        (s.Owner, s.OwnerToken) = await s.Account("CarOwner");
        (s.Driver, s.DriverToken) = await s.Account("CarDriver");
        (s.Stranger, s.StrangerToken) = await s.Account("CarOwner");
        await s.Db(async db =>
        {
            var label = "QA Member3 " + Guid.NewGuid().ToString("N");
            var station = new Station { Name = label, Address = "Synthetic verification fixture, not a real charging station", Latitude = 23.8m, Longitude = 90.4m };
            var c = new Charger { Station = station, ChargerCode = "QA-CCS-1", ConnectorType = "CCS2", PowerKW = 60, PricePerKWh = 25 };
            var c2 = new Charger { Station = station, ChargerCode = "QA-CCS-2", ConnectorType = "CCS2", PowerKW = 60, PricePerKWh = 25 };
            Vehicle NewVehicle(string suffix) => new() { OwnerUserId = s.Owner, RegistrationNo = Guid.NewGuid().ToString("N")[..20] + suffix, Model = label, ConnectorType = "CCS2", BatteryCapacityKWh = 50, BatteryPercent = 40, ConsumptionKWhPerKm = 0.18m };
            var v = NewVehicle("1"); var v2 = NewVehicle("2");
            db.AddRange(station, c, c2, v, v2); await db.SaveChangesAsync();
            db.VehicleDrivers.Add(new() { VehicleId = v.Id, DriverUserId = s.Driver, CanCharge = true }); await db.SaveChangesAsync();
            (s.Vehicle, s.OtherVehicle, s.Station, s.Charger, s.OtherCharger) = (v.Id, v2.Id, station.Id, c.Id, c2.Id);
        });
        s.As(s.OwnerToken); return s;
    }
    private async Task<(long, string)> Account(string role)
    {
        const string password = "Only-for-labelled-QA-tests-123";
        var r = await Client.PostAsJsonAsync("/api/auth/register", new { fullName = "QA Member3 verification", email = $"member3-{Guid.NewGuid():N}@example.test", phone = "01700000000", password, role });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var u = (await r.Content.ReadFromJsonAsync<UserResponse>())!;
        var login = await Client.PostAsJsonAsync("/api/auth/login", new { u.UserCode, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (u.Id, (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
    }
    public void As(string token) => Client.DefaultRequestHeaders.Authorization = new("Bearer", token);
    public async Task Db(Func<EnergyDbContext, Task> action) { using var scope = App.Services.CreateScope(); await action(scope.ServiceProvider.GetRequiredService<EnergyDbContext>()); }
    public async Task<HttpResponseMessage> Reserve(long? vehicle = null, long? charger = null, string? key = null)
    {
        using var r = new HttpRequestMessage(HttpMethod.Post, "/api/bookings"); r.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        r.Content = JsonContent.Create(new { vehicleId = vehicle ?? Vehicle, stationId = Station, chargerId = charger ?? Charger, expiresAtUtc = DateTime.UtcNow.AddYears(1) });
        return await Client.SendAsync(r);
    }
    public async Task<BookingResponse> Book(long? vehicle = null, long? charger = null, string? key = null) { var r = await Reserve(vehicle, charger, key); Assert.Equal(HttpStatusCode.Created, r.StatusCode); return (await r.Content.ReadFromJsonAsync<BookingResponse>())!; }
    public async Task<SessionResponse> Start(long id) { var r = await Client.PostAsync($"/api/bookings/{id}/start", null); Assert.Equal(HttpStatusCode.OK, r.StatusCode); return (await r.Content.ReadFromJsonAsync<SessionResponse>())!; }
    public Task<SessionResponse?> Progress(long id) => Client.GetFromJsonAsync<SessionResponse>($"/api/charging-sessions/{id}");
    public async Task<SessionResponse> Stop(long id) { var r = await Client.PostAsync($"/api/charging-sessions/{id}/stop", null); Assert.Equal(HttpStatusCode.OK, r.StatusCode); return (await r.Content.ReadFromJsonAsync<SessionResponse>())!; }
    public Task<ChargerAvailabilityResponse?> Availability() => Client.GetFromJsonAsync<ChargerAvailabilityResponse>($"/api/chargers/{Charger}/availability");
    public Task Tick() => App.Services.GetServices<IHostedService>().OfType<ChargingWorker>().Single().Tick(CancellationToken.None);
    public async ValueTask DisposeAsync() { Client.Dispose(); await App.DisposeAsync(); await parent.DisposeAsync(); }
}
public sealed class ChargingTests
{
    [Fact]
    public async Task Reservation_uses_server_180_seconds_and_idempotency_then_cancellation_releases()
    {
        await using var s = await ChargingScenario.Create();
        var b = await s.Book(key: "retry-key"); Assert.Equal(180, (b.ExpiresAtUtc - b.CreatedAtUtc).TotalSeconds); Assert.Equal(s.Clock.GetUtcNow().UtcDateTime, b.CreatedAtUtc);
        Assert.Equal(b.Id, (await s.Book(key: "retry-key")).Id);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Reserve()).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Reserve(s.OtherVehicle, s.OtherCharger, "retry-key")).StatusCode);
        Assert.Equal("Reserved", (await s.Availability())!.EffectiveStatus);
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsync($"/api/bookings/{b.Id}/cancel", null)).StatusCode);
        Assert.True((await s.Availability())!.IsAvailable); await s.Book(s.OtherVehicle);
    }
    [Fact]
    public async Task Request_time_expiry_rejects_start_and_frees_charger_without_worker()
    {
        await using var s = await ChargingScenario.Create(); var b = await s.Book(); s.Clock.Advance(180);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client.PostAsync($"/api/bookings/{b.Id}/start", null)).StatusCode);
        Assert.Equal("Expired", (await s.Client.GetFromJsonAsync<BookingResponse>($"/api/bookings/{b.Id}"))!.Status);
        Assert.True((await s.Availability())!.IsAvailable); var replacement = await s.Book(s.OtherVehicle);
        await s.Tick(); Assert.Equal("Reserved", (await s.Client.GetFromJsonAsync<BookingResponse>($"/api/bookings/{replacement.Id}"))!.Status);
    }
    [Fact]
    public async Task Worker_expires_and_completes_full_sessions_without_client_polling()
    {
        await using var s = await ChargingScenario.Create(); var b = await s.Book(); s.Clock.Advance(181); await s.Tick();
        await s.Db(async db => Assert.Equal("Expired", (await db.Bookings.FindAsync(b.Id))!.Status));
        var active = await s.Start((await s.Book()).Id); s.Clock.Advance(120); await s.Tick();
        var done = (await s.Progress(active.Id))!; Assert.Equal("Completed", done.Status); Assert.Equal(100, done.BatteryPercent); Assert.Equal(30, done.EnergyKWh); Assert.Equal(750, done.Cost); Assert.Equal(30, done.ElapsedSeconds);
        Assert.Equal(active.StartedAtUtc.AddSeconds(30), done.EndedAtUtc); Assert.True((await s.Availability())!.IsAvailable); Assert.Equal(1, s.Settlement.Calls);
    }
    [Fact]
    public async Task Progress_snapshots_tariff_persists_vehicle_and_repeated_start_stop_finalizes_once()
    {
        await using var s = await ChargingScenario.Create(); var b = await s.Book(); var a = await s.Start(b.Id); Assert.Equal(a.Id, (await s.Start(b.Id)).Id);
        Assert.Equal("Charging", (await s.Availability())!.EffectiveStatus);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Reserve(s.OtherVehicle)).StatusCode);
        await s.Db(async db => { (await db.Chargers.FindAsync(s.Charger))!.PricePerKWh = 99; await db.SaveChangesAsync(); });
        s.Clock.Advance(10); var p = (await s.Progress(a.Id))!;
        Assert.Equal(10, p.EnergyKWh); Assert.Equal(60, p.BatteryPercent); Assert.Equal(166.67m, p.EstimatedRangeKm); Assert.Equal(250, p.Cost); Assert.Equal(25, p.TariffPerKWh);
        var done = await s.Stop(a.Id); s.Clock.Advance(90); var retry = await s.Stop(a.Id);
        Assert.Equal(done.EnergyKWh, retry.EnergyKWh); Assert.Equal(done.EndedAtUtc, retry.EndedAtUtc); Assert.Equal("Unpaid", retry.PaymentStatus); Assert.Equal(1, s.Settlement.Calls);
        await s.Db(async db => { Assert.Equal(60, (await db.Vehicles.FindAsync(s.Vehicle))!.BatteryPercent); Assert.Single(await db.ChargingSessions.Where(x => x.BookingId == b.Id).ToArrayAsync()); });
    }
    [Fact]
    public async Task Unauthorized_driver_foreign_user_and_ineligible_vehicle_are_rejected()
    {
        await using var s = await ChargingScenario.Create(); var b = await s.Book(); s.As(s.StrangerToken);
        foreach (var suffix in new[] { "", "/cancel", "/start" })
        {
            var r = suffix == "" ? await s.Client.GetAsync($"/api/bookings/{b.Id}") : await s.Client.PostAsync($"/api/bookings/{b.Id}{suffix}", null); Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Reserve()).StatusCode);
        s.As(s.DriverToken); Assert.Equal(HttpStatusCode.NotFound, (await s.Client.PostAsync($"/api/bookings/{b.Id}/start", null)).StatusCode);
        s.As(s.OwnerToken); var active = await s.Start(b.Id); s.As(s.StrangerToken); Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/charging-sessions/{active.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.PostAsync($"/api/charging-sessions/{active.Id}/stop", null)).StatusCode);
        s.As(s.OwnerToken); await s.Stop(active.Id);
        await s.Db(async db => { (await db.VehicleDrivers.FindAsync(s.Vehicle, s.Driver))!.CanCharge = false; await db.SaveChangesAsync(); }); s.As(s.DriverToken); Assert.Equal(HttpStatusCode.Forbidden, (await s.Reserve()).StatusCode);
        s.As(s.OwnerToken); await s.Db(async db => { (await db.Vehicles.FindAsync(s.Vehicle))!.ConnectorType = "Type2"; await db.SaveChangesAsync(); }); Assert.Equal(HttpStatusCode.BadRequest, (await s.Reserve()).StatusCode);
        await s.Db(async db => { var v = (await db.Vehicles.FindAsync(s.Vehicle))!; v.ConnectorType = "CCS2"; v.BatteryPercent = 100; await db.SaveChangesAsync(); }); Assert.Equal(HttpStatusCode.Conflict, (await s.Reserve()).StatusCode);
        await s.Db(async db => { (await db.Vehicles.FindAsync(s.Vehicle))!.BatteryPercent = 40; (await db.Chargers.FindAsync(s.Charger))!.Status = "Offline"; await db.SaveChangesAsync(); }); Assert.Equal(HttpStatusCode.Conflict, (await s.Reserve()).StatusCode);
    }
    [Fact]
    public async Task Driver_can_book_with_permission_owner_is_payer_and_activity_blocks_member2_edits()
    {
        await using var s = await ChargingScenario.Create(); s.As(s.DriverToken); var b = await s.Book(); s.As(s.OwnerToken);
        var vehicle = (await s.Client.GetFromJsonAsync<VehicleResponse>($"/api/vehicles/{s.Vehicle}"))!;
        using var r = new HttpRequestMessage(HttpMethod.Delete, $"/api/vehicles/{s.Vehicle}/drivers/{s.Driver}"); r.Headers.TryAddWithoutValidation("If-Match", vehicle.RowVersion);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client.SendAsync(r)).StatusCode);
        s.As(s.DriverToken); var a = await s.Start(b.Id); s.Clock.Advance(2); await s.Stop(a.Id);
        await s.Db(async db => Assert.Equal(s.Owner, (await db.ChargingSessions.FindAsync(a.Id))!.PayerOwnerUserId));
    }
    [Theory]
    [InlineData(-10, 0, 40)]
    [InlineData(10, 10, 60)]
    [InlineData(9999, 30, 100)]
    public void Math_caps_energy_and_never_moves_before_start(double seconds, decimal energy, decimal battery)
    {
        var session = new ChargingSession { CapacityKWh = 50, InitialBatteryPercent = 40, PowerKW = 60, TariffPerKWh = 25, SimulationSpeed = 60, StartedAtUtc = DateTime.UtcNow };
        var result = ChargingMath.Calculate(session, session.StartedAtUtc.AddSeconds(seconds)); Assert.Equal(energy, result.EnergyKWh); Assert.Equal(battery, result.BatteryPercent); Assert.Equal(energy * 25, result.Cost);
    }
}
public sealed class ChargingSqlServerTests
{
    [SharedSqlServerFact]
    public async Task Sql_hosted_worker_expires_and_recovers_after_failed_completion()
    {
        await using var s = await ChargingScenario.Create(true, true);
        async Task Until(Func<Task<bool>> predicate)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!await predicate()) await Task.Delay(50, deadline.Token);
        }
        var b = await s.Book(); s.Clock.Advance(180);
        await Until(async () => { bool expired = false; await s.Db(async db => expired = (await db.Bookings.FindAsync(b.Id))!.Status == "Expired"); return expired; });
        var session = await s.Start((await s.Book()).Id); s.Settlement.Fail = true; s.Clock.Advance(60);
        await Until(() => Task.FromResult(Volatile.Read(ref s.Settlement.Calls) > 0));
        await s.Db(async db => Assert.Equal("Active", (await db.ChargingSessions.FindAsync(session.Id))!.Status));
        s.Settlement.Fail = false;
        await Until(async () => { bool done = false; await s.Db(async db => done = (await db.ChargingSessions.FindAsync(session.Id))!.Status == "Completed"); return done; });
        var completed = (await s.Progress(session.Id))!; Assert.Equal(100, completed.BatteryPercent); Assert.Equal(30, completed.EnergyKWh); Assert.Equal(750, completed.Cost); Assert.Equal(30, completed.ElapsedSeconds); Assert.Equal(session.StartedAtUtc.AddSeconds(30), completed.EndedAtUtc);
        Assert.True((await s.Availability())!.IsAvailable);
    }

    [SharedSqlServerFact]
    public async Task Sql_same_charger_and_same_vehicle_races_allow_exactly_one_reservation()
    {
        await using var s = await ChargingScenario.Create(true);
        var results = await Task.WhenAll(s.Reserve(), s.Reserve(s.OtherVehicle)); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Created); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        var b = (await results.Single(x => x.IsSuccessStatusCode).Content.ReadFromJsonAsync<BookingResponse>())!; await s.Client.PostAsync($"/api/bookings/{b.Id}/cancel", null);
        results = await Task.WhenAll(s.Reserve(), s.Reserve(charger: s.OtherCharger)); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Created); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        b = (await results.Single(x => x.IsSuccessStatusCode).Content.ReadFromJsonAsync<BookingResponse>())!; await s.Client.PostAsync($"/api/bookings/{b.Id}/cancel", null);
        results = await Task.WhenAll(s.Reserve(key: "sql-retry"), s.Reserve(key: "sql-retry")); Assert.All(results, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var ids = await Task.WhenAll(results.Select(async r => (await r.Content.ReadFromJsonAsync<BookingResponse>())!.Id)); Assert.Equal(ids[0], ids[1]); await s.Client.PostAsync($"/api/bookings/{ids[0]}/cancel", null);
    }
    [SharedSqlServerFact]
    public async Task Sql_concurrent_start_stop_and_worker_finalize_once_with_persisted_snapshot()
    {
        await using var s = await ChargingScenario.Create(true); s.As(s.DriverToken); var b = await s.Book(); var starts = await Task.WhenAll(s.Start(b.Id), s.Start(b.Id)); Assert.Equal(starts[0].Id, starts[1].Id); var id = starts[0].Id;
        s.Clock.Advance(10); var done = await Task.WhenAll(s.Stop(id), s.Stop(id)); Assert.All(done, x => { Assert.Equal(10, x.EnergyKWh); Assert.Equal(250, x.Cost); Assert.Equal(60, x.BatteryPercent); }); Assert.Equal(1, s.Settlement.Calls);
        await s.Tick(); Assert.Equal(1, s.Settlement.Calls); Assert.True((await s.Availability())!.IsAvailable);
        await s.Db(async db => { var session = (await db.ChargingSessions.FindAsync(id))!; Assert.Equal(s.Owner, session.PayerOwnerUserId); Assert.Equal("Completed", session.Status); Assert.Equal("Unpaid", session.PaymentStatus); Assert.Equal(60, (await db.Vehicles.FindAsync(s.Vehicle))!.BatteryPercent); Assert.Equal(8, session.RowVersion.Length); });
        // A new host proves that persistence and calculations do not rely on server-process memory.
        await using var restarted = new SharedSqlServerFactory(); using var client = restarted.CreateClient();
        using var scope = restarted.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EnergyDbContext>(); var persisted = await db.ChargingSessions.AsNoTracking().SingleAsync(x => x.Id == id); Assert.Equal(250, persisted.Cost); Assert.Equal(10, persisted.EnergyKWh);
    }
    [SharedSqlServerFact]
    public async Task Sql_failed_settlement_rolls_back_completion_battery_and_release_then_retry_succeeds()
    {
        await using var s = await ChargingScenario.Create(true); var active = await s.Start((await s.Book()).Id); s.Clock.Advance(10); s.Settlement.Fail = true;
        var failed = await s.Client.PostAsync($"/api/charging-sessions/{active.Id}/stop", null); Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        await s.Db(async db => { var a = (await db.ChargingSessions.FindAsync(active.Id))!; Assert.Equal("Active", a.Status); Assert.Null(a.EndedAtUtc); Assert.Equal(0, a.EnergyKWh); Assert.Equal(40, (await db.Vehicles.FindAsync(s.Vehicle))!.BatteryPercent); Assert.Equal("Charging", (await db.Chargers.FindAsync(s.Charger))!.Status); });
        s.Settlement.Fail = false; var done = await s.Stop(active.Id); Assert.Equal(250, done.Cost); Assert.Equal("Completed", done.Status); Assert.True((await s.Availability())!.IsAvailable);
    }
    [SharedSqlServerFact]
    public async Task Sql_expiry_start_cancel_races_do_not_release_new_reservation_and_indexes_reject_duplicates()
    {
        await using var s = await ChargingScenario.Create(true); var b = await s.Book(); s.Clock.Advance(180);
        await Task.WhenAll(s.Tick(), s.Client.PostAsync($"/api/bookings/{b.Id}/start", null), s.Client.PostAsync($"/api/bookings/{b.Id}/cancel", null));
        Assert.Equal("Expired", (await s.Client.GetFromJsonAsync<BookingResponse>($"/api/bookings/{b.Id}"))!.Status);
        var replacement = await s.Book(s.OtherVehicle); await s.Tick(); Assert.Equal("Reserved", (await s.Availability())!.EffectiveStatus);
        await s.Db(async db => { db.Bookings.Add(new() { BookedByUserId = s.Owner, VehicleId = s.Vehicle, ChargerId = s.Charger, IdempotencyKey = Guid.NewGuid().ToString("N"), CreatedAtUtc = s.Clock.GetUtcNow().UtcDateTime, ExpiresAtUtc = s.Clock.GetUtcNow().UtcDateTime.AddSeconds(180) }); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); });
        var start = s.Client.PostAsync($"/api/bookings/{replacement.Id}/start", null); var cancel = s.Client.PostAsync($"/api/bookings/{replacement.Id}/cancel", null); var responses = await Task.WhenAll(start, cancel);
        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict));
        await s.Db(async db => { var sessions = await db.ChargingSessions.Where(x => x.BookingId == replacement.Id).ToArrayAsync(); Assert.True(sessions.Length <= 1); });
        var sessions = (await s.Client.GetFromJsonAsync<SessionResponse[]>("/api/charging-sessions/active"))!; foreach (var session in sessions) await s.Stop(session.Id);
    }
}
