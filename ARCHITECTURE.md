# E-Footwear API: architecture

Clean Architecture. Dependencies point inward only:

    API (Presentation)  ->  BLL  <-  DAL
                                   |
                                Domain

## Layer map (classic name = project name)

| Classic name | Project | Holds | May reference |
|---|---|---|---|
| **Domain** | `EFootwear.Domain` | Core types shared by all layers: business exceptions, later entities and value rules | nothing |
| **BLL** (Business Logic Layer) | `EFootwear.BLL` | Services (use cases) such as `AuthService`, `StoreService`; the rules of the business; **interfaces** for everything it needs (repositories, password hasher, current user) | Domain |
| **DAL** (Data Access Layer) | `EFootwear.DAL` | Repositories (all database access, EF Core + Npgsql), `DbSession`, Argon2 hasher, DI wiring | BLL (to implement its interfaces) |
| **API / Presentation** | `E-Footwear-Api` | Controllers, authentication handler, error format, `Program.cs` | BLL, DAL (only to register DI) |
| Tests | `EFootwear.Tests` | Unit and integration tests | all |

Request flow:

    HTTP request -> Controller (API) -> Service (BLL) -> Repository (DAL) -> PostgreSQL
                                          |
                                     uses interfaces only; never sees SQL

## What goes where
- **Domain:** shared building blocks. No SQL, no HTTP.
- **BLL:** decisions and rules ("who may do what, in which order"). No SQL, no HTTP. Talks to the database only through interfaces such as `IAuthRepository`.
- **DAL:** SQL and database calls only. No business decisions. Implements the BLL interfaces.
- **API:** turns HTTP into a service call and the result into HTTP. No business rules.

## Rules that keep tenancy safe
- The API connects as `pos_app_login` (member of `pos_app`), never as `postgres`. The API refuses to start otherwise.
- Every query goes through `DbSession` (DAL), which sets `app.company_id`, `app.user_id`, `app.all_stores`, `app.store_ids`
  at the start of each transaction. Row-level security in the database reads them.
- Company and store come from the validated session (`ICurrentUser`), never from the request body, URL or headers.
- A use case ends with `IUnitOfWork.CommitAsync()`. If it throws, the transaction is rolled back.
- Permissions: `[RequirePermission("CODE")]` on the action (calls `sec.require_permission`).

## Adding a feature (vertical slice), e.g. Products
1. **BLL:** `Products/ProductService.cs` + `IProductRepository` interface (+ DTOs).
2. **DAL:** `Products/ProductRepository.cs` (SQL via `DbSession.GetAsync()`); register both in `DependencyInjection.cs`.
3. **API:** `ProductsController` with `[Authorize]` and `[RequirePermission(...)]`.
4. **Tests.**

## Database and ids (schema v7)

- `db/schema_v7_uuid.sql` is the baseline: apply it to an empty database as `postgres`. Later changes go in `db/migrations/` (numbered from 0001).
- Every id is a `uuid` (PostgreSQL 18 `uuidv7()`, time-ordered). In C# they are `Guid`; in the API and the frontend they are strings.
- The head office of a company is the store with the nil id `00000000-0000-0000-0000-000000000000`. It replaces the old store id 0.
- Document numbers (`invoice_no`, `voucher_no`, ...), `row_ver` and `change_seq` stay `bigint`: they are counters, not ids.
- `db/legacy_bigint/` holds the old numeric-id migrations. They are kept for reference only and must not be run.
- Logging: Serilog (console + daily file in `E-Footwear-Api/logs/`), configured in `appsettings*.json`. Every request has a correlation id (`X-Correlation-Id`).
