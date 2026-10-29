# ProductLink: Specification Document

> A product association and recommendation backend built to practice **.NET 10 / C# 14** from simple CRUD up to a large enterprise architecture.

---

## 1. Project Overview

**ProductLink** manages relationships between products (cross-sell, upsell, accessories, substitutes, bundles, frequently-bought-together) and serves recommendations to storefronts and internal tools.

### Goals
- Practice every major .NET 10 concept in a realistic domain.
- Grow the system in levels: single project, layered, modular, microservices, production-ready.
- Practice complex LINQ, SQL tuning, caching, messaging, observability, and security.

### Non-goals
- No frontend (API only; a Scalar/OpenAPI UI is enough).
- No real payment processing (orders are imported or simulated).

### Actors

| Actor | Description |
|---|---|
| Admin | Full access, manages users, tenants, feature flags |
| Merchandiser | Manages products, associations, rules |
| Analyst | Read-only access to reports and metrics |
| API Client (Storefront) | Machine client; reads recommendations, posts events |
| System (Worker) | Background jobs that compute scores, import data, and publish events |

---

## 2. Technology Stack

| Concern | Choice |
|---|---|
| Runtime | .NET 10, C# 14 |
| API style | Minimal APIs (primary) + Controllers (comparison in Level 1) |
| ORM | EF Core 10 (+ Dapper for hot read paths) |
| Database | SQL Server 2022; Redis for cache |
| Auth | ASP.NET Core Identity, JWT, passkeys, policy-based authorization |
| Validation | FluentValidation |
| Mapping | Mapperly |
| Messaging | RabbitMQ or Kafka via MassTransit/Wolverine |
| Jobs | BackgroundService + Hangfire/Quartz |
| Cache | HybridCache + Redis |
| Search | OpenSearch/Elasticsearch |
| Gateway | YARP |
| Observability | Serilog, OpenTelemetry, Grafana/Prometheus/Jaeger |
| Orchestration | .NET Aspire, Docker, Kubernetes |
| Testing | xUnit, NSubstitute, Testcontainers, WebApplicationFactory, NetArchTest, NBomber/k6, BenchmarkDotNet |
| AI (bonus) | Microsoft.Extensions.AI, EF Core vector search, MCP server |

---

## 3. Domain Model

### 3.1 Entities

- **Tenant**: an organization using the system (multi-tenancy).
- **Category**: hierarchical product category.
- **Brand**
- **Product**: sellable item identified by SKU.
- **ProductAttribute**: key/value attributes (JSON column in EF Core 10).
- **Association**: directed link `SourceProduct -> TargetProduct` with a type, score, and validity window.
- **AssociationRule**: automatic rule (for example, category A suggests category B).
- **AssociationScore**: computed market-basket metrics (support, confidence, lift).
- **Customer**
- **Order / OrderItem**: source data for scoring.
- **RecommendationEvent**: impressions and clicks used to measure effectiveness.
- **Bundle / BundleItem**: grouped products sold together.

### 3.2 Association types

| Value | Meaning |
|---|---|
| `CrossSell` | Complementary product from another category |
| `UpSell` | Higher-priced or premium alternative |
| `Accessory` | Required or optional add-on |
| `Substitute` | Interchangeable replacement |
| `Bundle` | Part of a defined bundle |
| `FrequentlyBoughtTogether` | Computed from order history |

### 3.3 Business rules

1. A product cannot be associated with itself.
2. The pair (`Source`, `Target`, `Type`, `TenantId`) must be unique.
3. `Substitute` associations are symmetric; the system creates and removes the reverse link.
4. Association scores range from 0 to 1.
5. An association is **active** when `ValidFrom <= now` and (`ValidTo` is null or `ValidTo > now`).
6. Inactive or out-of-stock targets are excluded from recommendations.
7. Deleting a product is a soft delete; associations referencing it become inactive.
8. Manual associations always outrank computed ones at equal score (`Source = Manual` boosts by a configurable factor).
9. Every write to products and associations is audited.

---

## 4. Database Schema

> Types use SQL Server naming. All tables include `TenantId` unless noted. "Audit columns" means `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`, `RowVersion`.

### 4.1 Catalog

**Tenants**
| Column | Type | Notes |
|---|---|---|
| Id | uniqueidentifier | PK |
| Name | nvarchar(200) | |
| Slug | nvarchar(100) | unique |
| IsActive | bit | |
| CreatedAt | datetimeoffset | |

**Categories**
| Column | Type | Notes |
|---|---|---|
| Id | uniqueidentifier | PK |
| TenantId | uniqueidentifier | FK, indexed |
| ParentId | uniqueidentifier? | self FK |
| Name | nvarchar(200) | |
| Path | nvarchar(500) | materialized path, for example `/1/4/9/` |
| Audit columns | | |

**Brands**
| Column | Type | Notes |
|---|---|---|
| Id | uniqueidentifier | PK |
| TenantId | uniqueidentifier | |
| Name | nvarchar(200) | unique per tenant |

