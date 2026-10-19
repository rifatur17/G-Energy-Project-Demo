# Member 5 verification — 19 October 2026 (Asia/Dhaka)

## Resume and scope

Started Member 5 from Member 4 main `104c967d8919e310d084aba4136071a47ac6fc8b`. On resume, backend commit `6959b0172422f2cc6ea86beb00c112636ba4e6a4` and frontend commit `fe185dbaa54511ac4334dd6bcd8e88602c4ff72a` existed locally, with four uncommitted test files. The remaining work was final booking/payment read-only verification, focused regression coverage, full checks, documentation, test/docs commits and verified push. Fetched origin and confirmed no remote advancement. Preserved Member 1–4 commits and implementation; no reset/clean/force push or history rewrite.

All Member 5 authors use repository-local Rifatur Rahman <rifaturrahman1131@gmail.com>. Git Credential Manager credentials were captured without displaying values; GitHub `/user` verified `rifatur17`, and the repository API confirmed push permission. Git author and GitHub account were checked separately. Release hashes and remote verification are recorded in the final task report; reproduce with `git ls-remote origin refs/heads/main` and `git log origin/main`.

## Current automated results

**67 backend tests passed, zero failed, zero skipped**, Release configuration, both SQL opt-ins enabled. TRX confirms **20 actual SQL Server tests**: 15 existing Member 1–4 and five new Member 5 tests. Other 47 backend tests use non-SQL fixtures and are not SQL concurrency verification. Focused Admin filter separately passed 13 tests (ten new Member 5 plus three existing Admin-related cases). **54 frontend tests passed across seven files**, including ten new Admin tests. Backend Release build passed with zero warnings/errors. Frontend TypeScript/Vite production build passed (55 modules). EF pending-model check reports no changes since the last migration.

Five new real SQL tests cover:

1. Admin-created charger, exact 180-second booking, start/stop, final cost, top-up/payment retry/refund, Admin projections/aggregate comparison to SQL, and persistence read through a new application host.
2. Actual simultaneous booking vs charger deactivation: exactly one succeeds and the other returns 409; no Offline charger with a valid Reserved hold.
3. Actual simultaneous station deactivation vs booking, then charger creation vs station deactivation under the membership gate. No booking succeeds on the disabled station.
4. Active-session station/charger edit refusal; final bill remains unchanged after later tariff editing; SQL rowversion/station stale edit refusal; newly created station's returned version works immediately after SQL decimal round trip.
5. Normal cancellation/completion/payment/refund/expiry fixtures. Repeated payment returns the same debit, duplicate refund conflicts, and Admin history pages associate the correct booking/session/payment/refund references. Repeated filtered/paginated reads compare complete captured booking, session, charger, wallet and ledger rows byte-for-byte before/after, including rowversions. Expired effective state can remain persisted Reserved without Admin writing it. Real SQL-backed Owner/Driver booking and transaction requests return 403; response projections omit password/email/phone/token fields.

Non-SQL Admin tests additionally cover anonymous/Owner/Driver denial for every read and configuration mutation route, bootstrap refusal to promote Owner, dashboard gross/refund/net and failed-top-up exclusion, validation/unique codes/If-Match, live activity guards, minimal projections/pagination/date validation, and absence of Admin pay/refund endpoints. Frontend tests cover real-data rendering through mocked API boundaries, guards, review-before-save, conflict/loading/empty/error handling, filter/pagination/refresh and persisted bill/refund displays without financial controls. These component tests are not browser or SQL claims.

Existing authentication assertions were retained; their test now mocks the new Admin dashboard dependency. A map fallback test initially raced the asynchronous tile-error listener. It now waits for actual listener attachment before firing the same error; existing fallback assertions were preserved. During focused new-test development, a wrong entity path and mismatched UI text selectors were corrected; all final results above are from the corrected checkout.

### Commands actually run

From repository root (the local `dotnet` below is `C:\Test\runtimes\projectflow-dotnet\dotnet.exe`):

```powershell
$env:G_ENERGY_SHARED_SQLSERVER = 'Server=localhost;Database=GEnergyDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
$env:G_ENERGY_TEST_SQLSERVER = 'Server=localhost;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
dotnet test --configuration Release --filter 'FullyQualifiedName~Admin' --logger 'console;verbosity=minimal'
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build --logger 'trx;LogFileName=member5-final.trx' --logger 'console;verbosity=minimal'
dotnet ef migrations has-pending-model-changes --project backend/GEnergy.Api --configuration Release --no-build
cd frontend
npm test -- --run src/__tests__/admin.test.tsx
npm test
npm run build
```

Stopped external development API hosts before deterministic shared SQL tests: their live worker scans all active sessions and can interfere with test clocks. Tests remain serialized through existing SharedSqlServer collection; explicit HTTP race requests inside tests remain concurrent. TRX/build outputs are ignored and not committed. Existing isolated SQL tests create/delete only their own randomly named test catalogs. Shared QA rows are retained.

