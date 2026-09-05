# Shared basic controls

`OrderWeb.SharedUI.Controls` is the canonical control library for Mother POS and Client POS.

**Design tokens:** authoritative keys are `Pos*` from `OrderWebTheme` (palette, typography, dimensions, component styles). Legacy `Ow*` and Mother/Client semantic keys are temporary aliases onto `Pos*`. New screens must use `Pos*` (or shared controls) — do not hardcode colours, font sizes, radii, or spacing.

Host applications provide data, commands, and navigation only.

| Requirement | Shared control |
| --- | --- |
| Primary and secondary buttons | `SharedButton` (`ButtonVariant`) |
| Text and PIN inputs | `SharedTextInput`, `SharedPinInput` |
| Number keypad | `NumberKeypad` |
| Dashboard tile | `DashboardTile` |
| Sidebar item | `SidebarItemView` |
| Header | `ApplicationHeader` (`HeaderView` remains available for compact cards) |
| User/terminal information | `UserTerminalInfo` |
| Connection indicator | `ConnectionIndicator` |
| Table card | `TableCard` |
| Product button | `ProductButton` |
| Category button | `CategoryButton` |
| Order line | `OrderLineView` |
| Status badge | `StatusBadge` (`StatusKind`) |
| Confirmation and error dialogs | `ConfirmationDialog`, `ErrorDialog` |

Both application projects reference this project and merge `OrderWebTheme` in `App.xaml`, then call `DesignSystemBootstrap.LockHostResources` so `Pos*` wins over local dictionaries. Syncfusion theme remains Mother-only.

New screens consume these types with:

```xml
xmlns:shared="clr-namespace:OrderWeb.SharedUI.Controls;assembly=OrderWeb.SharedUI"
```

Compatibility wrappers may retain an application-facing type name, but their visual tree must be a shared control rather than another implementation.

## Shared application shell

`ApplicationShellFrame` owns the full authenticated application frame: Mother-style header, role-filtered navigation, current user and terminal identity, Mother connection status, the main content host, logout, loading overlay, and recoverable error overlay.

Menu ownership remains with each host. Mother supplies operational and administration routes after applying its service and role policy. Client supplies only Client-supported routes. `ApplicationNavigationItem.AllowedRoles` provides the final basic-user/manager/admin filter inside the shared sidebar.

`ApplicationSidebar` is also exposed separately so Mother can use the same navigation surface inside its native MAUI `Shell` flyout while retaining its established route engine. `ApplicationHeader` is used by Mother page top bars and Client page compatibility wrappers, keeping the frame visually identical during page-by-page migration.

## Shared authentication presentation

`AuthenticationLoginView` (in `OrderWeb.SharedUI.Views`) is the shared Mother-style login surface: brand panel, PIN dots, number keypad, clock action, loading overlay, and error/status banners.

Supporting controls:

| Requirement | Shared control |
| --- | --- |
| PIN dots | `PinDotsView` |
| Number keypad | `NumberKeypad` |
| Role selection | `RoleSelectionView` |
| Auth loading | `AuthenticationBusyOverlay` |
| Session expired | `SessionExpiredDialog` |
| Re-pairing required | `RepairRequiredView` |

Hosts keep authentication services separate:
- Mother: `MotherAuthenticationService` → local auth/DB
- Client: `ClientAuthenticationService` → Mother API

Terminal pairing remains Client-owned; only the disabled/re-pair presentation is shared.

## Shared floor / table plan

`FloorPlanView` (in `OrderWeb.SharedUI.Views`) is the shared Mother-style restaurant layout surface used by both hosts.

It covers:

| Requirement | Shared behaviour |
| --- | --- |
| Floor selector tabs | Host supplies `FloorTabDto` list; view raises `FloorSelected` |
| Floor background | Optional image path from host DTO |
| Table positioning | Absolute canvas with 20px snap grid while layout edit is allowed |
| Table shapes / sizes | Square 120×120 and wider rectangle tiles via `FloorTableVisualStyles` |
| Status colours | Free green, occupied amber, reserved/problem red (Mother palette) |
| Occupied / free / reserved | Mapped by host into `FloorTableVisualState` |
| Current order + guest count | Shown on occupied tiles from DTO fields |
| Table action sheet | Open / Move / Merge when capabilities allow |
| Cover picker | Shown for free tables before open |
| Responsive tablet layout | Compact overlays under ~900px width |
| Stale / offline chip | Sync chip + optional status banner when `ShowStaleWarning` |

Hosts map services into `FloorPlanDto` and handle action events only:

- Mother: `MotherFloorService` / `MotherTableService` → MariaDB (`FloorService`, `RestaurantTableService`, `TableSessionService`)
- Client: `ClientFloorService` / `ClientTableService` → SQLite cache + Mother API

SharedUI never branches on host type. Client sets `ShowStaleWarning` / offline capabilities so cached floor data is clearly marked stale while Mother is unreachable.