**Products**
| Column | Type | Notes |
|---|---|---|
| Id | uniqueidentifier | PK |
| TenantId | uniqueidentifier | |
| Sku | nvarchar(64) | unique per tenant |
| Name | nvarchar(300) | |
| Description | nvarchar(max) | |
| CategoryId | uniqueidentifier | FK |
| BrandId | uniqueidentifier? | FK |
| Price | decimal(18,2) | |
| Currency | char(3) | |
| StockQuantity | int | |
| Status | smallint | Draft, Active, Discontinued |
| Attributes | json | JSON column (EF Core 10) |
| Embedding | vector(1536)? | bonus AI feature |
| IsDeleted / DeletedAt | bit / datetimeoffset? | soft delete via named query filter |
| Audit columns | | |

Indexes: `(TenantId, Sku)` unique, `(TenantId, CategoryId, Status)`, JSON index or computed column on frequently queried `Attributes` paths, full-text index on `Name`.

### 4.2 Associations

**Associations**
| Column | Type | Notes |
|---|---|---|
| Id | uniqueidentifier | PK |
| TenantId | uniqueidentifier | |
| SourceProductId | uniqueidentifier | FK |
| TargetProductId | uniqueidentifier | FK |
| Type | smallint | enum |
| Score | decimal(5,4) | 0 to 1 |
| Source | smallint | Manual, Rule, Computed |
| ValidFrom | datetimeoffset | |
| ValidTo | datetimeoffset? | |
| Priority | int | manual ordering |
| Audit columns | | |

Constraints: unique `(TenantId, SourceProductId, TargetProductId, Type)`; check `SourceProductId <> TargetProductId`.
Indexes: `(TenantId, SourceProductId, Type, Score DESC)` with `INCLUDE (TargetProductId)`; filtered index on active rows.

**AssociationRules**
| Column | Type | Notes |
|---|---|---|
| Id | uniqueidentifier | PK |
| TenantId | uniqueidentifier | |
| Name | nvarchar(200) | |
| SourceCategoryId | uniqueidentifier? | |
| TargetCategoryId | uniqueidentifier? | |
| Condition | json | for example `{"minPrice":100,"brandId":"..."}` |
| AssociationType | smallint | |
| DefaultScore | decimal(5,4) | |
| IsEnabled | bit | |
| Audit columns | | |

**AssociationScores** (output of the scoring job)
| Column | Type | Notes |
|---|---|---|
| Id | bigint | PK |
| TenantId | uniqueidentifier | |
| SourceProductId | uniqueidentifier | |
| TargetProductId | uniqueidentifier | |
| Support | decimal(8,6) | |
| Confidence | decimal(8,6) | |
| Lift | decimal(10,4) | |
| CoOccurrenceCount | int | |
| ComputedAt | datetimeoffset | |
| RunId | uniqueidentifier | FK to `ScoringRuns` |

**ScoringRuns**
| Column | Type | Notes |
|---|---|---|
| Id | uniqueidentifier | PK |
| TenantId | uniqueidentifier | |
| StartedAt / FinishedAt | datetimeoffset | |
| Status | smallint | Running, Succeeded, Failed |
| OrdersProcessed | int | |
| PairsWritten | int | |
| Error | nvarchar(max)? | |

**Bundles** and **BundleItems**
| Table | Columns |
|---|---|
| Bundles | Id, TenantId, Name, BundlePrice, ValidFrom, ValidTo, IsActive |
| BundleItems | BundleId, ProductId, Quantity (composite PK `BundleId, ProductId`) |

### 4.3 Orders

**Customers**
| Column | Type | Notes |
|---|---|---|
| Id | uniqueidentifier | PK |
| TenantId | uniqueidentifier | |
| ExternalRef | nvarchar(100) | unique per tenant |
| Email | nvarchar(256) | PII, encrypted or hashed |
| FullName | nvarchar(200) | PII |
| Address | json | complex type |
| AnonymizedAt | datetimeoffset? | GDPR |

**Orders**
| Column | Type | Notes |
|---|---|---|
| Id | uniqueidentifier | PK |
| TenantId | uniqueidentifier | |
| CustomerId | uniqueidentifier | FK |
| OrderDate | datetimeoffset | indexed, range-partition candidate |
| Status | smallint | |
| TotalAmount | decimal(18,2) | |

**OrderItems**
| Column | Type | Notes |
|---|---|---|
| Id | bigint | PK |
| OrderId | uniqueidentifier | FK, indexed |
| ProductId | uniqueidentifier | FK, indexed |
| Quantity | int | |
| UnitPrice | decimal(18,2) | |

### 4.4 Tracking and analytics

**RecommendationEvents**
| Column | Type | Notes |
|---|---|---|
| Id | bigint | PK |
| TenantId | uniqueidentifier | |
| SessionId | nvarchar(100) | |
| SourceProductId | uniqueidentifier | |
| RecommendedProductId | uniqueidentifier | |
| EventType | smallint | Impression, Click, AddToCart, Purchase |
| OccurredAt | datetimeoffset | partitioned by month |

