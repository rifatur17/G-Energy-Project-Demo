# Member 3 verification — 19 October 2026

## Repository and environment

Continued existing clean main at `ef8e2e08ef84e93dea5a15e56ea5568b6b46376b`. Fetched origin before implementation and again before final validation; no remote commits had arrived. Preserved Member 1/2 history and applied migrations. Repository-local author is Rifatur Rahman <rifaturrahman1131@gmail.com>. Existing Git Credential Manager credentials were used securely; GitHub `/user` verified `rifatur17`, and repository permissions reported push=true. No credential values were printed or committed. The final push/hash result is recorded in the task report and can be checked with `git ls-remote origin refs/heads/main`.

Actual SQL Server: default `localhost` / `DESKTOP-0MG4DVB`, service MSSQLSERVER running, Database Engine 17.0.1000.7. Database: the existing `GEnergyDb`, Windows Authentication. Existing .NET user secrets supply the local connection and JWT key. No shared database was created, reset, dropped, restored or downgraded.

## Migration and data preservation

Reviewed generated SQL before applying `20261019041722_AddBookingAndCharging` through `scripts/Initialize-DevelopmentDatabase.ps1 -Through Latest`. EF reported no pending model changes and successfully applied it. SQL migration history directly confirms:

1. `20261018185818_InitialIdentity`
2. `20261018192412_AddVehiclesAndStationDiscovery`
3. `20261019041722_AddBookingAndCharging`

The additive script creates only Bookings and ChargingSessions. Direct sys.tables/sys.indexes/sys.foreign_keys/sys.check_constraints queries verified both tables; native rowversion; BookingCode/user-idempotency uniqueness; reserved charger/vehicle and active session charger/vehicle filtered unique indexes; unique session BookingId; history/expiry indexes; all eight NO ACTION foreign keys enabled; all four CHECK constraints enabled and trusted. A direct SQL-backed EF insert attempting a second Reserved booking on the charger failed with DbUpdateException.

Before migration, existing data comprised 26 Users, 3 Vehicles, 3 VehicleDrivers, 5 Stations and 10 Chargers. SHA-256 fingerprints of ordered full rows, counts and primary-key boundaries were captured outside the repository. All five full-table fingerprints matched immediately after migration. After SQL integration testing, fingerprints of the original rows still matched, including original password hashes and rowversions. Existing data and QA rows were preserved. Tests retain new uniquely labelled `QA Member3 …` accounts/vehicles/stations/bookings/sessions and synthetic @example.test emails; sample locations are not verified real charging stations. Browser verification used an additional new Owner/vehicle and a new QA charger from this task, never an original Member 1/2 vehicle/charger.

## Automated results

Final validation: **41 backend tests passed, zero failures, zero skips** with both SQL environment variables set; **34 frontend tests passed across five files**. Backend Release compilation succeeded with zero warnings/errors; frontend TypeScript + Vite production build succeeded. EF `has-pending-model-changes` confirmed consistency. Existing test assertions were preserved. Older deterministic factories disable autonomous scanning; a separate real hosted-worker test enables it. The legacy missing-adapter fail-closed test explicitly installs its legacy adapters rather than accidentally testing the now-integrated application.

The backend total contains 32 non-SQL tests and nine real SQL Server tests. Five new Member 3 tests use the actual shared GEnergyDb, two existing shared tests verify identity/mobility, and two existing isolated SQL tests create/delete only their own random test catalogs as previously designed. InMemory tests are not SQL concurrency/transaction evidence.

Member 3 automated coverage includes:

