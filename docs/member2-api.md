# Member 2 API and integration contract

All routes use the existing API base URL (development: http://localhost:5080), JWT `Authorization: Bearer <accessToken>` and ProblemDetails conventions. Member 1's auth endpoints and claim names are unchanged. Internal IDs are SQL bigint keys; role or ownership is never inferred from UserCode prefixes.

## Routes

| Method | Route | Access / result |
| --- | --- | --- |
| GET | /api/vehicles | Owner's vehicles or Driver's active assignments, 200 array |
| POST | /api/vehicles | CarOwner only; 201 safe VehicleResponse |
| GET | /api/vehicles/{id} | Owner or active assigned Driver; 200 |
| PUT | /api/vehicles/{id} | Actual Owner only; If-Match required; 200 updated vehicle |
| GET | /api/vehicles/{id}/status | Owner/assigned Driver; simulated battery/range and CanCharge |
| GET | /api/vehicles/{id}/drivers | Actual Owner only; active assignments |
| POST | /api/vehicles/{id}/drivers | Actual Owner; UserCode lookup and assignment; If-Match; 204 |
| PUT | /api/vehicles/{id}/drivers/{driverId} | Actual Owner; change CanCharge; If-Match; 204 |
| DELETE | /api/vehicles/{id}/drivers/{driverId} | Actual Owner; soft revoke; If-Match; 204 |
| GET | /api/drivers/lookup?userCode=GE-DRV-001002 | CarOwner; active CarDriver's ID/code/name only |
| GET | /api/stations?connectorType=CCS2&search=Cumilla | Authenticated; active stations and matching chargers |
| GET | /api/stations/{id} | Authenticated; active station with all chargers |

Admin is intentionally excluded from vehicle routes; Member 5 adds separately authorized operational endpoints. No vehicle deletion, ownership transfer, public station mutation or browser-owned charging endpoint exists. Foreign/missing vehicle IDs return 404 to avoid disclosing another Owner's resources. Wrong roles return 403; missing/invalid JWT returns 401. Invalid input/missing If-Match returns 400; duplicate registration/assignment, stale version and activity conflicts return 409.

## Vehicle requests and responses

POST/PUT body (OwnerUserId, DriverUserId and calculated range are never accepted as authority):

```json
{
  "registrationNo": "DHAKA AB-1234",
  "model": "Demo EV",
  "connectorType": "CCS2",
  "batteryCapacityKWh": 50,
  "batteryPercent": 40,
  "consumptionKWhPerKm": 0.18
}
```

Registration: 2–32 characters, trimmed, repeated whitespace collapsed, uppercased, globally unique database index. Model: 2–100 characters, trimmed. Connectors: exactly CCS2, Type2 or CHAdeMO. Capacity: 0.01–999999.99 kWh; battery: 0–100%; consumption: 0.0001–9999.9999 kWh/km. Capacity/percentage accept at most two decimals, consumption four; excess precision is rejected before SQL rounding. Only the Owner may change these simulated inputs, under the activity guard.

VehicleResponse includes the request fields plus `id`, `estimatedRangeKm`, `isSimulated: true`, `canCharge`, `rowVersion` (base64), `createdAtUtc` and `updatedAtUtc`. Estimated range is rounded to two decimals using capacity × (batteryPercent / 100) / consumption. For 50 kWh, 40%, 0.18 kWh/km it is 111.11 km. Status response contains vehicleId, batteryPercent, estimatedRangeKm, isSimulated, canCharge and updatedAtUtc. No live telemetry is claimed.

For PUT and every assignment mutation, obtain the latest rowVersion from GET vehicle, then send `If-Match: "<base64 rowVersion>"`. Reload the vehicle after 204 to obtain its new version. A stale version returns 409; clients must reload and review before retrying. SQL Server generates the rowversion after each vehicle/assignment mutation; timestamps and version do not come from the browser.

Assignment POST: `{ "userCode": "GE-DRV-001002", "canCharge": false }`. Permission PUT: `{ "canCharge": true }`. Assigned user must be an active CarDriver and cannot be the Owner. Active duplicate assignment returns 409; a revoked relationship is reactivated rather than duplicated. DELETE retains the relationship with IsActive=false/CanCharge=false. DriverResponse contains driverUserId, userCode, fullName, canCharge and isActive; email, phone and credentials are excluded.

## Shared authorization / mutation integration (Member 3 and 4)

Inject `VehicleAccess` and use `CanViewAsync(vehicleId, userId)` for read access, `CanChargeAsync(vehicleId, userId)` for reservation/charging authorization. CanCharge requires an active CarOwner account for the vehicle and either that actual Owner or an active CarDriver with an active CanCharge assignment. Passing a forged Owner/Driver ID from the browser is forbidden; use CurrentUser.Id after JWT validation. DTO canCharge is display information and is never proof for a subsequent action.

`VehicleWriteScope.OpenAsync(db, vehicleId, ct)` starts a SQL transaction and reads the root vehicle with `UPDLOCK, HOLDLOCK`. All vehicle/assignment changes hold this lock through permission/activity checks, SaveChanges and CommitAsync. Member 3 must acquire the same root lock before checking assignments or creating/changing reservations/sessions, and agree vehicle → charger → dependent row lock order. Perform CanCharge again in that transaction. Do not wrap this helper in a second transaction; extend the coordinated transaction boundary instead. The InMemory test path deliberately has no SQL locking semantics.

Replace `IVehicleActivityGuard` with a scoped adapter in the same DbContext/transaction that throws MobilityConflictException when a Reserved booking has ExpiresAtUtc > server UTC now or a charging session is Active. Apply it to every mutation path, including workers/Admin changes that affect those resources. At exactly expiry the hold is no longer effective. This module does not define competing booking/session tables.

Set `Modules__BookingEnabled=true` when introducing booking, and replace both integration adapters in that same change. The current NoBookingModuleActivityGuard permits changes only while booking is absent. If the flag is enabled without replacement, vehicle/assignment mutations fail with 409 rather than bypassing activity checks. This flag is an integration gate, not an implementation of reservations.

## Stations and availability

StationResponse: id, name, address, latitude, longitude, isDemo and chargers. ChargerResponse: id, chargerCode, connectorType, powerKW, pricePerKWh, currency (BDT), configuredStatus, effectiveStatus, isAvailable and availabilitySource. Supported persisted statuses: Available, Reserved, Charging, Offline. Connector filtering removes non-matching chargers and stations with no matches. Search matches name/address, maximum 120 characters. Inactive stations are hidden, including direct details.

`IChargerAvailabilityProvider.GetAsync(chargers, ct)` is a batch integration hook. With no booking module, ConfiguredChargerAvailability reports the persisted status with source ConfiguredStatus; this is a configured snapshot, not verified transactional availability. Member 3 replaces it with a UTC-aware reservation/session adapter that resolves expired holds without waiting for a worker. It must return all requested charger IDs and a meaningful source. Missing entries become Unknown/unavailable; Offline cannot be overridden by the adapter. When BookingEnabled=true without replacement, other chargers become Unknown with source IntegrationRequired. The booking API must revalidate availability under locks regardless of what discovery displayed.

Member 5 reuses Stations/Chargers from the shared EnergyDbContext; do not create another definition or initial migration. Admin mutations and operational summaries remain Member 5's work. Foreign keys use Restrict deletes to preserve future history.

## Migration and sample data

Migration: `20261018192412_AddVehiclesAndStationDiscovery`, following Member 1 InitialIdentity. It adds Vehicles, VehicleDrivers (composite key), Stations and Chargers, with foreign keys, unique indexes, rowversions and SQL checks. It does not alter Users or create financial/booking tables. Review delta SQL in member2-migration.sql; apply migrations sequentially:

```powershell
dotnet tool restore
dotnet ef database update --project backend/GEnergy.Api
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project backend/GEnergy.Api -- --seed-discovery
```

The explicit development-only seed adds five labelled sample locations: Dhaka, Daudkandi, Cumilla, Feni and Chattogram, each with CCS2 60 kW / 25 BDT and Type2 22 kW / 20 BDT demo points. These coordinates, powers and tariffs are illustrative, not verified station/model information. It adds missing named sample stations/point codes without overwriting existing settings, serializes concurrent SQL seed commands with a short table lock, and rejects Production. Member 5 should reuse these records for the demo/release seed rather than duplicate them.

## Frontend integration

Routes: /vehicles, /vehicles/:id (Owner/Driver), /stations (all authenticated roles). Use the shared navigation, AuthProvider, Protected guard and typed useMobilityApi service; its 401 handling clears the tab session. Modules keep errors/loading/empty states; a failed request never substitutes invented stations. A vehicle's compatible-station link preselects its connector. Booking UI is a future integration, not a working button.

Discovery uses Leaflet, standard HTTPS OpenStreetMap tiles and visible attribution. Requests occur only for normal map viewing; no tile prefetch/offline downloader is implemented. Geolocation runs only after clicking Use my location and the browser's permission flow; denial/unavailability/10-second timeout leaves the selectable demo origin and list usable. Haversine output is explicitly approximate straight-line distance, never ETA or road distance. Tile/map errors preserve the independent list; List only hides the map entirely.

References: [Leaflet API](https://leafletjs.com/reference), [OpenStreetMap tile policy](https://operations.osmfoundation.org/policies/tiles/).
