# Member 4: demo wallet and payment contract

Uses the existing EnergyDbContext, JWT policies, SQL Server GEnergyDb, vehicle locks and Member 3 IChargingSettlement. No real bank, card processor, bKash, Nagad or payment network is contacted. No payment credentials are collected. Currency is BDT and every top-up/payment/refund is simulated.

## Ownership and schema

Wallets belong to active CarOwner accounts, because Member 3 snapshots the vehicle Owner as PayerOwnerUserId even when a Driver starts charging. Drivers keep their existing charging access but cannot access an Owner wallet, top up, pay or refund it. Admin has no wallet permission. Wallet endpoints use the authenticated identity; none accepts an arbitrary wallet/user ID. Another Owner's transaction/session returns 404; Driver/Admin wallet requests return 403. Authentication continues to reject inactive accounts.

`20261019044347_AddWalletAndPayments` adds:

- Wallets: unique UserId FK, decimal(18,2) Balance >= 0, UTC creation/update timestamps and native SQL rowversion.
- WalletTransactions: immutable append-only application ledger with unique WT reference, wallet FK, type, status, positive decimal(18,2) amount, scoped idempotency key, mock requested outcome, description, optional session/original-transaction links and UTC creation/completion timestamps.
- ChargingSessions.PaymentReference: nullable nvarchar(48). Existing cost/status/timestamps remain authoritative and unchanged.

Ledger types are TopUp, ChargingPayment and Refund. TopUp has Completed or Failed outcome; charging payments/refunds are Completed. This synchronous deterministic mock needs no durable Pending state. CHECK constraints enforce type/status/link combinations, positive amounts and completion >= creation. All four new FKs use NO ACTION. The composite original-transaction/wallet FK prevents a refund linking across wallets. Unique indexes enforce one wallet per user, one key per wallet, one completed charging payment per session and one refund per original payment. The original payment is never edited or deleted to implement a refund. EnergyDbContext rejects modified/deleted ledger entities through SaveChanges and SaveChangesAsync; raw DBA SQL is outside this application-level immutability guarantee.

Review [member4-migration.sql](member4-migration.sql). No historical migration was changed. Apply explicitly using the existing initialization script after reviewing pending SQL; startup never automatically migrates or resets the database.

## APIs

All routes require the existing CarOwner policy and use safe ProblemDetails on errors.

| Method | Route | Request / result |
| --- | --- | --- |
| GET | `/api/wallet` | Safely initializes this Owner's wallet if absent; returns id, balance, BDT, timestamps, demo limits and isDemo. |
| GET | `/api/wallet/transactions?take=20&before=123` | Own ledger, descending ID, keyset pagination. take 1–100; before optional positive ID. Pass the oldest ID for the next page. |
| GET | `/api/wallet/transactions/{id}` | Own immutable transaction with reference, amount, type/status, timestamps, session/original links, canRefund and refundReference. |
| POST | `/api/wallet/top-up` | JSON `{amount:1000,outcome:"Success"}` or outcome Failure. Required Idempotency-Key header (1–100 nonblank characters). Returns transaction, current balance and BDT. |
| POST | `/api/charging-sessions/{sessionId}/pay` | No amount/body required. Pays this Owner's completed session using its saved final cost. Returns original payment and current balance on repeat. |
| POST | `/api/wallet/transactions/{id}/refund` | No amount/body required. Full one-time demo refund of an eligible own completed charging payment. Returns refund and balance. |

Processed top-ups return HTTP 200 even when the mock outcome is Failed; inspect transaction.status. Failed transactions are retained and never credit the wallet. Invalid input is 400. Insufficient balance, limit breaches, ineligible/already-refunded payments and incomplete sessions are 409. Foreign/nonexistent resource IDs are 404. There is no direct balance-edit endpoint. Additional client balance/price/payer fields cannot change the authoritative values.

Top-ups reject nonpositive amounts, amounts above the configured transaction limit and more than two meaningful decimal places. Idempotency keys are wallet-scoped and internally prefixed `topup:`; payment/refund keys use separate system prefixes. A same-key retry returns the original immutable transaction without calling the provider again. Changed amount/outcome with the same key returns 409. SQL key comparison follows the existing database collation. A Failed request is final for its key; use a new key for a new payment attempt.

## Transaction and concurrency guarantees

Every balance writer uses a SQL transaction and UPDLOCK,HOLDLOCK on the payer User followed by Wallet. The User lock makes first initialization safe before any wallet row exists. Charging paths follow the shared order: Vehicle → Charger → payer User → Wallet. Wallet-only reads/initialization/top-up use User → Wallet and never then acquire a vehicle lock. Unique indexes and wallet rowversion add database enforcement. All future Member 5/payment code must preserve this order.

Top-up persists the provider result, ledger insert and any credit in one transaction. The provider interface IPaymentProvider is separate from the ledger. MockPaymentProvider is deterministic, supports Success/Failure and optional forced failure, and performs no external side effects. Calls are short and synchronous; a real external provider must not be substituted with irreversible side effects inside this database transaction.

