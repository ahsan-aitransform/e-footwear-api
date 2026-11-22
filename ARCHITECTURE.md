# E-Footwear API: architecture and module-building guide

Read this before adding code. It is the template every module follows. Decisions behind it are in
`docs/PHASE-0-requirements-verification.md` (D-008 to D-012): clean architecture, EF Core, whole-rupee money, long names,
permissions checked on the server for every endpoint.

## 1. Layers (dependencies point inward only)

    API (Presentation)  ->  BLL  <-  DAL
                                |
                             Domain

| Layer | Project | Holds | May reference |
|---|---|---|---|
| Domain | `EFootwear.Domain` | Entities, value rules, constants (`RoleCodes`, `ErrorCodes`, `PermissionCatalog`), `DomainException`. No SQL, no HTTP. | nothing |
| BLL (business logic / application) | `EFootwear.BLL` | Services (use cases), contracts (request/response records), **interfaces** for everything it needs (`I...Repository`, `IPasswordHasher`, `IDateTimeProvider`, `IUnitOfWork`, `ICurrentUser`). No SQL, no HTTP. | Domain |
| DAL (data access / infrastructure) | `EFootwear.DAL` | Repositories, EF Core context and entity configurations, `DatabaseSession`, migration runner, DI wiring. No business decisions. | BLL |
| API (presentation) | `E-Footwear-Api` | Controllers, authentication, authorization, error format, `Program.cs`, operator commands. No business rules. | BLL, DAL (DI only) |
| Tests | `EFootwear.Tests` | Unit and integration tests | all |

Request flow: `HTTP -> Controller -> Service (BLL) -> Repository (DAL) -> PostgreSQL`. The service sees interfaces only.

## 2. Folder convention per module

Module names are the PascalCase of Part 25.3: `Platform, Core, Security, MasterData, Parties, Catalogue, Inventory, Sales,
Purchases, Orders, Accounting, CashManagement, HumanResources, Payroll, Commissions, Expenses, Promotions, Integrations,
OfflineSynchronization, Reporting, Audit`.

    EFootwear.Domain/<Module>/                       entities, rules, status-code constants
    EFootwear.BLL/<Module>/Services/                 one use-case class per area:  ProductCatalogService
    EFootwear.BLL/<Module>/Contracts/                requests, responses:          CreateProductRequest, ProductListItemResponse
    EFootwear.BLL/<Module>/Abstractions/             repository interfaces:        IProductRepository
    EFootwear.DAL/<Module>/Repositories/             implementations:              ProductRepository
    EFootwear.DAL/<Module>/Configurations/           EF mapping, one per entity:   ProductConfiguration
    E-Footwear-Api/Controllers/<Module>/             controllers:                  ProductsController
    EFootwear.Tests/<Module>/                        tests

Namespaces follow the folders (`EFootwear.BLL.Catalogue.Contracts`). **One public type per file; the file has the type's name.**

## 3. Naming: long, self-explanatory, no abbreviations

| Kind | Rule | Example |
|---|---|---|
| Request / response | `<Verb><Thing>Request`, `<Thing><Shape>Response` | `CreateProductRequest`, `ProductListItemResponse`, `CurrentSessionResponse` |
| Repository | `I<Thing>Repository` in BLL, `<Thing>Repository` in DAL | `IProductRepository` |
| Service | `<Area>Service` | `UserManagementService` |
| Methods | verb phrase ending in `Async` when asynchronous | `GetProductDetailsByIdAsync`, `ListUsersAsync` |
| Entity configuration | `<Entity>Configuration` | `CompanyUserConfiguration` |
| Constants class | by meaning, static, upper-case values only where they mirror SQL/JSON | `ErrorCodes`, `ApiRoutes`, `SessionContextKeys` |

Never abbreviate: `cancellationToken` (not `ct`), `unitOfWork` (not `uow`), `databaseSession`, `databaseContext`,
`requestContext`, `request`, `repository`, `clientIpAddress`. Lambda parameters are words too (`store => store.Id`, `user => user.Email`).
Test names are sentences: `A_company_token_is_refused_on_every_platform_endpoint_with_403`.

### Single sources of truth (no magic strings or numbers anywhere else)

