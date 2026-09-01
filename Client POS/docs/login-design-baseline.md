# Client Login Design Baseline

The Client POS login should match the Mother POS login experience closely so staff do not feel like they are using a different system.

Source reference in the current Mother POS:

```text
Features/Authentication/Pages/LoginPage.xaml
Features/Authentication/Pages/LoginPage.xaml.cs
```

## Keep From Mother Login

### Layout

- Two-column layout on wide screens.
- Left side for branding and restaurant identity.
- Right side for PIN login.
- Centered content with strong touch spacing.

### Branding

- POS/OrderWeb branding.
- Restaurant name from Mother/bootstrap cache.
- Optional restaurant description/location if available.

### PIN Login

- 4-digit PIN entry.
- Numeric keypad.
- PIN dot display.
- Clear/reset behavior.
- Automatic login when 4 digits are entered.

### Feedback States

- Error banner for wrong PIN or denied access.
- Status banner for connection/session problems.
- Loading indicator while login is checked.
- Clear message if the Client is disconnected from Mother.

### Role Routing

After successful login, route by role/permissions:

- Staff/User dashboard.
- Manager dashboard.
- Admin/Owner dashboard.
- Super Admin with Client-allowed features only.

## Change For Client POS

The Client login must not use:

- Direct MariaDB authentication.
- Terminal Mother/Child checks.
- Child schema gate checks.
- Database connection string checks.
- Navigation back to Mother setup.

Instead, Client login should use:

- Stored paired Mother address.
- Stored device token.
- Mother API login endpoint.
- Mother-returned session token.
- Mother-returned role and permission list.
- Cached restaurant info from bootstrap sync.

## Client Login Flow

```text
Client app is paired
Client has bootstrap cache
User enters PIN
Client calls Mother auth API
Mother validates PIN/user
Mother returns session + permissions
Client opens role dashboard
```

## Disconnected Login Rule

Version 1 should not allow real login if Mother is unavailable.

If Mother is disconnected:

- Show the same login UI.
- Show a clear Mother disconnected status.
- Allow Demo Mode only if the user selected Demo Mode.
- Do not allow real payment/order actions from an offline login.

## Demo Mode Login

Demo Mode can use sample users for design/testing, but it must show a visible Demo Mode banner.

Suggested demo roles:

- Staff PIN.
- Manager PIN.
- Admin PIN.

Do not use `admin/admin` as the real Client login model.

