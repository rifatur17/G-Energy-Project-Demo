# Shared foundation contract

Member 1 owns Users, authentication and shared UI. Member 2 now provides Vehicles, VehicleDrivers, Stations and Chargers in the same DbContext; Members 3-5 add their remaining modules. Booking, charging and wallet tables are not defined yet. Coordinate every migration in sequence against the latest main; never generate competing initial migrations.

Use .NET 10 / EF Core 10 and SQL Server. Persist UTC timestamps. Other modules reference Users.Id (bigint), not the display UserCode. Do not infer authorization from a code prefix. Use named role policies and CurrentUser.Id, then query resources with owner/assignment predicates before reading or mutating them. The reusable ResourceOwner requirement handles direct owner checks; Member 2 provides VehicleAccess for active driver assignment checks including CanCharge for charging; Members 3/4 must call it inside their mutations. A role alone never grants access to another user's data.

Member 4 should create a zero-balance wallet for each Owner through its own idempotent service/unique OwnerUserId constraint, including existing owners. No wallet is created by the identity module. Member 5 owns station operations and release coordination; the development Admin bootstrap and explicit sample discovery seed are described in README. No team coordination messages have been sent; this document is the proposed handoff contract for review.

## Exact integration steps

1. Pull the latest main, install .NET 10 and Node, and follow the root README to configure SQL Server and a random JWT key. Run `dotnet tool restore`, `dotnet restore`, then `dotnet ef database update --project backend/GEnergy.Api`. Start the API with `dotnet run --project backend/GEnergy.Api` (port 5080). Start the frontend with `npm ci` then `npm run dev` in frontend (port 5173).
2. Register an Owner/Driver at `POST /api/auth/register`. Keep the returned UserCode. Login at `POST /api/auth/login` using that code/password. Read `accessToken`, then pass `Authorization: Bearer <accessToken>` to `GET /api/auth/me` and every module endpoint.
3. In controllers inject CurrentUser for trusted Id/UserCode/Role after `[Authorize]`. Apply `[Authorize(Policy = Roles.CarOwner)]`, Roles.CarDriver or Roles.Admin as appropriate. Never take caller identity from a body/query parameter. Build ownership into database queries, rechecking mutable assignments inside the same transaction as critical writes.
4. To check direct ownership: `await authorization.AuthorizeAsync(User, entity.OwnerUserId, "ResourceOwner")`; return Forbid() on failure. Member 2 implements the separate assignment predicate for Drivers in VehicleAccess. Members 3/4 reuse that service to require active assignment and CanCharge before reservation, charging and use of an Owner wallet. The resource owner ID comes from a database row, never from an unverified client claim.
5. Frontend modules get `{ user, accessToken, logout }` from `useAuth()` and use the exported `request<T>(path, options, accessToken)` transport. Handle ApiError statuses: logout on 401, show permission feedback on 403, conflict feedback on 409 and field errors on 400. Only the backend decides access. Read role/userCode from the server-verified profile for display. Keep each module inside the shared Layout and the matching Protected route. Existing entry routes: `/dashboard/admin`, `/dashboard/owner`, `/dashboard/driver`.
6. Add entities to the shared EnergyDbContext using separate configuration classes as modules grow. After a coordinated schema review, generate a uniquely named migration with `dotnet ef migrations add <DescriptiveName> --project backend/GEnergy.Api`. Review SQL, apply to a disposable SQL Server, and verify `dotnet ef migrations has-pending-model-changes --project backend/GEnergy.Api`. Do not edit an applied/shared migration or introduce another DbContext owning Users.
7. Keep API errors compatible with ProblemDetails (see api-contract.md), validate request DTOs, use explicit response DTOs and UTC timestamps. Register each module's services in Program.cs. Module tests may share the WebApplicationFactory approach; SQL concurrency/financial behavior must be tested against real SQL Server.

## Per-member handoff

| Member | Add next | Integration boundary |
| --- | --- | --- |
| 2 | Vehicles, assignments, station discovery | FK to Users.Id; owner policy plus active Driver assignment checks; never mutate identity from vehicle DTOs |
| 3 | Bookings, expiry, charging | Reuse Member 2 permissions; preserve reservation/charging transaction rules; agree finalizer contract with Member 4 |
| 4 | Wallet, mock top-up, settlement | Create/backfill Owner wallets idempotently with unique OwnerUserId; no Driver wallet; server-side payment identity |
| 5 | Admin modules, station seed, release QA | Admin policy on every operation; run the environment-based Admin bootstrap; run SQL tests and the integrated demo |

The initial migration creates only Users and UserCodeNumbers; the subsequent Member 2 migration adds the mobility/discovery schema. See [Member 2 contract](member2-api.md) for locking, availability adapters, routes and schema ownership. The draft contract is ready for team review; no external team agreement or completed downstream integration is claimed.
