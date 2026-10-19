# Member 3: booking and charging

Uses the existing EnergyDbContext, SQL Server provider, Users, Vehicles, assignments, Stations and Chargers. No wallet or physical charging hardware is implemented.

## Lifecycle and permissions

Bookings transition `Reserved → Expired | Cancelled | Consumed`. Consumed means exactly one session was created; follow its `Active → Completed` state for charging progress. Terminal bookings cannot be reused. A new reservation requires a new idempotency key. Server UTC creation and expiry are exactly 180 seconds apart by default. Client timestamps are ignored.

An active Owner can reserve their vehicle. An active assigned CarDriver requires both IsActive and CanCharge, and the vehicle Owner must remain active. A booking can be read/cancelled/started by its original booker or the vehicle Owner. Another assigned Driver cannot take over somebody else's reservation. Starting rechecks current charging eligibility. Session reads/stops are restricted to the original booker, starter or snapshotted payer Owner. Admin has no charging mutation permission. Foreign booking/session IDs return 404; denied vehicle charging permission returns 403.

Vehicle/assignment edits use Member 2's root lock and the new BookingActivityGuard; effective reservations and active sessions block edits. Full batteries, connector mismatches, inactive stations, Offline chargers, conflicting vehicles/chargers and mismatched station/charger IDs are rejected. Unexpired holds are effective until the exact expiry boundary (`now >= ExpiresAtUtc` is expired).

## API

All routes require the existing bearer JWT. Booking/session routes require VehicleUser (CarOwner or CarDriver). Discovery/availability also allows authenticated Admin reads.

| Method | Route | Contract |
| --- | --- | --- |
| POST | `/api/bookings` | JSON `{vehicleId,stationId,chargerId}`; required `Idempotency-Key` header, 1–100 nonblank characters. 201 with Location and BookingResponse. Same user/key/payload returns the same booking; changed payload returns 409. |
| GET | `/api/bookings` | Latest 100 accessible reservations, refreshed for expiry. |
| GET | `/api/bookings/{id}` | Authoritative booking status and request-time expiry. |
| POST | `/api/bookings/{id}/cancel` | Reserved becomes Cancelled, releases charger; repeated cancel returns the terminal booking. Consumed returns 409; stop the session instead. |
| POST | `/api/bookings/{id}/start` | Atomically consumes the hold and returns SessionResponse. Repeated start returns the existing session (including when completed). |
| GET | `/api/chargers/{id}/availability` | Refreshes expired holds; returns chargerId, effectiveStatus, isAvailable, serverTimeUtc. |
| GET | `/api/charging-sessions` | Latest 100 accessible sessions, with refreshed progress. |
| GET | `/api/charging-sessions/active` | Accessible sessions still Active after progress/automatic completion. |
| GET | `/api/charging-sessions/{id}` | Refreshes and persists server-calculated progress. |
| POST | `/api/charging-sessions/{id}/stop` | Finalizes once and returns persisted summary; repeats do not recalculate or repeat settlement. |

BookingResponse includes id, bookingCode, vehicleId, stationId, chargerId, stationName, chargerCode, status, createdAtUtc, expiresAtUtc, serverTimeUtc and nullable sessionId. SessionResponse includes IDs, status, paymentStatus, settlementNote, start/end/server timestamps, batteryPercent, estimatedRangeKm, energyKWh, cost, elapsedSeconds, powerKW, tariffPerKWh, simulationSpeed, isSimulated and currency (BDT). No client energy, battery, duration, tariff, price or payer is accepted. Invalid input returns 400; state conflicts return 409 with safe ProblemDetails. SQL uniqueness/concurrency/deadlock conflicts return a generic retry message.

Existing `/api/stations` and station details use BookingAvailabilityProvider, preserving the discovery/map dataset. It batches reservations/sessions and returns effective Reserved/Charging/Available, preserving Offline. Expired holds do not make discovery unavailable even before physical cleanup. Availability is advisory; reservation is checked again under locks.

## SQL concurrency and expiry

Migration `20261019041722_AddBookingAndCharging` is additive. Review [member3-migration.sql](member3-migration.sql); EF migrations remain authoritative. It adds only Bookings and ChargingSessions, rowversion columns, UTC datetime2 fields, explicit decimal precision, eight NO ACTION foreign keys, four state/expiry/numeric CHECK constraints, history/expiry indexes and these uniqueness guarantees:

- BookingCode and (BookedByUserId, IdempotencyKey).
- One Reserved booking per charger and per vehicle, via filtered unique indexes.
- One session per BookingId.
- One Active session per charger and per vehicle, via filtered unique indexes.

Every mutation begins one SQL transaction, locks the existing Vehicle using `UPDLOCK,HOLDLOCK`, then locks affected Chargers in ascending ID order. Expired holds for those chargers are persisted before inserting replacements, freeing the filtered indexes. Vehicle-root locks are shared with Member 2 edits and assignments. The charger lock serializes reservations across different vehicles and protects cross-table booking/session transitions; the filtered indexes provide additional database enforcement. All future code writing charging state must follow this same lock protocol; the cross-table invariant is not a standalone SQL CHECK constraint.

