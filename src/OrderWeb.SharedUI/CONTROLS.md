# Shared basic controls

`OrderWeb.SharedUI.Controls` is the canonical control library for Mother POS and Client POS. Its controls use the `Ow*` Mother POS design tokens from `OrderWebTheme`; host applications provide data, commands, and navigation only.

| Requirement | Shared control |
| --- | --- |
| Primary and secondary buttons | `PosButton` (`SharedButton` compatibility base, `ButtonVariant`) |
| Text and PIN inputs | `SharedTextInput`, `SharedPinInput` |
| Number keypad | `NumberKeypad` |
| Dashboard tile | `DashboardTile` |
| Sidebar item | `SidebarItemView` |
| Header | `PosHeader` (`ApplicationHeader` compatibility base; `HeaderView` remains available for compact cards) |
| User/terminal information | `UserTerminalInfo` |
| Connection indicator | `ConnectionIndicator` |
| Floor selector | `FloorSelector` |
| Guest count | `GuestCountControl` |
| Table card | `TableCard` |
| Product button | `ProductButton` |
| Category button | `CategoryButton` |
| Order line | `OrderLineView` |
| Status badge | `StatusBadge` (`StatusKind`) |
| Confirmation and error dialogs | `ConfirmationDialog`, `ErrorDialog` |
| Session expired | `SessionExpiredDialog` |

Both application projects reference this project and merge `OrderWebTheme` in `App.xaml`. New screens consume these types with:

```xml
xmlns:shared="clr-namespace:OrderWeb.SharedUI.Controls;assembly=OrderWeb.SharedUI"
```

Compatibility wrappers may retain an application-facing type name, but their visual tree must be a shared control rather than another implementation.

The Phase 4 proof controls are `PosButton`, `DashboardTile`, and `PosHeader`.
Their shared dimensions use the `PosButton*`, `PosDashboardTile*`, and
`PosHeader*` theme resources. Mother remains the canonical visual reference.

## Shared application shell

`ApplicationShellFrame` owns the full authenticated application frame: Mother-style header, role-filtered navigation, current user and terminal identity, Mother connection status, the main content host, logout, loading overlay, and recoverable error overlay.

Menu ownership remains with each host. Mother supplies operational and administration routes after applying its service and role policy. Client supplies only Client-supported routes. `ApplicationNavigationItem.AllowedRoles` provides the final basic-user/manager/admin filter inside the shared sidebar.

`ApplicationSidebar` is also exposed separately so Mother can use the same navigation surface inside its native MAUI `Shell` flyout while retaining its established route engine. `ApplicationHeader` is used by Mother page top bars and Client page compatibility wrappers, keeping the frame visually identical during page-by-page migration.
