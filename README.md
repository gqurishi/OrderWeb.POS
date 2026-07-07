# POS-in-NET

POS-in-NET is a .NET MAUI point-of-sale application with a separate database setup utility and installer workflow for Mother and Child terminals.

## At A Glance

- App title: POS-in-NET
- App ID: `com.companyname.posinnet`
- Display version: `1.0`
- Build version: `1`
- Solution projects:
  - `POS-in-NET`
  - `OrderWeb.DatabaseSetup`
  - `OrderWeb.DatabaseSetup.Tests`

## Platform And Framework Versions

### Main App

- Framework: .NET 10 MAUI
- Target frameworks:
  - `net10.0-maccatalyst`
  - `net10.0-windows10.0.19041.0` on Windows
- Windows package type: None
- Minimum platform versions:
  - Windows: `10.0.17763.0`
  - iOS: `15.0`
  - MacCatalyst: `15.0`
  - Tizen: `6.5`

### Database Setup Tool

- Framework: `net10.0`
- Output: self-contained single-file helper
- Purpose: database create, backup, migrate, verify, and version checks

### Test Project

- Framework: `net10.0`
- Purpose: tests for the database setup tool

## Package Versions

### App Packages

- `Microsoft.Maui.Controls` `10.0.80`
- `Microsoft.Extensions.Logging.Debug` `10.0.9`
- `Microsoft.WindowsAppSDK` `2.2.0` on Windows
- `MySqlConnector` `2.6.1`
- `Newtonsoft.Json` `13.0.3`
- `BCrypt.Net-Next` `4.0.3`
- `CommunityToolkit.Maui` `14.2.0`
- `CommunityToolkit.Mvvm` `8.4.2`

### Syncfusion Packages

All Syncfusion UI and PDF packages are pinned to `33.2.15`:

- `Syncfusion.Maui.Core`
- `Syncfusion.Maui.DataGrid`
- `Syncfusion.Maui.Charts`
- `Syncfusion.Maui.Scheduler`
- `Syncfusion.Maui.ListView`
- `Syncfusion.Maui.Inputs`
- `Syncfusion.Maui.Buttons`
- `Syncfusion.Maui.TabView`
- `Syncfusion.Maui.Popup`
- `Syncfusion.Maui.Calendar`
- `Syncfusion.Pdf.Net.Core`

### Database Setup Packages

- `MySqlConnector` `2.6.1`

### Test Packages

- `Microsoft.NET.Test.Sdk` `17.12.0`
- `xunit` `2.9.2`
- `xunit.runner.visualstudio` `2.8.2`

## Database Stack

- Database connector: MySqlConnector
- Server family: MySQL / MariaDB
- Default application DB port: `3306`
- App database config keys:
  - `db_host`
  - `db_database`
  - `db_username`
  - `db_password`
  - `db_port`

### Important Database Notes

- The MAUI app does not run DDL directly on Mother terminals.
- Database work is handled by `OrderWeb.DatabaseSetup.exe`.
- The installer documentation references a bundled MariaDB MSI:
  - `mariadb-10.11.18-winx64.msi`
- The repo does not pin a separate MySQL server version inside the app project itself.

## Installer And Deployment

- Unified installer script: `Installer/OrderWebPOS-Setup.iss`
- Legacy scripts are still present for reference and testing:
  - `Installer/OrderWebPOS-Mother.iss`
  - `Installer/OrderWebPOS-Child.iss`
  - `Installer/OrderWebPOS-Update-Mother.iss`
  - `Installer/OrderWebPOS-Update-Child.iss`
- Shared installer helpers: `Installer/shared/DatabaseSetup.iss.inc`

### Installer Flow

#### Mother terminal

1. Install the app and `OrderWeb.DatabaseSetup.exe`
2. Ensure MariaDB is installed and running
3. On first install, configure the MariaDB root password
4. Create the POS database and app account
5. Save DB configuration to `C:\ProgramData\OrderWebPOS\orderweb-database.json`
6. Verify schema and version
7. Launch the app and complete initial admin setup

#### Child terminal

1. Install the app and database helper
2. Optionally copy `orderweb-database.json` from the Mother terminal
3. Check schema version if config exists
4. Pair with the Mother terminal using the pairing flow in Terminal Setup
5. Block login if the Mother DB schema is behind

### Build Output Notes

- The Windows app is published self-contained
- Windows App SDK output is bundled with the app
- `OrderWeb.DatabaseSetup.exe` is published self-contained and single-file
- Typical setup output: `Installer\Output\OrderWebPOS-Setup-1.0.0.exe`

## Build And Run

### Windows App Build

The repo includes a Windows Debug task that builds the MAUI app for:

- `net10.0-windows10.0.19041.0`

### Installer Build

1. Publish app and setup helper into `publish\win-x64`
2. Ensure the MariaDB MSI is available in `Installer\Prerequisites\MariaDB\`
3. Compile the installer with Inno Setup 6

## Runtime And Data Paths

- Database config file: `C:\ProgramData\OrderWebPOS\orderweb-database.json`
- Install manifest: `C:\ProgramData\OrderWebPOS\install-manifest.json`
- Installer DB log: `C:\ProgramData\OrderWebPOS\installer-database-setup.log`
- MariaDB install log: `C:\ProgramData\OrderWebPOS\mariadb-msi-install.log`
- Backup files: `C:\ProgramData\OrderWebPOS\Backups\`

## Security Level

Security level: **moderate business-app security**.

### What Is Protected

- User passwords are stored as hashes, not plain text.
- Authentication uses `BCrypt.Net-Next` for password verification.
- Role-based access control is enforced through `RoleAccessService`.
- Sensitive admin-only areas such as user management, reports, printer setup, terminal health, and settings are restricted by role.
- Cash drawer access is limited to managers and admins.
- Login and PIN validation are checked against the database with short timeouts and activity logging.

### Database And Credential Handling

- The app connects to MariaDB/MySQL through `MySqlConnector`.
- Database credentials are stored in the local `orderweb-database.json` file under `C:\ProgramData\OrderWebPOS\`.
- The installer and database setup tool use root/app credentials during provisioning, then write the app database config for runtime use.
- The repo does not show full disk encryption, secrets vaulting, or 2FA.

### Security Boundaries

- Child terminals are blocked from privileged features unless the current role allows them.
- The MAUI app does not run database schema changes directly on Mother terminals; schema work is handled by `OrderWeb.DatabaseSetup.exe`.
- The app logs authentication activity and denies access when a route is not allowed for the current role.

### Security Limitations

- No evidence in the repo of end-to-end encryption for all app traffic.
- No evidence of multi-factor authentication.
- Security depends on proper MariaDB password handling, Windows machine access control, and network protection.
- This is not a certified security audit or compliance statement; it is a codebase-based summary of the implemented controls.

## Repository Layout

- `Features/` contains the main app features such as Authentication, Menu, Orders, Payments, Printing, Reports, Restaurant, Settings, Terminal, and TimeClock.
- `OrderWeb.DatabaseSetup/` contains the standalone database administration tool.
- `OrderWeb.DatabaseSetup.Tests/` contains tests for the database setup tool.
- `Installer/` contains the installer scripts and packaging assets.
- `Database/` contains SQL migrations, seeds, and schema documentation.

## Notes

- `Directory.Build.props` disables strict Xcode version checking for the MacCatalyst build.
- The app uses `MySqlConnector` 2.6.1 for cross-platform database access instead of `MySql.Data`.
- The solution currently targets .NET 10 across the app, database tool, and tests.
