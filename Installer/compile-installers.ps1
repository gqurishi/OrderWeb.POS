# Compile OrderWeb POS Inno Setup script.
# Requires Inno Setup 6+ (ISCC.exe on PATH or at default install location).

param(
    [string]$Configuration = "Release",
    [string]$InnoSetupCompiler = "",
    [string]$SignToolCommand = $env:ORDERWEB_SIGNTOOL_COMMAND,
    [switch]$AllowUnsigned,
    [switch]$IncludeLegacyInstallers
)

$ErrorActionPreference = "Stop"
$installerDir = $PSScriptRoot

if (-not $InnoSetupCompiler) {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            $InnoSetupCompiler = $candidate
            break
        }
    }
}

if (-not $InnoSetupCompiler -or -not (Test-Path $InnoSetupCompiler)) {
    throw "ISCC.exe not found. Install Inno Setup 6 or pass -InnoSetupCompiler."
}

if ([string]::IsNullOrWhiteSpace($SignToolCommand) -and -not $AllowUnsigned) {
    throw "Production installer signing is required. Set ORDERWEB_SIGNTOOL_COMMAND, or use -AllowUnsigned only for local validation builds."
}

$publishExe = Join-Path (Split-Path -Parent $installerDir) "publish\win-x64\POS-in-NET.exe"
if (-not (Test-Path $publishExe)) {
    throw "Missing publish inputs. Run Installer\build-installer-inputs.ps1 first."
}

$scripts = @(
    "OrderWebPOS-Setup.iss"
)

if ($IncludeLegacyInstallers) {
    $scripts += @(
    "OrderWebPOS-Mother.iss",
    "OrderWebPOS-Child.iss",
    "OrderWebPOS-Update-Mother.iss",
    "OrderWebPOS-Update-Child.iss"
    )
}

foreach ($script in $scripts) {
    Write-Host "Compiling $script..."
    $compilerArguments = @(
        "/DMyAppVersion=1.0.1"
    )
    if (-not [string]::IsNullOrWhiteSpace($SignToolCommand)) {
        $compilerArguments += "/DSignInstaller=1"
        $compilerArguments += "/Sorderweb=$SignToolCommand"
    }
    $compilerArguments += (Join-Path $installerDir $script)
    & $InnoSetupCompiler $compilerArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup failed for $script with exit code $LASTEXITCODE."
    }
}

Write-Host ""
Write-Host "Installer written to Installer\Output\"
