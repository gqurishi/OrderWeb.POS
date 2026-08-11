#Requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Mother', 'Child')]
    [string]$Role,

    [string]$DatabaseSetupPath = "$env:ProgramFiles\OrderWeb POS\OrderWeb.DatabaseSetup.exe",

    [string]$ConfigPath = "$env:ProgramData\OrderWebPOS\orderweb-database.json"
)

$checks = [System.Collections.Generic.List[object]]::new()
function Add-Check {
    param([string]$Check, [ValidateSet('PASS','FAIL','WARN','MANUAL')][string]$Status, [string]$Details)
    $checks.Add([pscustomobject]@{ Check = $Check; Status = $Status; Details = $Details })
}

$config = $null
try {
    $config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
    Add-Check 'Database config present' 'PASS' $ConfigPath
    Add-Check 'Application database user' $(if ($config.databaseUser -eq 'orderweb_app') { 'PASS' } else { 'FAIL' }) `
        $(if ($config.databaseUser -eq 'orderweb_app') { 'orderweb_app (not root)' } else { 'Application is not configured with orderweb_app.' })
    $strongPassword = -not [string]::IsNullOrWhiteSpace($config.databasePassword) -and
        $config.databasePassword.Length -ge 24 -and
        $config.databasePassword -cmatch '[A-Z]' -and
        $config.databasePassword -cmatch '[a-z]' -and
        $config.databasePassword -match '[0-9]' -and
        $config.databasePassword -match '[^A-Za-z0-9]'
    Add-Check 'Strong application password' $(if ($strongPassword) { 'PASS' } else { 'FAIL' }) `
        $(if ($strongPassword) { 'Password meets policy; value not displayed.' } else { 'Rotate the application password.' })
    $sslMode = if ($config.databaseSslMode) { $config.databaseSslMode } else { 'Preferred' }
    Add-Check 'MariaDB TLS requested' $(if ($sslMode -in @('Preferred','Required','VerifyCA','VerifyFull')) { 'PASS' } else { 'FAIL' }) "SslMode=$sslMode"
} catch {
    Add-Check 'Database config present' 'FAIL' $_.Exception.Message
}

if (Test-Path -LiteralPath $DatabaseSetupPath -PathType Leaf) {
    & $DatabaseSetupPath check-version --config-path $ConfigPath --required 28 --quiet
    Add-Check 'Database schema version 28' $(if ($LASTEXITCODE -eq 0) { 'PASS' } else { 'FAIL' }) `
        $(if ($LASTEXITCODE -eq 0) { 'Database meets this release requirement.' } else { 'Terminal must remain blocked until Mother migration 028 is applied.' })

    $securityOutput = & $DatabaseSetupPath connection-security --config-path $ConfigPath --json 2>$null
    if ($LASTEXITCODE -eq 0) {
        try {
            $connectionSecurity = $securityOutput | ConvertFrom-Json
            Add-Check 'Active MariaDB TLS session' $(if ($connectionSecurity.tlsActive) { 'PASS' } else { 'WARN' }) `
                $(if ($connectionSecurity.tlsActive) { "Cipher=$($connectionSecurity.cipher)" } else { 'TLS is preferred but the server did not negotiate it; configure certificates before requiring TLS.' })
        } catch {
            Add-Check 'Active MariaDB TLS session' 'WARN' 'Could not parse the connection-security result.'
        }
    } else {
        Add-Check 'Active MariaDB TLS session' 'WARN' 'Could not inspect the live MariaDB session.'
    }
} else {
    Add-Check 'Database schema version 28' 'FAIL' "Database setup utility missing: $DatabaseSetupPath"
    Add-Check 'Active MariaDB TLS session' 'WARN' 'Database setup utility is unavailable.'
}

