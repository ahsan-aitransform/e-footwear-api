# E-Footwear API (v6 schema)

.NET 10 · clean architecture · Dapper + Npgsql · PostgreSQL 16 with row-level security.

```
db/migrations/0001_baseline_v6.sql    your FootwearPOS v6 schema, byte-for-byte (see ../docs/V6-SCHEMA-FINDINGS.md)
db/migrations/0002_platform_hardening.sql   additive fixes found while running it
src/EFootwear.Domain          money rounding, permission codes, the invoice calculator (pure, unit-tested)
src/EFootwear.Application     services (permissions, validation, orchestration) + repository interfaces
src/EFootwear.Infrastructure  all SQL (Dapper), unit of work, migrator, Argon2id, company provisioning
E-Footwear-Api                controllers, session authentication, error mapping, operator CLI
E-Footwear-Api.Tests          integration tests on a real PostgreSQL (one scratch database per run)
```

## How it works

* The v6 database owns the business rules (journals, stock, commissions, transfers, lockout, sessions). The API verifies the password hash,
  checks permissions and stores, builds consistent documents and calls the database; it does not re-implement posting.
* Every request runs `SET LOCAL ROLE pos_app` with `app.company_id / user_id / store_ids / all_stores / store_id` set per transaction
  (`set_config(..., true)`), so a pooled connection can never carry one tenant's scope into another.
* Session token: `Authorization: Bearer {companyId}.{secret}`; optional `X-Store-Id` chooses the working store (must be one the user holds).
  The database validates the session on every call, so blocking a user or removing a permission applies to the very next request.
* Errors are RFC 7807: `{ title, code, errors{field:[…]} }`. Database messages from the schema's own rules are passed through as 422.
* Money is whole rupees (`bigint`); quantities have 3 decimals; costs 4.

## Run

```bash
# 1. a database and an owner login (superuser or the database owner), plus a non-superuser login for the API itself
dotnet user-secrets --project E-Footwear-Api set "ConnectionStrings:Admin"   "Host=…;Database=efw;Username=owner;Password=…"
dotnet user-secrets --project E-Footwear-Api set "ConnectionStrings:Default" "Host=…;Database=efw;Username=efw_api;Password=…"
dotnet user-secrets --project E-Footwear-Api set "Database:ApiRole" "efw_api"       # granted pos_app by the migrator

dotnet run --project E-Footwear-Api -- migrate
OWNER_PASSWORD='ChangeMe-123' dotnet run --project E-Footwear-Api -- create-company "My Shoes" MAIN "Main Store" owner "Owner Name"
dotnet run --project E-Footwear-Api                                     # http://localhost:5069 , Swagger at /swagger in Development
```

Sign in with the company id or its exact name. The first owner password is temporary: the API refuses everything but change-password until it is changed.
Environment variables always override user-secrets.

## Tests

```bash
export EFW_TEST_ADMIN="Host=localhost;Port=55432;Username=postgres;Password=…"   # a SCRATCH server — databases are created and dropped
dotnet test -c Verify        # use a separate configuration if a `dotnet run` has the Debug build locked
```

## Endpoints (all under `/api/v1`, bearer session unless noted)

| Area | Routes |
|---|---|
| auth | `POST auth/login` (anonymous) · `POST auth/logout` · `GET auth/me` · `POST auth/change-password` |
| admin | `users` (+`meta`, `{id}/reset-password`, `{id}/status`, `login-history`) · `stores` · `devices` · `company` · `masters/{kind}` (+`categories`, `sizes`) · `parties` (+`{id}/move-staff`) |
| catalogue | `catalog/products` · `variants` · `variants/{id}/prices` · `store-price` · `price-history` · `prices/bulk` · `prices/batches/{id}/reverse` · `lookup?barcode=` · `search` · `price-bands` |
| stock | `stock` · `stock/by-size/{product}` · `stock/ledger` · `inventory/adjustments` · `inventory/audits` · `inventory/claims` · `transfers` (+`scan-send`, `scan-receive`, `ship`, `close`, `cancel`, `loss-cases`) |
| promotions | `promotions` (+`publish`, `cancel`, `refresh`, `channels`, `conflicts`, `deals`) |
| trading | `sales` (+`returns`, `exchanges`, `{id}/void`, `{id}/receipt`, `{id}/returnable`, `price-drifts`) · `purchases` (+`returns`, `{id}/void`) |
| money | `accounting/accounts` · `vouchers` · `parties/{id}/ledger` · `opening-balances` · `vendors/*` · `expenses` · `hr/commission-plans` · `hr/commissions` · `hr/payroll` · `cash/promises` · `cash/counts` · `reports/{key}` |

Operational notes: `Workers:PriceRefresh` (default true) rebuilds promotion prices in the background; extend the monthly partitions
(`core.create_monthly_partitions`) before 2028; file storage and the offline sync batch API are not implemented (see `../docs/IMPLEMENTATION-STATUS.md`).