### 4.5 Identity and security

| Table | Purpose |
|---|---|
| AspNetUsers, AspNetRoles, AspNetUserRoles, AspNetUserClaims, AspNetUserPasskeys | ASP.NET Core Identity (including passkeys) |
| RefreshTokens | Id, UserId, TokenHash, ExpiresAt, RevokedAt, ReplacedBy |
| ApiClients | Id, TenantId, Name, KeyHash, Scopes, RateLimitPerMinute, IsActive |

### 4.6 Infrastructure tables

| Table | Purpose |
|---|---|
| OutboxMessages | Id, Type, Payload (json), OccurredAt, ProcessedAt, Attempts, Error |
| InboxMessages | MessageId, Consumer, ProcessedAt (idempotent consumers) |
| IdempotencyKeys | Key, RequestHash, ResponseStatus, ResponseBody, CreatedAt, ExpiresAt |
| AuditLogs | Id, TenantId, UserId, EntityName, EntityId, Action, OldValues (json), NewValues (json), OccurredAt, CorrelationId |
| FeatureFlags | Key, TenantId?, IsEnabled, Rules (json) |
| ImportJobs | Id, FileName, Status, TotalRows, FailedRows, ErrorReportPath, CreatedBy |

### 4.7 ER overview

```
Tenant 1---* Category 1---* Product *---1 Brand
Product 1---* Association (as Source)
Product 1---* Association (as Target)
Product 1---* ProductAttribute (json)
Customer 1---* Order 1---* OrderItem *---1 Product
Product *---* Bundle (via BundleItem)
Association *---1 AssociationRule (optional origin)
ScoringRun 1---* AssociationScore
Product 1---* RecommendationEvent
```

### 4.8 SQL Server specific notes

| Topic | Guidance |
|---|---|
| Docker image | `mcr.microsoft.com/mssql/server:2025-latest` (or `2022-latest`) in `docker-compose.yml` |
| EF provider | `Microsoft.EntityFrameworkCore.SqlServer` |
| JSON columns | SQL Server 2025 / Azure SQL have a native `json` type that EF Core 10 maps to. On 2022 or older, use `nvarchar(max)` with EF's JSON mapping |
| Vectors (bonus) | Native `vector` type and EF Core 10 vector search need SQL Server 2025 or Azure SQL. Skip on 2022 |
| Concurrency | Use the native `rowversion` column (`[Timestamp]` / `IsRowVersion()`) |
| Strings | `nvarchar` for Unicode; add `Collation` deliberately (case-insensitive by default) |
| Indexes | Covering indexes with `INCLUDE`, filtered indexes, and columnstore indexes for `OrderItems` analytics |
| Recursive queries | `WITH cte AS (...)` without the `RECURSIVE` keyword; set `OPTION (MAXRECURSION n)` |
| Bulk loading | `SqlBulkCopy` for millions of rows; `MERGE` or staging table + `UPDATE` for upserts |
| Partitioning | Partition function + scheme on `Orders.OrderDate` and `RecommendationEvents.OccurredAt` |
| Tuning tools | SSMS actual execution plan, Query Store, `SET STATISTICS IO ON`, missing-index DMVs |
| Identity | `Guid.CreateVersion7()` (.NET 9+) gives sequential GUIDs and avoids clustered index fragmentation |

---

## 5. Project Structure

### 5.1 Level 1 to 3: modular monolith (Clean Architecture)

```
ProductLink/
├── ProductLink.slnx
├── Directory.Build.props
├── Directory.Packages.props          # central package management
├── global.json
├── .editorconfig
├── docker-compose.yml
├── src/
│   ├── ProductLink.Domain/
│   │   ├── Common/                   # Entity, AggregateRoot, ValueObject, IDomainEvent
│   │   ├── Catalog/                  # Product, Category, Brand
│   │   ├── Associations/             # Association, AssociationRule, enums
│   │   ├── Orders/                   # Order, OrderItem, Customer
│   │   └── Exceptions/
│   ├── ProductLink.Application/
│   │   ├── Abstractions/             # IAppDbContext, ICurrentUser, IClock, ICacheService
│   │   ├── Behaviors/                # validation, logging, transaction pipeline
│   │   ├── Catalog/                  # Commands, Queries, DTOs, Validators
│   │   ├── Associations/
│   │   ├── Recommendations/
│   │   ├── Orders/
│   │   ├── Reports/
│   │   └── DependencyInjection.cs
│   ├── ProductLink.Infrastructure/
│   │   ├── Persistence/              # DbContext, Configurations, Migrations, Interceptors
│   │   ├── Identity/
│   │   ├── Caching/                  # HybridCache + Redis
│   │   ├── Messaging/                # Outbox, MassTransit consumers
│   │   ├── Search/                   # OpenSearch client
│   │   ├── Files/                    # CSV/Excel import
│   │   └── DependencyInjection.cs
│   ├── ProductLink.Api/
│   │   ├── Endpoints/                # one file per feature group
│   │   ├── Middleware/               # exception handling, correlation id, idempotency
│   │   ├── Extensions/
│   │   ├── Program.cs
│   │   └── appsettings*.json
│   ├── ProductLink.Worker/           # scoring, outbox publisher, imports
│   └── ProductLink.AppHost/          # .NET Aspire orchestration
│       └── ProductLink.ServiceDefaults/
├── tests/
│   ├── ProductLink.Domain.Tests/
│   ├── ProductLink.Application.Tests/
│   ├── ProductLink.Api.IntegrationTests/     # Testcontainers
│   ├── ProductLink.Architecture.Tests/       # NetArchTest
│   ├── ProductLink.Benchmarks/               # BenchmarkDotNet
│   └── ProductLink.LoadTests/                # NBomber / k6
├── docs/
│   ├── adr/                          # architecture decision records
│   └── runbooks/
└── deploy/
    ├── docker/
    ├── k8s/
    └── helm/
```

