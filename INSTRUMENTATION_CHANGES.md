# Instrumentation Changes, Task 2
## Flow: Customer Searches and Views a Product

This document tracks every file created or modified as part of the OpenTelemetry
instrumentation for the Catalogue, Search, Pricing flow.

---

## New Files

### `src/Libraries/Nop.Core/Telemetry/NopActivitySource.cs`
A single static `ActivitySource` instance shared across every layer of the
application. Lives in `Nop.Core` because that project has no outbound
dependencies, meaning `Nop.Data` and `Nop.Services` can both
use it without adding a reference to any OTel SDK package. `ActivitySource`
itself is part of `System.Diagnostics` in the .NET base class library, so
no NuGet package is needed at this level.

### `src/Libraries/Nop.Core/Telemetry/NopMetrics.cs`
Defines the two custom metrics for this flow using `System.Diagnostics.Metrics`
(also part of the BCL, no extra dependency). The OTel SDK hooks into this
`Meter` at startup.

**`catalog.search.result_count`** (Histogram)
Records the number of products returned by each search query, tagged with
`has_keyword` and `has_category`. A sustained shift toward zero in this
distribution, without any HTTP errors, is an early signal that the catalog
has a silent availability problem (mass product deactivation, search provider
failure, ACL misconfiguration). An operator can alert when the p50 drops below
a threshold before customers start complaining.

**`catalog.price.discount_calculations`** (Counter)
Counts every price calculation, tagged `discount_applied=true/false`. During
an active promotional campaign, every eligible product calculation should
produce `discount_applied=true`. If that rate drops to zero while a sale is
supposed to be live, the discount configuration is broken. This metric catches
that failure before the first customer support ticket because pricing logic
errors don't produce HTTP errors.

### `src/Presentation/Nop.Web.Framework/Infrastructure/ObservabilityStartup.cs`
Implements `INopStartup` at `Order = -5`, which means it runs before every
other startup class. This registers the full OpenTelemetry SDK:
- ASP.NET Core instrumentation (inbound HTTP request spans)
- HttpClient instrumentation (outbound call spans to payment gateways, shipping providers, etc.)
- .NET runtime metrics (GC, thread pool)
- OTLP exporter pointing at the OTel Collector

No existing files are modified by this class. The `INopStartup` discovery
mechanism picks it up automatically via reflection.

### `src/Presentation/Nop.Web.Framework/Telemetry/PiiSanitizingProcessor.cs`
An OTel `BaseProcessor<Activity>` that runs before export and removes:
- Any tag whose key matches a known-sensitive pattern (e.g. `email`, `password`, `card`, `token`)
- Any tag whose string value contains an email address pattern

nopCommerce passes full `Customer` and `Order` objects through its service
layer. Rather than trying to remember which fields are safe at every
instrumentation point, this single processor enforces the PII boundary
centrally. It is registered as the last processor in the pipeline so it
applies regardless of which exporter is used.

### `src/Presentation/Nop.Web/App_Data/appsettings.json`
OTel endpoint and service name configuration read by `ObservabilityStartup`.
The config key is `OpenTelemetry:OtlpEndpoint`.

Note: nopCommerce loads configuration exclusively from `App_Data/appsettings.json`,
not from the standard `appsettings.json` at the project root. Placing the block
in the wrong file causes silent fallback to defaults and no data is exported.

The OTLP transport is HTTP/protobuf (port 4318), not gRPC (port 4317). gRPC
requires the `Grpc.Net.Client` native channel to be available at runtime;
HTTP/protobuf works without it and is more predictable across environments.

```json
{
  "OpenTelemetry": {
    "ServiceName": "nopCommerce",
    "OtlpEndpoint": "http://localhost:4318"
  }
}
```

### `observability/docker-compose.yml`
Brings up the full observability stack:
- **MySQL 8.4**, application database, port 3306, credentials `nop/noppassword`
- **OTel Collector**, receives OTLP on port 4317 (gRPC) and 4318 (HTTP), fans out to Jaeger and Prometheus
- **Jaeger**, trace storage and UI at `http://localhost:16686`
- **Prometheus**, scrapes metrics from the collector at `http://localhost:9090`
- **Grafana**, dashboards at `http://localhost:3000` (admin/admin), with Prometheus and Jaeger auto-provisioned as data sources

The `version` field is intentionally absent (removed) because recent Docker Compose versions print an obsolete warning if it is present.

### `observability/otel-collector-config.yaml`
OTel Collector pipeline configuration. Receives OTLP over gRPC and HTTP,
batches signals, forwards traces to Jaeger and metrics to a Prometheus scrape
endpoint.

### `observability/prometheus.yml`
Prometheus scrape config. Targets the collector's Prometheus exposition
endpoint every 10 seconds.

### `observability/grafana/provisioning/datasources/datasources.yaml`
Auto-provisions Prometheus and Jaeger as Grafana data sources on first start,
so no manual setup is needed.

### `observability/grafana/provisioning/dashboards/dashboards.yaml`
Tells Grafana to load dashboard JSON files from the provisioning directory.

---

## Modified Files

### `src/Presentation/Nop.Web.Framework/Nop.Web.Framework.csproj`
Added five OTel SDK NuGet packages:

