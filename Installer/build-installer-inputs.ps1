# Build publish folders consumed by Inno Setup scripts.
# Run on Windows from the repository root:
#   powershell -ExecutionPolicy Bypass -File Installer\build-installer-inputs.ps1

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "publish\win-x64",
    [string]$TargetFramework = "net9.0-windows10.0.19041.0"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

Write-Host "Building OrderWeb POS installer inputs ($Configuration / $Runtime)..."

$appProject = Join-Path $repoRoot "POS-in-NET.csproj"
$setupProject = Join-Path $repoRoot "OrderWeb.DatabaseSetup\OrderWeb.DatabaseSetup.csproj"
$publishDir = Join-Path $repoRoot $OutputRoot

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir | Out-Null

Write-Host "Publishing MAUI Windows app..."
dotnet publish $appProject `
    -c $Configuration `
    -f $TargetFramework `
    -p:RuntimeIdentifierOverride=$Runtime `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    --self-contained true `
    -o $publishDir

Write-Host "Publishing OrderWeb.DatabaseSetup.exe..."
$setupPublishDir = Join-Path $env:TEMP "orderweb-database-setup-publish"
if (Test-Path $setupPublishDir) {
    Remove-Item $setupPublishDir -Recurse -Force
}

dotnet publish $setupProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $setupPublishDir

Copy-Item (Join-Path $setupPublishDir "*") $publishDir -Recurse -Force

$requiredSetupFiles = @(
    "OrderWeb.DatabaseSetup.exe",
    "Migrations\001_initial_schema.sql",
    "Migrations\VERIFY_REQUIRED_SCHEMA.sql"
)

foreach ($requiredFile in $requiredSetupFiles) {
    $requiredPath = Join-Path $publishDir $requiredFile
    if (-not (Test-Path $requiredPath)) {
        throw "Required published file is missing: $requiredPath"
    }
}

Write-Host ""
Write-Host "Installer inputs ready:"
Write-Host "  $publishDir"
Write-Host ""
Write-Host "Next: compile Inno Setup scripts from Installer\"
Write-Host "  ISCC.exe OrderWebPOS-Mother.iss"
Write-Host "  ISCC.exe OrderWebPOS-Child.iss"
Write-Host "  ISCC.exe OrderWebPOS-Update-Mother.iss"
Write-Host "  ISCC.exe OrderWebPOS-Update-Child.iss"