| What | Where |
|---|---|
| Every URL | `ApiRoutes` (API) |
| Permission definitions | `PermissionCatalogDefinitions` (Domain); constants `PermissionCodes` are **generated** from it |
| Role codes, statuses | `RoleCodes`, `UserStatusCodes`, `CompanyStatusCodes` (Domain) |
| Error codes | `ErrorCodes` (Domain) |
| PostgreSQL session keys (`app.company_id` ...) | `SessionContextKeys` (DAL); the SQL side is documented in that file |
| Policies, auth scheme | `AuthorizationPolicies`, `HttpAuthorizationScheme`, `SessionAuthenticationDefaults` (API) |
| Paging limits | `PaginationDefaults` (BLL) |
| Validation limits and patterns | a `...Rules` class in Domain (`StoreRules`, `CompanyRules`, `UserCreationRules`) |

## 4. Adding a module (checklist, e.g. Catalogue / Products)

1. **Phase 0 row.** Find the requirement rows in `docs/phase-0/03-requirements-traceability-matrix.csv`; mention their ids in your summary.
2. **Schema.** If the baseline `db/schema_v7_uuid.sql` already has the tables and functions (most do), use them. Otherwise add a migration (section 10).
3. **Domain:** entities in `Domain/<Module>/`, implementing `IHasRowVersion` when the table has `row_ver`.
4. **DAL:** one `IEntityTypeConfiguration<T>` per entity in `DAL/<Module>/Configurations/` (found automatically), a `DbSet` line above `// <database-sets:end>` in `ApplicationDatabaseContext`, repositories in `DAL/<Module>/Repositories/`.
5. **BLL:** contracts, repository interfaces, services. A use case ends with `unitOfWork.CommitAsync(cancellationToken)`.
6. **DI:** add one line per service/repository above your module's marker in `DAL/DependencyInjection.cs`, e.g. `// <module-registrations:catalogue:end>`. Use the Edit tool, never rewrite the file.
7. **Routes:** add a nested class to `ApiRoutes` above `// <api-routes:end>`.
8. **Permissions:** add the screens to `PermissionCatalogDefinitions` if they are missing, then regenerate the constants (section 5).
9. **Controller:** `[ApiController]`, `[Authorize(Policy = AuthorizationPolicies.CompanyUserOnly)]`, `[Route(ApiRoutes....)]`, and `[RequirePermission(PermissionCodes....)]` on **every** action.
10. **Tests:** unit tests for rules; integration tests for endpoints (section 11), including a permission-denied case and a cross-company case.
11. **Build and test** (section 12). `ControllerAuthorizationConventionTests` fails if you forgot a permission.

## 5. Permissions (Part 6)

* The catalog `modules -> screens -> actions` is defined once in `Domain/Security/Permissions/PermissionCatalogDefinitions.cs`.
  Code format: `<MODULE>.<SCREEN>.<ACTION>` (upper snake case, dots), e.g. `PRODUCTS.NEW_PRODUCT.ADD`. Actions: `VIEW, ADD, EDIT, DELETE,
  PRINT, EXPORT, APPROVE, CHANGE_SELLING_PRICE, GIVE_DISCOUNT`.
* `PermissionCodes` (`PermissionCodes.Products.NewProduct.Add`) is generated: after editing the catalog run
  `dotnet run --project E-Footwear-Api -- generate-permission-codes`. A unit test fails when the file is stale.
* At startup `PermissionCatalogSynchronizer` upserts the catalog into `sec.permissions` (insert/update only, never delete). The tree shown to
  users is `GET /api/v1/permissions/catalog`.
* **Legacy codes.** The SQL functions call `sec.require_permission(company, user, 'SALE_CREATE')`. These 38 codes stay in the catalog as the
  module `RESTRICTED_OPERATIONS` (`PermissionCodes.RestrictedOperations.Sales.SaleCreate`). When an action calls such a function, require **both**
  the screen code and the legacy code (several `[RequirePermission]` attributes are ANDed).
* `[RequirePermission(code)]` checks `RequestContext.GrantedPermissionCodes`, loaded once per request at authentication. The company owner holds the whole
  catalog by role. A platform administrator holds none and is refused unless the action sets `AllowPlatformAdministrator = true`.
* Policies: `PlatformAdministratorOnly` (every `/platform` endpoint) and `CompanyUserOnly` (every company endpoint).
* **Role templates** (`DefaultRolePermissionTemplates`) are SUGGESTIONS (business policy is open). They are copied into `sec.user_permissions` when a user is
  created and can then be changed per user; a template never blocks an override.
