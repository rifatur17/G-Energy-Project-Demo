# Member 1 verification evidence

Recorded 19 October 2026 (Asia/Dhaka). Implementation reviewed through commit `bfd0aff`; subsequent documentation-only changes do not change the tested code.

## Environment

- Windows x64; .NET SDK 10.0.402; ASP.NET Core / EF Core packages 10.0.13.
- React 19.3.1, React Router 7.18.4, Vite 8.3.4, TypeScript 7.0.2 (resolved dependency versions are in package-lock.json).
- Fresh clone of the requested repository; original main contained only README at `a77b5e4`. No existing working code or user changes were overwritten.
- Repository-local Git author: Rifatur Rahman <rifaturrahman1131@gmail.com>.

## Executed checks

| Check | Result |
| --- | --- |
| Backend dependency restore | Passed |
| Full solution build | Passed, 0 warnings / 0 errors |
| Backend automated HTTP/security tests | 11 passed, 0 failed |
| SQL Server integration test | 1 explicitly skipped; test connection variable unset |
| Frontend automated tests | 15 passed, 0 failed |
| Frontend TypeScript / production build | Passed |
| npm audit | 0 known vulnerabilities |
| NuGet audit including transitive packages | No known vulnerable packages in API or tests |
| EF migration generation and SQL script generation | Passed |
| EF pending model changes check | No pending model changes |
| Browser layout review | Login checked at desktop and 390 × 844 mobile; signup inspected; no captured console warnings/errors |
| Staged changes | Diff checks and token/private-key pattern scan reviewed; no generated build outputs or real credentials committed |

Backend tests use ASP.NET Core WebApplicationFactory, EF InMemory and a test-only atomic code generator. They exercise registration, role prefixes, normalized duplicate email, password hashing/no hash response, generic login failure, public Admin rejection, phone validation, JWT issuer/audience/signature/expiry, claim/database mismatch, role policies, direct resource ownership, account disablement, rate limiting, repeatable Admin seeding and production bootstrap/Swagger restrictions. Test policy endpoints are absent from the production API assembly.

Frontend tests use mocked HTTP/API responses and cover registration role selection, copy ID, duplicate-email feedback, login/logout, role-aware restored sessions, forged cached roles overridden by server profile, private-route redirect, rejected sessions, network retry, expiry, bearer headers and API error parsing.

## Unverified database and integration behavior

No SQL Server service, LocalDB, sqlcmd or Docker executable was available. Only MySQL was present, which is not an acceptable substitute for this schema.

The initial migration and SQL script were generated and reviewed, but were **not applied to a SQL Server instance**. SQL-backed registration, SQL sequence allocation under concurrency, database uniqueness races, SQL password-hash persistence, Admin bootstrap against SQL Server, SQL UTC round trips and a complete browser → API → SQL Server signup/login journey remain unverified here.

Run the opt-in SQL test described in README on a disposable SQL Server before claiming database acceptance. InMemory test success does not establish SQL Server constraint or concurrency correctness. The test is committed and reports SKIP instead of falsely passing when no SQL connection is configured.

Members 2–5 modules and the full charging/payment journey have not been implemented or verified by this Member 1 task. Shared contracts are proposed for team review; external coordination has not been performed.

## Git delivery

Remote: https://github.com/rifatur17/G-Energy-Project-Demo; target/local branch: main.

Fetch succeeded and the remote retained the original initial commit. Both non-interactive push dry-run and an actual push failed because Git could not obtain authentication credentials:

```text
fatal: Cannot prompt because user interactivity has been disabled.
fatal: unable to get password from user
```

No push succeeded and no history was rewritten. All implementation work is committed locally. After authenticating Git for the requested GitHub account with write permission, fetch/review remote changes and push using:

```powershell
git fetch origin
git log --oneline --left-right origin/main...main
git push origin main
```

If remote main has advanced, integrate the new commits safely before pushing; never force-push. Use `git log --oneline origin/main..main` to list the local delivery commits, including this documentation commit.
