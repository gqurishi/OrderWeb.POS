# Client POS UI migration inventory

Status: Step 1 complete.  This is an inventory only; no Client screen, route, style, or asset was removed.

## Target architecture

`OrderWeb.SharedUI` owns the operational POS appearance.  `OrderWeb.Client` remains the host for pairing, terminal identity, Mother API calls, local SQLite cache, synchronization, and offline policy.

The Client may have terminal-specific operational-recovery screens, but it must not have a second visual POS design after migration.

## Current entry points and navigation

| Entry point / route owner | Current behavior | Classification | Migration destination |
| --- | --- | --- | --- |
| `AppShell` -> `MainPage` | The application always opens the large legacy Client host page. | Keep temporarily as legacy fallback | Replace its normal operational entry path with the shared shell once the route host is ready. |
| `MainPage` | Large legacy, code-built dashboard/order/navigation surface. It contains duplicate navigation and operational presentation paths. | Keep temporarily as legacy fallback | Retire screen-by-screen after each equivalent SharedUI route is live. |
| `LoginPage` | Uses the SharedUI login view and selects a shared Client dashboard by role. | Existing SharedUI screen/control | Keep; finish connection/session and capability binding. |
| `SharedClientDashboardPage` / role dashboard classes | Uses `ApplicationShellFrame`, `DashboardView`, and `PosNavigationCatalog`. | Existing SharedUI screen/control | This is the intended operational Client start frame. Expand it as the common route host. |

## Client-only terminal and recovery screens

| Screen | Current role | Classification | Reason |
| --- | --- | --- | --- |
| `ConnectToMotherPage` | Mother-server pairing/connection setup. | Remain Client-only | A Mother workstation never pairs to another Mother server. |
| `BootstrapProgressPage` | Initial bootstrap and local-cache preparation. | Remain Client-only | It manages Client local synchronization. |
| Terminal registration / activation flow | Implemented through Client connection/bootstrap services rather than one clearly named screen. | Remain Client-only | It establishes terminal authorization. |
| Reconnect / forced full-resync presentation | Connection and cache services provide the behavior; a dedicated recovery route is still needed. | Create as a new Client-only screen | It must explain reconnect, stale cache, and resync safely. |
| Local cache reset / repair | Service capability exists; a deliberate user-facing route is still needed. | Create as a new Client-only screen | It is terminal maintenance, not a shared POS feature. |
| Update-required / incompatible Client | Compatibility service provides the state; a dedicated blocking page is still needed. | Create as a new Client-only screen | It prevents an obsolete terminal from performing unsupported actions. |

## Operational POS screens

| Current Client screen | Current state | Classification | SharedUI migration target | Priority |
| --- | --- | --- | --- | --- |
| `SharedClientDashboardPage` | Already hosts `ApplicationShellFrame` and `DashboardView`. | Existing SharedUI screen/control | Make it the single normal post-login route host and remove legacy dashboard paths later. | 1 |
| `RestaurantPage` | Restaurant/table entry route. | Keep temporarily as legacy fallback | Shared floor/table screen using floor selector, table cards, status, cached/stale state. | 2 |
| `TableLayoutPage` | Table-layout presentation and opens `OrderPage`. | Keep temporarily as legacy fallback | Same shared floor/table screen as `RestaurantPage`; consolidate the two Client visual paths. | 2 |
| `OrderPage` | Legacy table order-entry view with Client-specific controls and dialogs. | Keep temporarily as legacy fallback | Shared `OrderEntryView`; connect to authoritative `IOrderService` Client implementation. | 3 |
| `LiveOrderPage` | Legacy live-order/order-mode presentation. | Keep temporarily as legacy fallback | Shared order-entry route/state, or a shared order-list view if it is truly a distinct permitted workflow. | 3 |
| `CollectionOrderPage` | Legacy collection order flow. | Keep temporarily as legacy fallback | Shared order-entry view configured for collection. | 3 |
| `DeliveryOrderPage` | Legacy delivery order flow. | Keep temporarily as legacy fallback | Shared order-entry view configured for delivery. | 3 |
| `OnlineOrdersPage` / `OnlineOrderDetailsView` | Legacy web-order list/detail flow. | Keep temporarily as legacy fallback | Create a shared permitted order-list/detail surface, if Client is authorized to use it. | 6 |
| `PaymentPage` | Its code-behind already hosts SharedUI `PaymentView`. | Existing SharedUI screen/control | Keep and finish the authoritative Client payment-service route. | 4 |
| `PrintStatusView` | Client-specific print status presentation. | Replace with an existing SharedUI screen/control | Use shared toast/status/error/confirmation presentation for Mother-authoritative print results. | 5 |
| `CustomerSearchView` | Client-specific customer search presentation. | Create as a new SharedUI screen | Shared customer search/list presentation. | 5 |
| `CustomerFormView` | Client-specific customer details/edit presentation. | Create as a new SharedUI screen | Shared customer details/assign presentation; Mother controls access and validation. | 5 |

## Shared dialogs and reusable controls

| Current Client component | Classification | SharedUI destination |
| --- | --- | --- |
| `ClientTopBar` | Replace with existing SharedUI control | Transitional wrapper around `PosHeader`; pages should use the shared shell/header directly. |
| `ClientSidebar` | Replace with existing SharedUI control | `ApplicationSidebar` and `PosNavigationCatalog`. |
| `ClientToast` | Replace with existing SharedUI control | Shared toast/status presentation. |
| `ConnectionStatusView` | Replace with existing SharedUI control | Shared `ConnectionIndicator`. |
| `NumericKeypadView` | Replace with existing SharedUI control | Shared numeric keypad. |
| `ConfirmDialog` | Replace with existing SharedUI control | Shared confirmation dialog. |
| `NoteDialog` | Replace with existing SharedUI control | Shared order-note dialog. |
| `ModifierDialog` | Replace with existing SharedUI control | Shared modifier/addon/variant presentation. |
| `MoreOptionsDialog` | Replace with existing SharedUI control | Shared order more-options dialog. |
| `PaymentMethodDialog` | Replace with existing SharedUI control | Shared payment-method selection within `PaymentView`. |
| `MenuItemGridView` | Keep temporarily as legacy fallback | Shared product/category grid in `OrderEntryView`. |
| `OrderSummaryView` | Keep temporarily as legacy fallback | Shared basket/totals presentation in `OrderEntryView`. |

