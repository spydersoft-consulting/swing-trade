#!/usr/bin/env bash
# Builds the two container images the way CI does: `dotnet publish` first, then a Dockerfile that only does `COPY . /app`.
# Usage: scripts/build-images.sh [tag]            (default tag: local)
# Set CONTAINER_CLI=podman to use podman (default: docker if present, else podman).
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tag="${1:-local}"
cli="${CONTAINER_CLI:-$(command -v docker >/dev/null 2>&1 && echo docker || echo podman)}"
out="$root/artifacts/publish"

rm -rf "$out"
dotnet publish "$root/src/api/src/Spydersoft.SwingTrade.Api/Spydersoft.SwingTrade.Api.csproj" -c Release -o "$out/api"
# Publishing the web host also builds the React app (yarn install + yarn build) and copies it into wwwroot.
dotnet publish "$root/src/web/src/Spydersoft.SwingTrade.Frontend/Spydersoft.SwingTrade.Frontend.csproj" -c Release -o "$out/web"

"$cli" build -t "swing-trade-api:$tag" -f "$root/src/api/src/Spydersoft.SwingTrade.Api/Dockerfile" "$out/api"
"$cli" build -t "swing-trade-web:$tag" -f "$root/src/web/Dockerfile" "$out/web"

echo "Built swing-trade-api:$tag and swing-trade-web:$tag with $cli"
