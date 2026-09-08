# Collection Multi-Terminal Rules (Phase 0 — Frozen)

**Scope:** Collection orders. Delivery and Table use the same Mother-hub pattern — see `DELIVERY_MULTI_TERMINAL_RULES.md` and `TABLE_MULTI_TERMINAL_RULES.md`.

**Status:** Phase 0–5 complete. Phase 6 (full QA matrix) still pending.

Executable mirror: `OrderWeb.Contracts.Access.CustomerOrderHubRules` / `CollectionOrderHubRules`.

---

## Frozen product rules

1. Mother MariaDB is the only real Collection order.  
2. Client SQLite is cache only.  
3. Mother is the hub.  
4. Same Mother order id everywhere.  
5. Online-required mutations for create/edit/pay/void/kitchen/print.  
6. Offline: view cached list OK; open-for-edit / save blocked.  
7. Any Client may open/edit/finish Mother Collection orders.

---

## Phase 1 — Create & sync — Done

## Phase 2 — Open existing on any Client — Done

## Phase 3 — Safe concurrent edit — Done

## Phase 4 — Live refresh UX — Done

| Signal / surface | Behavior | Status |
|---|---|---|
| Mother persist (till or Client API) | `OrderService` publishes WS `order.updated` with order id to all Clients | **Pass** |
| Client API upsert / void / pay | Also `AppDataRefreshService` with Client source so Mother Live Order reloads | **Pass** |
| Client Live Order list | `MotherEventClient` → refresh open cards from Mother | **Pass** |
| Client open order (`OrderPage` + MainPage order screen) | Matching `order.updated` reloads from Mother; dirty local basket shows conflict hint | **Pass** |
| Mother Live Order / open placement | Existing `AppDataRefresh` path + Client-source refresh | **Pass** |

**Done when:** Terminal 1 updates Collection → Mother knows → Terminal 3 Live Order and open order show the updated Mother copy without app restart.

## Phase 5 — Full Collection actions from any Client — Done

| Action | Mother-backed path | Status |
|---|---|---|
| Send to kitchen | Upsert + kitchen print documents / IP printers | **Pass** (OrderPage + MainPage; offline `PrintCollectionOrder`) |
| Print receipt/bill | `MotherPrintClient` → Mother print queue | **Pass** |
| Take payment (cash) | `ClientPaymentService` → Mother payment API; revision accepts Version or UpdatedAt.Ticks | **Pass** (User + Manager get `TakePayments`) |
| Void / cancel | `POST /api/client/orders/void` from OrderPage + MainPage Collection | **Pass** |
| Customer search/link | `MotherCustomerClient` | **Pass** (unchanged) |

**Done when:** Client A creates Collection → Client B opens from Live Order → Client B can kitchen / print / pay / void on Mother.

**Note:** Card/gift payments remain unavailable until Mother providers are configured (cash works).

---

## Implementation phases

| Phase | Status |
|---|---|
| **0** Rules | **Done** |
| **1** Create/sync | **Done** |
| **2** Reopen | **Done** |
| **3** Conflict | **Done** |
| **4** Live `order.updated` refresh | **Done** |
| **5** Finish actions any Client | **Done** |
| **6** Full QA matrix | Pending |