- Successful reservation, server-generated exact 180-second lifetime despite injected client expiry, retry idempotency and conflicting payloads.
- Cancellation/repeated cancellation; expiry at the exact boundary; expired start rejection; replacement holds not released by stale expiry processing.
- Owner/Driver permission and foreign-user booking/session isolation; unauthorized Driver, connector mismatch, full battery and Offline rejection; payer Owner snapshot; Member 2 assignment edits blocked by activity.
- Start/repeated start, progress, stop/repeated stop; tariff snapshot; persisted vehicle battery, energy, cost and range; battery/energy cap; timestamps and deterministic full-charge instant.
- Real concurrent different-vehicle/same-charger reservations: exactly one succeeds. Real same-vehicle/different-charger reservations: exactly one succeeds. Concurrent same-key retries: one persisted booking.
- Real concurrent starts/stops: one session, one successful settlement participant call, unchanged repeat results. New application host reads the same SQL-persisted summary.
- Real settlement participant exception: completion, battery, energy and charger release roll back together. Retrying succeeds from fresh request scope.
- Real expiry/start/cancel races, start-versus-cancel race, and SQL filtered-index rejection of direct conflicting insert.
- Real hosted BackgroundService with test clock and 100ms scan: expires without status polling, logs/retries a failed finalization, then completes at 100% / 30 kWh / 750 BDT and releases the charger. Host disposal covers worker cancellation.

Frontend tests cover API reservation and idempotency key retention, server-relative countdown progression/clamping, expiry on returning, cancellation, start navigation, progress/summary rendering, stop, network/state errors, and failed-expired-start refresh. All existing login/signup/auth, vehicle/driver, discovery and map tile fallback tests also passed. Map fallback assertions remain intact; tile-error state updates are awaited.

### Reproduce

Use the existing local configuration. Substitute the actual dotnet executable if it is not on PATH (this computer used `C:\Test\runtimes\projectflow-dotnet\dotnet.exe`). Stop other development hosts against the shared database while running deterministic SQL tests.

```powershell
$env:G_ENERGY_SHARED_SQLSERVER = 'Server=localhost;Database=GEnergyDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
$env:G_ENERGY_TEST_SQLSERVER = 'Server=localhost;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
dotnet test --configuration Release --no-restore
dotnet ef migrations has-pending-model-changes --project backend/GEnergy.Api
cd frontend
npm test
npm run build
```

Shared tests require migrations to be applied explicitly first; they never migrate/reset/delete GEnergyDb. The test-clock SQL checks run real transactions and locks but are small integration races, not a production load/endurance test.

## Live browser and actual-clock SQL verification

Ran the API on localhost:5080 with the default worker, 180-second expiry and 60× simulation; frontend on localhost:5173. Signed in through the browser using a new labelled QA Owner. The reservation survived browser refresh and the countdown continued from server time.

Actual default-clock booking #27 was created at `2026-10-19T04:32:39.6148553Z`, expiring exactly at `04:35:39.6148553Z`. Navigated away to station discovery before expiry, so booking/status requests were no longer cleaning it up. Direct SQL at `04:35:47.5994328Z` confirmed Expired and Charger Available, following the worker's next periodic scan. Discovery also refreshed to Available.

Through the real UI: selected the QA station/charger and eligible vehicle, reserved, cancelled and re-reserved, then started session #10 from booking #29. Refreshed during active charging. Stopped and restarted the backend process; the same authenticated browser route resumed the same active SQL-backed session with increasing battery/energy, then stopped successfully before full charge. Direct SQL confirmed:

| Field | Persisted value |
| --- | --- |
| Status / payment | Completed / Unpaid |
| Energy | 28.946033 kWh (UI 28.946) |
| Final cost | 723.65 BDT |
| Session and vehicle battery | 97.89% |
| Elapsed real seconds | 28.946 |
| StartedAtUtc | 2026-10-19T04:36:25.8259569Z |
| EndedAtUtc | 2026-10-19T04:36:54.7719907Z |
| Charger | Available |

The mobile-width browser summary displayed the saved range, energy, duration, final cost and Unpaid / Wallet integration pending notice. A local screenshot is retained outside the repository at `C:\Test\member3-charging-summary.png`; it contains only synthetic demo data.

## Remaining integration work

Member 4 must implement the transaction-bound IChargingSettlement participant and unique SessionId debit described in [the contract](member3-booking-charging.md#member-4-settlement-contract). No payment was performed or claimed. Member 5 must preserve the shared lock protocol when administering chargers/vehicles and define its own activity/reporting authorization. Member 3 has no physical charger integration, verified real station data, payment gateway, production load-test evidence or operational deployment. Latest-100 history lists need pagination for larger usage. These are documented integration boundaries, not failures of the verified demo flow.
