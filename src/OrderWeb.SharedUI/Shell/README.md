# Shell

The shared shell owns visual composition for the header, sidebar, user and
terminal identity, connection state, content region, logout, session-expired,
and presentation overlays. Hosts provide navigation items after applying
permissions, features, and terminal capabilities via `PosNavigationCatalog`.

`ApplicationShellFrame` currently remains in `Controls` for compatibility and
will move here only when both host integrations can be updated together.
