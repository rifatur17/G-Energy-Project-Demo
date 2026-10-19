# Presenting the Admin panel locally

On 20 October 2026 a dedicated presentation Admin was provisioned in the existing local SQL Server `localhost / GEnergyDb` using the existing Development-only AdminSeeder. The account is active, uses role Admin and stores an ASP.NET Core password hash. Existing accounts were preserved; no schema migration or database reset was needed.

## Log in on this computer

1. Start the API and frontend using the [setup guide](member5-admin-qa.md).
2. Obtain the presentation UserCode and password from the local file `C:\Test\g-energy-presentation-admin.json`. This private file is outside the repository; never copy it into Git or show its password during the presentation.
3. Open `http://localhost:5173/login`, enter the **UserCode** in User ID and the corresponding password. Email is not the login identifier.
4. Login opens `/dashboard/admin`. Use **Admin tools** or the Admin navigation to show the remaining pages.

Database account data is local to SQL Server. A Git push does not copy Users rows, password hashes or local credentials to GitHub or another computer. On another computer use the README's secure `--seed-admin` bootstrap with a distinct email/password and that computer's configured development database. There is no shared/default Admin password. Repeating the seed with an existing Admin email returns its code without changing its password; it cannot promote an Owner/Driver.

## Suggested presentation sequence

- **Overview:** explain real API/SQL-backed user, station, charger, booking/session counts. Distinguish simulated gross charging payments, refunds and net totals.
- **Network:** show station/charger configuration. Use a new clearly labelled demo station if demonstrating changes; review before saving. Valid reservations and active charging prevent configuration edits.
- **Bookings:** filter by station/status, refresh and show booking reference, coded participant, timestamps, expiry and related session.
- **Sessions:** show the persisted server-calculated energy, battery, bill and payment reference. A consumed booking and a completed charging session are different states.
- **Transactions:** distinguish completed/failed top-ups, charging debits and separate refunds. Original payment/refund references stay linked. Admin monitoring cannot charge, credit or refund a wallet.
- **Wallets and Users:** show coded access and balance overviews; no passwords/hashes or private contact fields are returned.

All charging, batteries and payments are simulated. QA/sample locations are not verified real charging stations. Use your existing Owner/Driver accounts in separate browser sessions when demonstrating their flows and Admin access denial. Do not display local credential files, environment variables, JWTs or password inputs on presentation slides.
