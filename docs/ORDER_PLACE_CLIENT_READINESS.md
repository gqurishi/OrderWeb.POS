# Order Place — Client readiness (Phase 7)

**Status:** Phase 8 implemented on Client  
**Audience:** Client / Mother Order Place  
**Canonical UX:** `docs/ORDER_PLACE_DESIGN_SPEC.md`  
**Controls:** `OrderWeb.SharedUI/Controls/OrderPlace/`  
**Shell:** `OrderWeb.SharedUI/Views/OrderPlaceShellView.cs`  
**Host contract:** `OrderWeb.SharedUI/Hosting/OrderPlaceHost.cs` (`IOrderPlaceHost`)  
**Client host:** `OrderWeb.Client/Services/ClientOrderPlaceHost.cs`  
**Client page:** `OrderWeb.Client/Pages/Orders/OrderPage` (thin host)  
**Mother host:** `OrderWeb.Mother/.../MotherOrderPlaceHost.cs`  
**Mother page:** `OrderPlacementPageSimple` (thin host — same shell as Client)

---

## 1. Architecture rule

| Layer | Owns |
|-------|------|
| **SharedUI** | Tokens (`Op*`), chips, product grid, line row, qty, action buttons, totals block, sandbox |
| **Host (Mother / Client)** | Menu load, pricing, basket mutations, send/void/pay/print, PIN, MORE dialogs, multi-terminal |
| **Mother HTTP** | Client never talks to OrderWeb cloud for order place; **Client → Mother → …** |

SharedUI must stay host-neutral: **no DB, no HTTP, no hard-coded Mother hex colours/fonts in business logic**. Prefer `Op*` / `Ow*` resources via `ControlResources` / `.Use(...)`.

Today Mother and Client both host `OrderPlaceShellView` via `IOrderPlaceHost` (`MotherOrderPlaceHost` / `ClientOrderPlaceHost`). Logic stays in each host; **do not fork a second SharedUI component set**.

---

## 2. `IOrderPlaceHost` — what Client must supply

Implement `OrderWeb.SharedUI.Hosting.IOrderPlaceHost` (or a thin adapter) that:

1. Fills `OrderPlaceSessionState` (header, categories, products, lines, totals, type flags)
2. Handles UI intents: category/sub/add/qty/note/void/more/send/print/pay
3. Raises `StateChanged` after Mother round-trips so the view rebinds

### Session fields Client must drive

| Field | Source |
|-------|--------|
| `Kind` | Table / Collection / Delivery |
| `HeaderTitle` / `HeaderDetail` | Table+guests or customer name·phone |
| `Categories` / `Subcategories` / `Products` | Mother menu, **priced for kind** (table vs takeaway) |
| `Lines` | Basket with modifiers/notes/sent cue |
| `Subtotal` / `Discount` / `ServiceCharge` / `DeliveryFee` / `Total` | Host totals |
| `ShowServiceCharge` | **true only for Table** |
| `ShowDeliveryFee` | **true only for Delivery** |
| `PrintActionLabel` | `PRINT BILL` (table) / `PRINT RECEIPT` (takeaway) |
| `PaymentActionLabel` | `PAYMENT £xx.xx` |

### Commands (UI → host)

| Method | Expected host behaviour |
|--------|-------------------------|
| `AddProductAsync` | Variant → quick note → addons → add/merge unsent (same pipeline as Mother) |
| `SetLineQuantityAsync` | Respect sent-line rules (extra unit / mark unsent / remove at 0) |
| `SendAsync` | Commit + kitchen routes via Mother |
| `PrintAsync` | Table: bill only. Takeaway: receipt **+** kitchen (Mother print APIs) |
| `PayAsync` | Table: split allowed. COL/DEL: **full bill only** |
| `VoidAsync` / `MoreAsync` | PIN/role rules; MORE hides Transfer/Merge/Fire/SC on takeaway; wired to Mother `/api/client/orders/*` |

---

## 3. Mother APIs Client already has (reuse)

These exist under Mother `/api/client/…` (session-gated). Client already wraps several:

| Capability | Mother route (examples) | Client today |
|------------|-------------------------|--------------|
| Menu snapshot | `GET /api/client/menu` | Cached / sync paths |
| Orders upsert / get | `POST/GET /api/client/orders` | `MotherOrderClient` |
| Void | `POST /api/client/orders/void` (+ reason/PIN/tableId) | `MotherOrderClient.VoidCollectionOrderAsync` |
| Discount / SC / transfer / merge / fire / loyalty / drawer | `POST /api/client/orders/discount\|service-charge\|transfer-table\|merge-tables\|fire-course\|loyalty-redeem\|cash-drawer/open` | Order Place MORE |
| Previous orders | `GET /api/client/orders/previous?phone=` | Takeaway MORE |
| Payments | `POST /api/client/payments` | Shared payment + gift/loyalty |
| Print | `POST /api/client/prints` | Print client |
| Customers | search / upsert | Collection/Delivery entry |
| Delivery quote | delivery-zones lookup/quote | Delivery fee |
| Loyalty / gift | `/api/client/loyalty/*`, `/gift-cards/*` | Feature pages |
| Live conflict | WS `order.updated` etc. | Live Order refresh patterns |

