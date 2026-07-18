#Requires -Version 5.1

[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9._-]{1,20}$')]
    [string]$PosUserName = 'OrderWebPOS',

    [string]$AppPath = "$env:ProgramFiles\OrderWeb POS\POS-in-NET.exe",

    [int]$MinimumFreeDiskPercent = 15,

    [int]$MaximumUpdateAgeDays = 45
)

$checks = [System.Collections.Generic.List[object]]::new()

function Add-Check {
    param(
        [string]$Check,
        [ValidateSet('PASS', 'FAIL', 'WARN', 'MANUAL')][string]$Status,
        [string]$Details
    )

    $checks.Add([pscustomobject]@{
        Check = $Check
        Status = $Status
        Details = $Details
    })
}

$os = Get-CimInstance Win32_OperatingSystem
$build = [Environment]::OSVersion.Version.Build
$isWindows10Production = $build -ge 19045 -and $build -lt 22000
$isWindows11Production = $build -ge 22000
$supportedWindows = $isWindows10Production -or $isWindows11Production
$businessEdition = $os.Caption -match 'Pro|Enterprise|Education'

Add-Check 'Supported Windows version' $(if ($supportedWindows) { 'PASS' } else { 'FAIL' }) `
    $(if ($supportedWindows) {
        "$($os.Caption), build $build"
    } else {
        "$($os.Caption), build $build. Use Windows 10 22H2 (19045) or Windows 11."
    })
Add-Check 'Windows business edition' $(if ($businessEdition) { 'PASS' } else { 'FAIL' }) `
    $(if ($businessEdition) { $os.Caption } else { "$($os.Caption). Home edition is not supported for a production till." })

if ($isWindows10Production) {
    Add-Check 'Windows 10 security servicing' 'MANUAL' `
        'Windows 10 22H2 is supported only while enrolled in Microsoft ESU or another applicable supported servicing programme. Confirm entitlement and activation.'
}

$latestHotFix = Get-HotFix -ErrorAction SilentlyContinue |
    Where-Object InstalledOn |
    Sort-Object InstalledOn -Descending |
    Select-Object -First 1
if ($latestHotFix) {
    $updateAge = ((Get-Date) - $latestHotFix.InstalledOn).Days
    Add-Check 'Recent Windows update' $(if ($updateAge -le $MaximumUpdateAgeDays) { 'PASS' } else { 'FAIL' }) `
        "$($latestHotFix.HotFixID) installed $($latestHotFix.InstalledOn.ToString('yyyy-MM-dd')) ($updateAge days ago)"
} else {
    Add-Check 'Recent Windows update' 'WARN' 'Could not determine the latest installed update; verify Windows Update manually.'
}

