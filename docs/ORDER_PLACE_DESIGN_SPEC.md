# Order Place Design Spec (Phase 0)

**Status:** Agreed baseline for Mother redesign → Client reuse later  
**Date:** 2026-09-09  
**Scope now:** Mother POS only  
**Reuse later:** Client POS via SharedUI  
**Architecture decision:** **One shared Order Place shell** + three entry rituals (not three full pages)

Primary Mother page today: `src/OrderWeb.Mother/Features/Orders/Pages/OrderPlacementPageSimple.xaml(.cs)`

---

## 1. Product decisions (locked)

| Decision | Choice |
|----------|--------|
| Pages | **1 shell**, modes: Table / Collection / Delivery |
| Entry | Keep separate: Visual Layout covers / Collection modal / Delivery modal |
| Pricing | Table = dine-in price; COL/DEL = takeaway price; DEL adds zone fee |
| Behaviour | **Freeze** send/void/pay/print/lifecycle/multi-terminal; redesign chrome only first |
| Payment button | Top-right of order header (next to Order # / table) |
| Components | Build/extend in **OrderWeb.SharedUI** first |

---

## 2. Layout tokens (finger-first POS)

Target hardware: **14″ / 15″ / 15.5″ landscape** touchscreens. Test at Windows scaling **100%** and **125%**.

| Token | Value | Notes |
|-------|--------|--------|
| `SplitMenu` | ~0.70 | Left menu/product workspace |
| `SplitOrder` | ~0.30 | Right order + actions (min ~320–360 dp usable) |
| `HeaderHeight` | 56–64 | Slim; no oversized page title |
| `TouchMin` | 44×44 | Absolute minimum hit target |
| `TouchComfort` | 48–56 | Preferred for primary actions |
| `ActionGap` | 8–12 | Between adjacent controls |
| `SectionGap` | 12–16 | Between cats / sub-band / grid |
| `CategorySubGap` / band | **18** + muted band | Keeps main cats vs filters distinct |
| `CategoryHeight` | 44–48 | Strong main-category chips |
| `SubcategoryHeight` | 36–40 | Lighter subcategory pills |
| `ProductCardMinWidth` | **100** | Drives column count (~30% denser) |
| `ProductCardMinHeight` | **60** | Name + price; full card tappable |
| `ProductColumns` | Adaptive | Fit as many as `minWidth` allows; aim **4** on 14″, **5** when width allows |
| `OrderLineMinHeight` | 56–64 | Qty +/− must remain finger-safe |
| `BottomActionStack` | Fixed | Totals + NOTES/VOID/MORE + SEND/PRINT |

### Scroll rules
- **Page must not scroll** as a whole.
- Scroll **only**: product grid, order-item list.
- Header, category rows, totals, and primary actions stay visible.

### Responsive rule
Larger screens → **more products/columns**, not fatter whitespace. Never hard-code column count.

---

## 3. Button roles (visual hierarchy)

| Role | Controls | Visual |
|------|----------|--------|
| **Primary** | PAYMENT, SEND TO KITCHEN | Strongest fill / largest |
| **Secondary** | PRINT, NOTES | Visible but quieter |
| **Destructive** | VOID | Red; not enormous |
| **Utility** | MORE ▼ | Neutral grey |

### Bottom action station (target)

```
Subtotal
Service charge / Delivery fee (by type)
TOTAL
────────────────
[ NOTES ] [ VOID ] [ MORE ▼ ]
[ SEND TO KITCHEN ] [ PRINT ]
[        PAYMENT £xx.xx        ]
```

Workflow: choose food → build → check total → Send/Print → Payment.

---

## 4. Type matrix — Table vs Collection vs Delivery

| Concern | Table | Collection | Delivery |
|---------|-------|------------|----------|
| **Entry** | Visual Layout → covers (if new) → place | CollectionCustomerModal (name+phone) → place | DeliveryCustomerModal (address/postcode/zone fee) → place |
| **Header** | `TABLE N • G GUESTS` | `COLLECTION` + name · phone | `DELIVERY` + name · phone |
| **Guests** | Show / required at open | Hide | Hide |
| **Canonical type** | `table` | `pickup` | `delivery` |
| **Price list** | Dine-in / `table` | Takeaway | Takeaway |
| **Service charge row** | Show (applied / removed / not included) | Hide | Hide |
| **Delivery fee row** | Hide | Hide | **Show** (fee in total; redesign must surface it) |
| **Draft persist** | May stay in-memory until first Send/Pay | Persists earlier (Active draft) | Persists earlier |
| **Payment** | Full / split / by items / partial allowed | Full bill only | Full bill only |
| **SEND** | Kitchen tickets; marks sent | Kitchen takeaway routes | Kitchen takeaway routes |
| **PRINT meaning** | Customer bill/receipt (not kitchen-send) | Receipt **+** kitchen; success also completes send | Same as Collection |
| **NOTES** | Order-level notes | Same | Same |
| **VOID** | Uncommitted table: release/discard; else reason + PIN rules | Reason + PIN; return dashboard | Same as Collection |
| **MORE — always candidates** | Discount, Loyalty, Cash Drawer | Discount, Loyalty, Cash Drawer | Discount, Loyalty, Cash Drawer |
| **MORE — table-only** | Fire Course, Table Transfer, Merge Tables, SC remove/restore | **Hide** Transfer/Merge/Fire/SC (today Transfer/Merge still appear — fix in redesign) | Same hide rule |
| **MORE — takeaway** | — | Previous Orders (needs phone) | Previous Orders (needs phone) |
| **Resume** | Live Order / floor reopen | Live Order reopen | Live Order / Web Orders reopen |

---

## 5. Must not break (regression freeze)

Do **not** change these behaviours while redesigning chrome (Phases 1–4). Only rebind UI to existing handlers.

### Kitchen / lifecycle
- Send commits unsent lines → kitchen print → `SentPartial` / `SentFull`
- Unsent vs sent line tracking
- Failure rollback to NotSent where today rolls back
- Table session food-served / occupied updates on send

### Void / money
- Void reason + manager PIN rules (role exceptions unchanged)
- Block void when payments already approved (current rule)
- Table payment split / by items / partial persistence
- Takeaway forced full bill
- Tip / service-charge interaction for table
- Discount / loyalty / gift payment entry points

### Catalog add path
- Variant → quick note → addons → merge matching unsent lines
- Meal deal picker
- Tasting menu package + course lines
- Effective price by table vs takeaway

### Multi-terminal / durability
- Remote change reload / conflict toast
- Draft save on navigate-away / idle (except uncommitted local table rules)
- `INavigationCommitParticipant` behaviour
- Order number generation timing (table vs takeaway)

### Print ownership
- Mother owns printers; takeaway PRINT dual copy behaviour unchanged unless product explicitly revisits it later

---

## 6. Current Mother entry inventory

```
Dashboard / sidebar
  ├─ Restaurant → VisualTablePage ("visuallayout")
  │     └─ tap table (+ covers if needed)
  │           └─ OrderPlacementPageSimple(table, covers, session?, existingId?)
  │
  ├─ Collection → CollectionCustomerModal ("collection")
  │     └─ name + phone required
  │           └─ OrderPlacementPageSimple("COL") + SetCollectionOrderInfo(...)
  │
  ├─ Delivery → DeliveryCustomerModal ("delivery")
  │     └─ address + postcode → DeliveryZoneService fee
  │           └─ OrderPlacementPageSimple("DEL") + SetDeliveryOrderInfo(..., fee)
  │
  ├─ Live Order → OrderPlacementPageSimple(existingOrderId) / table session reopen
  └─ Web Orders → OrderPlacementPageSimple(existingOrderId)
```

| Piece | Path |
|-------|------|
| Shared place UI | `Features/Orders/Pages/OrderPlacementPageSimple.xaml(.cs)` |
| In-memory basket | `Features/Restaurant/Models/TableOrder.cs` |
| Floor entry | `Features/Restaurant/Pages/VisualTablePage.xaml(.cs)` |
| Collection entry | `Features/Customers/Pages/CollectionCustomerModal.xaml(.cs)` |
| Delivery entry | `Features/Customers/Pages/DeliveryCustomerModal.xaml(.cs)` |
| Live reopen | `Features/Orders/Pages/LiveOrderPage.xaml(.cs)` |
| MORE dialog | `Features/Orders/Views/MoreOptionsDialog.*` |

**Out of Phase 0–4 scope:** redesigning entry modals / floor plan (unless blocking).

---

## 7. SharedUI placement (decided)

**Project:** `src/OrderWeb.SharedUI`

| Area | Placement |
|------|-----------|
| Colours / density | Extend `Themes/OrderWebPalette.xaml` (+ optional `Themes/OrderPlaceTokens.xaml`) |
| Existing seeds | `Controls/SharedCards.cs` (`CategoryButton`, `ProductButton`, `OrderLineView`), `Controls/SharedButton.cs`, `Views/OrderEntryView.cs`, `ViewModels/OrderEntryViewModel.cs` |
| New / evolved controls | Prefer `Controls/OrderPlace/` **or** evolve existing types in place — **do not** invent a second parallel set |
| Host-neutral state | Grow `OrderEntryViewModel` (menu, basket, totals, actions); hosts supply data/commands |
| Mother host | `OrderPlacementPageSimple` + `MotherOrderPlaceHost` → SharedUI `OrderPlaceShellView` ✅ |
| Client later | Same SharedUI view; Client wires Mother HTTP ✅ |

### Required control set (Phase 1+)

| Control | Responsibility |
|---------|----------------|
| `CategoryButton` | Strong selected main category |
| `SubcategoryButton` | Lighter selected subcategory pill |
| `HorizontalChipScroller` | Single-row horizontal scroll (no wrap) |
| `ProductCard` | Compact name+price; full-card tap; adaptive grid |
| `OrderItemRow` | Name, price, modifiers/notes, qty |
| `QuantityControl` | − / count / + |
| `PosActionButton` | primary / secondary / destructive / utility |
| `OrderTotalsBlock` | Subtotal, SC, delivery fee, TOTAL |
| `OrderPlaceShell` (view) | 70/30 + dual scroll + bottom action station |

### Client-ready host contract (Phase 7 ✅)

See `docs/ORDER_PLACE_CLIENT_READINESS.md` and `OrderWeb.SharedUI/Hosting/OrderPlaceHost.cs`.

Host must provide: categories, subcategories, products (priced for type), basket lines, totals, commands for add/qty/note/void/send/print/pay/more — **no DB/HTTP inside SharedUI**.

---

## 8. Target UX (Table reference)

Waiter path: **Category → Subcategory → Item → Item → check total → Send / Payment** with minimal scroll.

Left (~70%): main cats → subs → product grid  
Right (~30%): Table/guests (or customer) + PAYMENT top-right → scrolling lines → fixed totals + NOTES/VOID/MORE + SEND/PRINT

---

## 9. Implementation phases (after this spec)

| Phase | Focus |
|-------|--------|
| **1** | SharedUI tokens + controls |
| **2** | Mother shell 70/30; Payment bottom; dual scroll |
| **3** | Compact cats/subs + adaptive product grid |
| **4** | Order lines + action hierarchy |
| **5** | Type policies (header/totals/MORE/PRINT) |
| **6** | Hardware QA 14–15.5″ / 100–125% |
| **7** | Client readiness docs/hooks |
| **8** | Client Order Place (later) |

---

## 10. Explicit non-goals (Phase 0–4)

- Three separate Order Place pages  
- Rewriting OrderService / payment engine / kitchen routing  
- Changing Collection/Delivery entry modals  
- Client UI implementation  
- Softening multi-terminal conflict rules  

---

## 11. Done criteria for Phase 0

- [x] Layout tokens written  
- [x] Button roles written  
- [x] Table / COL / DEL matrix written  
- [x] Must-not-break list written  
- [x] Mother entry inventory written  
- [x] SharedUI placement decided  

### Phase 1 — Reusable components ✅

- [x] Tokens: `Op*` keys in `OrderWeb.SharedUI/Themes/OrderWebDimensions.xaml`
- [x] Controls: `OrderWeb.SharedUI/Controls/OrderPlace/OrderPlaceControls.cs`
- [x] Sandbox view: `OrderPlaceSandboxView`
- [x] Mother host: `OrderPlaceSandboxPage` route `orderplacesandbox`
- [x] Mother + SharedUI build succeeded

### Phase 2 — Mother shell layout ✅

- [x] Header | Left ~70% | Right ~30% on `OrderPlacementPageSimple`
- [x] Slim header title: `TABLE N • G GUESTS` / COL / DEL
- [x] Left: categories → subs → product scroll host
- [x] Right: fixed identity → scroll lines → fixed totals + actions
- [x] PAYMENT in order header top-right (shows total)
- [x] Page does not scroll; only product grid + order list scroll
- [x] Existing handlers / `x:Name`s preserved; Mother build succeeded

### Phase 3 — Menu workspace ✅

- [x] Removed multi-row grey/purple category carousels
- [x] Main categories: one `HorizontalChipScroller` + `OrderPlaceCategoryButton`
- [x] Subcategories: one lighter `HorizontalChipScroller` + `OrderPlaceSubcategoryButton`
- [x] Products: `OrderPlaceProductGrid` adaptive columns from `OpProductCardMinWidth`
- [x] Full-card tap → existing add-item pipeline (variant → note → addon → add)
- [x] Meal Deals / Tasting as category chips + special product cards
- [x] Mother windows build succeeded

### Phase 4 — Order panel + actions ✅

- [x] Order lines via `OrderPlaceLineRow` (name, price, qty, modifiers/notes, optional Fire/Note)
- [x] Light SENT cue on kitchen-reached lines
- [x] Totals: Subtotal / Discount / Service charge / Delivery fee (room) / TOTAL
- [x] Bottom station: NOTES | VOID | MORE → SEND | PRINT; PAYMENT in header
- [x] Visual weight via `PosActionButton` roles (Payment amber, Send green, Print/Notes secondary, Void red, More grey)
- [x] Existing click handlers preserved; Mother windows build succeeded

### Phase 5 — Type policies ✅

- [x] Header modes: TABLE + guests / COLLECTION customer / DELIVERY customer (same page)
- [x] Totals: SC table-only; Delivery fee DEL-only; SC hidden for takeaway
- [x] Pricing unchanged (`GetPricingOrderType` table vs takeaway)
- [x] Payment: takeaway forced full bill (`IsTakeawayStyleOrder`); table keeps split options
- [x] PRINT labels: `PRINT BILL` (table) / `PRINT RECEIPT` (takeaway receipt+kitchen); handlers unchanged
- [x] MORE: hide Transfer/Merge/Fire/SC on COL/DEL; Previous Orders for takeaway
- [x] Mother windows build succeeded

**Next:** Phase 6 — hardware QA 14–15.5″ / 100–125% (physical Mother devices).

### Phase 6 — Hardware QA (physical)

- [ ] Run on-site / lab: 14″ / 15″ / 15.5″ landscape @ 100% and 125% scaling  
- Checklist lives with ops; not automated in-repo  

### Phase 7 — Client readiness ✅

- [x] Host contract: `OrderWeb.SharedUI/Hosting/OrderPlaceHost.cs` (`IOrderPlaceHost` + `OrderPlaceSessionState`)
- [x] Doc: `docs/ORDER_PLACE_CLIENT_READINESS.md` (APIs, basket UX gap, styling rule, Phase 8 checklist)
- [x] SharedUI Order Place note: token-only styling; no DB/HTTP in controls
- [x] Mother remains host of services; Client Phase 8 reuses same controls

### Phase 8 — Client Order Place ✅

- [x] SharedUI `OrderPlaceShellView` hosts chrome (70/30, dual scroll, bottom Payment)
- [x] `ClientOrderPlaceHost` : `IOrderPlaceHost` over Mother menu/order/print/pay
- [x] `OrderPage` thin host — Table / COL / DEL / Live entry constructors unchanged
- [x] Online gates for send/print/pay/void via `ClientOfflinePolicy`
- [x] Takeaway PRINT = receipt + kitchen (`SendToKitchenAndReceiptAsync`); table = bill print
- [x] Type chrome: SC table-only, delivery fee DEL, PRINT BILL / PRINT RECEIPT labels
- [x] Client windows build succeeded

**Done when:** Client waiters get Mother-like Order Place muscle memory on SharedUI chrome.

### Package 1 — shared shell (Mother + Client) ✅

- [x] Mother `OrderPlacementPageSimple` XAML matches Client chrome (menu + `ShellHost`)
- [x] `MotherOrderPlaceHost` implements `IOrderPlaceHost` over existing page logic
- [x] Mother builds `OrderPlaceSessionState` and rebinds via `OrderPlaceStateChanged`
- [x] Mother windows build succeeded
