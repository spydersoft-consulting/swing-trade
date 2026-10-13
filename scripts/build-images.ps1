<#
.SYNOPSIS
  Builds the two container images the way CI does: dotnet publish first, then a Dockerfile that only does COPY . /app.
.PARAMETER Tag
  Image tag (default: local).
.PARAMETER Cli
  Container CLI: docker or podman (default: docker if present, else podman).
#>
param(
    [string] $Tag = "local",
    [string] $Cli = $(if (Get-Command docker -ErrorAction SilentlyContinue) { "docker" } else { "podman" })
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$out = Join-Path $root "artifacts/publish"

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
dotnet publish "$root/src/api/src/Spydersoft.SwingTrade.Api/Spydersoft.SwingTrade.Api.csproj" -c Release -o "$out/api"
if ($LASTEXITCODE) { throw "api publish failed" }
# Publishing the web host also builds the React app (yarn install + yarn build) and copies it into wwwroot.
dotnet publish "$root/src/web/src/Spydersoft.SwingTrade.Frontend/Spydersoft.SwingTrade.Frontend.csproj" -c Release -o "$out/web"
if ($LASTEXITCODE) { throw "web publish failed" }

& $Cli build -t "swing-trade-api:$Tag" -f "$root/src/api/src/Spydersoft.SwingTrade.Api/Dockerfile" "$out/api"
if ($LASTEXITCODE) { throw "api image build failed" }
& $Cli build -t "swing-trade-web:$Tag" -f "$root/src/web/Dockerfile" "$out/web"
if ($LASTEXITCODE) { throw "web image build failed" }

Write-Host "Built swing-trade-api:$Tag and swing-trade-web:$Tag with $Cli"
