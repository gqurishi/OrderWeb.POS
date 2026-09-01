# Mother POS and Client POS Integration

## Source of truth

- The repository root contains Mother POS, which owns MariaDB and the authoritative restaurant data.
- `Client POS/` is the only authoritative Client POS source directory.
- Client POS has its own solution, SDK selection, build output, release lifecycle, and local SQLite cache.
- Generated files (`bin/`, `obj/`, packages, local databases, logs, and user settings) are not versioned.
- Standalone Client POS copies are deployment or test copies, not editable sources of truth.

## System boundary

Client POS must communicate with Mother POS only through the Mother HTTP API and WebSocket endpoint. It must not connect directly to MariaDB or require Mother database credentials.

Mother POS owns:

- pairing-code validation and terminal activation;
- terminal-token issuance, validation, revocation, and disablement;
- staff PIN validation and session issuance;
- authoritative menu, order, customer, payment, printing, and terminal state;
- heartbeat receipt and WebSocket event publication.

Client POS owns:

- device identity and pairing UI;
- secure persistence of terminal and user-session credentials;
- its local SQLite cache and offline queue;
- Client UI, local interaction state, and reconnect behaviour;
- sending API commands and consuming Mother events.

## Current connection contract

| Stage | Mother endpoint | Client requirement |
|---|---|---|
| Pairing/bootstrap | `POST /api/client/bootstrap` | Send pairing code, terminal name, device identity, platform, and app version. Accept the current flat activation response and the legacy nested `payload` response. |
| Login | `POST /api/client/login` | Send PIN, terminal token, and app version. Persist the returned user session token. |
| Heartbeat | `POST /terminals/heartbeat` | Send terminal ID and terminal token with device/status details. |
| Live events | `ws://<mother>:<port>/ws?token=<terminal-token>` | URL-encode the token and require an HTTP `101 Switching Protocols` response. |

The bootstrap response must provide a non-empty terminal ID, terminal token, API base URL or sufficient host/port data to derive it, and WebSocket URL or sufficient host/port/path data to derive it.

## Compatibility rules

1. A Mother API contract change and its Client consumer change should be made and reviewed together in this repository.
2. Existing request names and response fields must remain compatible until all deployed Client POS versions have been upgraded.
3. Additive response fields are preferred. Renaming or moving a field requires a compatibility period in Mother POS.
4. Authentication failures must be distinguishable from network failures and invalid response parsing.
5. HTTP `200` with `success: true` is a successful bootstrap; subsequent WebSocket or heartbeat errors must be reported as a separate connection stage.
6. Secrets and complete tokens must not be written to normal logs or user-facing diagnostics.
7. Pairing codes are one-time credentials. A retry after successful activation must reuse saved terminal credentials rather than consuming the pairing code again.

## Coordinated verification

Before releasing either application:

1. Build Mother POS and Client POS independently.
2. Create a new Client terminal and pairing code in Mother POS.
3. Verify bootstrap returns HTTP `200` and Client persists the activation credentials.
4. Verify the WebSocket upgrades with HTTP `101` using the issued token.
5. Verify PIN login and user-session persistence.
6. Verify heartbeat updates Mother terminal health.
7. Disable and re-enable the terminal from Mother POS and verify Client behaviour.
8. Restart both applications and verify reconnect without re-pairing.

## Build boundaries

- Mother POS uses the root solution and its root SDK/build configuration.
- Client POS uses `Client POS/OrderWeb.Client.sln` and `Client POS/global.json`.
- Do not add Client build outputs to the Mother solution or commit generated artifacts.