if ($Role -eq 'Mother') {
    $service = @('OrderWebMariaDB','MariaDB','MySQL') | ForEach-Object { Get-CimInstance Win32_Service -Filter "Name='$_'" -ErrorAction SilentlyContinue } | Select-Object -First 1
    Add-Check 'MariaDB service automatic' $(if ($service -and $service.StartMode -eq 'Auto') { 'PASS' } else { 'FAIL' }) `
        $(if ($service) { "Name=$($service.Name); start=$($service.StartMode); state=$($service.State)" } else { 'MariaDB service not found.' })

    $manifestPath = "$env:ProgramData\OrderWebPOS\network-deployment.json"
    $manifest = if (Test-Path -LiteralPath $manifestPath) { Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json } else { $null }
    $expectedChildren = @($manifest.childIps)
    $allowRule = Get-NetFirewallRule -DisplayName 'OrderWeb POS - MariaDB from known Child tills' -ErrorAction SilentlyContinue
    $publicBlock = Get-NetFirewallRule -DisplayName 'OrderWeb POS - Block public MariaDB access' -ErrorAction SilentlyContinue
    $actualChildren = if ($allowRule) { @(($allowRule | Get-NetFirewallAddressFilter).RemoteAddress) } else { @() }
    $allowListMatches = $manifest -and
        @($expectedChildren | Where-Object { $_ -notin $actualChildren }).Count -eq 0 -and
        @($actualChildren | Where-Object { $_ -notin $expectedChildren }).Count -eq 0
    Add-Check 'Exact Child firewall allow-list' $(if ($allowListMatches) { 'PASS' } else { 'FAIL' }) `
        "Expected=$($expectedChildren -join ','); actual=$($actualChildren -join ',')"
    Add-Check 'Public MariaDB firewall block' $(if ($publicBlock -and $publicBlock.Enabled) { 'PASS' } else { 'FAIL' }) 'TCP 3306 must be blocked on the Public profile.'

    $broadRules = @(Get-NetFirewallRule -Direction Inbound -Action Allow -Enabled True -ErrorAction SilentlyContinue |
        Where-Object DisplayName -ne 'OrderWeb POS - MariaDB from known Child tills' |
        Where-Object {
            @($_ | Get-NetFirewallPortFilter -ErrorAction SilentlyContinue |
                Where-Object { $_.Protocol -eq 'TCP' -and $_.LocalPort -eq '3306' }).Count -gt 0
        })
    Add-Check 'No broad MariaDB firewall rules' $(if ($broadRules.Count -eq 0) { 'PASS' } else { 'FAIL' }) `
        $(if ($broadRules.Count -eq 0) { 'Only the exact Child allow-list can open TCP 3306.' } else { ($broadRules.DisplayName -join ', ') })

    try {
        $acl = Get-Acl -LiteralPath "$env:ProgramData\OrderWebPOS"
        $usersWrite = @($acl.Access | Where-Object {
            $_.IdentityReference -match 'Users$' -and
            ($_.FileSystemRights.ToString() -match 'Write|Modify|FullControl')
        }).Count -gt 0
        Add-Check 'ProgramData ACL restricted' $(if (-not $acl.AreAccessRulesProtected -or $usersWrite) { 'FAIL' } else { 'PASS' }) `
            "InheritanceProtected=$($acl.AreAccessRulesProtected); standard-user-write=$usersWrite"
    } catch {
        Add-Check 'ProgramData ACL restricted' 'FAIL' $_.Exception.Message
    }
}

Add-Check 'Private POS VLAN' 'MANUAL' 'Confirm POS terminals and printers are isolated from guest/public networks.'
Add-Check 'No internet TCP 3306 forwarding' 'MANUAL' 'Confirm router/firewall has no WAN port-forward or inbound rule for TCP 3306.'
Add-Check 'Address reservations' 'MANUAL' 'Confirm Mother, every Child, and every printer has a static address or DHCP reservation.'

$checks | Format-Table -AutoSize -Wrap
$failures = @($checks | Where-Object Status -eq 'FAIL')
if ($failures.Count) {
    Write-Error "$($failures.Count) required network/MariaDB check(s) failed."
    exit 1
}

Write-Host "`nAutomated network/MariaDB checks passed. Complete all MANUAL sign-offs before go-live." -ForegroundColor Green