### 5.2 Level 4: microservices split

```
services/
├── catalog-service/            # products, categories, brands
├── association-service/        # associations, rules, bundles
├── recommendation-service/     # read-optimized serving, cache, search
├── order-service/              # orders, customers, ingestion
├── scoring-service/            # market-basket computation (worker)
├── identity-service/           # users, tokens, API clients
├── gateway/                    # YARP
└── shared/
    ├── Contracts/              # integration events, gRPC protos
    └── BuildingBlocks/         # outbox, logging, telemetry, resilience
```

| Service | Owns | Publishes events | Consumes events |
|---|---|---|---|
| Catalog | Products, Categories, Brands | `ProductCreated/Updated/Deleted`, `StockChanged` | |
| Association | Associations, Rules, Bundles | `AssociationChanged` | `ProductDeleted`, `ScoresComputed` |
| Order | Orders, Customers | `OrderPlaced` | `ProductDeleted` |
| Scoring | AssociationScores, ScoringRuns | `ScoresComputed` | `OrderPlaced` |
| Recommendation | Read models, cache, search index | | `ProductUpdated`, `AssociationChanged`, `StockChanged` |
| Identity | Users, tokens, API clients | `UserCreated` | |

### 5.3 Layer dependency rules

```
Api -> Application -> Domain
Infrastructure -> Application -> Domain
Domain depends on nothing.
```
Enforced by architecture tests.

---

## 6. API Specification

Base URL: `/api/v{version}`. All responses use JSON; errors use RFC 9457 `ProblemDetails`. All write endpoints accept an optional `Idempotency-Key` header. List endpoints support `page`, `pageSize`, `sort`, and filters.

### 6.1 Authentication and users

| Method | Route | Description | Auth |
|---|---|---|---|
| POST | `/auth/register` | Register user | Anonymous / Admin |
| POST | `/auth/login` | Issue JWT + refresh token | Anonymous |
| POST | `/auth/refresh` | Rotate refresh token | Anonymous |
| POST | `/auth/logout` | Revoke refresh token | User |
| POST | `/auth/passkeys/register` | Register a passkey | User |
| POST | `/auth/passkeys/login` | Passkey sign-in | Anonymous |
| GET | `/users/me` | Current profile | User |
| GET | `/users` | List users | Admin |
| PUT | `/users/{id}/roles` | Assign roles | Admin |
| POST | `/api-clients` | Create API key | Admin |
| DELETE | `/api-clients/{id}` | Revoke API key | Admin |

### 6.2 Catalog

| Method | Route | Description | Auth |
|---|---|---|---|
| GET | `/products` | Paged list; filter by category, brand, price, status, search | Any |
| GET | `/products/{id}` | Details with attributes | Any |
| GET | `/products/by-sku/{sku}` | Lookup by SKU | Any |
| POST | `/products` | Create | Merchandiser |
| PUT | `/products/{id}` | Replace (requires `If-Match` row version) | Merchandiser |
| PATCH | `/products/{id}` | Partial update | Merchandiser |
| DELETE | `/products/{id}` | Soft delete | Merchandiser |
| POST | `/products/{id}/restore` | Undo soft delete | Admin |
| PATCH | `/products/{id}/stock` | Adjust stock | Merchandiser |
| GET | `/products/{id}/history` | Audit trail | Analyst |
| POST | `/products/bulk-update` | `ExecuteUpdate` bulk change | Merchandiser |
| GET | `/categories` | Tree or flat list | Any |
| GET | `/categories/{id}` | Details + children | Any |
| POST/PUT/DELETE | `/categories[/{id}]` | Manage | Merchandiser |
| GET/POST/PUT/DELETE | `/brands[/{id}]` | Manage brands | Merchandiser |
| GET | `/search/products?q=` | Full-text/fuzzy search (OpenSearch) | Any |

### 6.3 Associations

