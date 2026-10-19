# Member 2: shared SQL Server integration verified

Verified 19 October 2026, Asia/Dhaka. This supersedes the earlier no-SQL environment limitation in member2-verification.md, which remains a historical record.

## Shared database and migrations

The actual running SQL Server default instance is localhost / MSSQLSERVER (DESKTOP-0MG4DVB), Microsoft SQL Server Standard Developer Edition 17.0.1000.7. Windows Authentication is used. **GEnergyDb** is the same database configured and verified for Member 1; no second application database or duplicate Users table was created.

Member 1's original migration was already on origin/main, and its real-SQL verification commits 4e4b380 and 593c236 were pushed as rifatur17 before Member 2 application. We inspected/reused the existing additive migration; no unnecessary new schema migration or history rewrite was needed.

Both migrations are now applied in dbo.__EFMigrationsHistory (EF 10.0.13):

1. 20261018185818_InitialIdentity
2. 20261018192412_AddVehiclesAndStationDiscovery

Nine Member 1 QA Users were present immediately before Member 2 setup and remained present afterward. The new shared SQL test additionally compares a private digest of all pre-existing User records before/after its operations, including password-hash fields, without printing the data. It passed. A second Latest setup run compared private digests across all five business tables and preserved every row/value. No application database or existing record was deleted/recreated.

## Direct SQL catalog inspection

- Users: identity PK, unique UserCode and NormalizedEmail indexes, Role check, UTC datetime2 storage and UserCodeNumbers sequence remain intact.
- Vehicles: identity PK; OwnerUserId FK to Users; unique RegistrationNo; battery/positive capacity/consumption and connector checks; SQL-generated rowversion.
- VehicleDrivers: composite PK (VehicleId, DriverUserId); FKs to Vehicles and Users; CanCharge, IsActive and UTC UpdatedAtUtc.
- Stations: identity PK, name/address, decimal(9,6) coordinates, coordinate-range check, IsActive and IsDemo.
- Chargers: identity PK, StationId FK; unique (StationId, ChargerCode); positive power/tariff, status and connector checks; SQL-generated rowversion.
- All four FKs use NO_ACTION deletes. All FKs and seven check constraints are enabled/trusted (is_disabled=0 and is_not_trusted=0). SQL catalogs name the rowversion underlying type timestamp; both columns are native 8-byte versions, not date/time values.

The development-only `--seed-discovery` command ran twice against GEnergyDb. It retained exactly five labelled sample stations and ten chargers. These are illustrative Dhaka/Daudkandi/Cumilla/Feni/Chattogram locations, not verified real charging stations.

## Tests actually run

With both real-SQL environment settings enabled, the full backend suite passed **27/27, 0 skipped**:

- 23 existing HTTP/service tests (their InMemory coverage remains unit/integration evidence, not SQL evidence).
- 2 original isolated SQL Server tests: concurrency-safe registration/duplicate-email constraints and native vehicle rowversion/serialized stale updates (one success/one conflict). Their existing harness creates/deletes only its own new disposable catalogs, never GEnergyDb.
- 2 shared-database SQL tests against GEnergyDb itself: SharedIdentitySqlServerTests and SharedMobilitySqlServerTests. They never migrate/reset/delete the shared database and retain labelled QA records.

The new shared mobility test verifies actual vehicle storage/range, Owner foreign-read/edit isolation, Driver create/edit rejection, active valid Driver assignment, wrong-role/self/invalid relationships, persisted CanCharge false then true, station/connector reads, fresh-host login/vehicle/assignment/station persistence, and preservation of pre-existing Users. Native SQL statements verify duplicate vehicle registration, duplicate assignment composite key, duplicate station charger code, invalid Owner/Vehicle/Driver/Station FKs, positive charger power and coordinate bounds. Each intentionally failing statement runs inside its own rolled-back verification transaction; no existing record is modified by those constraint probes. Existing SQL tests also execute battery bounds and simultaneous stale-version updates.

Browser verification used the **actual application process and SQL Server provider**, not the previous temporary InMemory fixture. A labelled SQL verification Owner and Driver were registered through the API, a vehicle was saved, and CanCharge assigned. The browser displayed that SQL-backed vehicle and assignment. The backend process was stopped/restarted, then the browser page was reloaded; it retained the vehicle, 40% battery, 111.11 km range and charging permission. A screenshot is saved locally at C:/Test/output/g-energy-sqlserver-persistence.png (outside Git). Synthetic browser verification credentials are in local .NET user secrets only; no passwords/tokens/hashes are in source or this report.

Frontend regression tests: **26 passed**. TypeScript/production build passed. The setup script's downgrade guard was tested: requesting InitialIdentity after Member 2 application was refused before migration, preserving both migrations. Re-running Latest reported no pending migrations and preserved private table fingerprints.

## Repeat locally

PowerShell 7, repository root (add -DotnetPath if the SDK is not on PATH):

```powershell
./scripts/Initialize-DevelopmentDatabase.ps1 -Server localhost -Database GEnergyDb -Through Latest
$env:G_ENERGY_SHARED_SQLSERVER = 'Server=localhost;Database=GEnergyDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
$env:G_ENERGY_TEST_SQLSERVER = $env:G_ENERGY_SHARED_SQLSERVER
dotnet test
dotnet run --project backend/GEnergy.Api
```

G_ENERGY_SHARED_SQLSERVER explicitly opts in to additive verification in GEnergyDb; it retains uniquely labelled QA records. G_ENERGY_TEST_SQLSERVER separately opts in to the existing disposable-catalog tests; credentials need database-create permission for those. Do not point either setting at production. The shared tests do not substitute EF InMemory.

At the final data checkpoint: 26 synthetic verification Users, 3 Vehicles, 3 assignments, 5 sample Stations and 10 Chargers. Counts increase when additive shared tests run again; do not delete/recreate the database to make counts match a previous report.

## Delivery and remaining module boundaries

Member 2 test/documentation commits use Sabekunnaher Antora <antorasabekunnaher@gmail.com> and are pushed using sabekunnaher-17. The final delivery message reports verified remote hashes rather than pre-claiming a push here.

There is no remaining SQL Server accessibility or migration blocker. Members 3–5 still own actual bookings/expiry/charging adapters, wallet/settlement and Admin/release QA. No booking/payment concurrency or hardware behavior is claimed by these database checks; Member 3 must integrate the activity/availability adapters and common transaction locks described in member2-api.md.
