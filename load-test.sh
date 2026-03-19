#!/usr/bin/env bash
# Load test for the "Customer searches and views a product" flow.
#
# Simulates three concurrent user types hitting the nopCommerce storefront:
#   - Searchers:  type a query and browse the result page
#   - Browsers:   open a product page directly (triggers price calculation)
#   - Wanderers:  hit a category page (triggers search with no keyword)
#
# Usage:
#   ./load-test.sh [BASE_URL] [DURATION_SECONDS] [CONCURRENCY]
#
# Examples:
#   ./load-test.sh                                   # defaults: localhost:5000, 60s, 5 workers
#   ./load-test.sh http://localhost:5000 120 10       # 120s, 10 workers
#
# Requirements: bash, curl (standard on any Linux system)

BASE_URL="${1:-http://localhost:5000}"
DURATION="${2:-60}"
CONCURRENCY="${3:-5}"

# Product slugs that have a 20% discount applied (products 1-5)
DISCOUNTED_PRODUCTS=(
  "build-your-own-computer"
  "digital-storm-vanquish-custom-performance-pc"
  "lenovo-ideacentre"
  "apple-macbook-pro"
  "asus-laptop"
)

# Regular product slugs (no discount, exercises the false branch of the counter)
OTHER_PRODUCTS=(
  "apple-iphone-16-128gb"
  "beats-pill-wireless-speaker"
  "camera"
  "book"
  "50-physical-gift-card"
  "awesome"
  "compact"
  "apple-ipad"
)

# Search queries — mix of keyword and empty (category-style)
SEARCH_QUERIES=(
  "laptop"
  "apple"
  "computer"
  "phone"
  "camera"
  "book"
  ""
  "gift"
)

# Category pages (triggers SearchProductsAsync with has_keyword=false)
CATEGORY_PAGES=(
  "apparel"
  "books"
  "computer"
  "cell"
  "apple"
  "camera"
  "apparel-2"
)

END_TIME=$(( $(date +%s) + DURATION ))

# Worker function — runs one virtual user loop until END_TIME
worker() {
  local worker_id="$1"
  local requests=0
  local errors=0

  while [ "$(date +%s)" -lt "$END_TIME" ]; do
    # Pick a random behaviour weighted toward product views
    local roll=$(( RANDOM % 10 ))

    if [ "$roll" -lt 3 ]; then
      # 30% chance: search with a keyword
      local q="${SEARCH_QUERIES[$(( RANDOM % ${#SEARCH_QUERIES[@]} ))]}"
      local url="$BASE_URL/search?q=$(python3 -c "import urllib.parse; print(urllib.parse.quote('$q'))" 2>/dev/null || echo "$q")&pagesize=6"
      STATUS=$(curl -s -o /dev/null -w "%{http_code}" --max-time 10 "$url")

    elif [ "$roll" -lt 5 ]; then
      # 20% chance: browse a category page
      local slug="${CATEGORY_PAGES[$(( RANDOM % ${#CATEGORY_PAGES[@]} ))]}"
      STATUS=$(curl -s -o /dev/null -w "%{http_code}" --max-time 10 "$BASE_URL/$slug")

    elif [ "$roll" -lt 8 ]; then
      # 30% chance: view a discounted product (discount_applied=true)
      local slug="${DISCOUNTED_PRODUCTS[$(( RANDOM % ${#DISCOUNTED_PRODUCTS[@]} ))]}"
      STATUS=$(curl -s -o /dev/null -w "%{http_code}" --max-time 10 "$BASE_URL/$slug")

    else
      # 20% chance: view a non-discounted product (discount_applied=false)
      local slug="${OTHER_PRODUCTS[$(( RANDOM % ${#OTHER_PRODUCTS[@]} ))]}"
      STATUS=$(curl -s -o /dev/null -w "%{http_code}" --max-time 10 "$BASE_URL/$slug")
    fi

    requests=$(( requests + 1 ))
    if [ "$STATUS" != "200" ]; then
      errors=$(( errors + 1 ))
    fi

    # Small random think time between requests (100ms - 300ms)
    sleep "0.$(( 1 + RANDOM % 2 ))"
  done

  echo "worker $worker_id finished: $requests requests, $errors errors"
}

# Startup
echo "================================================"
echo " nopCommerce load test"
echo " Target:      $BASE_URL"
echo " Duration:    ${DURATION}s"
echo " Concurrency: $CONCURRENCY workers"
echo "================================================"

# Check the app is reachable before starting
if ! curl -s -o /dev/null -w "" --max-time 5 "$BASE_URL" 2>/dev/null; then
  echo "ERROR: $BASE_URL is not reachable. Start the app first with 'make run'."
  exit 1
fi

echo "Starting workers..."

# Launch workers in background
for i in $(seq 1 "$CONCURRENCY"); do
  worker "$i" &
done

# Progress indicator
ELAPSED=0
while [ "$ELAPSED" -lt "$DURATION" ]; do
  sleep 5
  ELAPSED=$(( ELAPSED + 5 ))
  REMAINING=$(( DURATION - ELAPSED ))
  printf "\r  [%ds elapsed, %ds remaining]  " "$ELAPSED" "$REMAINING"
done

# Wait for all workers to finish
wait

echo ""
echo "================================================"
echo " Done. Check results:"
echo "  Grafana:    http://localhost:3000"
echo "  Jaeger:     http://localhost:16686"
echo "  Prometheus: http://localhost:9090"
echo "================================================"
