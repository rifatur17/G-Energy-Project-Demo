# Member 4 verification — 19 October 2026

## Starting state and identity

Continued clean local main at `d21de7eb1c9e0c3ad087bc4a3c88ca518f1a8ad7`. Fetched origin before edits and before final validation; local/remote started 0/0 and no new remote work arrived. No reset, clean, force push or shared history rewrite was used. Member 1–3 implementations/migrations were retained.

Repository-local author: Sabekunnaher Antora <antorasabekunnaher@gmail.com>. Existing Git Credential Manager credentials were read securely without printing values. GitHub `/user` independently verified the authenticated account was `sabekunnaher-17`, and the repository API reported push=true. Author identity and authentication were checked separately. Final pushed hashes and remote main are provided in the task report; confirm with `git ls-remote origin refs/heads/main`.

## Actual SQL Server and migration

Confirmed running MSSQLSERVER default Database Engine, localhost / DESKTOP-0MG4DVB, existing GEnergyDb, Windows Authentication. Reused local .NET user secrets. No shared database was recreated, reset, dropped, restored or downgraded.

Reviewed [generated SQL](member4-migration.sql) before applying only new migration `20261019044347_AddWalletAndPayments` through the existing initialization script. SQL history directly confirms all four migrations:

- 20261018185818_InitialIdentity
- 20261018192412_AddVehiclesAndStationDiscovery
- 20261019041722_AddBookingAndCharging
- 20261019044347_AddWalletAndPayments

Direct SQL catalog inspection verified Wallets and WalletTransactions, ChargingSessions.PaymentReference, decimal(18,2) money, wallet native rowversion, unique UserId, transaction reference and wallet/key indexes, filtered unique completed-session payment and refund indexes, the same-wallet original-transaction composite FK, and all four new NO ACTION FKs. All four new CHECK constraints are enabled and trusted. SQL tests directly attempted duplicate payment/refund inserts and the database rejected both with DbUpdateException.

Pre-task data: 117 Users, 43 Vehicles, 24 VehicleDrivers, 23 Stations, 46 Chargers, 35 Bookings and 14 ChargingSessions. Captured counts, original key boundaries and SHA-256 fingerprints of ordered full rows outside the repository. After migration, SQL tests and browser verification, the original rows in all seven tables still matched, including original password hashes, charging costs/status and rowversions. The additive nullable PaymentReference does not populate or change historical sessions. Only new labelled QA records were added. Tests never delete/reset/migrate GEnergyDb; the two existing isolated SQL tests still create/delete only their own newly generated test catalogs.

## Automated verification

Final result: **57 backend tests passed, zero failed, zero skipped**, with both SQL opt-in variables set. This includes **15 real SQL Server tests** (six new Member 4, nine existing Member 1–3). The other 42 are non-SQL tests and are not claimed as SQL transaction/concurrency verification. **44 frontend tests passed across six files**. Backend Release build: zero warnings/errors. Frontend TypeScript/Vite production build: passed. EF has-pending-model-changes: no changes since the last migration.

New wallet backend coverage:

- Stable lazy wallet initialization; anonymous/Driver denial; scoped wallet/history/transaction access.
- Invalid/nonpositive/over-limit/fractional top-up rejection; ignored client balance; success, deterministic failure, same-key retry, conflicting payload, history keyset pagination.
- Active/foreign/Driver payment denial; insufficient balance leaves the completed bill unpaid and the wallet unchanged.
- Authoritative persisted final cost despite a forged client payment amount; repeated payment returns one debit and reference; automatic charging finalization pays the Owner wallet even for Driver-started sessions.
- Refund eligibility and original ownership, full refund only, duplicate refusal, immutable original retained; a refunded session cannot be charged again.
- Application DbContext refuses ledger edits/deletes.

Six new real SQL tests verify:

1. Five simultaneous initializations produce one wallet; five same-key top-ups produce one credit. Failed top-up is retained without a credit.
2. Two completed bills compete for a 300 BDT wallet: exactly one 250 BDT bill is paid, the other conflicts, balance stays 50. Five concurrent retries of the successful session return the same payment with no additional debit.
3. Four concurrent refund requests: exactly one credit, three 409 responses; original payment retained; payment retry after refund does not debit again.
4. SaveChanges interceptor throws AFTER SQL writes but BEFORE commit for top-up, manual payment and refund. Fresh scopes confirm balance/ledger/session all roll back; retry succeeds consistently.
5. Direct duplicate completed-payment/refund inserts fail SQL uniqueness enforcement; a new application host reads the persisted wallet, three-entry ledger and Paid session.
6. Actual automatic Member 3 completion plus wallet settlement: injected after-write exception rolls back completion/end timestamp/payment reference, energy, vehicle battery, charger release, debit and payment ledger together. Retry completes and pays exactly once.

