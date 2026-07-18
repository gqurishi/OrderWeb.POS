#Requires -Version 5.1
#Requires -RunAsAdministrator

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidatePattern('^[A-Za-z0-9._-]{1,20}$')]
    [string]$PosUserName = 'OrderWebPOS',

    [string]$AppPath = "$env:ProgramFiles\OrderWeb POS\POS-in-NET.exe",

    [switch]$CreatePosAccount,

    [SecureString]$PosAccountPassword
)

$ErrorActionPreference = 'Stop'
$deploymentRoot = Join-Path $env:ProgramData 'OrderWebPOS\TillDeployment'
$assignedAccessSource = Join-Path $PSScriptRoot 'Set-OrderWebAssignedAccess.ps1'
$assignedAccessTarget = Join-Path $deploymentRoot 'Set-OrderWebAssignedAccess.ps1'
$assignedAccessXml = Join-Path $deploymentRoot 'assigned-access.xml'
$appTaskName = 'OrderWeb POS - Start at logon'
$assignedAccessTaskName = 'OrderWeb POS - Configure Assigned Access'

function Write-Step {
    param([string]$Message)
    Write-Host "`n== $Message ==" -ForegroundColor Cyan
}

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [switch]$IgnoreExitCode
    )

    & $FilePath @Arguments
    $exitCode = $LASTEXITCODE
    if (-not $IgnoreExitCode -and $exitCode -ne 0) {
        throw "$FilePath failed with exit code $exitCode."
    }
}

Write-Step 'Checking Windows edition and application path'
$os = Get-CimInstance Win32_OperatingSystem
$build = [Environment]::OSVersion.Version.Build
$isWindows10Production = $build -ge 19045 -and $build -lt 22000
$isWindows11Production = $build -ge 22000
if (-not ($isWindows10Production -or $isWindows11Production)) {
    throw "Windows 10 22H2 (build 19045) or Windows 11 is required. Detected: $($os.Caption), build $build."
}

if ($os.Caption -notmatch 'Pro|Enterprise|Education') {
    throw "Windows Pro, Enterprise, or Education is required. Home edition is not supported. Detected: $($os.Caption)."
}

if ($isWindows10Production) {
    Write-Warning 'Windows 10 reached end of standard support. Confirm this till is enrolled and activated for Microsoft ESU (or another applicable supported servicing programme) before go-live.'
}

$resolvedAppPath = [Environment]::ExpandEnvironmentVariables($AppPath)
if (-not (Test-Path -LiteralPath $resolvedAppPath -PathType Leaf)) {
    throw "OrderWeb POS was not found at '$resolvedAppPath'. Install the app first or supply -AppPath."
}

