# Client visual foundation (Step 2)

**Goal:** Client operational UI uses the same SharedUI design system as Mother. Client only looks different on terminal-specific screens (pairing, activation, reconnect, sync recovery, offline status).

## Authoritative visual source

| Layer | Source |
| --- | --- |
| Colours, typography, spacing, radii | `OrderWeb.SharedUI` `Pos*` tokens via `OrderWebTheme` |
| Buttons, inputs, cards, badges | `OrderWeb.SharedUI.Controls` |
| Loading / empty / error / offline | `LoadingOverlayView`, `EmptyStateView`, `ErrorStateView`, `OfflineStatusBannerView` |
| Dialogs | `ConfirmationDialog`, `ErrorDialog` |
| Responsive rules | `ResponsiveLayout` |
| Navigation | Shared `ApplicationSidebar` / `ApplicationShellFrame` fed by `PosNavigationCatalog` |
| Theme lock at startup | `DesignSystemBootstrap.LockHostResources` in `App.xaml.cs` |

## Capability-driven navigation (Step 4)

Client menu visibility comes from Mother-issued `client.*` capabilities via `PosNavigationCatalog` + `ClientNavigationService`, not a Client-local role menu.

| Concern | Source |
| --- | --- |
| Route / icon / label | `PosNavigationCatalog` |
| Required capability | `PosCapabilityKeys` (`client.*`) |
| Mother connection required | Catalog entry (`payment`, `cashdrawer`, reports, settings, …) |
| Cached / offline OK | Catalog entry (`dashboard`, floors/tables, live orders, …) |
| Login grants | Mother `/api/client/login` merges `ClientCapabilityGrants` into `permissions` |

Settings and reports stay hidden unless Mother grants those capabilities (Admin/Manager defaults only).

## Rules for new Client work

1. **Do not add** new `Client*` colours, fonts, margins, or button styles in `Resources/Styles`.
2. **Do not** introduce new implicit `TargetType` styles for operational chrome.
3. Prefer SharedUI controls (`SharedButton`, `SharedTextInput`, `DashboardTile`, `StatusBadge`, …) or `{DynamicResource Pos*}` tokens.
4. Thin host wrappers are allowed (example: `ClientTopBar` → `ApplicationHeader`) when the visual tree is SharedUI.
5. Remove hard-coded Client colours/margins/fonts **only when that screen migrates** to SharedUI. Legacy styles remain until then.
6. **Do not** add Client-only sidebar item lists; extend `PosNavigationCatalog` and Mother capability grants instead.

## Allowed Client-only UI

- Pairing Mother server
- Terminal registration / activation
- Connection / reconnect recovery
- Local cache reset / re-sync
- Required Client update screen
- Offline status presentation (still uses SharedUI tokens / `OfflineStatusBannerView`)

## Shared order-entry (Step 7)

Normal Client order-entry uses SharedUI `OrderEntryView` + `OrderEntryViewModel` hosted by `SharedOrderEntryPage`.

| Concern | Source |
| --- | --- |
| Categories / products / modifiers | SharedUI order-entry + `IMenuCatalogService` |
| Basket, qty, notes, quick notes, discount, tax/service/total | `OrderEntryView` / `BasketSummaryView` |
| Mutations | `ClientOrderService` (`IOrderService`) → Mother authority |
| Display totals while waiting | Local estimate (`IsDisplayEstimate`) |
| Submitted order | Waits for Mother authoritative response before success status |
| Conflict / loading / pending | SharedUI loading overlay + conflict banner |

**Rule:** Client UI → `ClientOrderService` → Mother API/authority → Mother domain → MariaDB. Client may show estimates; it must not treat an order as submitted until Mother confirms.

**Rollback:** `OrderPage` is obsolete and reachable only when `LegacyOrderEntryAccess` preference is enabled (`client.order_entry.use_legacy_rollback`).

## Completion gate

New Client work does not create new Client-only visual styles. SharedUI is the visual foundation. Client navigation uses the same shared visual nav as Mother, with Mother-provided permissions controlling visibility. Normal order-entry uses SharedUI; the legacy Client `OrderPage` is rollback-only.
