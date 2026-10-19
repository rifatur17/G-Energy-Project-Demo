# Member 5 — Admin operations and QA

This extends Members 1–4 in the same application and SQL Server database. No new entity, migration, duplicate ledger or refund endpoint was introduced. All payments, balances, batteries and charging are simulated. Sample/QA locations are not verified real charging stations.

## Local setup

Confirmed Windows SQL Server default instance MSSQLSERVER, server localhost / DESKTOP-0MG4DVB, database GEnergyDb, Windows Authentication. Preserve the database and all applied migrations. Configure `ConnectionStrings:DefaultConnection` through .NET user secrets or environment variables and provide `Jwt:SigningKey` securely as described in the README. No working secrets belong in source control.

From the repository root in PowerShell:

```powershell
dotnet tool restore
dotnet restore
dotnet ef migrations list --project backend/GEnergy.Api
dotnet ef migrations has-pending-model-changes --project backend/GEnergy.Api
# Inspect any genuinely pending migration and its SQL before updating the shared database.
dotnet ef migrations script --idempotent --project backend/GEnergy.Api --output "$env:TEMP/GEnergy-pending-review.sql"
# After reviewing the script; no Member 5 migration is required:
dotnet ef database update --project backend/GEnergy.Api
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project backend/GEnergy.Api
```

In a second terminal run `npm ci` then `npm run dev` from `frontend`; copy `.env.example` to local `.env` when first setting up. API http://localhost:5080, frontend http://localhost:5173. This computer's SDK is `C:\Test\runtimes\projectflow-dotnet\dotnet.exe`. Windows Application Control blocks the generated API executable here. The verified alternative is `dotnet build -c Release` followed by `dotnet bin/Release/net10.0/GEnergy.Api.dll --urls http://localhost:5080` from `backend/GEnergy.Api` with Development environment. No security settings were weakened.

Use the README's secure Development Admin bootstrap: read the email and password interactively, set `AdminSeed__Email` and `AdminSeed__Password` only for `--seed-admin`, and clear both in `finally`. It prints only the generated UserCode. On this computer use the same Release DLL with `--seed-admin` instead of the blocked executable. There is no default Admin password and no public Admin registration. Existing Owner/Driver accounts cannot be promoted by the bootstrap; Production provisioning is outside this demo.

## Routes and authorization

Every `/api/admin` action requires the existing server-side Admin policy. Anonymous callers receive 401; authenticated CarOwner/CarDriver callers receive 403. Frontend guards also restrict the pages, but the API is authoritative. Existing Member 1 account activation/role validation is retained.

| Method/path under `/api/admin` | Behavior |
| --- | --- |
| GET `/dashboard` | User, station, charger, hold/session counts; simulated payment, refund, net and balance totals |
| GET `/stations` | Search by name/address, active filter, descending ID pages |
| POST `/stations` | Create station configuration |
| PUT `/stations/{id}` | Edit configuration; requires returned version in quoted If-Match |
| GET/POST `/stations/{id}/chargers` | List/create station's chargers |
| PUT `/chargers/{id}` | Edit configuration; requires returned rowVersion in quoted If-Match |
| GET `/bookings` | Effective status, persisted status, UTC timestamps/expiry and related session |
| GET `/charging-sessions` | Persisted server cost/energy/battery, payer/booking/payment references |
| GET `/wallet-transactions` | Completed/Failed TopUp, ChargingPayment and Refund records with original/refund references |
| GET `/wallets` | Coded Owner and current simulated balance |
| GET `/users` | UserCode, role, active status and registration time only |

Lists accept `take` 1–100 (default 20) and `before` for strictly older IDs. Bookings accept `status`, `stationId`, `from`, `to`; sessions accept `status`, `payment`, `stationId`, `from`, `to`; transactions accept `type`, `status`, `from`, `to`; users accept `role`. Date filters require ordered UTC timestamps. UI date inputs use browser local time and convert to UTC. Filters refresh the first page; Load older records appends keyset pages.

