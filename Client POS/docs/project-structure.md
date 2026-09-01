# Client POS Project Structure

This folder is reserved for the new separate OrderWeb Client POS application.

## Proposed Structure

```text
Client POS/
  OrderWeb.Client.sln
  OrderWeb.Client/
    App shell and platform-specific MAUI client app
  OrderWeb.Client.Shared/
    Shared DTOs, contracts, models, constants, validation helpers
  OrderWeb.Client.Tests/
    Unit tests for client cache, sync, API contracts, and state handling
  docs/
    Planning, architecture, scope, and handoff documents
```

## Folder Responsibilities

### OrderWeb.Client

The MAUI application for:

- Windows
- Mac
- iPad
- Android

This project should contain:

- Connect to Mother UI.
- Demo Mode UI.
- Login and dashboard UI based on the Mother POS PIN login experience.
- POS screens.
- SQLite cache implementation.
- Mother API client.
- WebSocket client.
- Reconnect and diagnostics UI.

It should not contain direct MariaDB access.

### OrderWeb.Client.Shared

Shared client-side contracts and models:

- API request/response models.
- WebSocket event models.
- Permission constants.
- Bootstrap DTOs.
- Cached entity models.
- Common validation helpers.
- Design constants shared across Client screens where appropriate.

This project should not contain Mother-only services or MariaDB code.

### OrderWeb.Client.Tests

Tests for:

- SQLite cache logic.
- Bootstrap mapping.
- Event ordering/checkpoint handling.
- Permission display logic.
- Reconnect state rules.
- Demo Mode data behavior.