## Manager and permitted feature screens

These are not automatically Client features.  Mother-provided capabilities decide whether they appear.  When permitted, they must use SharedUI framing, theme, controls, dialogs, loading, and error presentation.

| Current Client screen | Classification | Planned treatment |
| --- | --- | --- |
| `CashDrawerPage` | Keep temporarily as legacy fallback | Shared manager frame; keep capability and authoritative Mother action checks. |
| `GiftCardPage` | Keep temporarily as legacy fallback | Shared manager frame, only if permitted. |
| `LoyaltyPage` | Keep temporarily as legacy fallback | Shared manager frame, only if permitted. |
| `ReservationPage` | Keep temporarily as legacy fallback | Shared manager frame or future shared reservation feature, only if permitted. |
| `OrderHistoryPage` | Keep temporarily as legacy fallback | Shared permitted order-history/list view; protect customer information. |

## Styles and assets to review later

| Area | Classification | Safe action now |
| --- | --- | --- |
| `Resources/Styles/Colors.xaml` | Legacy fallback | Keep until all dependent pages use SharedUI tokens. |
| `Resources/Styles/Styles.xaml` | Legacy fallback | Keep until all dependent pages use SharedUI styles. |
| Client image resources | Legacy fallback / terminal-specific where applicable | Keep assets until route-by-route visual parity is verified; restaurant-specific images must come from dynamic cache, not compiled assets. |

## Required migration order

1. Make `SharedClientDashboardPage` the normal Client operational host after login.
2. Migrate restaurant/floor/table routes.
3. Migrate all order modes to one shared order-entry view.
4. Replace legacy order dialogs and product/basket controls.
5. Complete customer, payment, and print presentation.
6. Migrate only permitted manager pages.
7. Add Client-only reconnect, cache repair, and update-required pages.
8. Verify each route on Client Windows and Client Android, then remove its legacy counterpart.

## Step 1 completion evidence

- The Client has one Shell entry route (`AppShell` -> `MainPage`) and a second, partially adopted SharedUI operational route (`LoginPage` -> `SharedClientDashboardPage`).
- The inventory identifies all pages, reusable Client controls, and terminal-specific flows currently present under `src/OrderWeb.Client`.
- No Client screen was deleted or redirected by this inventory step.
- The next implementation step is to make the SharedUI dashboard/shell the normal post-login operational host while preserving `MainPage` as fallback.

## Step 5 dashboard migration status

- Complete: `MainPage.ShowDashboard()` now hosts `OrderWeb.SharedUI.Views.DashboardView` inside `ApplicationShellFrame` for every operational role.
- The Client supplies capability-filtered tiles, SQLite-backed table/open-order/pending-sync badges, loading states, enabled states, and Mother connection/terminal state.
- Active tiles are Tables & Floors, New Order, Open Orders, Sync Status, and Terminal Status when the authenticated capabilities permit them. Customer presentation is deliberately displayed disabled until its own SharedUI migration step, so it cannot reopen a Client-only screen.
- The former code-built Client dashboard methods remain only as temporary rollback code; they are not on the active `ShowDashboard()` route.

## Step 6 floor and table migration status

- Complete for the active Client restaurant route: `ShowRestaurantLayout()` now hosts SharedUI `RestaurantTablesView` inside `ApplicationShellFrame`.
- The Client maps its SQLite floor/table snapshot into shared contracts, including table status, open-order indicator, guest count, total, and session status.
- SharedUI renders floor selection, shared table cards/status colours, guest selection, loading, empty, and disconnected/stale-cache presentation. It accepts a host-supplied floor background; the Client uses the shared neutral fallback until a restaurant-specific cached floor image exists.
- `RestaurantPage`, `TableLayoutPage`, and the older code-built restaurant layout remain as fallback code and are not deleted in this step.

## Step 8 order-dialog migration status

- The active Client order surface now presents SharedUI dialogs for addons/variants, notes, quick notes, quantity, discounts, manager approval, void confirmation, previous orders, more options, tasting-menu selection, and course progress.
- Dialogs are hosted by `ApplicationShellFrame.DialogContent`. They only collect a choice or text; confirmation callbacks invoke Client order services or create a pending Mother request outside the dialog.
- The old modal `NoteDialog`, `ModifierDialog`, and `MoreOptionsDialog` files remain as fallback for legacy pages and are not deleted in this step.
- Mother must adopt these same SharedUI dialog components when its legacy order page is migrated; that remaining host migration does not belong in the Client-only route change.

## Step 9 customer migration status

- The active Client Customers route now hosts SharedUI `CustomerFlowView` for customer search, details, collection/delivery details, customer assignment, and permitted-history presentation.
- Customer access is capability checked before the route opens. Search asks Mother first; only the deliberately limited active-order customer cache is consulted when Mother has no result.
- Creating or assigning a customer and starting a collection/delivery order is blocked offline and requires Mother confirmation. The cache retains no customer directory, email, or loyalty data; it only keeps the customer needed for the active order and retains address/postcode only for delivery.
- The legacy collection and delivery customer pages remain as fallback routes until their whole order journeys are migrated to SharedUI.