Shared SQL test classes use a serialized xUnit collection because the real hosted worker scans the whole shared database. This prevents one test clock/settlement probe from completing another scenario's session. Explicit concurrent HTTP requests inside tests still run concurrently. Existing assertions were not removed or weakened. The original charging fixture accepts an optional service override and QA label so wallet tests reuse actual registration/booking/start/stop flows. Existing auth, vehicle/driver, discovery, expiry/charging and SQL tests all pass.

Frontend coverage includes wallet loading/empty state, amount validation, failed mock result, balance/history refresh, pending duplicate-submit protection, stable uncertain-outcome retry key, sessionStorage recovery after refresh, eligible refund action, insufficient funds/top-up link, successful pay reference and no payment controls for Drivers/already-paid sessions. Existing frontend assertions remain intact; the charging component test now supplies the auth context required by the new Owner payment panel.

### Reproduce

Stop other development hosts against the shared database before deterministic SQL tests, since a real worker intentionally scans all active sessions.

```powershell
$env:G_ENERGY_SHARED_SQLSERVER = 'Server=localhost;Database=GEnergyDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
$env:G_ENERGY_TEST_SQLSERVER = 'Server=localhost;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
dotnet ef migrations has-pending-model-changes --project backend/GEnergy.Api
cd frontend
npm test
npm run build
```

This computer uses `C:\Test\runtimes\projectflow-dotnet\dotnet.exe`. Windows Application Control blocked the generated API .exe during live launch; running the same Release DLL through the installed dotnet host succeeded. No security settings were changed. This is a local launch constraint, not a database/payment integration failure.

## Live browser / real SQL evidence

Used a NEW synthetic `QA Member4 Browser Verification` Owner, vehicle and a newly added Member 4 QA station/charger. Never paid or changed an original Member 3 QA session. The real default simulation completed new session #57 with authoritative final bill 33.22 BDT and initially Unpaid status. Browser interactions verified:

1. Pay with zero balance: clear insufficient-funds error, still Unpaid.
2. Mock Failure top-up of 1000: Failed ledger entry, balance 0.
3. Mock Success top-up of 1000: Completed ledger entry, balance 1000; browser refresh retains both outcomes.
4. Stopped/restarted the backend; browser still read the same 1000 BDT wallet/history.
5. Pay session #57: Paid with reference `WT-0770D5A8D00A45ADAAF9A501D50C6125`, balance 966.78 BDT, exactly one 33.22 BDT debit.
6. Full demo refund: balance restored to 1000 BDT, new reference `WT-A7E8F0CB5CB54AD6A690D7F212526C37`, original payment retained and refund action removed after refresh.

Direct SQL confirmed these four immutable ledger rows:

| ID | Type | Status | Amount BDT | Original ID |
| --- | --- | --- | --- | --- |
| 99 | TopUp | Failed | 1000.00 | — |
| 100 | TopUp | Completed | 1000.00 | — |
| 101 | ChargingPayment | Completed | 33.22 | — |
| 102 | Refund | Completed | 33.22 | 101 |

Session #57 remains Completed/Paid with the original payment reference and an explanatory refunded settlement note; it cannot be billed again. Wallet balance is 1000.00 BDT. Screenshot outside the repository: `C:\Test\member4-wallet-verification.png`, containing synthetic demo data only. No real money was transferred and no banking/payment credentials were collected.

## Remaining boundaries

No remaining Member 4 implementation blocker was found in this environment. The provider and full-refund policy are explicitly demo-only. No real gateway, cash-out, dispute management, receipt document generation or production load/endurance/security certification is claimed. Refund credits obey the configured wallet balance cap. Zero-cost and above-limit charging bills stay unpaid without a ledger debit. Member 5 must preserve the documented lock order and distinguish payment/refund/net totals in reports. See [wallet/payment contract](member4-wallet-payment.md) for API, policy and real-gateway follow-up requirements.
