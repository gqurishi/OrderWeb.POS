param(
    [switch]$Restore
)

$ErrorActionPreference = 'Continue'
$solutionRoot = Split-Path -Parent $PSScriptRoot
$logDirectory = Join-Path $PSScriptRoot 'logs'
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null

$builds = @(
    @{ Name = 'Contracts'; Project = 'src/OrderWeb.Contracts/OrderWeb.Contracts.csproj'; Framework = $null },
    @{ Name = 'SharedUI-Windows'; Project = 'src/OrderWeb.SharedUI/OrderWeb.SharedUI.csproj'; Framework = 'net10.0-windows10.0.19041.0' },
    @{ Name = 'Mother-Windows'; Project = 'src/OrderWeb.Mother/OrderWeb.Mother.csproj'; Framework = 'net10.0-windows10.0.19041.0' },
    @{ Name = 'Client-Windows'; Project = 'src/OrderWeb.Client/OrderWeb.Client.csproj'; Framework = 'net10.0-windows10.0.19041.0' },
    @{ Name = 'Client-Android'; Project = 'src/OrderWeb.Client/OrderWeb.Client.csproj'; Framework = 'net10.0-android' }
)

$results = foreach ($build in $builds) {
    $arguments = @('build', $build.Project, '--verbosity:minimal')
    if (-not $Restore) { $arguments += '--no-restore' }
    if ($build.Framework) { $arguments += @('-f', $build.Framework) }

    $logFile = Join-Path $logDirectory ("step0-{0}.log" -f $build.Name.ToLowerInvariant())
    Write-Host "Building $($build.Name)..."
    Push-Location $solutionRoot
    try {
        & dotnet @arguments 2>&1 | Tee-Object -FilePath $logFile
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    [PSCustomObject]@{
        Target = $build.Name
        Status = if ($exitCode -eq 0) { 'Passed' } else { 'Failed' }
        Log = $logFile
    }
}

$results | Format-Table -AutoSize
if ($results.Status -contains 'Failed') { exit 1 }
