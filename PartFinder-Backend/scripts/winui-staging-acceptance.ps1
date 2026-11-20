# Phase 03A.2 — WinUI manual acceptance launcher (staging Vercel API)
# Run on a Windows workstation with PartFinder source. Does NOT automate UI clicks.

$ErrorActionPreference = 'Stop'
$stagingBase = 'https://partfinder-backend-staging.vercel.app'

Write-Host "Staging API: $stagingBase"
Write-Host "Org code: 900152"
Write-Host ""
Write-Host "1) Bootstrap identities (password from your vault, not printed):"
Write-Host '   $env:WMS_STAGING_TEST_PASSWORD = "<store in password manager>"'
Write-Host '   cd PartFinder-Backend; npm run wms:staging-hosted-identities'
Write-Host ""
Write-Host "2) Enable staging stock posting on Vercel project partfinder-backend-staging only:"
Write-Host '   cd C:\Users\user\Desktop\PartFinder-Staging-Deploy\PartFinder-Backend'
Write-Host '   vercel env rm WMS_STAGING_STOCK_POSTING Production --yes'
Write-Host '   "true" | vercel env add WMS_STAGING_STOCK_POSTING Production --yes'
Write-Host '   vercel deploy --prod --yes'
Write-Host ""
Write-Host "3) Hosted HTTP preflight (admin JWT):"
Write-Host "   node scripts/staging-http-smoke.mjs"
Write-Host '   `$env:WMS_STAGING_API_BASE = "' + $stagingBase + '"'
Write-Host '   `$env:WMS_SMOKE_EMAIL = "wms-admin-staging@partfinder.acceptance"'
Write-Host '   `$env:WMS_SMOKE_PASSWORD = $env:WMS_STAGING_TEST_PASSWORD'
Write-Host ""
Write-Host "4) WinUI against staging (not production):"
Write-Host '   $env:PARTFINDER_STAGING_API = "1"'
Write-Host '   dotnet build PartFinder\PartFinder\PartFinder.csproj -c Debug -p:Platform=x64'
Write-Host '   dotnet run --project PartFinder\PartFinder\PartFinder.csproj -c Debug -p:Platform=x64'
Write-Host ""
Write-Host "Checklist: docs/wms-phase03/03A-2-winui-acceptance-checklist.md"
Write-Host "Accounts: wms-admin-staging@ / operator-a-staging@ / operator-b-staging@ @partfinder.acceptance"