| Package | Version | Purpose |
|---|---|---|
| `OpenTelemetry.Extensions.Hosting` | 1.10.0 | `.AddOpenTelemetry()` hosting integration |
| `OpenTelemetry.Instrumentation.AspNetCore` | 1.10.1 | Automatic inbound HTTP spans |
| `OpenTelemetry.Instrumentation.Http` | 1.10.1 | Automatic outbound HttpClient spans |
| `OpenTelemetry.Instrumentation.Runtime` | 1.10.0 | .NET runtime metrics (GC, threads) |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 1.10.0 | OTLP export to collector |

Also added `OpenTelemetry.Exporter.Console` (1.10.0) as a temporary debug
exporter. This prints spans to stdout so it is possible to confirm the SDK
is active without needing the collector to be reachable. It should be removed
once the pipeline is confirmed working end-to-end.

These packages only live in `Nop.Web.Framework` because that is where
`ObservabilityStartup` lives and where the SDK is configured. No other
project needs them.

### `src/Libraries/Nop.Data/EntityRepository.cs`
Added `using System.Diagnostics` and `using Nop.Core.Telemetry`.

Added activity spans in four places:

**`GetByIdAsync`**, span created inside the `getEntityAsync` local function,
which is only invoked on a cache miss. This means cached hits produce no span
noise; you only see a DB span when the database is actually queried. Tags:
`db.entity_type`, `db.entity_id`.

**`InsertAsync`**, span wrapping `_dataProvider.InsertEntityAsync`. Tags:
`db.entity_type`.

**`UpdateAsync`**, span wrapping `_dataProvider.UpdateEntityAsync`. Tags:
`db.entity_type`, `db.entity_id`.

**`DeleteAsync`**, span wrapping the soft-delete or hard-delete path. Tags:
`db.entity_type`, `db.entity_id`.

`EntityRepository<TEntity>` is the single data access choke point; all 40+
service domains go through it, so these four changes give DB-level visibility
across the entire application.

### `src/Libraries/Nop.Services/Catalog/ProductService.cs`
Added `using System.Diagnostics` and `using Nop.Core.Telemetry`.

**`SearchProductsAsync`**, two changes:

1. An activity span is started at the top of the method with tags:
   - `search.has_keyword`, whether a text query was present (the keyword
     value itself is never tagged, as search terms can contain personal names)
   - `search.has_category_filter`, `search.has_price_filter`
   - `search.page_index`, `search.page_size`
   - `search.result_count`, set after the query executes

2. The two `return` statements at the end of the method (one for
   plugin-sorted results, one for standard results) were merged into a single
   variable so the result count can be recorded in one place before returning.
   The `catalog.search.result_count` histogram is recorded here.

### `src/Libraries/Nop.Services/Catalog/PriceCalculationService.cs`
Added `using System.Diagnostics` and `using Nop.Core.Telemetry`.

**`GetFinalPriceAsync`**, two changes:

1. An activity span is started at the top of the method with tags:
   - `price.product_id`
   - `price.include_discounts`
   - `price.quantity`
   - `price.discount_applied` and `price.discount_amount`, set after the
     cache/calculation resolves

2. The `catalog.price.discount_calculations` counter is incremented before
   the return, tagged with whether a discount was actually applied.

---

## What Was Deliberately Not Changed

**`DefaultLogger.cs`**, nopCommerce's custom database logger has no relationship
to `Microsoft.Extensions.Logging`, so it is invisible to the OTel log pipeline.
Bridging it would require modifying the logger to dual-write into MEL, which is
invasive. The traces provide enough signal for the assignment's purposes; the
log gap is documented in the architecture analysis.

**Plugin files**, payment and shipping plugins are separate assemblies.
Their internal logic is not instrumented directly. The HttpClient
instrumentation in `ObservabilityStartup` captures the outbound network calls
at the transport layer, which gives latency and error visibility without
touching plugin code.

**`NopStartup.cs`**, no service registrations were changed. `ObservabilityStartup`
runs at Order -5 before `NopStartup` (Order 2000), so the OTel SDK is fully
configured before any business service is resolved.

---

## Bugs Fixed During Integration

### Grafana metric name prefix mismatch
The OTel Collector's Prometheus exporter is configured with `namespace: nopcommerce`,
which prepends `nopcommerce_` to every metric name it exposes. The initial
dashboard used bare names for standard ASP.NET Core metrics
(`http_server_request_duration_seconds_count`) which returned no data. Fixed by
prefixing all HTTP metric queries with `nopcommerce_`:

- `http_server_request_duration_seconds_count` becomes `nopcommerce_http_server_request_duration_seconds_count`
- `http_server_request_duration_seconds_bucket` becomes `nopcommerce_http_server_request_duration_seconds_bucket`

Custom metrics (`nopcommerce_catalog_*`) were already correct because they were
named with the prefix from the start.

### gRPC OTLP not working, switched to HTTP/protobuf
The initial exporter used gRPC on port 4317. gRPC requires a native channel
that was not available in the runtime environment, causing silent export failures.
Switched both the trace and metrics OTLP exporters to HTTP/protobuf on port 4318,
which has no native dependency.

### OTel config in wrong appsettings file
The `OpenTelemetry` config block was initially placed in
`src/Presentation/Nop.Web/appsettings.json`. nopCommerce does not load that
file at runtime; it loads exclusively from `App_Data/appsettings.json`.
Moved the block to the correct file.

