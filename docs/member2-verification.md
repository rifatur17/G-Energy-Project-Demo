# Member 2 verification evidence

Recorded 19 October 2026 (Asia/Dhaka). Member 1 history through 62c66a6 was preserved. The repository started clean on main, fetched with no remote divergence, and contained no competing vehicle, station, booking or charging module. One additive migration was generated against that shared baseline. No external team agreement is implied; the concrete contract is in member2-api.md for Members 1/3/4/5 to review.

## Checks actually executed

| Check | Result |
| --- | --- |
| Backend build / tests | Build passed; 23 HTTP/service tests passed, including the original 11 auth tests |
| SQL Server integration tests | 2 explicitly skipped because G_ENERGY_TEST_SQLSERVER is unset |
| Frontend tests | 26 passed, including all original 15 auth/transport tests |
| Frontend TypeScript / production build | Passed |
| npm audit | 0 known vulnerabilities reported |
| NuGet audit including transitive packages | No known vulnerable packages reported |
| EF migration / delta SQL generation | Passed |
| EF pending model changes | None |
| Browser HTTP check | Login, vehicle list/details, Owner battery edit/save and range update, station/map loading; desktop/mobile layout inspected |
| Browser console check | No captured errors/warnings in the inspected discovery view |
| Review | Migration and staged diff reviewed; no real credentials, build output or temporary QA fixture committed |

Backend tests use WebApplicationFactory with EF InMemory and test-only UserCodes, not SQL Server. New cases cover ownership/role isolation; invalid numeric inputs; range; driver lookup/assignment/revoke; CanCharge and inactive accounts; activity guard conflicts; configured and adapter availability; offline protection; active station filtering/search and idempotent sample seeding. A fake active-activity guard and fake expiry adapter verify the integration hooks, not a real booking engine.

Frontend tests mock APIs and Leaflet events. They cover loading/error/empty/retry states; add/edit; assignment and permission controls; driver read-only views; connector/demo-origin filters; location denial; Haversine distance; and station-list survival after tile failure.

The browser check used the real frontend and HTTP API with a temporary WebApplicationFactory/Kestrel fixture in ignored obj files. Its database was in memory and its account/data were synthetic. It verified a save from 40% / 111.11 km to 55% / 152.78 km, not SQL persistence. No production fallback database was added. The temporary fixture is not part of the delivered application.

## Exact unverified checks / remaining work

No SQL Server service, LocalDB or Docker was available (only MySQL). The migration has NOT been applied to real SQL Server here. SQL FK/check/unique enforcement, native rowversion generation, concurrent duplicate vehicle inserts, root-row locking, stale concurrent writes, UTC round trips and SQL seed serialization remain unverified.

The opt-in SQL test suite includes migration application, vehicle uniqueness, native rowversion, two simultaneous stale-version updates (one success/one conflict), numeric SQL check rejection and discovery seed/read. Set a test-only G_ENERGY_TEST_SQLSERVER connection and run `dotnet test --filter FullyQualifiedName~SqlServerTests` on a disposable-capable SQL Server. Those tests create/delete only generated GEnergy_AuthTests_ / GEnergy_MobilityTests_ catalogs.

Real reservation/session expiry and activity restrictions require Member 3's adapters, common vehicle lock order and SQL tests. No live charging, telemetry, real stations or payments are claimed. Member 4 integrates Owner wallet settlement with the trusted vehicle permission service. Member 5 implements Admin CRUD/release QA against these shared tables, seeds/reviews sample data, applies migrations on the demo database and verifies the full journey.

## Git delivery

Repository-local author: Sabekunnaher Antora <antorasabekunnaher@gmail.com>. Git Credential Manager account sabekunnaher-17 was verified through the GitHub API; repository push permission is true. Target is https://github.com/rifatur17/G-Energy-Project-Demo, branch main. No shared history was rewritten. Final push/hash verification is reported in the delivery message; this record deliberately does not pre-claim a push result.
