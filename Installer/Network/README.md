# OrderWeb POS Network and MariaDB Deployment

This release requires database schema version `26`. Child terminals block when the Mother database is below `26`; version `25` is no longer sufficient.

## Router and switch preparation

Before running the Mother script:

- create a dedicated private POS VLAN/subnet with no route from the guest Wi-Fi;
- reserve or statically assign the Mother, every Child, and every printer IP;
- do not configure any WAN/NAT/port-forward rule for TCP `3306`;
- allow required outbound internet access for OrderWeb services, Windows Update, and time synchronisation;
- record switch ports, VLAN ID, device name, MAC address, and reserved IP in the site deployment record.

## Harden the Mother

Install OrderWeb POS and MariaDB first. Then open Windows PowerShell as Administrator:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\Installer\Network\Configure-OrderWebNetwork.ps1 `
  -MotherIp 192.168.50.10 `
  -ChildIp 192.168.50.21,192.168.50.22 `
  -PrinterIp 192.168.50.31,192.168.50.32
```

The script prompts securely for the MariaDB administrator password. It does not place that password in command-line arguments, logs, configuration, or Git.

It then:

- verifies exact RFC1918 private addresses and the Mother address assigned to this PC;
- warns when the Mother still uses DHCP so the router reservation can be confirmed;
- disables legacy inbound firewall rules that broadly allow TCP `3306`;
- blocks TCP `3306` on the Public Windows Firewall profile;
- allows TCP `3306` on Private/Domain profiles only from the exact Child IP list;
- replaces MariaDB wildcard `orderweb_app` hosts with exact Child IP accounts;
- keeps MariaDB running with Automatic startup;
- restricts `C:\ProgramData\OrderWebPOS` to Administrators/System full control and standard users read-only;
- records a non-secret network deployment manifest.

Use `-RequireTls` only after installing and validating MariaDB server certificates on every Child. Without it, the application uses `SslMode=Preferred`: it negotiates TLS when the server supports it and otherwise remains compatible while certificate deployment is completed. See the [MariaDB account TLS options](https://mariadb.com/docs/server/reference/sql-statements/account-management-sql-statements/alter-user) and [MySqlConnector TLS connection options](https://mysqlconnector.net/connection-options/).

## Verify every terminal

Mother:

```powershell
.\Installer\Network\Test-OrderWebNetwork.ps1 -Role Mother
```

Each Child:

```powershell
.\Installer\Network\Test-OrderWebNetwork.ps1 -Role Child
```

The verifier checks that the application uses `orderweb_app`, the password meets production policy without displaying it, TLS is requested, and schema version `26` is available. It exits with code `1` on a required failure.

## Final manual tests

- [ ] From guest Wi-Fi, the Mother and printer IPs are unreachable.
- [ ] From outside the site, TCP `3306` is closed and no port-forward exists.
- [ ] Each approved Child connects to the Mother and an unapproved device/IP cannot connect.
- [ ] Mother, Child, and printer addresses remain unchanged after router and device restarts.
- [ ] MariaDB starts automatically after a Mother reboot.
- [ ] Every Child blocks login/operation while tested against a database below schema `26`.
- [ ] MariaDB TLS is active on a Child session before `-RequireTls` is enabled.
- [ ] The router/VLAN/firewall configuration is backed up.