* Hiding a button is not security: every action is protected on the server, and a test enforces it.

## 6. Tenancy (Parts 5.3 and 31)

* Company and store come from the validated session (`RequestContext` / `ICurrentUser`), **never** from the body, the URL or a header. Only platform
  administrators name a company (and the service re-checks it).
* All data access goes through `DatabaseSession`, which opens one transaction per request and sets `app.company_id`, `app.user_id`, `app.all_stores`,
  `app.store_ids`, `app.platform_admin` with `set_config(..., true)` (`SessionContextKeys`). Row-level security in PostgreSQL reads them.
* The API connects as `pos_app_login` (member of `pos_app`), never as a superuser; it refuses to start otherwise (`DatabaseRoleGuard`).
* Every company table has `company_id`, every store table also `store_id`; the head office is the store with the nil uuid.
* Before using a store id from a request, check `requestContext.CanAccessStore(storeId)`.

## 7. Data access patterns

* **Entities + EF Core** for plain reads and writes: map in a configuration class, query with `AsNoTracking()` and project to a response.
  Call `ConfigureRowVersionConcurrencyToken()` when the table has `row_ver`: the trigger increments it and a stale save throws
  `DbUpdateConcurrencyException`, which the API turns into `409 CONCURRENT_UPDATE`.
* **Database functions** (posting, numbering, reports; most business logic lives in SQL, do not re-implement it): call with interpolated raw SQL,
  which EF parameterizes:

      var databaseContext = await databaseSession.GetDatabaseContextAsync(cancellationToken);
      var row = await databaseContext.Database.SqlQuery<PostedInvoiceRow>($"""
          select out_invoice_id as "InvoiceId" from sales.post_invoice({companyId}, {storeId}, {clientTransactionId})
          """).SingleAsync(cancellationToken);

  Result classes are private nested classes with property names matching the quoted aliases.
* **Lists** take a `PageRequest`, return `PagedResult<T>`, and sort through a `SortableFields<TEntity>` map (a client can only sort by fields you allow).
  A controller binds the query string with `[FromQuery] PageQuery pageQuery` (API layer, `Binding/PageQuery.cs`) and calls `pageQuery.ToPageRequest()`. The frontend sends
  `?pageNumber=&pageSize=&sortField=&sortDirection=Ascending|Descending&searchText=`; the short spelling `?page=&sort=-field&search=` works too. The answer serializes as
  `{ items, page, pageSize, totalCount, totalPageCount }`. See `UserActivityController` for a complete example.
* Never `SaveChanges` outside the unit of work; `CommitAsync` saves and commits.
* Time: inject `IDateTimeProvider`; all business timestamps are UTC (`timestamptz`).

## 8. Money and quantities (decision D-009)

* Money is **whole rupees**: `core.money` is `bigint`, so C# uses `long`. Never `decimal`, `double` or `float` for money. Rounding happens in the database (`core.round_money`).
* Quantities are `core.qty numeric(14,3)`: C# `decimal`. Average cost is `core.unit_cost numeric(14,4)`: `decimal`.
* Percentages are `core.pct numeric(7,4)`: `decimal`, range 0 to 100.

## 9. Errors and idempotency

* Throw `DomainException(ErrorKind, message, errorCode?)`. The API answers RFC 7807 problem details with `status`, `title`, `detail`, `errorCode`
  (`ErrorCodes`) and `correlationId`: 400 validation, 401, 403 (`FORBIDDEN` or `PERMISSION_DENIED`), 404, 409 (`CONFLICT`, `CONCURRENT_UPDATE`), 423, 422 (database rule).
* Endpoints that create documents (sales, purchases, vouchers, transfers, expenses ...) accept an optional `clientTransactionId` (`Guid`). Check the table
  first: most documents already have a unique client id / `client_txn_uuid` column. Insert with it and let the **database unique constraint** make a retry safe:
  on a unique violation load and return the existing document. A retried request must never create a second invoice, payment, stock movement or journal entry.

## 10. Database setup and migrations

Fresh database, in this order (as the PostgreSQL owner):

    createdb E-Footwear
    psql -d E-Footwear -v ON_ERROR_STOP=1 -f db/schema_v7_uuid.sql                       # baseline = migration 0000
    psql -d E-Footwear -v pw="'<strong password>'" -f db/create_app_login.sql             # login role for the API
    dotnet run --project E-Footwear-Api -- migrate                                        # numbered migrations
    PLATFORM_ADMIN_PASSWORD='...' dotnet run --project E-Footwear-Api -- create-platform-admin <username> "<full name>"

