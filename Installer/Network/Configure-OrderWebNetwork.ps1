#Requires -Version 5.1
#Requires -RunAsAdministrator

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string]$MotherIp,

    [string[]]$ChildIp = @(),

    [string[]]$PrinterIp = @(),

    [string]$DatabaseSetupPath = "$env:ProgramFiles\OrderWeb POS\OrderWeb.DatabaseSetup.exe",

    [string]$MariaDbAdminUser = 'root',

    [SecureString]$MariaDbAdminPassword,

    [switch]$RequireTls
)

$ErrorActionPreference = 'Stop'
$allowRuleName = 'OrderWeb POS - MariaDB from known Child tills'
$publicBlockRuleName = 'OrderWeb POS - Block public MariaDB access'
$programDataPath = Join-Path $env:ProgramData 'OrderWebPOS'
$manifestPath = Join-Path $programDataPath 'network-deployment.json'

function ConvertTo-PrivateIpv4 {
    param([Parameter(Mandatory = $true)][string]$Value)

    $address = $null
    if (-not [Net.IPAddress]::TryParse($Value, [ref]$address) -or
        $address.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork) {
        throw "'$Value' is not an exact IPv4 address."
    }

    $bytes = $address.GetAddressBytes()
    $isPrivate = $bytes[0] -eq 10 -or
        ($bytes[0] -eq 172 -and $bytes[1] -ge 16 -and $bytes[1] -le 31) -or
        ($bytes[0] -eq 192 -and $bytes[1] -eq 168)
    if (-not $isPrivate) {
        throw "'$Value' is not an RFC1918 private IPv4 address."
    }

    return $address.ToString()
}

function Get-MariaDbService {
    foreach ($name in @('OrderWebMariaDB', 'MariaDB', 'MySQL')) {
        $service = Get-Service -Name $name -ErrorAction SilentlyContinue
        if ($service) { return $service }
    }
    return $null
}

$MotherIp = ConvertTo-PrivateIpv4 $MotherIp
$ChildIp = @($ChildIp | ForEach-Object { ConvertTo-PrivateIpv4 $_ } | Sort-Object -Unique)
$PrinterIp = @($PrinterIp | ForEach-Object { ConvertTo-PrivateIpv4 $_ } | Sort-Object -Unique)

$duplicates = @($ChildIp + $PrinterIp | Group-Object | Where-Object Count -gt 1)
if ($ChildIp -contains $MotherIp -or $PrinterIp -contains $MotherIp -or $duplicates.Count -gt 0) {
    throw 'Mother, Child, and printer IP addresses must be unique.'
}

$motherAddress = Get-NetIPAddress -AddressFamily IPv4 -IPAddress $MotherIp -ErrorAction SilentlyContinue |
    Where-Object { $_.AddressState -eq 'Preferred' } |
    Select-Object -First 1
if (-not $motherAddress) {
    throw "The Mother IP $MotherIp is not currently assigned to this computer. Configure the static address or DHCP reservation first."
}

$profile = Get-NetConnectionProfile -InterfaceIndex $motherAddress.InterfaceIndex -ErrorAction SilentlyContinue
if ($profile.NetworkCategory -eq 'Public') {
    throw "The Mother network is Public. Change it to Private before allowing MariaDB."
}

$adapterConfig = Get-CimInstance Win32_NetworkAdapterConfiguration -Filter "InterfaceIndex=$($motherAddress.InterfaceIndex)"
if ($adapterConfig.DHCPEnabled) {
    Write-Warning 'The Mother adapter uses DHCP. Confirm a router DHCP reservation for this MAC/IP before go-live.'
}

if (-not (Test-Path -LiteralPath $DatabaseSetupPath -PathType Leaf)) {
    throw "Database setup utility not found at '$DatabaseSetupPath'."
}

if ($null -eq $MariaDbAdminPassword) {
    $MariaDbAdminPassword = Read-Host "Enter the MariaDB administrator password for '$MariaDbAdminUser'" -AsSecureString
}

