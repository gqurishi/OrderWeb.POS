MariaDB Server MSI bundled with the mother installer.

Approved bundled version:
- mariadb-10.11.18-winx64.msi
- SHA256: f4eb5d16580c9ba1cd2789cd7b4019e0b6f55bc889bbf17e71c45edf7862fcdf

Policy:
- Use approved MariaDB LTS for new installs.
- Do not auto-upgrade MariaDB during normal POS app updates.
- MariaDB engine upgrades should be special installer releases only.

Fresh Mother install behavior:
- If MariaDB is already installed, the installer starts the existing service and uses the entered root/admin password.
- If MariaDB is missing, the installer silently installs this MSI with:
  - SERVICENAME=OrderWebMariaDB
  - PORT=3306
  - PASSWORD=<installer-entered-root-password>
  - UTF8=1
  - STDCONFIG=1
- The installer then runs:
  OrderWeb.DatabaseSetup.exe install-mother

If no MSI is present, the mother installer requires MariaDB to already be installed and running on the PC.
