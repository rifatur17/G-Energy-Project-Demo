# Member 1: real SQL Server setup and verification

Verified 19 October 2026, Asia/Dhaka. This supersedes the earlier environment limitation in verification.md; that document remains a historical record of the original run.

## Confirmed local environment

- SQL Server service MSSQLSERVER: running; default instance `localhost` (server reports DESKTOP-0MG4DVB).
- Microsoft SQL Server Standard Developer Edition 64-bit, engine 17.0.1000.7.
- Windows Authentication works as DESKTOP-0MG4DVB\Lenovo. The instance reports Windows-only authentication.
- Shared development database: **GEnergyDb**. Neither GEnergyDb nor the old illustrative GEnergy database existed before setup. GEnergyDb was created by EF; no existing database was dropped or recreated.
- Provider: Microsoft.EntityFrameworkCore.SqlServer 10.0.13; SDK 10.0.402.
- Connection settings and a random signing key are saved in .NET user secrets for g-energy-member1-development. No credentials/key are committed. Local-only certificate trust is enabled for this development instance.

## Repeat setup safely

From the repository root, in PowerShell 7:

```powershell
./scripts/Initialize-DevelopmentDatabase.ps1 -Server localhost -Database GEnergyDb -Through InitialIdentity
```

If dotnet is not on PATH, supply `-DotnetPath <absolute path to dotnet.exe>`. The script uses Windows Authentication, preserves a valid existing signing key, checks model/migration consistency and explicitly applies migrations. It has no reset/drop/EnsureDeleted path. Use `-Through Latest` for subsequent Member 2 setup; do not request InitialIdentity after later migrations are applied, because EF's target-migration command can roll back later migrations. The script rejects that downgrade (see guard in script).

appsettings.json keeps an empty connection/signing key. For a manual local setup, the non-password development connection is `Server=localhost;Database=GEnergyDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True`. This is development configuration, not a production certificate policy.

## Applied and directly inspected

At the Member 1 checkpoint, dbo.__EFMigrationsHistory contained `20261018185818_InitialIdentity` (EF 10.0.13). No new migration was needed: the existing migration and current snapshot were consistent.

Direct SQL catalog inspection verified:

- dbo.Users: bigint IDENTITY primary key, UserCode, FullName, Email, NormalizedEmail, Phone, PasswordHash, Role, IsActive, CreatedAtUtc; required columns are non-null.
- PK_Users, unique IX_Users_UserCode, unique IX_Users_NormalizedEmail.
- CK_Users_Role restricts Admin / CarOwner / CarDriver.
- dbo.UserCodeNumbers: bigint sequence, start 1001, increment 1. The service uses NEXT VALUE FOR, never MAX(Id)+1.

The initial migration SQL was reviewed; a fresh idempotent script was generated as an additional local review artifact. It adds the schema; no shared history was rewritten.

## Executed real SQL tests

1. Existing `Migration_sequence_constraints_and_concurrent_registration`: passed against this SQL Server engine. Its existing isolated-database harness creates/deletes only its own generated catalog; it never resets GEnergyDb.
2. New `SharedIdentitySqlServerTests`: passed against **GEnergyDb itself**, after only InitialIdentity had been applied. Eight simultaneous Owner/Driver registrations produced unique persisted codes; two case-variant duplicate-email requests produced one 201 and one 409; hashes persisted and verified with PasswordHasher; responses excluded password/hash; public Admin signup failed; invalid login returned 401; a fresh API host logged in the persisted account and read /me.

The shared test uses the real provider and leaves uniquely labelled `sql-identity-<guid>` QA accounts in place. It never migrates, deletes or recreates the shared database. Passwords are generated in memory and never logged. Run explicitly:

```powershell
$env:G_ENERGY_SHARED_SQLSERVER = 'Server=localhost;Database=GEnergyDb;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
dotnet test --filter FullyQualifiedName~SharedIdentitySqlServerTests
```

For the existing isolated SQL tests, set G_ENERGY_TEST_SQLSERVER to the same server connection and run the SqlServerTests filter. Their cleanup is restricted to the newly generated test catalogs, not GEnergyDb. InMemory tests are separate evidence and were not counted as SQL verification.

Member 2 must extend this same database with the already-shared AddVehiclesAndStationDiscovery migration. The Member 1 checkpoint retained all QA users before that migration. Later verification is in sql-server-member2-verification.md.
