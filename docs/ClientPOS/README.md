# OrderWeb Client POS

OrderWeb Client POS is the separate .NET MAUI client application for Windows, Mac, iPad, and Android devices.

The Client POS is always a client terminal. It never becomes the Mother POS, never connects directly to MariaDB, and never asks the user for database credentials.

The Client POS source lives at `src/OrderWeb.Client` in the repository. Do not maintain a second editable copy elsewhere.

## Architecture Rule

```text
Mother POS + MariaDB = source of truth
Client POS + SQLite = local cache and device store
Client POS talks to Mother only through API and WebSocket
```

## Initial Folder Structure

```text
src/OrderWeb.Client/
  README.md
  docs/
tests/OrderWeb.Client.Tests/
```

## Standalone Copy/Test

The Client POS is developed from the repository root with the unified solution.

Open:

```text
OrderWeb.POS.sln
```

or build directly:

```text
dotnet build "src/OrderWeb.Client/OrderWeb.Client.csproj" -f net10.0-maccatalyst --no-restore
```

Do not copy generated build/runtime folders when moving to another machine:

```text
OrderWeb.Client/bin/
OrderWeb.Client/obj/
```

The local SQLite cache is created automatically on the device after pairing/bootstrap. For real Mother POS testing, the other machine still needs network access to the Mother POS API and WebSocket endpoints.

The cross-application API and ownership rules are documented in `../docs/MOTHER_CLIENT_POS_INTEGRATION.md`.

Client role, Manager-operation, offline, audit, verification, and release policy is documented in `CLIENT_ROLE_POLICY.md` and `CLIENT_OPERATIONS_AND_RELEASE.md`.

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
