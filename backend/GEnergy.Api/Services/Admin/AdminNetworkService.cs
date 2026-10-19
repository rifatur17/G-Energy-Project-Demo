using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GEnergy.Api.Data;
using GEnergy.Api.DTOs;
using GEnergy.Api.Entities;
using GEnergy.Api.Services.Charging;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
namespace GEnergy.Api.Services.Admin;

public sealed class AdminNetworkService(EnergyDbContext db, TimeProvider clock, IChargerAvailabilityProvider availability)
{
    public static string Version(Station s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { s.Id, s.Name, s.Address, Latitude=s.Latitude.ToString("F6",System.Globalization.CultureInfo.InvariantCulture), Longitude=s.Longitude.ToString("F6",System.Globalization.CultureInfo.InvariantCulture), s.IsActive, s.IsDemo }))));
    private static ChargingException Missing() => new(404, "Station or charger was not found.");
    private static void Match(string supplied, string actual) { if (string.IsNullOrWhiteSpace(supplied)) throw new ChargingException(400, "If-Match is required."); if (supplied.Trim('"') != actual) throw new ChargingException(409, "Configuration changed. Reload and review before saving."); }
    private async Task<IDbContextTransaction?> Begin(CancellationToken ct) => db.Database.IsSqlServer() ? await db.Database.BeginTransactionAsync(ct) : null;
    private static async Task Commit(IDbContextTransaction? tx, CancellationToken ct) { if (tx is not null) await tx.CommitAsync(ct); }
    // Only Admin membership/configuration operations take this application lock. They then lock
    // Chargers in ascending ID order, BEFORE Station. Booking/charging never wait on this lock.
    // It prevents a concurrent Admin insert from changing the child set during station-wide checks.
    private async Task StationGate(long stationId, CancellationToken ct)
    {
        if (!db.Database.IsSqlServer()) return;
        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        await db.Database.ExecuteSqlRawAsync("EXEC @result = sys.sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;", new object[] { result, new SqlParameter("@resource", $"GEnergy:AdminStation:{stationId}") }, ct);
        if ((int)result.Value < 0) throw new ChargingException(409, "Station configuration is busy. Reload and retry.");
    }
    private Task<Charger?> LockCharger(long id, CancellationToken ct) => db.Database.IsSqlServer() ? db.Chargers.FromSqlInterpolated($"SELECT * FROM dbo.Chargers WITH (UPDLOCK,HOLDLOCK) WHERE Id={id}").SingleOrDefaultAsync(ct) : db.Chargers.SingleOrDefaultAsync(c => c.Id == id, ct);
    private async Task<Station> LockStation(long id, CancellationToken ct) => (db.Database.IsSqlServer() ? await db.Stations.FromSqlInterpolated($"SELECT * FROM dbo.Stations WITH (UPDLOCK,HOLDLOCK) WHERE Id={id}").SingleOrDefaultAsync(ct) : await db.Stations.SingleOrDefaultAsync(s => s.Id == id, ct)) ?? throw Missing();
    private async Task NotBusy(long[] chargerIds, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (await db.Bookings.AnyAsync(b => chargerIds.Contains(b.ChargerId) && b.Status == "Reserved" && b.ExpiresAtUtc > now, ct) || await db.ChargingSessions.AnyAsync(s => chargerIds.Contains(s.ChargerId) && s.Status == "Active", ct)) throw new ChargingException(409, "A charger has a valid reservation or active session. Finish or release it through the normal lifecycle before editing configuration.");
    }
    private static void StationInput(AdminStationRequest r)
    { if (r.Name.Trim().Length < 2 || r.Address.Trim().Length < 3 || decimal.Round(r.Latitude, 6) != r.Latitude || decimal.Round(r.Longitude, 6) != r.Longitude) throw new ChargingException(400, "Provide a station name/address and coordinates with at most six decimals."); }
    private static void ChargerInput(AdminChargerRequest r)
    { if (r.ChargerCode.Trim().Length < 2 || decimal.Round(r.PowerKW, 2) != r.PowerKW || decimal.Round(r.PricePerKWh, 2) != r.PricePerKWh) throw new ChargingException(400, "Provide a charger code and positive power/tariff with at most two decimals."); }
    private static AdminStationResponse StationDto(Station s, int count) => new(s.Id, s.Name, s.Address, s.Latitude, s.Longitude, s.IsActive, s.IsDemo, count, Version(s));
    private static void Apply(Station s, AdminStationRequest r) { s.Name = r.Name.Trim(); s.Address = r.Address.Trim(); s.Latitude = r.Latitude; s.Longitude = r.Longitude; s.IsActive = r.IsActive; s.IsDemo = r.IsDemo; }
    private void Apply(Charger c, AdminChargerRequest r) { c.ChargerCode = r.ChargerCode.Trim().ToUpperInvariant(); c.ConnectorType = r.ConnectorType; c.PowerKW = r.PowerKW; c.PricePerKWh = r.PricePerKWh; c.Status = r.IsActive ? "Available" : "Offline"; if (!db.Database.IsSqlServer()) c.RowVersion = RandomNumberGenerator.GetBytes(8); }
    public async Task<IReadOnlyList<AdminStationResponse>> Stations(string? search, bool? active, long? before, int take, CancellationToken ct)
    {
        var q = db.Stations.AsNoTracking().Where(s => (!before.HasValue || s.Id < before) && (!active.HasValue || s.IsActive == active));
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim(); q = q.Where(s => s.Name.Contains(term) || s.Address.Contains(term)); }
        var rows = await q.OrderByDescending(s => s.Id).Take(take).Select(s => new { s.Id, s.Name, s.Address, s.Latitude, s.Longitude, s.IsActive, s.IsDemo, Count = s.Chargers.Count }).ToListAsync(ct);
        return rows.Select(s => StationDto(new() { Id = s.Id, Name = s.Name, Address = s.Address, Latitude = s.Latitude, Longitude = s.Longitude, IsActive = s.IsActive, IsDemo = s.IsDemo }, s.Count)).ToArray();
    }
    public async Task<AdminStationResponse> CreateStation(AdminStationRequest r, CancellationToken ct)
    { StationInput(r); var s = new Station(); Apply(s, r); db.Stations.Add(s); await db.SaveChangesAsync(ct); return StationDto(s, 0); }
    public async Task<AdminStationResponse> UpdateStation(long id, AdminStationRequest r, string version, CancellationToken ct)
    {
        StationInput(r); await using var tx = await Begin(ct); await StationGate(id, ct);
        var ids = await db.Chargers.Where(c => c.StationId == id).OrderBy(c => c.Id).Select(c => c.Id).ToArrayAsync(ct); foreach (var charger in ids) await LockCharger(charger, ct);
        var s = await LockStation(id, ct); Match(version, Version(s)); await NotBusy(ids, ct); Apply(s, r); await db.SaveChangesAsync(ct); await Commit(tx, ct); return StationDto(s, ids.Length);
    }
    public async Task<IReadOnlyList<AdminChargerResponse>> Chargers(long stationId, long? before, int take, CancellationToken ct)
    {
        var station = await db.Stations.AsNoTracking().SingleOrDefaultAsync(s => s.Id == stationId, ct) ?? throw Missing();
        var rows = await db.Chargers.AsNoTracking().Where(c => c.StationId == stationId && (!before.HasValue || c.Id < before)).OrderByDescending(c => c.Id).Take(take).ToListAsync(ct); var effective = await availability.GetAsync(rows, ct);
        return rows.Select(c => new AdminChargerResponse(c.Id, c.StationId, station.Name, c.ChargerCode, c.ConnectorType, c.PowerKW, c.PricePerKWh, c.Status != "Offline", c.Status, !station.IsActive ? "StationInactive" : effective[c.Id].EffectiveStatus, Convert.ToBase64String(c.RowVersion))).ToArray();
    }
    public async Task<AdminChargerResponse> CreateCharger(long stationId, AdminChargerRequest r, CancellationToken ct)
    {
        ChargerInput(r); await using var tx = await Begin(ct); await StationGate(stationId, ct); var s = await LockStation(stationId, ct);
        if (!s.IsActive && r.IsActive) throw new ChargingException(409, "Activate the station before enabling a charger.");
        var code = r.ChargerCode.Trim().ToUpperInvariant(); if (await db.Chargers.AnyAsync(c => c.StationId == stationId && c.ChargerCode == code, ct)) throw new ChargingException(409, "Charger code already exists at this station.");
        var c = new Charger { StationId = stationId }; Apply(c, r); db.Chargers.Add(c); await db.SaveChangesAsync(ct); await Commit(tx, ct); return new(c.Id, stationId, s.Name, c.ChargerCode, c.ConnectorType, c.PowerKW, c.PricePerKWh, r.IsActive, c.Status, s.IsActive ? c.Status : "StationInactive", Convert.ToBase64String(c.RowVersion));
    }
    public async Task<AdminChargerResponse> UpdateCharger(long id, AdminChargerRequest r, string version, CancellationToken ct)
    {
        ChargerInput(r); var stationId = await db.Chargers.AsNoTracking().Where(c => c.Id == id).Select(c => (long?)c.StationId).SingleOrDefaultAsync(ct) ?? throw Missing();
        await using var tx = await Begin(ct); await StationGate(stationId, ct); var c = await LockCharger(id, ct) ?? throw Missing(); Match(version, Convert.ToBase64String(c.RowVersion));
        await NotBusy([id], ct); var s = await LockStation(stationId, ct); if (!s.IsActive && r.IsActive) throw new ChargingException(409, "Activate the station before enabling a charger.");
        var code = r.ChargerCode.Trim().ToUpperInvariant(); if (await db.Chargers.AnyAsync(x => x.StationId == stationId && x.Id != id && x.ChargerCode == code, ct)) throw new ChargingException(409, "Charger code already exists at this station.");
        Apply(c, r); await db.SaveChangesAsync(ct); await Commit(tx, ct); return new(c.Id, stationId, s.Name, c.ChargerCode, c.ConnectorType, c.PowerKW, c.PricePerKWh, r.IsActive, c.Status, s.IsActive ? c.Status : "StationInactive", Convert.ToBase64String(c.RowVersion));
    }
}
