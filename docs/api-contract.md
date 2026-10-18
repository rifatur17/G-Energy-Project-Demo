# Authentication API contract

Vehicle, Driver assignment and station routes are documented in [Member 2 API contract](member2-api.md).

Base URL in development: `http://localhost:5080`. JSON uses camelCase. Development-only OpenAPI: `/swagger/v1/swagger.json`; interactive UI: `/swagger`. The Swagger Bearer authorization button accepts the raw access token. Password fields are request-only; user responses always use explicit safe DTOs.

## POST /api/auth/register — anonymous

```json
{
  "fullName": "Demo Owner",
  "email": "owner@example.test",
  "phone": "01700000000",
  "password": "Example-only-long-passphrase",
  "role": "CarOwner"
}
```

These examples are illustrative request values, never seeded credentials. Full name: 2–100 characters, trimmed and checked again after trimming. Email: valid address up to 254 characters, trimmed; unique normalized email uses uppercase invariant. Phone: 7–20 characters, optional leading + and digits/spaces/parentheses/hyphens. Password: 12–128 characters, required, preserved exactly and hashed with ASP.NET Core PasswordHasher. Role is exactly `CarOwner` or `CarDriver`; Admin, unknown roles and wrong casing return 400. Unknown JSON properties are ignored and never mapped onto the entity.

201 Created, Location `/api/auth/me`:

```json
{
  "id": 1,
  "userCode": "GE-OWN-001001",
  "fullName": "Demo Owner",
  "email": "owner@example.test",
  "phone": "01700000000",
  "role": "CarOwner",
  "createdAtUtc": "2026-10-19T00:00:00Z"
}
```

Registration does not log in automatically. Display and copy UserCode before navigating to login. The Location endpoint requires a token obtained at login. UserCode comes from SQL Server `dbo.UserCodeNumbers`, starting at 1001; prefixes are OWN/DRV/ADM and decimal formatting pads to at least six digits. Gaps after failed inserts are expected. Never calculate the next number in application code. Both UserCode and NormalizedEmail have unique database indexes; concurrent insert conflicts return 409. Internal ID is a separate SQL bigint IDENTITY.

## POST /api/auth/login — anonymous

```json
{ "userCode": "GE-OWN-001001", "password": "Example-only-long-passphrase" }
```

UserCode is trimmed and uppercased; password is not trimmed. An extra role or identity property is ignored. Successful 200 response:

```json
{
  "accessToken": "<JWT access token>",
  "userCode": "GE-OWN-001001",
  "role": "CarOwner",
  "expiresAtUtc": "2026-10-19T00:30:00Z"
}
```

Unknown account, inactive account or wrong password: 401 with the same `Invalid UserCode or password.` title. A dummy password hash is verified for unknown accounts. No password/hash is returned. Hashes requiring an upgrade are refreshed after valid login.

## GET /api/auth/me — authenticated

Send `Authorization: Bearer <accessToken>`. 200 returns the same safe UserResponse fields as registration, loaded from the database. Missing, expired, incorrectly signed or misconfigured issuer/audience tokens return 401. Tokens for inactive accounts or whose role/UserCode no longer matches the database are also rejected.

JWT: HS256; `sub` is the internal Users.Id as a string; `userCode` is the generated public identifier; `role` is one of Admin/CarOwner/CarDriver; `jti` is a unique token identifier. Issuer, audience, expiration (1–120 minutes) and random signing key are configuration values. Default lifetime: 30 minutes; clock skew: 5 seconds. Claims mapping is disabled; use these exact claim names.

## Errors and HTTP status

ASP.NET Core ProblemDetails uses `application/problem+json` with `type`, `title`, `status` and a trace identifier when supplied by the framework. Validation responses add an `errors` field mapping property names to string arrays. Clients should use status plus title/errors; framework problem type URLs and titles may vary. Do not depend on a success envelope or a custom numeric error code.

```json
{
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "Role": ["Public registration supports CarOwner or CarDriver only."] }
}
```

| Status | Meaning |
| --- | --- |
| 400 | Invalid body/field validation, including public Admin registration |
| 401 | Missing/invalid token, invalid credentials or inactive account |
| 403 | Valid authenticated account lacks role/resource permission |
| 409 | Duplicate email or unique registration conflict |
| 429 | Authentication attempt limit exceeded; wait one minute |
| 500 | Unexpected server error; details are not sent to clients |

The shared rate limiter allows 20 register/login attempts combined per remote IP per minute, with no queue. It runs in memory per API process. Deployments behind a proxy must deliberately configure trusted forwarded headers and assess a distributed limiter; do not trust arbitrary client forwarding headers.

## Policies and ownership

Named policies: `Admin`, `CarOwner`, `CarDriver`, plus `ResourceOwner`. Policies use server-validated claims. Owners never automatically become Admins. ResourceOwner succeeds only when the authenticated `sub` equals the supplied internal owner ID; it does not implement driver assignment logic or an implicit Admin override. Foreign resource access returns 403 (or use a consistent 404 policy in modules that hide resource existence).

The authentication module does not implement other modules; vehicle and station routes are in the separate Member 2 contract. The role probe endpoints under `/test/policies` exist only in the test assembly, which is not deployed with the API.
