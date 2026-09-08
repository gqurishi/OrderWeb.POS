# Delivery Multi-Terminal Rules

**Scope:** Delivery orders. Same Mother-hub pattern as Collection.

**Status:** Parity with Collection Phases 0–5 (including live `order.updated` refresh). Full QA matrix still pending with Collection.

Executable mirror: `OrderWeb.Contracts.Access.CustomerOrderHubRules` (Delivery constants + `IsDeliveryOrderType` / `IsCustomerHubOrderType`).

---

## Frozen product rules

1. Mother MariaDB is the only real Delivery order.  
2. Client SQLite is cache only.  
3. Mother is the hub.  
4. Same Mother order id everywhere.  
5. Online-required mutations for create/edit/pay/void/kitchen/print.  
6. Offline: view cached list OK; open-for-edit / save blocked.  
7. Any Client may open/edit/finish Mother Delivery orders.

---

## Shared with Collection (already done)

| Capability | Status |
|---|---|
| Create → Mother upsert (`delivery`) | **Pass** |
| Stable OrderId on create (DeliveryOrderPage + MainPage) | **Pass** |
| Open from Live Order → `OpenOrderForEditAsync` | **Pass** |
| Conflict 409 / ExpectedVersion | **Pass** (Mother shared) |
| Live `order.updated` list + open-order reload | **Pass** (order-id agnostic) |
| Void / kitchen / print / cash pay via Mother | **Pass** |
| Offline gates for hub mutations | **Pass** |

**Done when:** Terminal 1 updates Delivery → Mother knows → Terminal 3 Live Order and open order show the updated Mother copy without app restart.
