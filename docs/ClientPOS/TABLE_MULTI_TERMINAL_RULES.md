# Table Multi-Terminal Rules

**Scope:** Table (dine-in) orders and table busy / available status.

**Status:** Mother-hub parity with Collection/Delivery for create/open/edit/void/pay/live refresh, plus `tables.updated` for floor occupancy.

Executable mirror: `OrderWeb.Contracts.Access.CustomerOrderHubRules` (`IsTableOrderType` / `IsCustomerHubOrderType` / `MotherOwnsTableBusyStatus`).

---

## Frozen product rules

1. Mother MariaDB is the only real Table order and the only source of busy/in-use status.  
2. Client SQLite is cache only (floor + order).  
3. Mother is the hub — no Client-to-Client sync.  
4. Same Mother order id everywhere.  
5. Opening a free table opens the order-create screen (empty basket OK), occupies the Mother table session, and creates the ledger order on the first item.  
6. Void or full pay closes the session and sets the table Available.  
7. Online-required mutations for open/edit/pay/void/kitchen/print.  
8. Offline: view cached floor/list OK; open-for-edit / save blocked.  
9. Any Client may open/edit/finish any Mother Table order.

---

## Behaviours

| Capability | Status |
|---|---|
| Open free table → Mother session + order-create (empty basket OK; ledger on first item) | **Pass** |
| Reopen occupied table → `OpenOrderForEditAsync` (never wipe lines) | **Pass** |
| Live Order Table card → Mother reopen | **Pass** |
| Edit lines / conflict 409 | **Pass** |
| Live `order.updated` refreshes open order + Live Order | **Pass** |
| Session open/link/close → WS `tables.updated` → Clients refresh floor | **Pass** |
| Void / full cash pay → close session → Available everywhere | **Pass** |
| Offline gates for hub mutations | **Pass** |

**Done when:** Terminal 1 seats Table 5 → Terminal 3 floor shows busy; Terminal 3 opens Table 5 → sees same order; edits sync live; pay/void frees the table on every terminal.
