#Requires -Version 5.1
#Requires -RunAsAdministrator

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param()

$ErrorActionPreference = 'Stop'
$deploymentRoot = Join-Path $env:ProgramData 'OrderWebPOS\TillDeployment'
$assignedAccessSource = Join-Path $PSScriptRoot 'Set-OrderWebAssignedAccess.ps1'
$assignedAccessTarget = Join-Path $deploymentRoot 'Set-OrderWebAssignedAccess.ps1'
$removalTaskName = 'OrderWeb POS - Remove Assigned Access'
$appTaskName = 'OrderWeb POS - Start at logon'

if (-not $PSCmdlet.ShouldProcess('OrderWeb POS account', 'Remove Assigned Access lock-down and automatic app startup')) {
    return
}

New-Item -ItemType Directory -Path $deploymentRoot -Force | Out-Null
Copy-Item -LiteralPath $assignedAccessSource -Destination $assignedAccessTarget -Force

$systemPrincipal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$systemAction = New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" `
    -Argument "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$assignedAccessTarget`" -Remove"
Register-ScheduledTask -TaskName $removalTaskName -Action $systemAction -Principal $systemPrincipal `
    -Description 'Removes the OrderWeb POS Assigned Access configuration as LocalSystem.' -Force | Out-Null
Start-ScheduledTask -TaskName $removalTaskName

$deadline = (Get-Date).AddSeconds(45)
do {
    Start-Sleep -Milliseconds 500
    $removalTask = Get-ScheduledTask -TaskName $removalTaskName
} while ($removalTask.State -eq 'Running' -and (Get-Date) -lt $deadline)

$removalTaskInfo = Get-ScheduledTaskInfo -TaskName $removalTaskName
if ($removalTask.State -eq 'Running') {
    throw 'Timed out while removing Windows Assigned Access.'
}
if ($removalTaskInfo.LastTaskResult -ne 0) {
    throw "Removing Windows Assigned Access failed with task result $($removalTaskInfo.LastTaskResult)."
}

Unregister-ScheduledTask -TaskName $removalTaskName -Confirm:$false
Unregister-ScheduledTask -TaskName $appTaskName -Confirm:$false -ErrorAction SilentlyContinue

Write-Host 'OrderWeb POS Assigned Access and automatic startup were removed.' -ForegroundColor Green
Write-Host 'Restart Windows to complete recovery. The POS account and machine security settings were retained.'

