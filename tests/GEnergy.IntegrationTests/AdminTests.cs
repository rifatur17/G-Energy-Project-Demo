using System.Net;
using System.Net.Http.Json;
using GEnergy.Api.Data;
using GEnergy.Api.DTOs;
using GEnergy.Api.Services;
using GEnergy.Api.Services.Charging;
using GEnergy.Api.Services.Wallets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;
namespace GEnergy.IntegrationTests;

public sealed class AdminScenario : IAsyncDisposable
{
    public ChargingScenario S = null!; public HttpClient Admin = null!;
    public static async Task<AdminScenario> Create(bool sql = false)
    {
        var s = await ChargingScenario.Create(sql, configure: services => { services.RemoveAll<IChargingSettlement>(); services.AddScoped<IChargingSettlement, WalletChargingSettlement>(); }, label: "Member5");
        const string password = "Only-for-labelled-admin-QA-123";
        var email = $"member5-admin-{Guid.NewGuid():N}@example.test";
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AdminSeed:Email"] = email, ["AdminSeed:Password"] = password }).Build();
        var code = await AdminSeeder.SeedAsync(s.App.Services, config, s.App.Services.GetRequiredService<IHostEnvironment>());
        var client = s.App.CreateClient(); var login = await client.PostAsJsonAsync("/api/auth/login", new { userCode = code, password }); Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        return new() { S = s, Admin = client };
    }
    public async Task<AdminStationResponse> Station() => (await Admin.GetFromJsonAsync<AdminStationResponse[]>("/api/admin/stations?take=100"))!.Single(x => x.Id == S.Station);
    public async Task<AdminChargerResponse> Charger() => (await Admin.GetFromJsonAsync<AdminChargerResponse[]>($"/api/admin/stations/{S.Station}/chargers"))!.Single(x => x.Id == S.Charger);
    public static object StationBody(bool active = true, string name = "QA Admin station", decimal lat = 23.8m) => new { name, address = "Synthetic sample station, not a verified charging site", latitude = lat, longitude = 90.4m, isActive = active, isDemo = true };
    public static object ChargerBody(bool active = true, string code = "QA-CCS-1", decimal power = 60, decimal tariff = 25, string connector = "CCS2") => new { chargerCode = code, connectorType = connector, powerKW = power, pricePerKWh = tariff, isActive = active, status = "Available", stationId = 999999 };
    public Task<HttpResponseMessage> UpdateStation(AdminStationResponse station, bool active = true, string? name = null) => Put(Admin, $"/api/admin/stations/{station.Id}", StationBody(active, name ?? station.Name), station.Version);
    public Task<HttpResponseMessage> UpdateCharger(AdminChargerResponse charger, bool active = true, decimal tariff = 25) => Put(Admin, $"/api/admin/chargers/{charger.Id}", ChargerBody(active, charger.ChargerCode, charger.PowerKW, tariff, charger.ConnectorType), charger.RowVersion);
    public static async Task<HttpResponseMessage> Put(HttpClient client, string path, object body, string version) { using var request = new HttpRequestMessage(HttpMethod.Put, path); request.Headers.Add("If-Match", '"' + version + '"'); request.Content = JsonContent.Create(body); return await client.SendAsync(request); }
    public async ValueTask DisposeAsync() { Admin.Dispose(); await S.DisposeAsync(); }
}
public sealed class AdminTests
{
    [Fact]
    public async Task Every_admin_route_rejects_anonymous_owner_and_driver_and_bootstrap_cannot_promote_owner()
    {
        await using var a = await AdminScenario.Create(); var s = a.S;
        foreach (var token in new string?[] { null, s.OwnerToken, s.DriverToken })
        {
            s.Client.DefaultRequestHeaders.Authorization = token is null ? null : new("Bearer", token); var expected = token is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            foreach (var path in new[] { "dashboard", "users", "stations", $"stations/{s.Station}/chargers", "bookings", "charging-sessions", "wallet-transactions", "wallets" }) Assert.Equal(expected, (await s.Client.GetAsync("/api/admin/" + path)).StatusCode);
            Assert.Equal(expected, (await s.Client.PostAsJsonAsync("/api/admin/stations", AdminScenario.StationBody())).StatusCode);
            Assert.Equal(expected, (await s.Client.PostAsJsonAsync($"/api/admin/stations/{s.Station}/chargers", AdminScenario.ChargerBody())).StatusCode);
            Assert.Equal(expected, (await AdminScenario.Put(s.Client, $"/api/admin/stations/{s.Station}", AdminScenario.StationBody(), "fake")).StatusCode);
            Assert.Equal(expected, (await AdminScenario.Put(s.Client, $"/api/admin/chargers/{s.Charger}", AdminScenario.ChargerBody(), "fake")).StatusCode);
        }
        string? ownerEmail = null; await s.Db(async db => ownerEmail = (await db.Users.FindAsync(s.Owner))!.Email);
        await Assert.ThrowsAsync<InvalidOperationException>(() => AdminSeeder.SeedAsync(s.App.Services, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AdminSeed:Email"] = ownerEmail, ["AdminSeed:Password"] = "Never-promote-existing-owner" }).Build(), s.App.Services.GetRequiredService<IHostEnvironment>()));
    }
    [Fact]
    public async Task Dashboard_counts_use_effective_expiry_and_net_ledger_not_failed_payments()
    {
        await using var a = await AdminScenario.Create(); var s = a.S; var first = (await a.Admin.GetFromJsonAsync<AdminDashboardResponse>("/api/admin/dashboard"))!;
        Assert.Equal(4, first.TotalUsers); Assert.Equal(1, first.ActiveStations); Assert.Equal(2, first.AvailableChargers); Assert.True(first.IsSimulated);
        var hold = await s.Book(); Assert.Equal(1, (await a.Admin.GetFromJsonAsync<AdminDashboardResponse>("/api/admin/dashboard"))!.ActiveBookings); s.Clock.Advance(180);
        var expired = (await a.Admin.GetFromJsonAsync<AdminBookingResponse[]>("/api/admin/bookings?status=Expired"))!.Single(); Assert.Equal(hold.Id, expired.Id); Assert.Equal("Expired", expired.Status); Assert.Equal("Reserved", expired.PersistedStatus);
        var done = await WalletFixture.Completed(s); await WalletFixture.TopUp(s); await WalletFixture.TopUp(s, 1000, "Failure"); var paid = await WalletFixture.Pay(s, done.Id); await s.Client.PostAsync($"/api/wallet/transactions/{paid.Transaction.Id}/refund", null);
        var summary = (await a.Admin.GetFromJsonAsync<AdminDashboardResponse>("/api/admin/dashboard"))!; Assert.Equal(1, summary.CompletedSessions); Assert.Equal(1, summary.CompletedChargingPayments); Assert.Equal(1, summary.SuccessfulTopUps); Assert.Equal(1, summary.FailedTopUps); Assert.Equal(250, summary.GrossChargingPayments); Assert.Equal(250, summary.Refunds); Assert.Equal(0, summary.NetChargingPayments); Assert.Equal(1000, summary.TotalWalletBalance); Assert.Equal(2, summary.AvailableChargers);
    }
    [Fact]
    public async Task Station_charger_validation_unique_codes_versions_and_safe_deactivation()
    {
        await using var a = await AdminScenario.Create(); var s = a.S;
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Admin.PostAsJsonAsync("/api/admin/stations", AdminScenario.StationBody(lat: 91))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Admin.PostAsJsonAsync("/api/admin/stations", AdminScenario.StationBody(name: "  "))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Admin.PostAsJsonAsync("/api/admin/stations", AdminScenario.StationBody(lat: 23.1234567m))).StatusCode);
        foreach (var body in new[] { AdminScenario.ChargerBody(power: 0), AdminScenario.ChargerBody(tariff: -1), AdminScenario.ChargerBody(connector: "wrong"), AdminScenario.ChargerBody(tariff: 1.001m) }) Assert.Equal(HttpStatusCode.BadRequest, (await a.Admin.PostAsJsonAsync($"/api/admin/stations/{s.Station}/chargers", body)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Admin.PostAsJsonAsync("/api/admin/stations/999999/chargers", AdminScenario.ChargerBody())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await a.Admin.PostAsJsonAsync($"/api/admin/stations/{s.Station}/chargers", AdminScenario.ChargerBody())).StatusCode);
        var created = await a.Admin.PostAsJsonAsync("/api/admin/stations", AdminScenario.StationBody()); Assert.Equal(HttpStatusCode.Created, created.StatusCode); var station = (await created.Content.ReadFromJsonAsync<AdminStationResponse>())!;
        Assert.Equal(HttpStatusCode.OK, (await a.UpdateStation(station, false)).StatusCode); Assert.Equal(HttpStatusCode.Conflict, (await a.UpdateStation(station, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await a.Admin.PostAsJsonAsync($"/api/admin/stations/{station.Id}/chargers", AdminScenario.ChargerBody())).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await a.Admin.PostAsJsonAsync($"/api/admin/stations/{station.Id}/chargers", AdminScenario.ChargerBody(active: false))).StatusCode);
        var c = await a.Charger(); Assert.Equal(HttpStatusCode.OK, (await a.UpdateCharger(c, false)).StatusCode); Assert.Equal(HttpStatusCode.Conflict, (await a.UpdateCharger(c, true)).StatusCode);
        s.As(s.OwnerToken); Assert.Equal(HttpStatusCode.Conflict, (await s.Reserve()).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Admin.PutAsJsonAsync($"/api/admin/chargers/{s.Charger}", AdminScenario.ChargerBody())).StatusCode);
    }
    [Fact]
    public async Task Valid_hold_and_active_session_block_all_configuration_edits_without_changing_lifecycle()
    {
        await using var a = await AdminScenario.Create(); var s = a.S; var b = await s.Book();
        Assert.Equal(HttpStatusCode.Conflict, (await a.UpdateStation(await a.Station(), false)).StatusCode); Assert.Equal(HttpStatusCode.Conflict, (await a.UpdateCharger(await a.Charger(), false)).StatusCode); Assert.Equal(HttpStatusCode.Conflict, (await a.UpdateCharger(await a.Charger(), true, 99)).StatusCode);
        var active = await s.Start(b.Id); Assert.Equal(HttpStatusCode.Conflict, (await a.UpdateStation(await a.Station(), false)).StatusCode); Assert.Equal(HttpStatusCode.Conflict, (await a.UpdateCharger(await a.Charger(), true)).StatusCode);
        s.Clock.Advance(10); var done = await s.Stop(active.Id); Assert.Equal(250, done.Cost); Assert.Equal(HttpStatusCode.OK, (await a.UpdateCharger(await a.Charger(), false)).StatusCode);
        var row = (await a.Admin.GetFromJsonAsync<AdminSessionResponse[]>("/api/admin/charging-sessions?status=Completed&payment=Unpaid"))!.Single(); Assert.Equal(done.Cost, row.Cost); Assert.Equal(done.EnergyKWh, row.EnergyKWh); Assert.Equal(60, row.BatteryPercent);
    }
    [Fact]
    public async Task Monitoring_is_paginated_minimal_and_cannot_mutate_wallet_or_sessions()
    {
        await using var a = await AdminScenario.Create(); var s = a.S; await WalletFixture.TopUp(s); var completed = await WalletFixture.Completed(s);
        var users = await a.Admin.GetStringAsync("/api/admin/users?take=1"); Assert.DoesNotContain("password", users, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("email", users, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("phone", users, StringComparison.OrdinalIgnoreCase);
        var transactions = (await a.Admin.GetFromJsonAsync<AdminTransactionResponse[]>("/api/admin/wallet-transactions?type=ChargingPayment&take=1"))!; Assert.Single(transactions); Assert.True(transactions[0].OwnerCanRefund); Assert.Equal(completed.PaymentReference, transactions[0].Reference);
        Assert.Single((await a.Admin.GetFromJsonAsync<AdminTransactionResponse[]>($"/api/admin/wallet-transactions?before={transactions[0].Id}"))!);
        Assert.Single((await a.Admin.GetFromJsonAsync<AdminWalletResponse[]>("/api/admin/wallets"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.Admin.GetAsync("/api/admin/bookings?status=Anything")).StatusCode); Assert.Equal(HttpStatusCode.BadRequest, (await a.Admin.GetAsync("/api/admin/users?take=101")).StatusCode); Assert.Equal(HttpStatusCode.BadRequest, (await a.Admin.GetAsync("/api/admin/bookings?from=2026-01-02T00:00:00Z&to=2026-01-01T00:00:00Z")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Admin.PostAsync($"/api/admin/charging-sessions/{completed.Id}/pay", null)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await a.Admin.PostAsync($"/api/charging-sessions/{completed.Id}/pay", null)).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await a.Admin.PostAsync($"/api/admin/wallet-transactions/{transactions[0].Id}/refund", null)).StatusCode);
        Assert.Equal(750, (await WalletFixture.Balance(s))!.Balance); Assert.Equal("Paid", (await s.Progress(completed.Id))!.PaymentStatus);
    }
}
[Collection("SharedSqlServer")]
public sealed class AdminSqlServerTests
{
    [SharedSqlServerFact]
    public async Task Sql_history_reads_preserve_booking_session_charger_wallet_and_refund_ledger()
    {
        await using var a = await AdminScenario.Create(true); var s = a.S;
        var cancelled = await s.Book();
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsync($"/api/bookings/{cancelled.Id}/cancel", null)).StatusCode);
        var completed = await WalletFixture.Completed(s);
        await WalletFixture.TopUp(s); await WalletFixture.TopUp(s, 100, "Failure");
        var paid = await WalletFixture.Pay(s, completed.Id);
        Assert.Equal(paid.Transaction.Id, (await WalletFixture.Pay(s, completed.Id)).Transaction.Id);
        var refundResponse = await s.Client.PostAsync($"/api/wallet/transactions/{paid.Transaction.Id}/refund", null);
        Assert.Equal(HttpStatusCode.OK, refundResponse.StatusCode);
        var refund = (await refundResponse.Content.ReadFromJsonAsync<WalletOperationResponse>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client.PostAsync($"/api/wallet/transactions/{paid.Transaction.Id}/refund", null)).StatusCode);
        var expired = await s.Book(); s.Clock.Advance(180);
        // Capture complete persisted rows, including rowversions, without serializing navigation cycles.
        async Task<string> Snapshot()
        {
            string result = "";
            await s.Db(async db => result = System.Text.Json.JsonSerializer.Serialize(new {
                Bookings = await db.Bookings.AsNoTracking().Where(b => b.Charger.StationId == s.Station).OrderBy(b => b.Id).ToListAsync(),
                Sessions = await db.ChargingSessions.AsNoTracking().Where(c => c.VehicleId == s.Vehicle).OrderBy(c => c.Id).ToListAsync(),
                Chargers = await db.Chargers.AsNoTracking().Where(c => c.StationId == s.Station).OrderBy(c => c.Id).ToListAsync(),
                Wallets = await db.Wallets.AsNoTracking().Where(w => w.UserId == s.Owner).ToListAsync(),
                Ledger = await db.WalletTransactions.AsNoTracking().Where(t => t.Wallet.UserId == s.Owner).OrderBy(t => t.Id).ToListAsync()
            }));
            return result;
        }
        var before = await Snapshot();
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var history = (await a.Admin.GetFromJsonAsync<AdminBookingResponse[]>($"/api/admin/bookings?stationId={s.Station}"))!;
            Assert.Equal("Cancelled", history.Single(b => b.Id == cancelled.Id).Status);
            Assert.Equal(completed.Id, history.Single(b => b.SessionId == completed.Id).SessionId);
            var expiry = history.Single(b => b.Id == expired.Id); Assert.Equal("Expired", expiry.Status); Assert.Equal("Reserved", expiry.PersistedStatus);
            var page = (await a.Admin.GetFromJsonAsync<AdminBookingResponse[]>($"/api/admin/bookings?stationId={s.Station}&take=1"))!;
            Assert.Single(page);
            Assert.All((await a.Admin.GetFromJsonAsync<AdminBookingResponse[]>($"/api/admin/bookings?stationId={s.Station}&before={page[0].Id}"))!, b => Assert.True(b.Id < page[0].Id));
            Assert.Empty((await a.Admin.GetFromJsonAsync<AdminBookingResponse[]>($"/api/admin/bookings?stationId={s.Station}&status=Reserved"))!);
            var session = (await a.Admin.GetFromJsonAsync<AdminSessionResponse[]>($"/api/admin/charging-sessions?stationId={s.Station}&payment=Paid"))!.Single();
            Assert.Equal(completed.Cost, session.Cost); Assert.Equal(paid.Transaction.Reference, session.PaymentReference);
            var ledger = (await a.Admin.GetFromJsonAsync<AdminTransactionResponse[]>("/api/admin/wallet-transactions?take=100"))!;
            var payment = ledger.Single(t => t.Id == paid.Transaction.Id);
            Assert.Equal(refund.Transaction.Reference, payment.RefundReference); Assert.False(payment.OwnerCanRefund);
            Assert.Equal(paid.Transaction.Reference, ledger.Single(t => t.Id == refund.Transaction.Id).OriginalReference);
            foreach (var path in new[] { $"bookings?stationId={s.Station}", "wallet-transactions?type=TopUp&status=Failed&take=1", "wallet-transactions?type=Refund&take=1", "wallets?take=1", "users?take=1" })
            {
                var json = await a.Admin.GetStringAsync("/api/admin/" + path);
                foreach (var secret in new[] { "password", "email", "phone", "token" }) Assert.DoesNotContain(secret, json, StringComparison.OrdinalIgnoreCase);
            }
        }
        foreach (var token in new[] { s.OwnerToken, s.DriverToken })
        {
            s.As(token);
            foreach (var path in new[] { "bookings", "wallet-transactions" }) Assert.Equal(HttpStatusCode.Forbidden, (await s.Client.GetAsync("/api/admin/" + path)).StatusCode);
        }
        Assert.Equal(before, await Snapshot());
        s.As(s.OwnerToken); Assert.Equal(1000, (await WalletFixture.Balance(s))!.Balance);
    }
    [SharedSqlServerFact]
    public async Task Sql_full_flow_admin_management_to_payment_refund_and_monitoring_persists()
    {
        await using var a = await AdminScenario.Create(true); var s = a.S;
        var added = await a.Admin.PostAsJsonAsync($"/api/admin/stations/{s.Station}/chargers", AdminScenario.ChargerBody(code: "QA-ADMIN-CREATED")); Assert.Equal(HttpStatusCode.Created, added.StatusCode); var charger = (await added.Content.ReadFromJsonAsync<AdminChargerResponse>())!; Assert.Equal(s.Station, charger.StationId);
        var b = await s.Book(charger: charger.Id); Assert.Equal(180, (b.ExpiresAtUtc - b.CreatedAtUtc).TotalSeconds);
        var booking = (await a.Admin.GetFromJsonAsync<AdminBookingResponse[]>($"/api/admin/bookings?stationId={s.Station}"))!.Single(); Assert.Equal("Reserved", booking.Status);
        var active = await s.Start(b.Id); s.Clock.Advance(10); var done = await s.Stop(active.Id); Assert.Equal("Unpaid", done.PaymentStatus); await WalletFixture.TopUp(s); var paid = await WalletFixture.Pay(s, done.Id); Assert.Equal(paid.Transaction.Id, (await WalletFixture.Pay(s, done.Id)).Transaction.Id); Assert.Equal(750, (await WalletFixture.Balance(s))!.Balance);
        var session = (await a.Admin.GetFromJsonAsync<AdminSessionResponse[]>($"/api/admin/charging-sessions?stationId={s.Station}&payment=Paid"))!.Single(); Assert.Equal(250, session.Cost); Assert.Equal(paid.Transaction.Reference, session.PaymentReference);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.PostAsync($"/api/wallet/transactions/{paid.Transaction.Id}/refund", null)).StatusCode);
        var ledger = (await a.Admin.GetFromJsonAsync<AdminTransactionResponse[]>("/api/admin/wallet-transactions?take=100"))!.Where(t => t.ChargingSessionId == done.Id).ToArray(); Assert.Equal(2, ledger.Length); Assert.False(ledger.Single(t => t.Type == "ChargingPayment").OwnerCanRefund); Assert.NotNull(ledger.Single(t => t.Type == "ChargingPayment").RefundReference);
        var summary = (await a.Admin.GetFromJsonAsync<AdminDashboardResponse>("/api/admin/dashboard"))!; await s.Db(async db => { Assert.Equal(await db.Users.CountAsync(), summary.TotalUsers); Assert.Equal(await db.WalletTransactions.Where(t => t.Type == "ChargingPayment").SumAsync(t => t.Amount), summary.GrossChargingPayments); Assert.Equal(await db.WalletTransactions.Where(t => t.Type == "Refund").SumAsync(t => t.Amount), summary.Refunds); });
        await using var restarted = new SharedSqlServerFactory(); using var client = restarted.CreateClient(); using var scope = restarted.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EnergyDbContext>(); Assert.Equal("Paid", (await db.ChargingSessions.FindAsync(done.Id))!.PaymentStatus); Assert.Equal(1000, (await db.Wallets.SingleAsync(w => w.UserId == s.Owner)).Balance); Assert.Equal("QA-ADMIN-CREATED", (await db.Chargers.FindAsync(charger.Id))!.ChargerCode);
    }
    [SharedSqlServerFact]
    public async Task Sql_booking_vs_charger_deactivation_cannot_both_succeed()
    {
        await using var a = await AdminScenario.Create(true); var s = a.S; var charger = await a.Charger(); var results = await Task.WhenAll(s.Reserve(), a.UpdateCharger(charger, false));
        Assert.Single(results, r => r.IsSuccessStatusCode); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        await s.Db(async db => { var holds = await db.Bookings.CountAsync(b => b.ChargerId == s.Charger && b.Status == "Reserved"); var c = (await db.Chargers.FindAsync(s.Charger))!; Assert.False(c.Status == "Offline" && holds > 0); });
        var bookings = (await s.Client.GetFromJsonAsync<BookingResponse[]>("/api/bookings"))!; foreach (var b in bookings.Where(b => b.Status == "Reserved")) await s.Client.PostAsync($"/api/bookings/{b.Id}/cancel", null);
    }
    [SharedSqlServerFact]
    public async Task Sql_station_deactivation_vs_booking_and_charger_creation_obey_membership_gate()
    {
        await using var a = await AdminScenario.Create(true); var s = a.S; var station = await a.Station(); var results = await Task.WhenAll(s.Reserve(), a.UpdateStation(station, false)); Assert.Single(results, r => r.IsSuccessStatusCode); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        var bookings = (await s.Client.GetFromJsonAsync<BookingResponse[]>("/api/bookings"))!; foreach (var b in bookings.Where(b => b.Status == "Reserved")) await s.Client.PostAsync($"/api/bookings/{b.Id}/cancel", null);
        var current = await a.Station(); if (!current.IsActive) { Assert.Equal(HttpStatusCode.OK, (await a.UpdateStation(current, true)).StatusCode); current = await a.Station(); }
        var create = a.Admin.PostAsJsonAsync($"/api/admin/stations/{s.Station}/chargers", AdminScenario.ChargerBody(code: "QA-MEMBERSHIP-RACE")); var deactivate = a.UpdateStation(current, false); await Task.WhenAll(create, deactivate); Assert.Equal(HttpStatusCode.OK, (await deactivate).StatusCode); Assert.True((await create).StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict);
        Assert.False((await a.Station()).IsActive); Assert.Equal(HttpStatusCode.Conflict, (await s.Reserve()).StatusCode);
    }
    [SharedSqlServerFact]
    public async Task Sql_active_sessions_prevent_configuration_override_and_versions_reject_stale_edits()
    {
        await using var a = await AdminScenario.Create(true); var s = a.S; var session = await s.Start((await s.Book()).Id);
        Assert.Equal(HttpStatusCode.Conflict, (await a.UpdateCharger(await a.Charger(), false)).StatusCode); Assert.Equal(HttpStatusCode.Conflict, (await a.UpdateStation(await a.Station(), false)).StatusCode);
        s.Clock.Advance(10); var done = await s.Stop(session.Id); Assert.Equal(250, done.Cost); var c = await a.Charger(); Assert.Equal(HttpStatusCode.OK, (await a.UpdateCharger(c, true, 99)).StatusCode); Assert.Equal(HttpStatusCode.Conflict, (await a.UpdateCharger(c, false)).StatusCode);
        var created = await a.Admin.PostAsJsonAsync("/api/admin/stations",AdminScenario.StationBody(name:"QA Coordinate precision"));
        var createdStation=(await created.Content.ReadFromJsonAsync<AdminStationResponse>())!;
        Assert.Equal(HttpStatusCode.OK,(await a.UpdateStation(createdStation,true,"QA Stable version after SQL round trip")).StatusCode);
        var station = await a.Station(); Assert.Equal(HttpStatusCode.OK, (await a.UpdateStation(station, true, "QA Edited station")).StatusCode); Assert.Equal(HttpStatusCode.Conflict, (await a.UpdateStation(station, false)).StatusCode); Assert.Equal(250, (await s.Progress(done.Id))!.Cost);
    }
}