ChargingWorker scans expired holds and advances active sessions in batches, using a fresh DI scope/transaction per resource. It catches/logs resource failures and retries on later iterations, supports shutdown cancellation, and creates no per-session tasks. Active-session paging prevents early long-running sessions starving later IDs. Request-time checks enforce expiry independently of the worker. No transaction waits for a timer to elapse.

## Simulation

At session start, snapshot capacity, initial battery, consumption, charger power, tariff, simulation speed and payer Owner. Subsequent charger price edits cannot change the session price. The persisted StartedAtUtc and these snapshots allow progress to resume after a browser refresh or server restart.

```
remainingKWh = capacityKWh × (100 - initialBatteryPercent) / 100
fullSeconds = remainingKWh × 3600 / (powerKW × simulationSpeed)
elapsedSeconds = clamp((serverUtc - startedAtUtc).TotalSeconds, 0, fullSeconds)
energyKWh = min(remainingKWh, powerKW × simulationSpeed × elapsedSeconds / 3600)
batteryPercent = clamp(initialBatteryPercent + energyKWh / capacityKWh × 100, 0, 100)
costBDT = round(energyKWh × snapshottedTariff, 2, AwayFromZero)
rangeKm = round(capacityKWh × batteryPercent / 100 / consumptionKWhPerKm, 2)
```

Partial energy is truncated to six decimal places, battery rounded to two, persisted elapsed real seconds to three. At full charge energy is exactly the remaining battery capacity, EndedAtUtc is the calculated full-charge instant, and automatic completion releases the charger. Stop before full uses the server stop instant. Vehicle battery is updated in the same transaction. Example: 50 kWh capacity, initial 40%, 60 kW, 60× speed, tariff 25 BDT: ten real seconds delivers 10 kWh, battery 60%, range 166.67 km, cost 250 BDT. Full after 30 real seconds: 30 kWh, 100%, 750 BDT.

## Local configuration and run

Reuse the existing user secrets and initialization script. Windows Authentication is preferred. Never commit real credentials or local database files.

```powershell
./scripts/Initialize-DevelopmentDatabase.ps1 -Server localhost -Database GEnergyDb -Through Latest
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project backend/GEnergy.Api
# second terminal
cd frontend
npm ci
npm run dev
```

The existing script refuses migration downgrades and preserves data. It validates the model snapshot before updating. No migration is applied automatically on application startup.

| Environment variable | Default | Purpose |
| --- | --- | --- |
| `Charging__ReservationSeconds` | 180 | Test override; allowed 1–3600. Keep 180 for the normal demo. |
| `Charging__SimulationSpeed` | 60 | Demo time multiplier, allowed 1–3600; snapshotted on start. |
| `Charging__WorkerIntervalMilliseconds` | 5000 | Scan delay, allowed 100–60000. |
| `Charging__WorkerEnabled` | true | Disable autonomous scanning for deterministic tests; request checks remain enforced. |

The real adapters are registered unconditionally in Program; the old Modules:BookingEnabled flag is no longer needed to enable this module. Legacy fail-closed adapters remain for their existing regression test, which explicitly installs them.

Frontend `/bookings?stationId=…&chargerId=…` accepts the selection from existing station cards; vehicle options require compatible connector, battery below 100% and CanCharge. The API revalidates all of them. `/bookings` shows persisted reservations, countdowns and history; `/charging-sessions/:id` shows progress and the final summary. Countdown uses the returned server time plus monotonic elapsed browser time. It is display-only. Screens poll every three seconds while visible and refresh on focus. Network retries retain the reservation key until payload changes or creation succeeds. API conflicts/errors remain visible and authoritative expiry is refreshed after failed start.

## Member 4 settlement contract

Implement scoped `IChargingSettlement.FinalizeAsync(ChargingCompletion, CancellationToken)` and replace `DeferredChargingSettlement` registration. The immutable completion contains SessionId, PayerOwnerUserId, EnergyKWh, Cost and EndedAtUtc, calculated only by this module.

It runs on Active → Completed inside the same SQL transaction and scoped EnergyDbContext, after flushing final session/battery values and before committing charger release. Use the SAME DbContext and transaction; do not open another context or commit independently. Enforce a unique ledger debit keyed by SessionId and lock/update the Owner wallet atomically. Never debit twice. Return SettlementResult("Paid", note) only for a successful persisted debit; insufficient funds must return ("Unpaid", explanation) so the session can still complete. Notes are required and at most 200 characters; status must be Paid or Unpaid. All wallet writers must adopt a compatible lock order (vehicle → chargers → wallet) to avoid deadlocks.

An exception rolls back session completion, vehicle battery and charger release, allowing a retry. Consequently the participant may be invoked again after a rolled-back attempt. It must not perform external irreversible side effects. Real payment integration would need a transactional outbox/idempotent external provider design outside Member 3. Repeated completed-session stop/start does not invoke it again. A future unpaid settlement/reconciliation API belongs to Member 4 and must preserve unique SessionId debits.

Current default always returns Unpaid / Wallet integration pending. The UI states that no wallet charge occurred. There is no wallet balance, payment gateway, receipt issuance, hardware communication or verified real-world station claim. History lists currently return the latest 100 rows; pagination and production operational monitoring are future work.
