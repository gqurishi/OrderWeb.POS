# OrderWeb Client POS

OrderWeb Client POS is the separate .NET MAUI client application for Windows, Mac, iPad, and Android devices.

The Client POS is always a client terminal. It never becomes the Mother POS, never connects directly to MariaDB, and never asks the user for database credentials.

This directory inside the `POS-in-NET` repository is the authoritative Client POS source. Do not maintain a second editable copy elsewhere. A standalone copy may be made for testing or deployment, but changes must be brought back to this directory.

## Architecture Rule

```text
Mother POS + MariaDB = source of truth
Client POS + SQLite = local cache and device store
Client POS talks to Mother only through API and WebSocket
```

## Initial Folder Structure

```text
Client POS/
  Directory.Build.props
  OrderWeb.Client.sln
  README.md
  docs/
  OrderWeb.Client/
  OrderWeb.Client.Shared/
  OrderWeb.Client.Tests/
```

## Standalone Copy/Test

This folder is designed to be copied by itself to another development machine for Client POS testing.

Open:

```text
Client POS/OrderWeb.Client.sln
```

or build directly:

```text
dotnet build "OrderWeb.Client/OrderWeb.Client.csproj" -f net9.0-maccatalyst --no-restore
```

Do not copy generated build/runtime folders when moving to another machine:

```text
OrderWeb.Client/bin/
OrderWeb.Client/obj/
```

The local SQLite cache is created automatically on the device after pairing/bootstrap. For real Mother POS testing, the other machine still needs network access to the Mother POS API and WebSocket endpoints.

The cross-application API and ownership rules are documented in `../docs/MOTHER_CLIENT_POS_INTEGRATION.md`.

## Phase 0 Status

Phase 0 defines the product rules and Version 1 scope before any app code is built.

See:

- `docs/phase-0-product-decisions.md`
- `docs/phase-1-client-app-foundation.md`
- `docs/phase-2-demo-mode.md`
- `docs/phase-5-sqlite-cache.md`
- `docs/phase-6-bootstrap-sync.md`
- `docs/phase-7-login-and-permissions.md`
- `docs/phase-8-dashboard.md`
- `docs/phase-9-table-and-order-workflow.md`
- `docs/phase-10-collection-and-delivery.md`
- `docs/phase-12-print-requests.md`
- `docs/phase-13-online-orders.md`
- `docs/login-design-baseline.md`
- `docs/version-1-scope.md`
- `docs/project-structure.md`
