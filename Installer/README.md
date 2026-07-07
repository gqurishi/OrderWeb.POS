# OrderWeb POS - Inno Setup Installer

Production installers call `OrderWeb.DatabaseSetup.exe` for all database work. The MAUI app never runs DDL on mother terminals.

## Main Setup

Use one setup for every PC. The wizard asks whether this PC is the Mother terminal or a Child terminal.

| Script | Purpose |
|---|---|
| `OrderWebPOS-Setup.iss` | Unified installer for Mother and Child terminals |

Legacy split scripts are still present for reference/testing:

| Script | Purpose |
|---|---|
| `OrderWebPOS-Mother.iss` | Fresh mother install |
| `OrderWebPOS-Child.iss` | Fresh child install |
| `OrderWebPOS-Update-Mother.iss` | Mother app update + DB migrate |
| `OrderWebPOS-Update-Child.iss` | Child app update + schema version check |

Shared Pascal helpers live in `shared/DatabaseSetup.iss.inc`.

## Flows

### Unified setup - Mother

1. Install app + `OrderWeb.DatabaseSetup.exe`
2. Ensure MariaDB is installed/running
   - If missing, install bundled MariaDB LTS MSI as `OrderWebMariaDB`
   - Use the installer-entered password as the new MariaDB root password
3. Prompt for MariaDB root/admin password on first Mother install
4. `install-mother` creates `orderweb_pos`, `orderweb_app`, and a random app password
5. Writes `C:\ProgramData\OrderWebPOS\orderweb-database.json`
6. `verify`
7. `check-version`
8. Launch app -> Terminal Setup -> Initial Admin wizard

If a Mother config already exists, the unified setup treats it as a Mother update/reinstall:

1. Replace app files
2. `backup`
3. `migrate`
4. `verify`
5. `check-version`

### Unified setup - Child

1. Install app + `OrderWeb.DatabaseSetup.exe` only
2. Optional: copy `orderweb-database.json` from the Mother PC
3. If config exists, run a non-blocking `check-version`
4. Launch app -> Child pairing in Terminal Setup
5. Enter Mother IP + pairing code created from Terminal Health on the Mother PC
6. App blocks login if the Mother DB schema is behind (`ChildSchemaVersionGateService`)

## Build (Windows)

```powershell
# 1. Publish app + setup exe into publish\win-x64
#    This uses self-contained .NET + Windows App SDK output for the POS app,
#    and a self-contained single-file OrderWeb.DatabaseSetup.exe.
powershell -ExecutionPolicy Bypass -File Installer\build-installer-inputs.ps1

# 2. Confirm the approved MariaDB MSI is present:
#    Installer\Prerequisites\MariaDB\mariadb-10.11.18-winx64.msi

# 3. Compile the unified installer (requires Inno Setup 6)
powershell -ExecutionPolicy Bypass -File Installer\compile-installers.ps1
```

Output: `Installer\Output\OrderWebPOS-Setup-1.0.0.exe`

To also compile the legacy split installers:

```powershell
powershell -ExecutionPolicy Bypass -File Installer\compile-installers.ps1 -IncludeLegacyInstallers
```

## Runtime Dependency Policy

- The Windows MAUI app is published with `--self-contained true`.
- The Windows App SDK files are included with `WindowsAppSDKSelfContained=true`.
- `OrderWeb.DatabaseSetup.exe` is published self-contained as a single-file helper.
- Customer PCs should not need a separate .NET Desktop Runtime install for these executables.
- The full publish folder must be installed; self-contained Windows App SDK output is not expected to be one small EXE.

## Manual CLI Equivalents

Mother fresh:

```bat
OrderWeb.DatabaseSetup.exe install-mother --root-user root --root-password %MARIA_ROOT_PWD%
OrderWeb.DatabaseSetup.exe verify
OrderWeb.DatabaseSetup.exe check-version
```

Mother update:

```bat
OrderWeb.DatabaseSetup.exe backup
OrderWeb.DatabaseSetup.exe migrate
OrderWeb.DatabaseSetup.exe verify
OrderWeb.DatabaseSetup.exe check-version
```

Child, when config exists:

```bat
OrderWeb.DatabaseSetup.exe check-version
```

## Logs And Config

| Path | Purpose |
|---|---|
| `C:\ProgramData\OrderWebPOS\orderweb-database.json` | App DB credentials, no root/admin password |
| `C:\ProgramData\OrderWebPOS\install-manifest.json` | Install metadata, no secrets |
| `C:\ProgramData\OrderWebPOS\installer-database-setup.log` | Installer DB command log |
| `C:\ProgramData\OrderWebPOS\mariadb-msi-install.log` | Verbose MariaDB MSI install log |
| `C:\ProgramData\OrderWebPOS\Backups\` | `.orderwebbackup` files |

## Notes

- Install the unified setup on the Mother PC first.
- On the Mother app, finish Terminal Setup and create the first admin user.
- From Mother Terminal Health, create a pairing code for each Child terminal.
- Install the same setup on each Child PC and choose Child Terminal.
- Normal POS updates do not upgrade MariaDB. Database engine upgrades must be special, tested installer releases.