Manual settlement first authorizes the snapshotted payer, locks the vehicle/charger, re-reads the session, and requires Completed with an end timestamp. It takes the final Cost directly from the persisted session, then locks the wallet. Debit + unique payment ledger insert + Paid status + PaymentReference commit together. Two concurrent payments for one session return the same payment; competing sessions on one wallet serialize and cannot overdraw. A prior successful payment is returned even after its refund, so that session cannot be charged again. A Paid session without a corresponding ledger is rejected for reconciliation instead of debited again.

WalletChargingSettlement replaces DeferredChargingSettlement in DI. Member 3 calls it after flushing Completed, inside the SAME transaction and scoped DbContext. With sufficient funds it pays automatically at stop/full-charge completion. Insufficient funds returns Unpaid so charging still completes and releases the charger; the Owner may top up and use the pay endpoint later. A disabled/unavailable payer also leaves completion unpaid rather than preventing a Driver from finishing. Zero-cost or above-limit bills remain unpaid with a clear explanation; no zero-value ledger movement is invented. Manual failure leaves balance and payment state unchanged.

Unexpected exceptions roll back all applicable writes. During automatic completion this includes session completion, battery/energy, charger release and wallet/payment records. A retry may call the participant again after rollback; committed completed-session retries never debit twice. SQL uniqueness/concurrency/deadlock errors use the existing safe conflict handler.

## Demo refund policy

With AllowDemoRefunds=true, an Owner can fully reverse their completed demo charging payment exactly once while the associated session is Completed/Paid. Top-ups (successful or failed), refunds, other users' payments and partial/arbitrary amounts are ineligible. The credit cannot exceed the original payment and must fit the configured wallet balance limit. If that limit blocks a refund, the operation is rejected atomically.

Refund creates a separate positive Refund ledger entry referencing the original payment and session. It restores the balance and updates the session's explanatory settlement note in one transaction. Original payment type/status/amount/reference remain untouched. Session PaymentStatus stays Paid to mean the bill was settled; wallet history exposes refundReference and the session note explicitly says refunded. This deliberate demo reversal does not reopen debt or permit repayment. Duplicate/concurrent refunds return 409 after the first success without a second credit. This is not a production dispute, cash-out or consumer-refund policy.

## Configuration and run

Reuse existing Windows-authenticated SQL connection and JWT user secrets. Do not commit secrets or database files.

| Environment variable | Default | Rule |
| --- | --- | --- |
| Wallet__MaxTransactionAmount | 100000 | Positive, <= 1000000 BDT, two decimals; applies to top-up and charging bills. |
| Wallet__MaxBalance | 1000000 | At least the transaction limit, <= 10000000 BDT, two decimals; credit limit. |
| Wallet__ForceMockFailure | false | Makes simulated top-ups fail deterministically. |
| Wallet__AllowDemoRefunds | true | Enables the full demo reversal policy. |

```powershell
./scripts/Initialize-DevelopmentDatabase.ps1 -Server localhost -Database GEnergyDb -Through Latest
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project backend/GEnergy.Api
# second terminal
cd frontend
npm ci
npm run dev
```

On this computer dotnet is `C:\Test\runtimes\projectflow-dotnet\dotnet.exe`. If Windows Application Control blocks the generated apphost executable, the standard managed host works: build, then run `dotnet bin/Release/net10.0/GEnergy.Api.dll --urls http://localhost:5080` from backend/GEnergy.Api with Development configured. This does not disable any protection.

Frontend `/wallet` is Owner-protected and uses real API data. It shows simulated balance, outcome selector, limits, top-up results, paged history and eligible refund controls. Completed unpaid session pages offer payment only to their payer Owner, show insufficient-funds errors and link to top-up. Paid sessions show PaymentReference. Submission guards are UX only; SQL provides the guarantees. An uncertain top-up keeps its wallet-scoped amount/outcome/key in sessionStorage for refresh/retry; only nonsensitive demo retry metadata is stored. Explicit final success/failure clears it; editing details begins a new attempt. If browser storage is unavailable, same-page retries still retain the key. History remains authoritative across refresh and backend restarts.

## Member 5 handoff / limitations

The demo now supports signup → vehicle/assignment → station → reservation → charging → simulated top-up/payment → eligible full refund. Drivers charge; the Owner pays. Use newly created labelled QA data rather than altering prior verification records. Reports must distinguish gross ChargingPayment, Refund and net ledger totals; refunded sessions retain Paid and a refund note. No Admin wallet-management permission, receipt document generator, real gateway, card/mobile credentials, hardware integration or production load-test claim is included. A real gateway needs durable provider idempotency, callback authentication/reconciliation and a transactional outbox/recoverable workflow; no distributed transaction or external queue is introduced here.
