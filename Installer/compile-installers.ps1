# Compile all OrderWeb POS Inno Setup scripts.
# Requires Inno Setup 6+ (ISCC.exe on PATH or at default install location).

param(
    [string]$Configuration = "Release",
    [string]$InnoSetupCompiler = ""
)

$ErrorActionPreference = "Stop"
$installerDir = $PSScriptRoot

if (-not $InnoSetupCompiler) {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
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

$publishExe = Join-Path (Split-Path -Parent $installerDir) "publish\win-x64\POS-in-NET.exe"
if (-not (Test-Path $publishExe)) {
    throw "Missing publish inputs. Run Installer\build-installer-inputs.ps1 first."
}

$scripts = @(
    "OrderWebPOS-Mother.iss",
    "OrderWebPOS-Child.iss",
    "OrderWebPOS-Update-Mother.iss",
    "OrderWebPOS-Update-Child.iss"
)

foreach ($script in $scripts) {
    Write-Host "Compiling $script..."
    & $InnoSetupCompiler `
        "/DMyAppVersion=1.0.0" `
        (Join-Path $installerDir $script)
}

Write-Host ""
Write-Host "Installers written to Installer\Output\"
