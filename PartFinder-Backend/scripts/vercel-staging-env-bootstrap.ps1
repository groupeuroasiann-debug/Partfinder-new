# Bootstrap Vercel env for partfinder-backend-staging ONLY.
# Run from: PartFinder-Backend (linked .vercel/project.json must be partfinder-backend-staging)
# Requires: vercel CLI logged in as groupeuroasiann-debug
# Does NOT modify production project partfinder-new.

$ErrorActionPreference = 'Stop'

$stagingBackendDefault = 'C:\Users\user\Desktop\PartFinder-Staging-Deploy\PartFinder-Backend'
$backendRoot = if ($env:WMS_STAGING_BACKEND_DIR) { $env:WMS_STAGING_BACKEND_DIR } else { (Resolve-Path (Join-Path $PSScriptRoot '..')).Path }
$projectJson = Join-Path (Join-Path $backendRoot '.vercel') 'project.json'
$stagingProjectJson = Join-Path (Join-Path $stagingBackendDefault '.vercel') 'project.json'
if (-not (Test-Path $projectJson) -and (Test-Path $stagingProjectJson)) {
    $backendRoot = $stagingBackendDefault
    $projectJson = $stagingProjectJson
}
if (Test-Path $projectJson) {
    $proj = Get-Content $projectJson | ConvertFrom-Json
    if ($proj.projectName -ne 'partfinder-backend-staging') {
        throw "Refusing: linked project is '$($proj.projectName)', expected partfinder-backend-staging"
    }
    Write-Host "Verified Vercel link: $($proj.projectName)"
} else {
    Write-Warning 'No .vercel/project.json beside backend — ensure CWD is linked staging backend folder.'
}

function Get-EnvFileValue([string]$FilePath, [string]$Key) {
    if (-not (Test-Path $FilePath)) { return $null }
    foreach ($line in Get-Content $FilePath) {
        if ($line -match "^\s*$Key\s*=\s*(.+)\s*$") {
            return $Matches[1].Trim().Trim('"').Trim("'")
        }
    }
    return $null
}

$localEnv = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..')) '.env.local'
if (-not $localEnv) {
    $localEnv = 'C:\Users\user\Desktop\Euroasiann\Partfinder-new\PartFinder-Backend\.env.local'
}

$mongoUri = $env:MONGODB_URI
if (-not $mongoUri) {
    $mongoUri = Get-EnvFileValue $localEnv 'MONGODB_URI'
}
if (-not $mongoUri) {
    throw 'MONGODB_URI not set; export MONGODB_URI or create .env.local (not committed).'
}

$jwt = $env:WMS_STAGING_JWT_SECRET
if (-not $jwt) {
    $jwt = [Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Maximum 256 }))
    Write-Host 'Generated new staging-only JWT_SECRET (not printed).'
}

$vars = @{
    MONGODB_URI               = $mongoUri
    JWT_SECRET                = $jwt
    WMS_DEPLOYMENT_TIER       = 'staging'
    WMS_STAGING_STOCK_POSTING = 'false'
    WMS_LIVE_STOCK_POSTING    = 'false'
    PORT                      = '3000'
}

$environments = @('production', 'preview', 'development')

foreach ($name in $vars.Keys) {
    $value = $vars[$name]
    foreach ($envName in $environments) {
        Write-Host "Setting $name for $envName ..."
        Push-Location $backendRoot
        try {
            $args = @('env', 'add', $name, '-e', $envName, '--value', $value, '--yes', '--sensitive')
            & vercel @args | Out-Null
            if ($LASTEXITCODE -ne 0) {
                & vercel @('env', 'add', $name, '-e', $envName, '--value', $value, '--yes', '--sensitive', '--force') | Out-Null
            }
        } finally {
            Pop-Location
        }
    }
}

Write-Host 'Done. Redeploy: vercel --prod (from linked PartFinder-Backend directory).'
Write-Host 'After isolation check, set WMS_STAGING_STOCK_POSTING=true on staging project only.'