| Method | Route | Description | Auth |
|---|---|---|---|
| GET | `/associations` | Paged; filter by source, target, type, active, source kind | Merchandiser |
| GET | `/associations/{id}` | Details | Merchandiser |
| POST | `/associations` | Create (validates business rules) | Merchandiser |
| PUT | `/associations/{id}` | Update score, validity, priority | Merchandiser |
| DELETE | `/associations/{id}` | Delete | Merchandiser |
| POST | `/associations/bulk` | Create many in one call | Merchandiser |
| POST | `/associations/import` | CSV/Excel import (returns import job id) | Merchandiser |
| GET | `/associations/export` | Export CSV (streamed) | Analyst |
| GET | `/products/{id}/associations` | Associations for a product, grouped by type | Any |
| GET | `/associations/orphans` | Products with no associations (left join) | Analyst |
| GET | `/associations/stale` | Products whose associations are all expired | Analyst |
| GET | `/associations/conflicts` | Detect cycles or contradictions (for example, substitute and upsell for same pair) | Analyst |

### 6.4 Rules and bundles

| Method | Route | Description | Auth |
|---|---|---|---|
| GET/POST | `/rules` | List/create rules | Merchandiser |
| GET/PUT/DELETE | `/rules/{id}` | Manage rule | Merchandiser |
| POST | `/rules/{id}/preview` | Show which associations the rule would create | Merchandiser |
| POST | `/rules/{id}/apply` | Run rule now (enqueues a job) | Merchandiser |
| GET/POST | `/bundles` | List/create bundles | Merchandiser |
| GET/PUT/DELETE | `/bundles/{id}` | Manage bundle | Merchandiser |
| POST | `/bundles/{id}/items` | Add item | Merchandiser |
| DELETE | `/bundles/{id}/items/{productId}` | Remove item | Merchandiser |

### 6.5 Recommendations (read-optimized, cached)

| Method | Route | Description | Auth |
|---|---|---|---|
| GET | `/recommendations/product/{id}` | Top N recommendations; `?type=&limit=&excludeOutOfStock=` | API Client |
| GET | `/recommendations/cart` | Recommendations for a set of products (`?productIds=`) | API Client |
| GET | `/recommendations/customer/{id}` | Personalized, based on order history | API Client |
| GET | `/recommendations/similar/{id}` | Vector similarity (bonus) | API Client |
| GET | `/recommendations/stream/{id}` | Server-Sent Events with live updates | API Client |
| POST | `/recommendations/events` | Track impressions/clicks (batched, fire and forget) | API Client |

### 6.6 Orders and ingestion

| Method | Route | Description | Auth |
|---|---|---|---|
| GET | `/orders` | Paged list | Analyst |
| GET | `/orders/{id}` | Details | Analyst |
| POST | `/orders` | Create (idempotent) | API Client |
| POST | `/orders/import` | Bulk CSV import | Admin |
| GET | `/customers/{id}` | Customer details | Analyst |
| POST | `/customers/{id}/export` | GDPR data export | Admin |
| DELETE | `/customers/{id}` | GDPR anonymize | Admin |

### 6.7 Analytics and reports

| Method | Route | Description |
|---|---|---|
| GET | `/reports/associations/summary` | Count, active, average score by type |
| GET | `/reports/associations/top` | Top-N associations per product |
| GET | `/reports/frequently-bought-together` | Co-occurrence pairs (support threshold) |
| GET | `/reports/basket-metrics` | Support, confidence, lift per pair |
| GET | `/reports/products/coverage` | % products with at least one active association |
| GET | `/reports/recommendations/ctr` | Click-through and conversion rate by association type |
| GET | `/reports/revenue-attribution` | Revenue attributed to recommendations |
| GET | `/reports/sales/trend` | Window-function sales trend (running totals, LAG) |
| GET | `/reports/substitute-graph/{id}?depth=3` | Recursive CTE traversal |

### 6.8 Scoring and jobs

| Method | Route | Description | Auth |
|---|---|---|---|
| POST | `/scoring/runs` | Trigger scoring (returns 202 + run id) | Admin |
| GET | `/scoring/runs` | List runs | Analyst |
| GET | `/scoring/runs/{id}` | Run status and statistics | Analyst |
| GET | `/imports/{id}` | Import job status | Merchandiser |
| GET | `/imports/{id}/errors` | Download error report | Merchandiser |

### 6.9 Administration and operations

| Method | Route | Description | Auth |
|---|---|---|---|
| GET/POST/PUT | `/tenants[/{id}]` | Manage tenants | Admin |
| GET | `/audit-logs` | Search audit trail | Admin |
| GET/PUT | `/feature-flags[/{key}]` | Toggle features | Admin |
| POST | `/cache/invalidate` | Invalidate cache by tag | Admin |
| GET | `/health/live` | Liveness | Anonymous |
| GET | `/health/ready` | Readiness (DB, Redis, broker) | Anonymous |
| GET | `/metrics` | Prometheus scrape (or OTLP) | Internal |

### 6.10 Standard behaviors