**Pricing:** resolve table vs takeaway on the **host** when mapping menu → `OrderPlaceProductItem.Price` (Mother already does this for type). Do not put price-list rules inside SharedUI.

---

## 4. API / product gaps for Phase 8 (basket UX)

The hard gap is **not** “missing menu/order endpoints” — it is **Client Order Place chrome + host orchestration**:

| Gap | Notes |
|-----|--------|
| SharedUI shell host on Client | Wire `OrderPage` (or successor) to Order Place controls + `IOrderPlaceHost` |
| Basket UX parity | Adaptive grid, chip cats/subs, bottom Payment station, type policies |
| Add pipeline dialogs | Variant / note / addon / meal deal / tasting — reuse SharedUI dialogs where present; host owns data |
| Send vs Print semantics | Must match Mother type matrix (do not invent Client-only print meaning) |
| MORE policy | Hide Transfer/Merge/Fire/SC on COL/DEL; Previous Orders for takeaway |
| Multi-terminal reload | On `order.updated`, re-fetch order and refresh `Session` (same as Live Order discipline) |
| Offline | Follow existing Client offline policy; gift/loyalty already online-only — order place should not invent offline kitchen send |

Optional later Mother API polish (only if Client host cannot compose from existing upsert+print+pay):

- Explicit “send kitchen” / “print bill” operation codes if Client needs clearer idempotency than upsert+print
- Documented error codes for conflict / stale revision (if not already surfaced on order upsert)

---

## 5. Styling — avoid Mother-only corners

| Do | Don’t |
|----|--------|
| Use `Op*` / `Ow*` tokens from SharedUI themes | Hard-code `#3B82F6` etc. inside SharedUI control logic |
| Let host set labels/strings on session state | Bake “Mother” copy into SharedUI |
| Keep density via `OrderPlaceLayout.ProductColumnCount` | Hard-code column counts per terminal SKU in UI |

Mother page code-behind may still use legacy colours until a later shell extraction; **new SharedUI work must stay token-based**.

---

## 6. Client Phase 8 checklist (start here)

1. **Read** `ORDER_PLACE_DESIGN_SPEC.md` type matrix (Table / COL / DEL).  
2. **Reuse** `Controls/OrderPlace/*` + themes — no parallel Client controls.  
3. **Implement** `IOrderPlaceHost` over `MotherOrderClient` + menu cache + print/pay clients.  
4. **Entry:** keep separate Collection/Delivery modals → same Order Place host with `Kind` set.  
5. **Chrome:** 70/30, Payment bottom, dual scroll only, type totals (SC / delivery fee).  
6. **Add item:** variant → note → addon → add; meal deal / tasting if Client sells them.  
7. **Pay:** COL/DEL full bill only; table may use existing SharedUI payment split paths via Mother.  
8. **Print / Send:** match Mother meanings; busy/idempotency guards.  
9. **MORE:** Discount, Loyalty, Cash Drawer; takeaway Previous Orders; table Transfer/Merge/Fire/SC via Mother APIs.  
10. **Conflict:** toast + refresh when idle; dirty basket warns only (no silent overwrite).  
11. **QA:** same finger/density checks as Mother Phase 6 on Client hardware.  
12. **Done when:** waiter can place COL/DEL/Table on Client with the same SharedUI chrome without redesigning components.
13. **Polish:** ChefLoader on open; PosToast for send/print/void/conflict; always 70/30 (no narrow stack).  
14. **Keep different:** table/COL/DEL entry screens; Client → Mother only (no Client cloud / local order engine).

---

## 7. Done criteria (Phase 7–8)

- [x] Host contract documented + `IOrderPlaceHost` in SharedUI  
- [x] API inventory + basket UX gap called out  
- [x] Styling rule: SharedUI tokens only  
- [x] Short Phase 8 checklist for Client engineers  
- [x] Phase 8: Client `OrderPage` hosts `OrderPlaceShellView` + `ClientOrderPlaceHost`  
- [x] Package 1: Mother `OrderPlacementPageSimple` hosts same shell via `MotherOrderPlaceHost`  
- [x] Package 2: Client add pipeline = Mother (variant → quick note → addons → add) via SharedUI dialogs  
- [x] Package 3: Client order panel — SC/discount, order NOTES, per-line SENT, meal deals/tasting  
- [x] Package 4: Client VOID + MORE — reason/PIN; Discount, SC, Transfer/Merge/Fire, Loyalty, Cash Drawer, Previous Orders via Mother APIs  
- [x] Package 5: COL/DEL full-bill payment; SEND/PRINT parity + leave after success; SharedUI cash keypad / quick tenders  
- [x] Package 6: ChefLoader/PosToast; conflict toast + no silent overwrite; always 70/30 side-by-side (entry + Client→Mother stay different)  
- [x] Entry paths (Table / COL / DEL / Live) still land on same `OrderPage`
