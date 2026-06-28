# OrderWeb POS — Inno Setup Installers

Production installers call `OrderWeb.DatabaseSetup.exe` for all database work. The MAUI app never runs DDL on mother terminals.

## Scripts

| Script | Purpose |
|---|---|
| `OrderWebPOS-Mother.iss` | Fresh mother install |
| `OrderWebPOS-Child.iss` | Fresh child install |
| `OrderWebPOS-Update-Mother.iss` | Mother app update + DB migrate |
| `OrderWebPOS-Update-Child.iss` | Child app update + schema version check |

Shared Pascal helpers live in `shared/DatabaseSetup.iss.inc`.

## Flows

### Mother (fresh)

1. Install app + `OrderWeb.DatabaseSetup.exe`
2. Ensure MariaDB is installed/running
   - If missing, install bundled MariaDB LTS MSI as `OrderWebMariaDB`
   - Use the installer-entered password as the new MariaDB root password
3. Prompt for MariaDB root/admin password
4. `install-mother` → creates `orderweb_pos`, `orderweb_app`, random app password
5. Writes `C:\ProgramData\OrderWebPOS\orderweb-database.json`
6. `verify`
7. `check-version`
8. Launch app → Terminal Setup → Initial Admin wizard

### Child (fresh)

1. Install app + `OrderWeb.DatabaseSetup.exe` only
2. Optional: copy `orderweb-database.json` from mother PC
3. If config exists: `check-version`
4. Launch app → child pairing in Terminal Setup (IP + pairing code)
5. App blocks login if mother DB schema is behind (`ChildSchemaVersionGateService`)

### Mother (update)

1. Replace app files
2. Requires existing `orderweb-database.json`
3. `backup` → `migrate` → `verify` → `check-version`
4. Launch app

### Child (update)

1. Replace app files only
2. If `orderweb-database.json` exists: `check-version`
3. Launch app (pairing unchanged)

## Build (Windows)

```powershell
# 1. Publish app + setup exe into publish\win-x64
#    This uses self-contained .NET + Windows App SDK output for the POS app,
#    and a self-contained single-file OrderWeb.DatabaseSetup.exe.
powershell -ExecutionPolicy Bypass -File Installer\build-installer-inputs.ps1

# 2. Confirm the approved MariaDB MSI is present:
#    Installer\Prerequisites\MariaDB\mariadb-10.11.18-winx64.msi

# 3. Compile all installers (requires Inno Setup 6)
powershell -ExecutionPolicy Bypass -File Installer\compile-installers.ps1
```

Output: `Installer\Output\*.exe`

Runtime dependency policy:

- The Windows MAUI app is published with `--self-contained true`.
- The Windows App SDK files are included with `WindowsAppSDKSelfContained=true`.
- `OrderWeb.DatabaseSetup.exe` is published self-contained as a single-file helper.
- Customer PCs should not need a separate .NET Desktop Runtime install for these executables.
- The full publish folder must be installed; self-contained Windows App SDK output is not expected to be one small EXE.

## Manual CLI equivalents

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

Child (when config exists):

```bat
OrderWeb.DatabaseSetup.exe check-version
```

## Logs and config

| Path | Purpose |
|---|---|
| `C:\ProgramData\OrderWebPOS\orderweb-database.json` | App DB credentials (no root) |
| `C:\ProgramData\OrderWebPOS\install-manifest.json` | Install metadata (no secrets) |
| `C:\ProgramData\OrderWebPOS\installer-database-setup.log` | Installer DB command log |
| `C:\ProgramData\OrderWebPOS\mariadb-msi-install.log` | Verbose MariaDB MSI install log |
| `C:\ProgramData\OrderWebPOS\Backups\` | `.orderwebbackup` files |

## Exit codes

See `OrderWeb.DatabaseSetup/ExitCodes.cs`. Installers treat non-zero as failure and show a message mapped from the exit code.

## Notes

- Only the **mother** terminal runs migrations.
- Child terminals never call `install-mother` or `migrate`.
- Use the same `AppId` for fresh + update scripts of the same terminal type so upgrades replace the existing install.
- Normal POS updates do not upgrade MariaDB. Database engine upgrades must be special, tested installer releases.