| Concern | Rule |
|---|---|
| Versioning | URL segment (`/api/v1`), header fallback; deprecation via `Sunset` header |
| Pagination | Offset (`page`, `pageSize`) for admin, keyset (`cursor`) for large feeds |
| Concurrency | `ETag` / `If-Match` with row version; `412 Precondition Failed` on mismatch |
| Idempotency | `Idempotency-Key` header on POST; replay returns the stored response |
| Rate limiting | Fixed window per IP, token bucket per API client |
| Errors | `400` validation, `401`, `403`, `404`, `409` conflict, `412`, `422`, `429`, `500`, all as ProblemDetails with `traceId` |
| Correlation | `X-Correlation-Id` accepted and returned, propagated to logs and events |

---

## 7. Key Algorithms and Complex Queries

| # | Feature | Technique |
|---|---|---|
| 1 | Frequently bought together | Self-join on `OrderItems`, group by pair, filter by support |
| 2 | Market-basket metrics | Support, confidence, lift computed in the Worker (raw SQL + `MERGE`/batch upsert) |
| 3 | Top-N per product | `GroupBy` + ordered `Take`, then window function `ROW_NUMBER()` in SQL |
| 4 | Orphan products | EF Core 10 `LeftJoin` |
| 5 | All-expired associations | Correlated `Any()` / `All()` |
| 6 | Dynamic search/filter | Composable `IQueryable` with specification pattern |
| 7 | Substitute graph | Recursive CTE via `FromSql` |
| 8 | Sales trend | Window functions through keyless entity or view |
| 9 | Personalized recommendations | Customer history, then associations, then exclude already purchased, then rank |
| 10 | Recommendation ranking | Blend of score, priority, stock, margin, and recency, configurable |
| 11 | Hot path reads | Compiled queries + HybridCache with tag invalidation |
| 12 | Bulk maintenance | `ExecuteUpdate` / `ExecuteDelete`, `SqlBulkCopy` batch insert |

---

## 8. Non-Functional Requirements

| Area | Target |
|---|---|
| Performance | p95 `GET /recommendations/product/{id}` under 50 ms (cache hit), under 200 ms (miss) |
| Throughput | 1,000 requests per second on a single node for recommendations |
| Data volume | 100k products, 5M orders, 20M order items |
| Availability | 99.9% for recommendation reads |
| Security | OWASP API Top 10 reviewed; secrets outside source control; PII encrypted at rest |
| Observability | Traces, metrics, structured logs with correlation ids; dashboards and alerts |
| Testing | 80%+ coverage on Domain and Application; integration tests for every endpoint |
| Maintainability | Architecture tests, analyzers, central package management |

---

## 9. Task Breakdown

Legend: `[ ]` to do. Estimates are for one developer learning as they go.

### Phase 0: Setup (2 to 3 days)

- [ ] 0.1 Install .NET 10 SDK, IDE, Docker, Git
- [ ] 0.2 Create repo, `.gitignore`, `.editorconfig`, `global.json`
- [ ] 0.3 Create `ProductLink.slnx` and the first project
- [ ] 0.4 Add `Directory.Build.props` (nullable, warnings as errors, analyzers)
- [ ] 0.5 Add `Directory.Packages.props` (central package management)
- [ ] 0.6 `docker-compose.yml` with SQL Server and Redis
- [ ] 0.7 Create `docs/adr/0001-record-architecture-decisions.md`
- [ ] 0.8 Configure Git branching and tagging convention

### Level 1: Fundamentals (1 to 2 weeks), tag `v1-crud`

**Domain and data**
- [ ] 1.1 Define entities: Product, Category, Brand, Association
- [ ] 1.2 Use C# 14 features where natural (`field` keyword, extension members, null-conditional assignment)
- [ ] 1.3 `AppDbContext` and Fluent API configurations (`IEntityTypeConfiguration`)
- [ ] 1.4 Value converters, enum-to-string, decimal precision
- [ ] 1.5 Initial migration, apply to SQL Server
- [ ] 1.6 Seed data (categories, brands, 200 products, 500 associations)

**API**
- [ ] 1.7 Minimal API endpoint groups for Products
- [ ] 1.8 Endpoint groups for Categories and Brands
- [ ] 1.9 Endpoint groups for Associations (with business rule validation)
- [ ] 1.10 Request/response DTOs and manual mapping
- [ ] 1.11 Validation (built-in Minimal API validation, then FluentValidation)
- [ ] 1.12 Global error handling with `IExceptionHandler` and `ProblemDetails`
- [ ] 1.13 Built-in OpenAPI 3.1 and Scalar UI
- [ ] 1.14 Options pattern for settings, `ILogger` usage
- [ ] 1.15 Rebuild one resource with Controllers to compare
- [ ] 1.16 Basic unit tests for entity rules

### Level 2: Real-world API (2 to 3 weeks), tag `v2-auth`

**Structure**
- [ ] 2.1 Refactor into Domain / Application / Infrastructure / Api projects
- [ ] 2.2 Service layer or CQRS handlers; decide repository vs direct `DbContext` and record in an ADR
- [ ] 2.3 Mapperly for mapping
- [ ] 2.4 Result pattern for expected failures

