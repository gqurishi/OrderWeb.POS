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

The executable source of truth is `OrderWeb.Contracts.Access.ClientAccessPolicy`.
