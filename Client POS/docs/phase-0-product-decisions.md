# Phase 0: Product Decisions

## Job 0.1: Client POS Rules

These rules define the new OrderWeb Client POS product.

### Core Rules

- Client POS is always a Client.
- Client POS never becomes Mother POS.
- Client POS never asks for MariaDB credentials.
- Client POS never connects directly to MariaDB.
- Client POS uses SQLite only as a local cache and device store.
- Mother POS and MariaDB remain the source of truth.
- Client POS talks only to Mother POS through local API and WebSocket.
- Client POS can use Demo Mode before a real Mother connection exists.

## Product Boundary

### Client POS Owns

- Device pairing experience.
- Local device identity.
- Secure storage for Client token/session metadata.
- SQLite cache for fast display.
- Client UI for daily POS operation.
- API calls to Mother POS for commands.
- WebSocket listener for live updates.
- Reconnect and diagnostics experience.
- Demo Mode for design and development.

### Client POS Does Not Own

- MariaDB setup.
- MariaDB schema migrations.
- Database backup or restore.
- Cloud API secrets.
- Direct online MySQL/MariaDB access.
- Master online order processing.
- Shared printer routing decisions.
- Final gift card or loyalty balance authority.
- Final payment/order/table authority.
- Full system setup.

## Data Authority

```text
Mother POS decides final truth.
Client POS displays cached state and requests actions.
```

The Client can cache menu, tables, open orders, online orders, reservations, recent customers, permissions, and sync metadata. The Mother must validate and commit every real business action.

## Demo Mode

Demo Mode exists so Client screens can be designed and tested before the Mother API is complete.

Demo Mode should:

- Load sample restaurant data.
- Show a visible Demo Mode status.
- Allow navigation through the main POS workflow.
- Avoid real sales, real cloud sync, and real database writes to Mother.