**Data access**
- [ ] 2.5 Pagination, filtering, sorting (offset and keyset)
- [ ] 2.6 `AsNoTracking`, projections with `Select`, `AsSplitQuery`
- [ ] 2.7 Soft delete using EF Core 10 named query filters
- [ ] 2.8 Complex types and JSON columns for attributes and address
- [ ] 2.9 Audit interceptor (`SaveChangesInterceptor`) filling audit columns
- [ ] 2.10 Optimistic concurrency with row version and `ETag`

**Security and API hygiene**
- [ ] 2.11 ASP.NET Core Identity, JWT, refresh tokens
- [ ] 2.12 Roles and policy-based authorization
- [ ] 2.13 Passkey registration and sign-in
- [ ] 2.14 API versioning
- [ ] 2.15 Rate limiting, CORS, response compression, output caching
- [ ] 2.16 Serilog with structured logging and correlation id middleware

**Testing**
- [ ] 2.17 xUnit unit tests for Application layer (NSubstitute)
- [ ] 2.18 Integration tests with `WebApplicationFactory` and Testcontainers
- [ ] 2.19 Test data builders and shared fixtures

### Level 3: Advanced and async (3 to 4 weeks), tag `v3-performance`

**Orders and analytics**
- [ ] 3.1 Add Customer, Order, OrderItem entities and migration
- [ ] 3.2 Data generator for 5M orders; load with bulk copy
- [ ] 3.3 Implement complex queries 1 to 5 from section 7
- [ ] 3.4 Read generated SQL for each, inspect execution plans, add indexes
- [ ] 3.5 Recursive CTE and window-function reports via `FromSql` / keyless entities
- [ ] 3.6 Compiled queries on hot paths
- [ ] 3.7 BenchmarkDotNet: LINQ vs raw SQL vs Dapper

**Caching and performance**
- [ ] 3.8 `HybridCache` with Redis, tag-based invalidation
- [ ] 3.9 Cache stampede protection and cache metrics

**Background work**
- [ ] 3.10 `ProductLink.Worker` project, `BackgroundService` basics
- [ ] 3.11 Market-basket scoring job (support, confidence, lift)
- [ ] 3.12 Hangfire or Quartz for scheduling, retries, dashboards
- [ ] 3.13 Channels pipeline for CSV import (producer/consumer)
- [ ] 3.14 `IAsyncEnumerable` streaming export endpoint
- [ ] 3.15 Server-Sent Events for live recommendation updates
- [ ] 3.16 `ExecuteUpdate` / `ExecuteDelete` bulk operations

**Reliability**
- [ ] 3.17 Idempotency-key middleware and table
- [ ] 3.18 Outbox pattern (write event and data in one transaction, publisher worker)
- [ ] 3.19 Health checks (live and ready)
- [ ] 3.20 OpenTelemetry traces, metrics, logs, exported to Jaeger/Prometheus/Grafana
- [ ] 3.21 .NET Aspire AppHost and ServiceDefaults

### Level 4: Enterprise architecture (4 to 6 weeks), tag `v4-enterprise`

**Design**
- [ ] 4.1 Domain-driven design: aggregates, value objects, domain events
- [ ] 4.2 CQRS with MediatR or hand-written dispatcher; pipeline behaviors (validation, logging, transaction)
- [ ] 4.3 Try a Vertical Slice version of one module and compare in an ADR
- [ ] 4.4 Architecture tests with NetArchTest

**Cross-cutting**
- [ ] 4.5 Multi-tenancy (tenant resolution, named query filters, per-tenant cache keys)
- [ ] 4.6 Audit log API with search
- [ ] 4.7 Feature flags (`Microsoft.FeatureManagement`)
- [ ] 4.8 GDPR export and anonymize flows

**Distributed system**
- [ ] 4.9 Split into Catalog, Association, Order, Scoring, Recommendation, Identity services
- [ ] 4.10 Message broker (RabbitMQ/Kafka) with MassTransit or Wolverine
- [ ] 4.11 Integration events, inbox pattern, idempotent consumers
- [ ] 4.12 Retry, dead-letter queue, poison message handling
- [ ] 4.13 Saga or choreography example (order placed, scored, cache refreshed)
- [ ] 4.14 YARP gateway with auth, rate limits, and routing
- [ ] 4.15 gRPC between Recommendation and Catalog services
- [ ] 4.16 Resilience: timeouts, retries, circuit breaker via `Microsoft.Extensions.Http.Resilience`
- [ ] 4.17 OpenSearch index, sync consumer, `/search/products` endpoint
- [ ] 4.18 Contract tests between services

### Level 5: Production and DevOps (2 to 3 weeks), tag `v5-production`

