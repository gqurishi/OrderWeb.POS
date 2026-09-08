# Client POS Product Rules

These rules are frozen and must be applied by Mother and Client.

- Client communicates only with its paired Mother POS.
- Mother alone communicates with MariaDB, network/IP printers, and OrderWeb cloud services.
- Mother decides which features and capabilities each Client terminal receives.
- There is one Mother POS authority per restaurant. Client never performs a second cloud login.
- Web Orders, printer setup, menu administration, users/roles, database management, backups, cloud settings, and full reports are Mother-only.
- Mother may grant a Client: tables, collection, delivery, live orders, reservations, customers, payments, gift cards, and loyalty.
- Reservations are disabled by default and appear only when Mother enables them for that terminal.
- Client may request a print, but Mother resolves and controls the configured network/IP printer.

## Collection multi-terminal (Phase 0 frozen)

- Mother MariaDB is the only real Collection order; Client cache is never the master.
- Create / edit / save / pay / void / kitchen print for Collection require Mother online.
- Offline: viewing a cached open-order list is allowed; edit/save is blocked.
- Full reopen-and-edit from any Client is Phase 2+; see `docs/ClientPOS/COLLECTION_MULTI_TERMINAL_RULES.md`.
- Executable mirror: `OrderWeb.Contracts.Access.CollectionOrderHubRules`.

The executable source of truth for Client access lists is `OrderWeb.Contracts.Access.ClientAccessPolicy`.
