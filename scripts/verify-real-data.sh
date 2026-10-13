#!/usr/bin/env bash
# Read-only check of real market data against a RUNNING Local API (starts nothing).
# Needs curl and jq. Usage: scripts/verify-real-data.sh [--no-refresh]
# Env: API (http://localhost:8180) IDP (http://localhost:8210) TIMEOUT_SEC (300) STALE_DAYS (4)
set -euo pipefail
API=${API:-http://localhost:8180}; IDP=${IDP:-http://localhost:8210}
TIMEOUT_SEC=${TIMEOUT_SEC:-300}; STALE_DAYS=${STALE_DAYS:-4}
command -v jq >/dev/null || { echo "jq is required" >&2; exit 2; }

TOKEN=$(curl -sf "$IDP/connect/token" -d grant_type=password -d client_id=swingtrade-api-test \
  -d client_secret=swingtrade-api-test-secret -d username=admin -d password='Test123!' -d scope=swingtrade | jq -r .access_token)
get() { curl -sf -H "Authorization: Bearer $TOKEN" "$API$1"; }

if [ "${1:-}" = "--no-refresh" ]; then
  RUN=$(get "/api/v1/refresh/runs?page=1&pageSize=1" | jq '.items[0]')
else
  CODE=$(curl -s -o /tmp/vrd_run.json -w '%{http_code}' -X POST -H "Authorization: Bearer $TOKEN" "$API/api/v1/refresh")
  if [ "$CODE" = 409 ]; then echo "A run is already active; waiting for it." >&2
    RUN=$(get "/api/v1/refresh/runs?page=1&pageSize=1" | jq '.items[0]')
  elif [ "$CODE" = 202 ] || [ "$CODE" = 200 ]; then RUN=$(cat /tmp/vrd_run.json)
  else echo "POST /refresh returned $CODE" >&2; exit 2; fi
  ID=$(echo "$RUN" | jq -r .id); END=$((SECONDS + TIMEOUT_SEC))
  while [ "$(echo "$RUN" | jq -r .status)" = Running ]; do
    [ $SECONDS -gt $END ] && { echo "Run $ID still Running after ${TIMEOUT_SEC}s" >&2; exit 2; }
    sleep 3; RUN=$(get "/api/v1/refresh/runs/$ID")
  done
fi
echo
echo "$RUN" | jq -r '"Run \(.id): status=\(.status) provider=\(.provider) ok=\(.tickersOk) failed=\(.tickersFailed)"'
echo "$RUN" | jq -r 'select(.message) | "Message (failed tickers / fallbacks): \(.message)"'

CUTOFF=$(date -d "-$STALE_DAYS days" +%F 2>/dev/null || date -v-"${STALE_DAYS}"d +%F)
FLAGGED=0; COUNT=0
printf '%-6s %-10s %9s %9s %9s %-12s %-14s %5s  %s\n' Ticker AsOf Last MA20 MA50 Trend Signal Bars Flags
while IFS= read -r row; do
  T=$(echo "$row" | jq -r .ticker)
  N=$(get "/api/v1/instruments/$T/bars?days=120" | jq '.bars | length')
  FL=""
  [ "$N" -lt 50 ] && FL="BARS<50 ($N); "
  echo "$row" | jq -e '.asOfDate == null' >/dev/null && FL+="NO DATA; "
  A=$(echo "$row" | jq -r '.asOfDate // empty'); [ -n "$A" ] && [[ "$A" < "$CUTOFF" ]] && FL+="OLD ASOF; "
  echo "$row" | jq -e '.stale == true' >/dev/null && FL+="STALE; "
  echo "$row" | jq -e '.ma50 == null' >/dev/null && FL+="NO MA50; "
  [ -n "$FL" ] && FLAGGED=$((FLAGGED+1)); COUNT=$((COUNT+1))
  echo "$row" | jq -r --arg n "$N" --arg f "$FL" \
    '[.ticker,(.asOfDate//"-"),(.last//"-"),(.ma20//"-"),(.ma50//"-"),.trend,.signal,$n,$f] | @tsv' |
    awk -F'\t' '{printf "%-6s %-10s %9s %9s %9s %-12s %-14s %5s  %s\n",$1,$2,$3,$4,$5,$6,$7,$8,$9}'
done < <(get /api/v1/watchlist | jq -c 'sort_by(.ticker)[]')
echo "$COUNT tickers listed (expected 12), $FLAGGED flagged."
echo "Bars is capped at 120 by the API; compare Last/MA20/MA50 with a broker chart (plain closes) per docs/M3_MANUAL_CHECK.md."
[ "$(echo "$RUN" | jq -r .status)" = Succeeded ] && [ "$FLAGGED" -eq 0 ] && [ "$COUNT" -eq 12 ]
