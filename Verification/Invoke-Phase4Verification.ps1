[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(100, 125, 150, 175)]
    [int]$Scaling,

    [string]$OutputDirectory = (Join-Path $PSScriptRoot "Results"),

    [switch]$SkipAutomatedTests
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $PSScriptRoot "phase4-workflows.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

if (-not $SkipAutomatedTests) {
    dotnet test (Join-Path $repoRoot "tests\OrderWeb.DatabaseSetup.Tests\OrderWeb.DatabaseSetup.Tests.csproj") --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "Automated regression tests failed. Phase 4 cannot be signed off."
    }
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$resultPath = Join-Path $OutputDirectory "phase4-$($Scaling)pct-$stamp.md"
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("# Phase 4 verification - $Scaling% Windows scaling")
$lines.Add("")
$lines.Add("Run date: $(Get-Date -Format o)")
$lines.Add("")
$lines.Add("Use the installed POS, its normal local database, receipt/kitchen printers and payment test mode. This runner does not change Windows scaling; confirm Windows is already set to $Scaling% before signing the result.")
$lines.Add("")
$lines.Add("## Performance evidence")
$lines.Add("")
$lines.Add("Attach the debug performance capture. Targets: tap <= $($manifest.performanceTargetsMilliseconds.tapFeedback) ms; navigation frame <= $($manifest.performanceTargetsMilliseconds.navigationFrame) ms; normal page data <= $($manifest.performanceTargetsMilliseconds.normalPageDataMaximum) ms.")
$lines.Add("")
$lines.Add("- [ ] Tap target passed")
$lines.Add("- [ ] Navigation target passed")
$lines.Add("- [ ] Page data target passed on the normal local network")
$lines.Add("")
$lines.Add("## Workflow matrix")
$lines.Add("")
$lines.Add("For each workflow, verify successful completion, visible footer actions, double-tap protection, and fresh state after returning.")
$lines.Add("")
foreach ($workflow in $manifest.workflows) {
    $lines.Add("- [ ] $workflow")
}
$lines.Add("")
$lines.Add("## Required outcomes")
$lines.Add("")
foreach ($outcome in $manifest.requiredOutcomes) {
    $lines.Add("- [ ] $outcome")
}
$lines.Add("")
$lines.Add("## Environment and notes")
$lines.Add("")
$lines.Add("- Display resolution:")
$lines.Add("- POS build/version:")
$lines.Add("- Database host/network:")
$lines.Add("- Printer/payment test result:")
$lines.Add("- Failures or screenshots:")
$lines.Add("")
$lines.Add("Sign-off: ____________________")

Set-Content -LiteralPath $resultPath -Value $lines -Encoding utf8
Write-Output $resultPath
