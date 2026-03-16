# Testing the Instrumentation

## 1. Build
```bash
cd AS-nopCommerce/src
dotnet restore && dotnet build --no-restore
```

## 2. Start observability stack
```bash
cd AS-nopCommerce/observability
docker compose up -d
```
- Jaeger → http://localhost:16686
- Prometheus → http://localhost:9090
- Grafana → http://localhost:3000 (admin / admin)

## 3. Run the app
```bash
cd AS-nopCommerce/src
dotnet run --project Presentation/Nop.Web/Nop.Web.csproj
```

## 4. Generate traffic
```bash
curl "http://localhost:5000/search?q=computer"
curl "http://localhost:5000/search?q=zzzznotaproduct"
curl "http://localhost:5000/product/1"
```

## 5. Check traces
Jaeger → select service **nopCommerce** → Find Traces.
Look for spans: `catalog.search.products`, `db.Product.GetById`, `catalog.price.calculate`.

## 6. Check metrics
Prometheus query:
```
nopcommerce_catalog_search_result_count_bucket
nopcommerce_catalog_price_discount_calculations_total
```
