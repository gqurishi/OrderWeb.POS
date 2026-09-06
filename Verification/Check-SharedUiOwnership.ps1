param(
    [string]$SolutionRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$shared = Join-Path $SolutionRoot 'src\OrderWeb.SharedUI'
$motherProject = Join-Path $SolutionRoot 'src\OrderWeb.Mother\OrderWeb.Mother.csproj'
$clientProject = Join-Path $SolutionRoot 'src\OrderWeb.Client\OrderWeb.Client.csproj'
$hosts = @($motherProject, $clientProject)

if (-not (Test-Path -LiteralPath $shared)) { throw "SharedUI project folder not found: $shared" }

$violations = @()
foreach ($project in $hosts) {
    $text = Get-Content -LiteralPath $project -Raw
    if ($text -match '<(Compile|MauiXaml|EmbeddedResource)\s+Include="[^\"]*OrderWeb\.SharedUI') {
        $violations += "$project includes SharedUI source for compilation. Hosts must use ProjectReference only."
    }
    if ($text -notmatch 'ProjectReference Include="\.\.\\OrderWeb\.SharedUI\\OrderWeb\.SharedUI\.csproj"') {
        $violations += "$project does not reference OrderWeb.SharedUI."
    }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host 'SharedUI ownership check passed: hosts reference the library and do not compile its source files.'
Write-Host 'Legacy UI deletion remains gated on per-screen visual and behavioral parity verification.'
