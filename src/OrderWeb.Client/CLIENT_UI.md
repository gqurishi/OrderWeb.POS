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
| Theme lock at startup | `DesignSystemBootstrap.LockHostResources` in `App.xaml.cs` |

## Rules for new Client work

1. **Do not add** new `Client*` colours, fonts, margins, or button styles in `Resources/Styles`.
2. **Do not** introduce new implicit `TargetType` styles for operational chrome.
3. Prefer SharedUI controls (`SharedButton`, `SharedTextInput`, `DashboardTile`, `StatusBadge`, …) or `{DynamicResource Pos*}` tokens.
4. Thin host wrappers are allowed (example: `ClientTopBar` → `ApplicationHeader`) when the visual tree is SharedUI.
5. Remove hard-coded Client colours/margins/fonts **only when that screen migrates** to SharedUI. Legacy styles remain until then.

## Allowed Client-only UI

- Pairing Mother server
- Terminal registration / activation
- Connection / reconnect recovery
- Local cache reset / re-sync
- Required Client update screen
- Offline status presentation (still uses SharedUI tokens / `OfflineStatusBannerView`)

## Completion gate

New Client work does not create new Client-only visual styles. SharedUI is the visual foundation.