- [ ] 5.1 Multi-stage Dockerfiles, non-root user, small images
- [ ] 5.2 Docker Compose for the full stack
- [ ] 5.3 Kubernetes manifests, then Helm chart (probes, resources, HPA)
- [ ] 5.4 GitHub Actions: build, test, analyze, Docker publish
- [ ] 5.5 Static analysis (Roslyn analyzers, SonarQube or CodeQL), dependency scanning
- [ ] 5.6 Migration bundles in CI/CD; practice expand/contract migration
- [ ] 5.7 Secrets management (User Secrets, Key Vault or equivalent)
- [ ] 5.8 Native AOT build of one lightweight service
- [ ] 5.9 Load tests (k6 or NBomber) and performance budget
- [ ] 5.10 Mutation testing (Stryker.NET) on the Domain layer
- [ ] 5.11 Dashboards, alerts, SLOs, and a runbook
- [ ] 5.12 Chaos exercise: kill broker/DB/Redis mid-test and debug with telemetry only

### Bonus: AI features (1 to 2 weeks), tag `v6-ai`

- [ ] 6.1 `Microsoft.Extensions.AI` embedding generation for products
- [ ] 6.2 Store vectors; EF Core 10 vector search for similar products
- [ ] 6.3 `/recommendations/similar/{id}` endpoint
- [ ] 6.4 Natural-language association suggestions from an LLM, behind a feature flag
- [ ] 6.5 MCP server exposing catalog and associations to AI agents

### Brownfield and hardening (ongoing)

- [ ] 7.1 Refactor v1 code without breaking tests
- [ ] 7.2 Deprecate API v1 using `Sunset` header and migrate clients to v2
- [ ] 7.3 OWASP API Top 10 review and fix list
- [ ] 7.4 Write a postmortem for each failure you induce
- [ ] 7.5 Keep ADRs current; write a final architecture overview

---

## 10. Milestones and Timeline

| Milestone | Tag | Approx. duration | Outcome |
|---|---|---|---|
| M0 Setup | `v0-setup` | 2 to 3 days | Repo, tooling, containers |
| M1 CRUD API | `v1-crud` | 1 to 2 weeks | Working catalog and association API |
| M2 Real-world API | `v2-auth` | 2 to 3 weeks | Layered, secured, tested |
| M3 Performance and async | `v3-performance` | 3 to 4 weeks | Fast, cached, observable, background jobs |
| M4 Enterprise | `v4-enterprise` | 4 to 6 weeks | Microservices, messaging, search |
| M5 Production | `v5-production` | 2 to 3 weeks | Containerized, CI/CD, load-tested |
| M6 AI (bonus) | `v6-ai` | 1 to 2 weeks | Vector search and MCP |

Total: roughly 4 to 5 months part-time.

---

## 11. Definition of Done (per task)

1. Code compiles with zero warnings.
2. Unit and integration tests pass.
3. Endpoint documented in OpenAPI with examples.
4. Logs, metrics, and traces cover the new code path.
5. Generated SQL reviewed for new queries.
6. Migration reviewed and reversible where possible.
7. ADR written for any significant design decision.
8. Commit message follows convention; tag created at milestone end.

---

## 12. Risks and Learning Notes

| Risk | Mitigation |
|---|---|
| Scope too large | Finish each level before starting the next; tag releases |
| Over-engineering early | Keep Level 1 as a single project; refactor only at Level 2 |
| Microservices complexity | Only split after the monolith is stable and well tested |
| In-memory test provider hides bugs | Use Testcontainers with the real database |
| Slow queries at scale | Seed millions of rows early and read execution plans |

---

## Appendix A: Suggested NuGet packages

| Purpose | Package |
|---|---|
| EF Core | `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.SqlServer` |
| Validation | `FluentValidation.DependencyInjectionExtensions` |
| Mapping | `Riok.Mapperly` |
| OpenAPI | `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore` |
| Logging | `Serilog.AspNetCore`, `Serilog.Sinks.Seq` |
| Caching | `Microsoft.Extensions.Caching.Hybrid`, `Microsoft.Extensions.Caching.StackExchangeRedis` |
| Jobs | `Hangfire`, or `Quartz.Extensions.Hosting` |
| Messaging | `MassTransit.RabbitMQ`, or `WolverineFx` |
| Gateway | `Yarp.ReverseProxy` |
| Resilience | `Microsoft.Extensions.Http.Resilience` |
| Telemetry | `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.*` |
| Feature flags | `Microsoft.FeatureManagement.AspNetCore` |
| AI | `Microsoft.Extensions.AI` |
| Testing | `xunit`, `NSubstitute`, `Testcontainers.MsSql`, `Microsoft.AspNetCore.Mvc.Testing`, `NetArchTest.Rules`, `BenchmarkDotNet`, `NBomber` |
| Bulk and raw SQL | `Dapper`, `EFCore.BulkExtensions` (optional) |

## Appendix B: Naming and conventions

- Namespaces mirror folders; one public type per file.
- Endpoints: plural nouns, kebab-case routes, `{id:guid}` constraints.
- Commands and queries: `CreateProductCommand`, `GetProductByIdQuery`.
- Integration events: past tense (`ProductUpdated`), versioned contracts in `Contracts`.
- Conventional commits (`feat:`, `fix:`, `refactor:`, `test:`, `docs:`).