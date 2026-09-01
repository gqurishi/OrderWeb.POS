# Phase 1: New Client App Foundation

Phase 1 defines the base identity, target platforms, navigation, and design system for the separate OrderWeb Client POS app.

## Job 1.1: Separate App Identity

Create a separate app/product identity:

```text
OrderWeb Client POS
```

This is separate from:

```text
OrderWeb Mother POS
```

### Identity Requirements

- Own app name.
- Own app icon.
- Own splash/launch branding.
- Own bundle/app identifier.
- Own versioning.
- Own solution/project folder.
- No Mother/Child setup language in first launch.

### Product Meaning

OrderWeb Client POS is always a connected Client terminal. It is installed on secondary devices such as:

- Windows POS terminal.
- MacBook/Mac desktop if needed.
- iPad.
- Android tablet.

It does not install MariaDB and does not expose system/database setup.

## Job 1.2: Platform Targets

Target platforms:

- Windows.
- MacCatalyst/MacBook.
- iOS/iPadOS.
- Android.

### Platform Notes

- Windows Client should support touch and mouse/keyboard.
- Mac Client should support MacCatalyst window sizing and pointer input.
- iPad Client should be landscape-first and handle sleep/wake reconnect.
- Android Client should be tablet-first and handle sleep/wake reconnect.

## Job 1.3: App Navigation

Create the main navigation structure around the Client-only flow.

### Required Navigation Areas

- Connect screen.
- Demo Mode entry.
- Login.
- Dashboard.
- Tables.
- Order.
- Payment.
- Online Orders.
- Diagnostics.
- Settings.

### Startup Flow

```text
If not paired:
    Show Connect to Mother POS

If paired:
    Try connect to Mother
    Refresh/sync cache
    Show Login

If Demo Mode selected:
    Load demo cache
    Show demo login/dashboard flow
```

### Navigation Rule

Client navigation must not include Mother setup, MariaDB setup, backup/restore, or database migration screens.

## Job 1.4: Design System

Prepare a shared touch-first design system for the Client app.

### Design Areas

- Colors.
- Buttons.
- Typography.
- Cards/panels.
- Status banners.
- Dialogs.
- Tablet spacing.
- Responsive desktop/tablet layout.
- Dark/light mode later if useful.

### Touch-First Requirements

- Large tap targets.
- Landscape-first tablet layouts.
- Clear order summary.
- Fast category switching.
- Minimal popups during order entry.
- Visible connection status.
- Visible Demo Mode status.
- Clear error and reconnect states.

## Login Design Requirement

The Client login screen should use the same core login experience as the Mother POS:

- Two-panel layout on wide screens.
- Restaurant/brand panel.
- PIN keypad login.
- PIN dot display.
- Error/status banner.
- Loading state.
- Role-based navigation after login.

The Client login must remove direct terminal database checks and instead check Mother connection/session state.

See `login-design-baseline.md`.