The operator commands read `ConnectionStrings:Admin` (owner) from user secrets (set `ASPNETCORE_ENVIRONMENT=Development`) or the environment
(`ConnectionStrings__Admin`). The API itself uses `ConnectionStrings:Default` (the application login).

Migrations: `db/migrations/NNNN_name.sql` plus `NNNN_name.down.sql`. Rules: the runner wraps each script in a transaction (no `BEGIN`/`COMMIT` in the
script); every up script has a down script; an applied script is never edited (checksum is verified; add a new migration); applied versions are recorded in
`ops.schema_migrations`. `migrate --down 0100` rolls back 0100 and everything newer, newest first. **Number ranges** (so parallel work never collides):

| Range | Area | Range | Area |
|---|---|---|---|
| 0001-0099 | foundation | 0800-0899 | HR, payroll, expenses |
| 0100-0199 | security and settings | 0900-0999 | promotions, analytics |
| 0200-0299 | master data, parties | 1000-1099 | orders |
| 0300-0399 | catalogue | 1100-1199 | reports |
| 0400-0499 | inventory | 1200-1299 | offline synchronization |
| 0500-0599 | sales | 1300-1399 | Shopify |
| 0600-0699 | purchasing | 1400-1499 | production readiness |
| 0700-0799 | accounting | | |

## 11. Tests

* Unit tests need nothing. Integration tests need the environment variable `EFW_TEST_ADMIN`: a server-level owner connection string **without a database**,
  e.g. `Host=localhost;Port=5432;Username=postgres;Password=<password>` (never commit the password).
* The fixture (`ApiTestEnvironment`) creates a uniquely named throwaway database (baseline + all migrations), starts the real API in memory against it as
  the non-superuser role `efw_test_login`, and drops the database at the end. Several people can run tests at the same time.
* Use `[Collection(ApiIntegrationCollection.Name)]` and `TestScenarioBuilder`: `CreatePlatformAdministratorClientAsync`, `CreateCompanyAsync`, `CreateStoreAsync`,
  `CreateUserAsync(creatorClient, companyId, roleCode, storeIds)`, `SignInAsync(email, password)`, `SetUserPermissionsAsync(companyId, userId, codes)`.
  Every call makes unique companies/users, so tests never collide.
* Required for each module (Part 32): validation, permission allowed and denied, tenant and store isolation, concurrency/idempotency where documents are created.

## 12. Build, run, parallel work

    dotnet build E-Footwear-Api.slnx --artifacts-path D:\office\E-Footwear\.artifacts\<your-name>
    $env:EFW_TEST_ADMIN = 'Host=localhost;Port=5432;Username=postgres;Password=...'
    dotnet test  E-Footwear-Api.slnx --artifacts-path D:\office\E-Footwear\.artifacts\<your-name> --no-build

* **Always build with your own `--artifacts-path`** so parallel builds do not lock each other's files. A running `E-Footwear-Api.exe` locks its output; start servers
  with `run_in_background` and stop them yourself. Smoke-run ports: **5101-5199** (one per person).
* Shared files are edited with small `Edit` calls at the marker lines, never rewritten: `// <module-registrations:<module>:end>`, `// <database-sets:end>`,
  `// <api-routes:end>`, `// <permission-catalog-modules:end>`. If the build breaks in a file you do not own, wait a minute and retry.
* Regenerate `PermissionCodes.generated.cs` only through the command; commit-free work: nothing is pushed to git without being asked.

## 13. Operator commands

| Command | Purpose |
|---|---|
| `migrate [--down <version>] [--directory <path>]` | apply or roll back `db/migrations` |
| `create-platform-admin <username> "<full name>"` | first platform administrator (password from `PLATFORM_ADMIN_PASSWORD`) |
| `generate-permission-codes [--output <path>]` | regenerate `PermissionCodes.generated.cs` |

## 14. Logging

Serilog (console + daily file in `E-Footwear-Api/logs/`), configured in `appsettings*.json`. Every request has a correlation id (`X-Correlation-Id`), returned in the
header and in error bodies. Log ids, never names, emails, passwords or tokens (`LogMask.Email` for emails).
