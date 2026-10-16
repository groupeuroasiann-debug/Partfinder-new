# Builds a signed sideload MSIX (dev cert CN=DELL must match Package.appxmanifest Identity Publisher for sideload).
# Run:  powershell -ExecutionPolicy Bypass -File .\PartFinder\build-msix.ps1

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$Project = Join-Path $Root "PartFinder\PartFinder.csproj"
$PropsDir = Join-Path $Root "PartFinder\Properties"
$PfxPath = Join-Path $PropsDir "PartFinderDev.pfx"
$PfxPassword = "PartFinderDev"
$Publisher = "CN=DELL"

$freeGb = [math]::Round((Get-PSDrive C).Free / 1GB, 1)
if ($freeGb -lt 3) {
  Write-Warning "Low disk space on C: ($freeGb GB free). MSIX publish needs several GB for NuGet/temp files."
  Write-Host "Free space: delete PartFinder\PartFinder\bin, PartFinder\PartFinder\obj, and empty Recycle Bin."
  Write-Host "If restore failed with 'Central Directory corrupt', delete the broken package folder under:"
  Write-Host "  $env:USERPROFILE\.nuget\packages\microsoft.netcore.app.crossgen2.win-x64\8.0.31"
  Write-Host ""
}

Write-Host "Ensuring dev code-signing certificate ($Publisher)..."
$securePwd = ConvertTo-SecureString $PfxPassword -AsPlainText -Force
$cert = Get-ChildItem Cert:\CurrentUser\My |
  Where-Object { $_.Subject -eq $Publisher -and $_.HasPrivateKey } |
  Sort-Object NotAfter -Descending |
  Select-Object -First 1

if (-not $cert) {
  $cert = New-SelfSignedCertificate -Type Custom -Subject $Publisher `
    -TextExtension @(
      "2.5.29.37={text}1.3.6.1.5.5.7.3.3"
      "2.5.29.19={text}CA=FALSE"
    ) `
    -KeyUsage DigitalSignature -FriendlyName "PartFinder MSIX Dev" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -KeyExportPolicy Exportable -KeySpec Signature -KeyLength 2048 -HashAlgorithm SHA256
}

New-Item -ItemType Directory -Force -Path $PropsDir | Out-Null
Export-PfxCertificate -Cert $cert -FilePath $PfxPath -Password $securePwd -Force | Out-Null
Write-Host "Certificate thumbprint: $($cert.Thumbprint)"

Write-Host "Publishing signed MSIX (Release, x64)..."
# ReadyToRun pulls Crossgen2 (~100MB+); disable here to reduce disk use and avoid corrupt partial downloads.
dotnet publish $Project `
  -c Release `
  -p:Platform=x64 `
  -p:WindowsPackageType=MSIX `
  -p:GenerateAppxPackageOnBuild=true `
  -p:AppxPackageSigningEnabled=true `
  -p:PackageCertificateThumbprint=$($cert.Thumbprint) `
  -p:AppxBundle=Never `
  -p:UapAppxPackageBuildMode=SideloadOnly `
  -p:PublishReadyToRun=false `
  -p:PublishTrimmed=false

if ($LASTEXITCODE -ne 0) {
  throw "dotnet publish failed (exit $LASTEXITCODE). MSIX was not rebuilt. Do not upload the old file in AppPackages."
}

$testDir = Get-ChildItem (Join-Path $Root "PartFinder\AppPackages") -Directory -Filter "PartFinder_*_x64_Test" |
  Sort-Object LastWriteTime -Descending |
  Select-Object -First 1

if (-not $testDir) {
  throw "AppPackages test folder not found after publish."
}

$msix = Get-ChildItem $testDir.FullName -Filter "*.msix" | Select-Object -First 1
if (-not $msix) {
  throw "No .msix found in $($testDir.FullName)."
}

$cerPath = Join-Path $testDir.FullName ($msix.BaseName + ".cer")
Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null

Write-Host ""
Write-Host "Built:"
Write-Host "  $($msix.FullName)"
Write-Host "  $cerPath"
Write-Host "  Modified: $($msix.LastWriteTime)"
Write-Host ""
Write-Host "Install: run PowerShell as Administrator, then:"
Write-Host "  powershell -ExecutionPolicy Bypass -File .\PartFinder\sideload-install.ps1"
