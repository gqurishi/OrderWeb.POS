#Requires -Version 5.1

[CmdletBinding()]
param(
    [string]$ConfigurationPath,

    [switch]$Remove
)

$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $identity.IsSystem) {
    throw 'Assigned Access must be applied as LocalSystem.'
}

$assignedAccess = Get-CimInstance -Namespace 'root\cimv2\mdm\dmmap' -ClassName 'MDM_AssignedAccess'
if ($null -eq $assignedAccess) {
    throw 'The Windows Assigned Access CSP is unavailable on this device.'
}

if ($Remove) {
    $assignedAccess.Configuration = $null
} else {
    if (-not (Test-Path -LiteralPath $ConfigurationPath -PathType Leaf)) {
        throw "Assigned Access configuration was not found at '$ConfigurationPath'."
    }

    $configuration = Get-Content -LiteralPath $ConfigurationPath -Raw
    $assignedAccess.Configuration = [System.Net.WebUtility]::HtmlEncode($configuration)
}

Set-CimInstance -CimInstance $assignedAccess | Out-Null