## SQL Server and original data preservation

Direct service/catalog queries confirm running MSSQLSERVER, localhost / DESKTOP-0MG4DVB, GEnergyDb, Windows Authentication and the existing SqlServer provider. No schema/entity/snapshot/migration change was needed. No migration was generated/applied by Member 5. Applied history is unchanged:

- `20261018185818_InitialIdentity`
- `20261018192412_AddVehiclesAndStationDiscovery`
- `20261019041722_AddBookingAndCharging`
- `20261019044347_AddWalletAndPayments`

Direct SQL catalog queries confirm all 15 check constraints and 16 foreign keys are enabled and trusted. Existing schema, unique indexes, UserCode sequence and rowversion conventions are retained; regression tests exercise their existing uniqueness/relationship enforcement. No database recreation, rollback, sequence reset or deletion of QA records occurred.

At Member 5 start, saved original key boundaries, counts and SHA-256 fingerprints of ordered complete rows outside the repository. After all current automated tests and the live journey, every original row in all nine tables matched its fingerprint:

| Table | Original rows verified unchanged |
| --- | ---: |
| Users | 355 |
| Vehicles | 167 |
| VehicleDrivers | 88 |
| Stations | 82 |
| Chargers | 164 |
| Bookings | 115 |
| ChargingSessions | 66 |
| Wallets | 35 |
| WalletTransactions | 89 |

The comparison includes existing password hashes, persisted amounts/statuses and rowversions without printing personal values. Only new labelled fixtures were added. These are the actual Member 5 starting counts, not older task counts.

## Live browser and API/SQL verification

Used the real SQL-backed API at localhost:5080 and React UI at localhost:5173, with a new synthetic Admin provisioned through existing Development AdminSeeder and normally registered synthetic Owner/Driver. No credentials, JWTs or private QA credential files are committed.

1. Admin UI created demo station #118 `QA Member5 Live F4FC8BDE`, charger #247 `QA-M5-LIVE-DC` (CCS2, 60 kW, 25 BDT/kWh), using review and confirmation.
2. Owner UI created vehicle #237 `QA-M5-122EBDC1` (50 kWh, initially 40%, 0.18 kWh/km), discovered the station through map/search and booked it.
3. Booking #153 `GE-BKG-EB8138BE14B245338247BC687A073B9F` has CreatedAtUtc 06:18:03.5200459 and ExpiresAtUtc 06:21:03.5200459, exactly 180 seconds (12:18:03–12:21:03 Asia/Dhaka). Admin attempted deactivation through UI during the valid hold and received 409 with the live reservation explanation.
4. Owner started session #82, refreshed while active and stopped it. Persisted Completed session has 7.930 kWh, 55.86% final battery and authoritative **198.26 BDT** bill. The UI showed Unpaid before funds were added.
5. Owner UI added 1000 demo BDT and paid; balance became 801.74. Two additional real HTTP payment retries returned original payment ledger #155 without a second debit. Owner UI issued the full demo refund; balance returned to 1000.00 and original payment stayed in history.
6. Admin booking UI filtered station 118 and refreshed: Consumed booking #153 shows station/charger/vehicle/coded participant, creation/expiry and related session #82. Admin session monitor showed Completed/Paid, same cost/energy/battery and original payment reference with refund explanation. Transaction type filter showed original Completed payment and linked refund, with refund eligibility No and no mutation controls.
7. Direct SQL verified payment #155 `WT-E0A9C90119F84755B764CF0EBBC54977` = 198.26, distinct refund #156 `WT-B0A7A0D71C1F4D68928D0BF39158C647` = 198.26, OriginalTransactionId 155. Session #82 remains Paid with original reference, wallet 1000.00.
8. Restarted the backend through the same managed Release DLL. Fresh browser logins still load booking #153 and payment/refund references; Owner wallet UI still loads 1000.00 and all three original demo ledger entries. Owner/Driver real HTTP requests were denied on all Admin read families; focused SQL tests additionally retain booking/transaction denial as regression coverage.

Screenshots outside the repository: `C:\Test\member5-booking-verification.png` and `C:\Test\member5-payment-verification.png`. These contain synthetic coded operational data. Browser loading, booking filter/refresh, transaction-type filter and read-only references were directly observed; comprehensive empty/error/pagination behavior is covered by component/API tests, not claimed as a separate live failure-injection exercise.

## Remaining boundaries

No implementation/test blocker remains. Mock payment/refund and simulated charging are demo-only; no real gateway/hardware or production load/endurance/security certification was tested. Dashboard cards are not one atomic global snapshot. Monitoring displays the last persisted charging snapshot and does not itself advance charging. Keyset history is newest-ID-first, without CSV export or arbitrary page numbers. Existing Owner refund policy, active-account validation and locks must be preserved in future Members 1–4 changes. A stale editor must be cancelled and reopened after refresh. Production Admin provisioning and operational deployment remain separate work. Windows executable launch restriction is handled by the verified managed-DLL launch, without changing Windows policy.
