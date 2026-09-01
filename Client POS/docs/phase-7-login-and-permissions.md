# Phase 7: Login And Permissions

## Purpose

After pairing and bootstrap, the Client POS shows login. The Client sends login to Mother POS and stores only the current session and permission snapshot locally.

Mother POS remains responsible for real authentication and permission truth.

## Login Methods

The Client POS now supports:

- PIN
- username/password
- staff code

Demo credentials:

- PIN `1111` = Staff
- PIN `2222` = Manager
- PIN `3333` = Admin
- PIN `9999` = Super Admin
- `staff/staff`
- `manager/manager`
- `admin/admin`
- `super/super`
- staff codes `S100`, `M200`, `A300`, `S900`

## Login Response

The Mother login placeholder returns:

- user ID
- user name
- role
- permission list
- session token
- expiry

Later this should become:

```text
POST /api/client/auth/login
```

## Permission Cache

The current session is saved in:

- `current_session`

The session permissions are saved in:

- `permissions_cache`

This is only the current client session cache. Mother POS remains the source of truth.

## Role Dashboards

Dashboard behavior now changes by role:

- Staff: restaurant, delivery, collection, live orders.
- Manager: staff features plus reports/cash/void/discount permissions.
- Admin: manager features plus menu review/request update and diagnostics style permissions.
- Super Admin: business/admin client features, terminal/business review, broader reports.

## Super Admin Rule

If Super Admin logs into Client POS, the Client can show business/admin features only.

Deep system setup remains Mother-only:

- database setup
- printer master setup
- terminal master setup
- integrations
- full menu/system configuration
- security/permission design

The Client should guide Super Admin to Mother POS for deep setup instead of trying to become Mother.
