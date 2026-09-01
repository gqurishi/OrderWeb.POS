# Phase 8: Dashboard

## Purpose

The Client POS dashboard changes by logged-in role and always shows the current connection/sync status.

## Staff Dashboard

Staff sees the daily POS actions:

- Tables
- Collection
- Delivery
- Online Orders
- Payments/Orders
- Logout

## Manager Dashboard

Manager includes Staff features plus:

- Discounts
- Refunds/Voids
- Reservations
- Gift Cards
- Loyalty
- Operational Reports

## Admin Dashboard

Admin includes:

- Reports
- Menu Updates
- Users/Staff
- Customers
- Business Controls allowed from Client
- Logout

Admin does not get deep system setup on Client.

## Super Admin Dashboard

Super Admin can see business/admin Client features, but deep setup remains Mother-only.

Client can show:

- Reports
- Menu/Business review
- Users/Staff summary
- Customers
- Business Controls
- Business Admin
- Logout

Mother-only remains:

- database setup
- master printer setup
- terminal master setup
- integration secrets
- permission design
- full system configuration

## Connection Status

The header now always shows one of:

- Connected
- Reconnecting
- Mother offline
- Syncing
- Demo Mode

Current demo behavior:

- Demo Mode shows `Demo Mode`.
- Bootstrap save shows `Syncing`.
- Successful bootstrap shows `Connected`.
- Bootstrap failure shows `Mother offline`.

Later WebSocket health should update `Reconnecting` and `Mother offline` in real time.
