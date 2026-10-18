using System.ComponentModel.DataAnnotations;
using GEnergy.Api.Authorization;
using GEnergy.Api.Data;
using GEnergy.Api.DTOs;
using GEnergy.Api.Entities;
using GEnergy.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
namespace GEnergy.Api.Controllers;

[ApiController, Route("api/vehicles"), Authorize(Policy = "VehicleUser")]
public sealed class VehiclesController(EnergyDbContext db, CurrentUser current, VehicleAccess access, IVehicleActivityGuard guard) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<VehicleResponse>>> List(CancellationToken ct)
    {
        var vehicles = await db.Vehicles.AsNoTracking().Include(v => v.Drivers).ThenInclude(d => d.Driver)
            .Where(v => v.OwnerUserId == current.Id || v.Drivers.Any(d => d.DriverUserId == current.Id && d.IsActive && d.Driver.IsActive && d.Driver.Role == Roles.CarDriver)).OrderBy(v => v.Id).ToListAsync(ct);
        return Ok(vehicles.Select(v => VehicleResponse.From(v, v.OwnerUserId == current.Id || v.Drivers.Any(d => d.DriverUserId == current.Id && d.IsActive && d.CanCharge && d.Driver.IsActive && d.Driver.Role == Roles.CarDriver))));
    }
    [HttpGet("{id:long}")]
    public async Task<ActionResult<VehicleResponse>> Details(long id, CancellationToken ct)
    {
        if (!await access.CanViewAsync(id, current.Id, ct)) return NotFound();
        var v = await db.Vehicles.AsNoTracking().SingleAsync(v => v.Id == id, ct);
        return Ok(VehicleResponse.From(v, await access.CanChargeAsync(id, current.Id, ct)));
    }
    [HttpGet("{id:long}/status")]
    public async Task<ActionResult<VehicleStatusResponse>> Status(long id, CancellationToken ct)
    {
        if (!await access.CanViewAsync(id, current.Id, ct)) return NotFound();
        var v = await db.Vehicles.AsNoTracking().SingleAsync(v => v.Id == id, ct);
        return Ok(new VehicleStatusResponse(v.Id, v.BatteryPercent, VehicleResponse.Range(v), true, await access.CanChargeAsync(id, current.Id, ct), v.UpdatedAtUtc));
    }
    [HttpPost, Authorize(Policy = Roles.CarOwner)]
    public async Task<ActionResult<VehicleResponse>> Create(VehicleRequest request, CancellationToken ct)
    {
        if (!ValidPrecision(request) || request.Model.Trim().Length < 2 || request.RegistrationNo.Trim().Length < 2) return Problem(statusCode: 400, title: "Check vehicle text and numeric precision (capacity/battery: 2 decimals; consumption: 4).");
        var registration = NormalizeRegistration(request.RegistrationNo);
        if (await db.Vehicles.AnyAsync(v => v.RegistrationNo == registration, ct)) return Problem(statusCode: 409, title: "Registration number is already in use.");
        var v = new Vehicle { OwnerUserId = current.Id }; Apply(v, request, registration); db.Vehicles.Add(v);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException e) when (IsUnique(e)) { return Problem(statusCode: 409, title: "Registration number is already in use."); }
        return CreatedAtAction(nameof(Details), new { id = v.Id }, VehicleResponse.From(v, true));
    }
    [HttpPut("{id:long}"), Authorize(Policy = Roles.CarOwner)]
    public async Task<ActionResult<VehicleResponse>> Update(long id, VehicleRequest request, [FromHeader(Name = "If-Match"), Required] string version, CancellationToken ct)
    {
        if (!ValidPrecision(request) || request.Model.Trim().Length < 2 || request.RegistrationNo.Trim().Length < 2) return Problem(statusCode: 400, title: "Invalid vehicle fields or numeric precision.");
        await using var write = await VehicleWriteScope.OpenAsync(db, id, ct); var v = write.Vehicle;
        if (v is null || v.OwnerUserId != current.Id) return NotFound();
        var conflict = Match(v, version); if (conflict is not null) return conflict;
        try { await guard.EnsureMutableAsync(id, ct); }
        catch (MobilityConflictException e) { return Problem(statusCode: 409, title: e.Message); }
        var registration = NormalizeRegistration(request.RegistrationNo);
        if (await db.Vehicles.AnyAsync(x => x.Id != id && x.RegistrationNo == registration, ct)) return Problem(statusCode: 409, title: "Registration number is already in use.");
        Apply(v, request, registration);
        var saved = await SaveAsync(ct); if (saved is not null) return saved;
        await write.CommitAsync(ct); return Ok(VehicleResponse.From(v, true));
    }
    [HttpGet("{id:long}/drivers"), Authorize(Policy = Roles.CarOwner)]
    public async Task<ActionResult<IReadOnlyList<DriverResponse>>> Drivers(long id, CancellationToken ct)
    {
        if (!await db.Vehicles.AnyAsync(v => v.Id == id && v.OwnerUserId == current.Id, ct)) return NotFound();
        return Ok(await db.VehicleDrivers.AsNoTracking().Where(d => d.VehicleId == id && d.IsActive).OrderBy(d => d.DriverUserId)
            .Select(d => new DriverResponse(d.DriverUserId, d.Driver.UserCode, d.Driver.FullName, d.CanCharge, d.IsActive)).ToListAsync(ct));
    }
    [HttpPost("{id:long}/drivers"), Authorize(Policy = Roles.CarOwner)]
    public async Task<IActionResult> Assign(long id, DriverAssignmentRequest request, [FromHeader(Name = "If-Match"), Required] string version, CancellationToken ct)
    {
        await using var write = await VehicleWriteScope.OpenAsync(db, id, ct); var v = write.Vehicle;
        if (v is null || v.OwnerUserId != current.Id) return NotFound();
        var conflict = Match(v, version); if (conflict is not null) return conflict;
        try { await guard.EnsureMutableAsync(id, ct); } catch (MobilityConflictException e) { return Problem(statusCode: 409, title: e.Message); }
        var code = request.UserCode.Trim().ToUpperInvariant();
        var driver = await db.Users.SingleOrDefaultAsync(u => u.UserCode == code && u.IsActive && u.Role == Roles.CarDriver && u.Id != current.Id, ct);
        if (driver is null) return Problem(statusCode: 400, title: "Choose an active registered CarDriver UserCode.");
        var assignment = await db.VehicleDrivers.SingleOrDefaultAsync(d => d.VehicleId == id && d.DriverUserId == driver.Id, ct);
        if (assignment is { IsActive: true }) return Problem(statusCode: 409, title: "Driver is already assigned. Update permission instead.");
        if (assignment is null) { assignment = new VehicleDriver { VehicleId = id, DriverUserId = driver.Id }; db.VehicleDrivers.Add(assignment); }
        assignment.IsActive = true; assignment.CanCharge = request.CanCharge; assignment.UpdatedAtUtc = DateTime.UtcNow; v.UpdatedAtUtc = DateTime.UtcNow;
        var saved = await SaveAsync(ct); if (saved is not null) return saved;
        await write.CommitAsync(ct); return NoContent();
    }
    [HttpPut("{id:long}/drivers/{driverId:long}"), Authorize(Policy = Roles.CarOwner)]
    public Task<IActionResult> Permission(long id, long driverId, DriverPermissionRequest request, [FromHeader(Name = "If-Match"), Required] string version, CancellationToken ct) => ChangeAssignment(id, driverId, version, request.CanCharge, false, ct);
    [HttpDelete("{id:long}/drivers/{driverId:long}"), Authorize(Policy = Roles.CarOwner)]
    public Task<IActionResult> Revoke(long id, long driverId, [FromHeader(Name = "If-Match"), Required] string version, CancellationToken ct) => ChangeAssignment(id, driverId, version, false, true, ct);
    private async Task<IActionResult> ChangeAssignment(long id, long driverId, string version, bool canCharge, bool revoke, CancellationToken ct)
    {
        await using var write = await VehicleWriteScope.OpenAsync(db, id, ct); var v = write.Vehicle;
        if (v is null || v.OwnerUserId != current.Id) return NotFound();
        var conflict = Match(v, version); if (conflict is not null) return conflict;
        try { await guard.EnsureMutableAsync(id, ct); } catch (MobilityConflictException e) { return Problem(statusCode: 409, title: e.Message); }
        var assignment = await db.VehicleDrivers.Include(d => d.Driver).SingleOrDefaultAsync(d => d.VehicleId == id && d.DriverUserId == driverId && d.IsActive, ct);
        if (assignment is null) return NotFound();
        if (!revoke && (!assignment.Driver.IsActive || assignment.Driver.Role != Roles.CarDriver)) return Problem(statusCode: 400, title: "Driver account is unavailable.");
        assignment.CanCharge = canCharge; assignment.IsActive = !revoke; assignment.UpdatedAtUtc = DateTime.UtcNow; v.UpdatedAtUtc = DateTime.UtcNow;
        var saved = await SaveAsync(ct); if (saved is not null) return saved;
        await write.CommitAsync(ct); return NoContent();
    }
    private ObjectResult? Match(Vehicle v, string version)
    {
        byte[] bytes; try { bytes = Convert.FromBase64String(version.Trim('"')); } catch (FormatException) { return Problem(statusCode: 400, title: "If-Match must contain the vehicle rowVersion."); }
        return bytes.SequenceEqual(v.RowVersion) ? null : Problem(statusCode: 409, title: "Vehicle changed. Reload and try again.");
    }
    private async Task<ObjectResult?> SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); return null; }
        catch (DbUpdateConcurrencyException) { return Problem(statusCode: 409, title: "Vehicle changed. Reload and try again."); }
        catch (DbUpdateException e) when (IsUnique(e)) { return Problem(statusCode: 409, title: "Registration or assignment conflicts with an existing record."); }
    }
    private static bool IsUnique(DbUpdateException e) => e.InnerException is SqlException { Number: 2601 or 2627 };
    private static bool ValidPrecision(VehicleRequest r) => decimal.Round(r.BatteryCapacityKWh, 2) == r.BatteryCapacityKWh && decimal.Round(r.BatteryPercent, 2) == r.BatteryPercent && decimal.Round(r.ConsumptionKWhPerKm, 4) == r.ConsumptionKWhPerKm;
    private static string NormalizeRegistration(string value) => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    private static void Apply(Vehicle v, VehicleRequest r, string registration)
    { v.RegistrationNo = registration; v.Model = r.Model.Trim(); v.ConnectorType = r.ConnectorType; v.BatteryCapacityKWh = r.BatteryCapacityKWh; v.BatteryPercent = r.BatteryPercent; v.ConsumptionKWhPerKm = r.ConsumptionKWhPerKm; v.UpdatedAtUtc = DateTime.UtcNow; }
}
