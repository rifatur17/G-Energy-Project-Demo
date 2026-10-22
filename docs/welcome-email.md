# Signup welcome email

New public Owner/Driver registrations create a `WelcomeEmails` delivery row in the same EF SaveChanges transaction as the User. One unique UserId index prevents duplicate welcome work. Existing users and Admin bootstrap are not backfilled or emailed. The feature defaults to **disabled**, as requested; SMTP will be configured later. Disabled signup still saves its delivery work and returns `welcomeEmailStatus: "NotConfigured"`. Enabled signup returns `Queued`, never a claim of successful inbox delivery. Registration itself does not call SMTP or fail because a mail server is down.

The message includes a personal welcome, generated User ID, account type, configured login link, a thank-you and the demo notice. It never includes the signup password, password hash, JWT, phone number or SMTP credentials. Use the password chosen during registration. This implements an emailed User ID reminder, not password recovery or email verification.

## Database and delivery

Migration `20261022094203_AddWelcomeEmailOutbox` adds only the `WelcomeEmails` table, its unique per-user index, pending-work index, attempts check and NO ACTION User FK. Existing schema/data is preserved. Review [SQL script](welcome-email-migration.sql), then run the existing explicit `dotnet ef database update --project backend/GEnergy.Api` when installing this version on another computer. Startup and the double-click launcher do not apply migrations automatically.

A background worker claims pending work with a five-minute lease and an EF concurrency token. A new token is retained after completion/failure to prevent stale-claim races. Sender operations have a 30-second cancellation timeout, mandatory validated TLS and no SMTP protocol/body logging. Failed deliveries retain their SQL row and retry after 30, 60, 120 seconds, etc., capped at one hour and ten attempts. After ten failed attempts the row stays retained but automatic retry stops; investigate configuration before an operator requeues it. Worker exceptions log only delivery ID, attempt and exception type, not server replies or credentials.

SMTP acceptance is recorded as SentAtUtc; it does not prove inbox arrival. Exactly-once inbox delivery is impossible with ordinary SMTP: a crash after SMTP acceptance but before the database update can cause a duplicate on retry. Retries reuse a stable Message-ID. Claim concurrency is tested against SQL Server. Pending work survives API restarts; an interrupted lease becomes eligible again after expiry.

The pending selector can be restricted to a delivery ID internally for deterministic tests/operator tooling. It is not an HTTP endpoint and there is no anonymous resend, preview or queue-inspection API. An email template is constructed only when dispatching the saved User's own welcome work; credentials are never copied into the outbox.

Turning Email.Enabled on later also processes pending registrations created while disabled. Review these recipients first, including labelled `@example.test` QA accounts. Do not enable delivery against test fixtures or use real SMTP in automated tests. This PC's queue was tested with a capturing sender only; no real email was sent.

## Configure later

Use .NET user secrets for local development, or a deployment secret store/environment variables. Never place working credentials in appsettings.json, frontend code, a Git commit or this document. Configuration keys:

| Key under Email | Meaning |
| --- | --- |
| Enabled | false until a sender is configured; true starts delivery on restart |
| WorkerEnabled | true in the app; false for deterministic dispatch tests |
| Host / Port | SMTP host and port, usually 587 for STARTTLS or 465 for implicit TLS |
| Security | StartTls or SslOnConnect; no plaintext option |
| Username / Password | SMTP username and provider-specific app password/key; both set or both blank for an approved authenticated-network relay |
| FromAddress / FromName | Authorized sender mailbox and display name |
| LoginUrl | Actual frontend login URL; HTTPS outside Development |

For example, set nonsecret values via `dotnet user-secrets set 'Email:Host' '<your-smtp-host>' --project backend/GEnergy.Api`. Enter the Password through a secure local prompt and pipe JSON into `dotnet user-secrets set`, as in the README's Admin bootstrap pattern; never type the secret into command history. User secrets are local development storage, not an encrypted production vault. A shared/deployed email must link to a reachable frontend URL; `http://localhost:5173/login` works only on this development computer. Restart the backend after configuring. Keep TLS certificate validation enabled.

The existing account-created screen always retains the copyable ID. It displays either 'email not set up' or 'queued', and explains that the password is not emailed. SMTP failures do not erase an account or its ID. Use the returned User ID to log in even while email is unavailable.

## Verification on 22 October 2026

Applied the reviewed additive migration to localhost / GEnergyDb. Direct SQL verified the new unique UserId index, pending-work index and enabled/trusted User FK. EF reports no pending model changes. After the full test suite, saved fingerprints of every original row in all nine pre-existing tables still matched, including existing credential hashes and financial records. No existing user was backfilled into the email queue.

Full Release backend suite: **74 passed, zero failed/skipped**, including **23 actual SQL Server tests**. Seven new welcome email tests verify disabled and configured registration responses, minimal safe message contents/HTML escaping, TLS/configuration validation, durable failure/retry through a new host, five concurrent dispatch attempts producing one captured message, unique per-user queue enforcement, and complete rollback of the User insert when SQL rejects the paired outbox insert. These use a capturing sender, not a real external mailbox. Frontend: **55 passed**, including truthful not-configured/queued states and the copyable ID. Release build: zero warnings/errors. TypeScript/Vite production build: passed. NuGet vulnerability inspection reported no vulnerable backend packages in the configured sources at verification time.

Commands: `dotnet test -c Release --filter FullyQualifiedName~WelcomeEmail`, `dotnet build -c Release --no-restore`, `dotnet test -c Release --no-build --logger "trx;LogFileName=welcome-email-final.trx"`, `dotnet ef migrations has-pending-model-changes --project backend/GEnergy.Api -c Release --no-build`, `dotnet list backend/GEnergy.Api package --vulnerable --include-transitive`, and frontend `npm test` / `npm run build`. Both existing SQL opt-in environment variables were set as described in the Member 5 verification guide. Development hosts were stopped for deterministic SQL tests and restarted afterwards. This computer's dotnet executable is `C:\Test\runtimes\projectflow-dotnet\dotnet.exe`.

Live browser signup against the real restarted API created labelled QA account `GE-OWN-001627`. The screen retained its copyable ID and showed 'Welcome email is not set up yet'. Direct SQL confirmed associated welcome work #73, Attempts 0 and SentAtUtc NULL, matching disabled SMTP rather than pretending delivery. Screenshot outside Git: `C:\Test\g-energy-welcome-signup.png`. No real mailbox was contacted.