if ($PSCmdlet.ShouldProcess('MariaDB and Windows Firewall', 'Apply exact Child allow-list and network hardening')) {
    $service = Get-MariaDbService
    if (-not $service) {
        throw 'MariaDB Windows service was not found.'
    }
    Set-Service -Name $service.Name -StartupType Automatic
    Start-Service -Name $service.Name

    Get-NetFirewallRule -Direction Inbound -Action Allow -Enabled True -ErrorAction SilentlyContinue |
        Where-Object DisplayName -ne $allowRuleName |
        ForEach-Object {
            $rule = $_
            $matches3306 = @($rule | Get-NetFirewallPortFilter -ErrorAction SilentlyContinue |
                Where-Object { $_.Protocol -eq 'TCP' -and $_.LocalPort -eq '3306' }).Count -gt 0
            if ($matches3306) {
                Write-Host "Disabling broad/legacy MariaDB firewall rule: $($rule.DisplayName)"
                Disable-NetFirewallRule -InputObject $rule
            }
        }

    Remove-NetFirewallRule -DisplayName $allowRuleName -ErrorAction SilentlyContinue
    Remove-NetFirewallRule -DisplayName $publicBlockRuleName -ErrorAction SilentlyContinue
    Set-NetFirewallProfile -Profile Domain, Private, Public -Enabled True -DefaultInboundAction Block

    New-NetFirewallRule -DisplayName $publicBlockRuleName -Direction Inbound -Action Block `
        -Protocol TCP -LocalPort 3306 -Profile Public | Out-Null
    if ($ChildIp.Count -gt 0) {
        New-NetFirewallRule -DisplayName $allowRuleName -Direction Inbound -Action Allow `
            -Protocol TCP -LocalPort 3306 -RemoteAddress $ChildIp -Profile Domain, Private | Out-Null
    }

    New-Item -ItemType Directory -Path $programDataPath -Force | Out-Null
    $aclArguments = @(
        $programDataPath,
        '/inheritance:r',
        '/grant:r', '*S-1-5-32-544:(OI)(CI)F',
        '/grant:r', '*S-1-5-18:(OI)(CI)F',
        '/grant:r', '*S-1-5-32-545:(OI)(CI)RX'
    )
    & "$env:SystemRoot\System32\icacls.exe" @aclArguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not restrict '$programDataPath'." }

    $plainPassword = [Net.NetworkCredential]::new('', $MariaDbAdminPassword).Password
    try {
        $env:ORDERWEB_ROOT_PASSWORD = $plainPassword
        $arguments = @('set-child-access', '--root-user', $MariaDbAdminUser, '--child-ips', ($ChildIp -join ','), '--quiet')
        if ($RequireTls) { $arguments += '--require-tls' }
        & $DatabaseSetupPath @arguments
        if ($LASTEXITCODE -ne 0) { throw "Database Child allow-list failed with exit code $LASTEXITCODE." }
    } finally {
        Remove-Item Env:\ORDERWEB_ROOT_PASSWORD -ErrorAction SilentlyContinue
        $plainPassword = $null
    }

    $manifest = [ordered]@{
        configuredAtUtc = [DateTime]::UtcNow.ToString('O')
        motherIp = $MotherIp
        childIps = $ChildIp
        printerIps = $PrinterIp
        mariaDbPort = 3306
        requiredSchemaVersion = 26
        tlsMode = $(if ($RequireTls) { 'Required' } else { 'Preferred' })
    }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    & "$env:SystemRoot\System32\icacls.exe" $manifestPath '/inheritance:r' '/grant:r' '*S-1-5-32-544:F' '/grant:r' '*S-1-5-18:F' '/grant:r' '*S-1-5-32-545:R' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not secure '$manifestPath'." }
}

Write-Host 'Mother network and MariaDB access hardening completed.' -ForegroundColor Green
Write-Host "Allowed Child IPs: $(if ($ChildIp.Count) { $ChildIp -join ', ' } else { 'none' })"
Write-Host 'Complete the router/VLAN/printer reservation checklist, then run Test-OrderWebNetwork.ps1 on every terminal.'