$posUser = Get-LocalUser -Name $PosUserName -ErrorAction SilentlyContinue
if ($posUser) {
    $administrators = Get-LocalGroup -SID 'S-1-5-32-544'
    $isAdmin = Get-LocalGroupMember -Group $administrators.Name -ErrorAction SilentlyContinue |
        Where-Object SID -eq $posUser.SID
    Add-Check 'Dedicated POS account' $(if ($posUser.Enabled) { 'PASS' } else { 'FAIL' }) ".\$PosUserName exists; enabled=$($posUser.Enabled)"
    Add-Check 'POS account is non-administrator' $(if ($isAdmin) { 'FAIL' } else { 'PASS' }) `
        $(if ($isAdmin) { 'Account is a local administrator.' } else { 'Account is a standard user.' })
} else {
    Add-Check 'Dedicated POS account' 'FAIL' ".\$PosUserName does not exist."
    Add-Check 'POS account is non-administrator' 'FAIL' 'Cannot verify a missing account.'
}

$resolvedAppPath = [Environment]::ExpandEnvironmentVariables($AppPath)
Add-Check 'OrderWeb POS installed' $(if (Test-Path -LiteralPath $resolvedAppPath -PathType Leaf) { 'PASS' } else { 'FAIL' }) $resolvedAppPath

$taskName = 'OrderWeb POS - Start at logon'
$task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
Add-Check "Scheduled task: $taskName" $(if ($task) { 'PASS' } else { 'FAIL' }) `
    $(if ($task) { "State=$($task.State); user=$($task.Principal.UserId)" } else { 'Task is missing.' })

Add-Check 'UK timezone' $(if ((Get-TimeZone).Id -eq 'GMT Standard Time') { 'PASS' } else { 'FAIL' }) (Get-TimeZone).Id
$timeService = Get-CimInstance Win32_Service -Filter "Name='W32Time'"
Add-Check 'Automatic clock synchronisation' `
    $(if ($timeService.StartMode -eq 'Auto' -and $timeService.State -eq 'Running') { 'PASS' } else { 'FAIL' }) `
    "StartMode=$($timeService.StartMode); state=$($timeService.State)"

$hibernateEnabled = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Power' -Name HibernateEnabled -ErrorAction SilentlyContinue).HibernateEnabled
Add-Check 'Hibernation disabled' $(if ($hibernateEnabled -eq 0) { 'PASS' } else { 'FAIL' }) "HibernateEnabled=$hibernateEnabled"

$powerOutput = powercfg.exe /query SCHEME_CURRENT SUB_SLEEP STANDBYIDLE 2>$null
$acSleepDisabled = ($powerOutput -join "`n") -match 'Current AC Power Setting Index:\s+0x00000000'
$dcSleepDisabled = ($powerOutput -join "`n") -match 'Current DC Power Setting Index:\s+0x00000000'
Add-Check 'Sleep disabled' $(if ($acSleepDisabled -and $dcSleepDisabled) { 'PASS' } else { 'FAIL' }) `
    "AC disabled=$acSleepDisabled; battery disabled=$dcSleepDisabled"

$firewallProfiles = Get-NetFirewallProfile -Profile Domain, Private, Public
$disabledProfiles = $firewallProfiles | Where-Object { -not $_.Enabled }
Add-Check 'Windows firewall enabled' $(if ($disabledProfiles) { 'FAIL' } else { 'PASS' }) `
    (($firewallProfiles | ForEach-Object { "$($_.Name)=$($_.Enabled)" }) -join '; ')

if (Get-Command Get-MpComputerStatus -ErrorAction SilentlyContinue) {
    $defender = Get-MpComputerStatus
    $defenderOk = $defender.AMServiceEnabled -and $defender.AntivirusEnabled -and $defender.RealTimeProtectionEnabled
    Add-Check 'Microsoft Defender enabled' $(if ($defenderOk) { 'PASS' } else { 'FAIL' }) `
        "service=$($defender.AMServiceEnabled); antivirus=$($defender.AntivirusEnabled); real-time=$($defender.RealTimeProtectionEnabled)"

    $preferences = Get-MpPreference
    $broadTargets = @(
        (Split-Path -Parent $resolvedAppPath).TrimEnd('\'),
        (Join-Path $env:ProgramData 'OrderWebPOS').TrimEnd('\')
    )
    $broadExclusions = @($preferences.ExclusionPath | Where-Object {
        $exclusion = $_.TrimEnd('\')
        $broadTargets -contains $exclusion
    })
    Add-Check 'No broad Defender exclusions' $(if ($broadExclusions.Count -eq 0) { 'PASS' } else { 'FAIL' }) `
        $(if ($broadExclusions.Count -eq 0) { 'No full application/data folder exclusions found.' } else { "Remove: $($broadExclusions -join ', ')" })
} else {
    Add-Check 'Microsoft Defender enabled' 'FAIL' 'Defender status commands are unavailable.'
    Add-Check 'No broad Defender exclusions' 'WARN' 'Could not inspect Defender exclusions.'
}

$systemDrive = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='$env:SystemDrive'"
$freePercent = [math]::Round(($systemDrive.FreeSpace / $systemDrive.Size) * 100, 1)
Add-Check 'Free system disk space' $(if ($freePercent -ge $MinimumFreeDiskPercent) { 'PASS' } else { 'FAIL' }) `
    "$freePercent% free; minimum=$MinimumFreeDiskPercent%; target=20%"

try {
    $assignedAccess = Get-CimInstance -Namespace 'root\cimv2\mdm\dmmap' -ClassName 'MDM_AssignedAccess' -ErrorAction Stop
    $assignedAccessConfiguration = [System.Net.WebUtility]::HtmlDecode($assignedAccess.Configuration)
    $assignedAccessOk = -not [string]::IsNullOrWhiteSpace($assignedAccessConfiguration) -and
        $assignedAccessConfiguration.Contains("<Account>$PosUserName</Account>") -and
        $assignedAccessConfiguration.Contains([System.Security.SecurityElement]::Escape($resolvedAppPath))
    Add-Check 'Assigned Access lock-down configured' $(if ($assignedAccessOk) { 'PASS' } else { 'FAIL' }) `
        $(if ($assignedAccessOk) { 'OrderWeb POS is the allowed application for the dedicated POS account.' } else { 'Assigned Access does not match this POS account/application.' })
} catch {
    Add-Check 'Assigned Access lock-down configured' 'WARN' 'Could not query the Assigned Access CSP as this administrator. Validate restrictions by signing in to the POS account.'
}

Add-Check 'Physical till security' 'MANUAL' 'Confirm the till is fixed/locked down before enabling automatic login.'
Add-Check 'UPS protection' 'MANUAL' 'Mother terminal, router, and network switch must be connected to and tested on a UPS.'
Add-Check 'BIOS power recovery' 'MANUAL' 'Set Restore on AC Power Loss to Power On and test it.'
Add-Check 'Automatic login' 'MANUAL' 'If approved, configure with Microsoft Sysinternals Autologon; never store the password in a script.'

$checks | Format-Table -AutoSize -Wrap
$failed = @($checks | Where-Object Status -eq 'FAIL')
if ($failed.Count -gt 0) {
    Write-Error "$($failed.Count) required Windows till check(s) failed."
    exit 1
}

Write-Host "`nAll automated Windows till checks passed. Complete the MANUAL sign-offs before go-live." -ForegroundColor Green
