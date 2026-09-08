# Mother POS and Client POS Integration

## Standing rule

Client talks only to Mother. Mother talks to MariaDB, printers, and cloud. Mother decides what each Client can see and do. Web orders stay on Mother. Reservations may go to Client.

There is one Mother POS per restaurant. Client POS never stores OrderWeb cloud credentials and never calls the cloud. Network and IP printers are configured and driven on Mother only.

The machine-readable copy of these lists lives in `OrderWeb.Contracts.Access.ClientAccessPolicy`.

## Source of truth

- The repository root contains Mother POS, which owns MariaDB and the authoritative restaurant data.
- `src/OrderWeb.Client` is the only authoritative Client POS source directory.
- Client POS has its own build output, release lifecycle, and local SQLite cache.
- Generated files (`bin/`, `obj/`, packages, local databases, logs, and user settings) are not versioned.
- Standalone Client POS copies are deployment or test copies, not editable sources of truth.

## System boundary

Client POS must communicate with Mother POS only through the Mother HTTP API and WebSocket endpoint. It must not connect directly to MariaDB, require Mother database credentials, or log in to OrderWeb cloud.

Mother POS owns:

- pairing-code validation and terminal activation;
- terminal-token issuance, validation, revocation, and disablement;
- staff PIN validation and session issuance;
- authoritative menu, order, customer, payment, printing, and terminal state;
- MariaDB, network/IP printers, and the restaurant cloud connection;
- website food orders (web orders);
- heartbeat receipt and WebSocket event publication;
- the access list that each Client terminal may use.

Client POS owns:

- device identity and pairing UI;
- secure persistence of terminal and user-session credentials;
- its local SQLite cache and offline queue;
- Client UI, local interaction state, and reconnect behaviour;
- sending API commands and consuming Mother events.

## Client access Mother may grant

Mother may turn these on for a Client terminal. Client must not invent extra items.

| Area | Client route / feature |
|---|---|
| Tables | `restaurant` / dine-in |
| Collection | `collection` |
| Delivery | `delivery` |
| Live orders | `liveorder` |
| Reservations | `reservation` |
| Customers | `customers` |
| Payments / cash drawer | `payments`, `cashdrawer` |
| Gift cards | `giftcards` |
| Loyalty | `loyalty` |

Reservations arrive at Mother first (including website bookings). Mother may then share them with a Client. Gift-card and loyalty work on Client must still go Client → Mother → cloud → Mother → Client.

## Blocked on every Client

These stay on Mother. Client must not show them, and Mother must not serve them to a Client session.

| Area | Why it stays on Mother |
|---|---|
| Web orders | Website food orders land on Mother only |
| Printer setup | Network / IP printers belong to Mother |
| Menu admin | Menu is configured on Mother |
| Users / staff admin | Administrator work on Mother |
| Database / backups | MariaDB lives on Mother |
| Cloud settings | Only Mother talks to OrderWeb cloud |
| Full reports | End-of-day and cloud reports stay on Mother |

Printer *use* is allowed from Client: Client may ask Mother to print. Printer *setup* is not.

## How Mother grants Client access

Mother stores a per-terminal list in `client_terminal_access`. Terminal Health has an **Access** button on each paired Client.

Default for a new Client: restaurant, collection, delivery, live orders, customers, payments, gift cards, and loyalty. **Reservations stay off** until Mother turns them on for that terminal. Web Orders is never grantable.

At PIN login Mother sends `features` and `routes`. Client shows only those tiles and menus. Mother also returns 403 if the Client calls an API for a feature that is off. Staff must sign in again on the Client after Mother changes the list.

| Stage | Mother endpoint | Client requirement |
|---|---|---|
| Pairing/bootstrap | `POST /api/client/bootstrap` | Send pairing code, terminal name, device identity, platform, and app version. Accept the current flat activation response and the legacy nested `payload` response. |
| Login | `POST /api/client/login` | Send PIN, terminal token, and app version. Persist the returned user session token only after a successful non-Admin login. Use only the capabilities and features Mother returned, intersected with `ClientAccessPolicy`. |
| Heartbeat | `POST /terminals/heartbeat` | Send terminal ID and terminal token with device/status details. |
| Live events | `ws://<mother>:<port>/ws?token=<terminal-token>` | URL-encode the token and require an HTTP `101 Switching Protocols` response. |

The bootstrap response must provide a non-empty terminal ID, terminal token, API base URL or sufficient host/port data to derive it, and WebSocket URL or sufficient host/port/path data to derive it.

An Administrator PIN submitted through a Client POS returns `403 Forbidden` with `errorCode: "admin_mother_only"`. Mother POS must not issue a Client session token for that request; Client POS must direct the user to Mother POS.

## Compatibility rules

1. A Mother API contract change and its Client consumer change should be made and reviewed together in this repository.
2. Existing request names and response fields must remain compatible until all deployed Client POS versions have been upgraded.
3. Additive response fields are preferred. Renaming or moving a field requires a compatibility period in Mother POS.
4. Authentication failures must be distinguishable from network failures and invalid response parsing.
5. HTTP `200` with `success: true` is a successful bootstrap; subsequent WebSocket or heartbeat errors must be reported as a separate connection stage.
6. Secrets and complete tokens must not be written to normal logs or user-facing diagnostics.
7. Pairing codes are one-time credentials. A retry after successful activation must reuse saved terminal credentials rather than consuming the pairing code again.

## Collection orders (multi-terminal — Phase 0 frozen)

Mother is the hub and MariaDB is the only authoritative Collection order store. Any Client is a terminal: it may cache open orders for display, but create, edit, save, pay, void, kitchen, and print confirmation require Mother online. Client-to-Client Collection sync is not allowed. Full reopen-and-edit from any Client is a later Collection phase.

Details: `docs/ClientPOS/COLLECTION_MULTI_TERMINAL_RULES.md` and `OrderWeb.Contracts.Access.CollectionOrderHubRules`.

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
9. Submit an Admin PIN through Client POS and verify the `admin_mother_only` rejection creates no Client session or token.
10. Verify a legacy Admin Client token is rejected by every protected Mother API endpoint.
11. Verify Client menus do not include Web Orders, printer setup, menu admin, cloud settings, or full reports.
12. Verify Reservations remain available on Client when Mother has granted that feature.
13. Verify a website food order appears on Mother and not on Client.

## Build boundaries

- Mother POS uses the root solution and its root SDK/build configuration.
- Client POS uses `OrderWeb.POS.sln` and the repository-root `global.json`.
- Do not add Client build outputs to the Mother solution or commit generated artifacts.