Write-Step 'Creating or validating the dedicated POS account'
$posUser = Get-LocalUser -Name $PosUserName -ErrorAction SilentlyContinue
if ($null -eq $posUser) {
    if (-not $CreatePosAccount) {
        throw "Local account '$PosUserName' does not exist. Re-run with -CreatePosAccount."
    }

    if ($null -eq $PosAccountPassword) {
        $PosAccountPassword = Read-Host "Enter a strong password for .\$PosUserName" -AsSecureString
    }

    if ($PSCmdlet.ShouldProcess("local user $PosUserName", 'Create standard POS account')) {
        $posUser = New-LocalUser -Name $PosUserName `
            -Password $PosAccountPassword `
            -Description 'Dedicated standard account for OrderWeb POS' `
            -AccountNeverExpires
    }
}

if ($null -eq $posUser) {
    if ($WhatIfPreference) {
        Write-Warning 'Account creation was skipped by -WhatIf; remaining account-specific actions cannot be simulated.'
        return
    }
    throw "Local account '$PosUserName' could not be created."
}

if (-not $posUser.Enabled) {
    if ($PSCmdlet.ShouldProcess("local user $PosUserName", 'Enable account')) {
        Enable-LocalUser -Name $PosUserName
    }
}

$administrators = Get-LocalGroup -SID 'S-1-5-32-544'
$isAdministrator = Get-LocalGroupMember -Group $administrators.Name -ErrorAction SilentlyContinue |
    Where-Object { $_.SID -eq $posUser.SID }
if ($isAdministrator) {
    if ($PSCmdlet.ShouldProcess("local user $PosUserName", 'Remove from local Administrators')) {
        Remove-LocalGroupMember -Group $administrators.Name -Member $PosUserName
    }
}

Write-Step 'Configuring UK time and clock synchronisation'
if ($PSCmdlet.ShouldProcess('Windows clock', 'Set UK timezone and enable time synchronisation')) {
    Set-TimeZone -Id 'GMT Standard Time'
    Set-Service -Name W32Time -StartupType Automatic
    Start-Service -Name W32Time -ErrorAction SilentlyContinue
    Invoke-NativeCommand -FilePath 'w32tm.exe' -Arguments @('/config', '/syncfromflags:manual', '/manualpeerlist:time.windows.com,0x9', '/update')
    Invoke-NativeCommand -FilePath 'w32tm.exe' -Arguments @('/resync', '/force') -IgnoreExitCode
}

Write-Step 'Disabling sleep and hibernation'
if ($PSCmdlet.ShouldProcess('active Windows power plan', 'Disable sleep and hibernation')) {
    Invoke-NativeCommand -FilePath 'powercfg.exe' -Arguments @('/hibernate', 'off')
    Invoke-NativeCommand -FilePath 'powercfg.exe' -Arguments @('/change', 'standby-timeout-ac', '0')
    Invoke-NativeCommand -FilePath 'powercfg.exe' -Arguments @('/change', 'standby-timeout-dc', '0')
}

Write-Step 'Enabling Windows firewall and Microsoft Defender real-time protection'
if ($PSCmdlet.ShouldProcess('Windows security controls', 'Enable firewall and Defender')) {
    Set-NetFirewallProfile -Profile Domain, Private, Public -Enabled True
    if (Get-Command Set-MpPreference -ErrorAction SilentlyContinue) {
        Set-MpPreference -DisableRealtimeMonitoring $false
    }
}

Write-Step 'Installing POS startup and Windows Assigned Access lock-down'
if ($PSCmdlet.ShouldProcess($deploymentRoot, 'Install startup task and Assigned Access policy')) {
    New-Item -ItemType Directory -Path $deploymentRoot -Force | Out-Null
    Copy-Item -LiteralPath $assignedAccessSource -Destination $assignedAccessTarget -Force

    $accountId = "$env:COMPUTERNAME\$PosUserName"
    $principal = New-ScheduledTaskPrincipal -UserId $accountId -LogonType Interactive -RunLevel Limited
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $accountId
    $settings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit (New-TimeSpan -Hours 0) `
        -MultipleInstances IgnoreNew `
        -RestartCount 5 `
        -RestartInterval (New-TimeSpan -Minutes 1)

    $appAction = New-ScheduledTaskAction -Execute $resolvedAppPath -WorkingDirectory (Split-Path -Parent $resolvedAppPath)
    Register-ScheduledTask -TaskName $appTaskName -Action $appAction -Trigger $trigger `
        -Principal $principal -Settings $settings -Description 'Starts OrderWeb POS whenever the dedicated POS user logs on.' -Force | Out-Null

    $escapedAppPath = [System.Security.SecurityElement]::Escape($resolvedAppPath)
    $escapedAccount = [System.Security.SecurityElement]::Escape($PosUserName)
    if ($isWindows11Production) {
        $assignedAccessConfiguration = @"
<?xml version="1.0" encoding="utf-8"?>
<AssignedAccessConfiguration xmlns="http://schemas.microsoft.com/AssignedAccess/2017/config"
                             xmlns:rs5="http://schemas.microsoft.com/AssignedAccess/201810/config"
                             xmlns:v5="http://schemas.microsoft.com/AssignedAccess/2022/config">
  <Profiles>
    <Profile Id="{2B1A46B7-7BC0-4D79-90D8-72B7A72EA43C}" Name="OrderWeb POS">
      <AllAppsList>
        <AllowedApps>
          <App DesktopAppPath="$escapedAppPath" />
        </AllowedApps>
      </AllAppsList>
      <v5:StartPins><![CDATA[{"pinnedList":[]}]]></v5:StartPins>
      <Taskbar ShowTaskbar="false" />
    </Profile>
  </Profiles>
  <Configs>
    <Config>
      <Account>$escapedAccount</Account>
      <DefaultProfile Id="{2B1A46B7-7BC0-4D79-90D8-72B7A72EA43C}" />
    </Config>
  </Configs>
</AssignedAccessConfiguration>
"@
    } else {
        # Windows 10 uses the legacy StartLayout schema. Windows 11 StartPins
        # (2022/v5) must not be sent to a Windows 10 Assigned Access CSP.
        $assignedAccessConfiguration = @"
<?xml version="1.0" encoding="utf-8"?>
<AssignedAccessConfiguration xmlns="http://schemas.microsoft.com/AssignedAccess/2017/config"
                             xmlns:rs5="http://schemas.microsoft.com/AssignedAccess/201810/config">
  <Profiles>
    <Profile Id="{2B1A46B7-7BC0-4D79-90D8-72B7A72EA43C}" Name="OrderWeb POS">
      <AllAppsList>
        <AllowedApps>
          <App DesktopAppPath="$escapedAppPath" rs5:AutoLaunch="true" />
        </AllowedApps>
      </AllAppsList>
      <StartLayout><![CDATA[
        <LayoutModificationTemplate xmlns="http://schemas.microsoft.com/Start/2014/LayoutModification"
                                    xmlns:defaultlayout="http://schemas.microsoft.com/Start/2014/FullDefaultLayout"
                                    xmlns:start="http://schemas.microsoft.com/Start/2014/StartLayout"
                                    Version="1">
          <LayoutOptions StartTileGroupCellWidth="6" />
          <DefaultLayoutOverride>
            <StartLayoutCollection>
              <defaultlayout:StartLayout GroupCellWidth="6">
                <start:Group Name="OrderWeb POS">
                  <start:DesktopApplicationTile Size="2x2" Column="0" Row="0"
                    DesktopApplicationLinkPath="%ALLUSERSPROFILE%\Microsoft\Windows\Start Menu\Programs\OrderWeb POS\OrderWeb POS.lnk" />
                </start:Group>
              </defaultlayout:StartLayout>
            </StartLayoutCollection>
          </DefaultLayoutOverride>
        </LayoutModificationTemplate>
      ]]></StartLayout>
      <Taskbar ShowTaskbar="false" />
    </Profile>
  </Profiles>
  <Configs>
    <Config>
      <Account>$escapedAccount</Account>
      <DefaultProfile Id="{2B1A46B7-7BC0-4D79-90D8-72B7A72EA43C}" />
    </Config>
  </Configs>
</AssignedAccessConfiguration>
"@
    }
    Set-Content -LiteralPath $assignedAccessXml -Value $assignedAccessConfiguration -Encoding UTF8

    $systemPrincipal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $systemAction = New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" `
        -Argument "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$assignedAccessTarget`" -ConfigurationPath `"$assignedAccessXml`""
    Register-ScheduledTask -TaskName $assignedAccessTaskName -Action $systemAction -Principal $systemPrincipal `
        -Description 'Applies the OrderWeb POS Assigned Access allow-list as LocalSystem.' -Force | Out-Null
    Start-ScheduledTask -TaskName $assignedAccessTaskName

    $deadline = (Get-Date).AddSeconds(45)
    do {
        Start-Sleep -Milliseconds 500
        $assignedAccessTask = Get-ScheduledTask -TaskName $assignedAccessTaskName
    } while ($assignedAccessTask.State -eq 'Running' -and (Get-Date) -lt $deadline)

    $assignedAccessTaskInfo = Get-ScheduledTaskInfo -TaskName $assignedAccessTaskName
    if ($assignedAccessTask.State -eq 'Running') {
        throw 'Timed out while applying Windows Assigned Access.'
    }
    if ($assignedAccessTaskInfo.LastTaskResult -ne 0) {
        throw "Windows Assigned Access failed with task result $($assignedAccessTaskInfo.LastTaskResult)."
    }

    Unregister-ScheduledTask -TaskName $assignedAccessTaskName -Confirm:$false
}

$systemDrive = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='$env:SystemDrive'"
$freePercent = [math]::Round(($systemDrive.FreeSpace / $systemDrive.Size) * 100, 1)
if ($freePercent -lt 20) {
    Write-Warning "System drive free space is $freePercent%. Free space should remain at 20% or higher (15% is the minimum)."
}

Write-Host "`nWindows till configuration completed for .\$PosUserName." -ForegroundColor Green
Write-Host 'Restart Windows, sign in to the POS account, and confirm the Assigned Access restrictions.'
Write-Host 'Run Test-OrderWebTill.ps1 from the administrator account after that first sign-in.'
Write-Host 'Automatic login is deliberately not configured by this script; use Microsoft Sysinternals Autologon only after confirming physical security.'
Write-Host 'UPS installation and BIOS restart-after-power-failure must be completed and signed off manually.'