Pages: `/dashboard/admin`, `/admin/stations`, `/admin/bookings`, `/admin/charging-sessions`, `/admin/wallet-transactions`, `/admin/wallets`, `/admin/users`. Overview polls every 15 seconds while visible and has manual refresh. Monitoring uses explicit refresh. Forms display a review step before saving and disable duplicate submission. If an edit conflicts, cancel it, refresh the network and reopen the latest record before reviewing again.

## Safety and financial semantics

All station edits are refused while any charger has a valid Reserved hold or Active session. Charger edits have the same guard for that charger, including tariff/code changes. No edit cancels a booking, ends charging or overrides a live Reserved/Charging state. No delete API exists. Disabled chargers use existing Offline status; station activation uses existing IsActive. An inactive station cannot receive an enabled charger. Validate supported connectors, SQL decimal precision, coordinates, positive power/tariff and per-station unique normalized charger codes.

SQL mutations use a transaction-scoped `sp_getapplock` per station for Admin membership/configuration changes, then sorted Charger locks before Station. The gate prevents child inserts racing a station-wide availability check. Existing booking/charging/payment locks remain Vehicle → sorted Chargers → payer User → Wallet. Admin never waits on Vehicle or calls wallet writers. Charger If-Match uses SQL rowversion; station If-Match hashes canonical configuration, with six-decimal invariant coordinates so SQL decimal round trips preserve the token. Station version is configuration-based, not a monotonic database rowversion.

Admin queries use projections/AsNoTracking and never invoke lifecycle advancement, settlement, refund, or wallet initialization. Expired means either persisted Expired or a Reserved hold past its deadline; both effective and persisted states are visible without writing. A consumed booking links to its session; session completion is shown on the session monitor. Session monetary values are the last persisted authoritative server calculation.

Gross charging payments sum Completed ChargingPayment rows; Refunds sum Completed Refund rows; Net = Gross − Refunds. Successful/failed top-ups are separate and do not count as charging revenue. Dashboard totals come from several short queries, so concurrent activity can produce a slightly different observation time across cards. Original Member 4 payment/refund rows remain immutable; a refunded session remains Paid with the original reference and an explanatory note. Refund eligibility is projected using the same shared Member 4 predicate. Only the existing Owner wallet flow can issue an eligible full demo refund. No Admin balance, payment, refund, user-role or arbitrary lifecycle mutation exists.

Operational responses omit names, emails, phone numbers, password hashes and tokens. They expose coded participants and vehicle/station references needed for operations. Keep Admin access restricted.

## Demonstration and regression

1. Bootstrap a synthetic Admin securely; register new Owner and Driver normally. Confirm both ordinary roles receive 403 on Admin APIs and cannot open Admin pages.
2. Admin creates a clearly labelled demo station and compatible charger using review/confirm. Owner adds a vehicle, discovers it through the map/list and reserves a charger.
3. Confirm the server supplies an exact three-minute hold; Admin sees the booking and cannot disable/edit its station/charger while reserved or charging.
4. Owner/authorized Driver starts simulated charging and stops it. Confirm the final server bill and persisted session history.
5. Owner tops up demo funds, pays the unpaid session, retries payment and issues one eligible full refund. Repeated pay must use the same debit; repeated refund must conflict.
6. Admin filters/refreshes bookings, sessions and transactions. Match references, bill and refund to SQL. Confirm read operations cannot change balances or ledger rows. Restart the backend and refresh to verify persistence.
7. Use new QA fixtures for expired/cancelled/live/completed states. Do not edit original user data to manufacture evidence.

See [verification report](member5-verification.md) for actual test commands, outcomes and retained synthetic examples. Existing Member 1–4 tests remain part of the full regression. Production gateway integration, real charger hardware, high-load/endurance/security certification and production account administration are not claimed.
