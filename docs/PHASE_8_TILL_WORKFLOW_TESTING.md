# Phase 8 Till Workflow Testing

Use this checklist on a real till setup after the performance/sync changes. These tests need the MAUI app, local database, OrderWeb cloud access, printer setup, and at least one second till for cross-terminal checks.

## Current Known Blocker

Web Orders pull has a separate server-side API issue:

- Endpoint: `OrderWeb /api/pos/pull-orders`
- Error: `Unknown column 'oi.item_name'`
- Impact: performance/sync changes can make polling smarter, but web orders will not pull correctly until the server query/schema mismatch is fixed on OrderWeb.
- POS-side expectation after server fix: `Sync Now`, background backup polling, and page background sync should populate the local web order cache.

## Pre-Test Setup

- Mother terminal configured and connected.
- Child terminal configured if cross-till testing is needed.
- OrderWeb tenant slug and POS API key saved.
- Kitchen/bar/receipt printers configured.
- At least one active menu item with print group routing.
- At least one test table and floor.
- At least one reservation available in OrderWeb or created locally for sync.
- At least one active gift card available for lookup/redeem.
- Offline test path prepared by disabling network temporarily.

## Workflow Checklist

| Area | Test | Expected Result | Status |
| --- | --- | --- | --- |
| Add item | Open order screen, add normal priced item, add zero-price item if available | Items appear instantly, totals are correct, zero-price item is accepted and can still route to printer | Not run |
| Kitchen/bar print | Send order with routed items | Print job queues immediately, kitchen/bar printer receives only routed items, UI stays responsive | Not run |
| Payment | Complete cash/card payment | Payment completes without background sync freezing the till; order/table state updates | Not run |
| Web order arrival | Create online order in OrderWeb | WebSocket/live path or backup poll stores order locally and Web Orders page shows cached order | Blocked until `oi.item_name` server API issue is fixed for pull-orders |
| Web order search | Search by order number/customer/phone | Search filters cached orders without lag and opens keyboard on focus | Not run |
| Other till table update | Change table/order state on second till | Current till receives typed live update; Visual Table updates current floor status without full rebuild where possible | Not run |
| Reservation sync | Create/update reservation from cloud or till | Reservation sync runs in managed background cadence without slowing active order/payment work | Not run |
| Gift card lookup | Scan/enter gift card for redeem | POS calls live OrderWeb lookup with purpose `redeem`; blocked cards show cloud message and stop | Not run |
| Gift card redeem | Redeem valid gift card amount | POS calls live redeem API with idempotency key; no cached-balance redemption | Not run |
| Offline recovery | Disable network, perform queued-safe action such as activate gift card after local payment, then reconnect | Failed activation is queued, queue stays within 50 items, flush runs when online/idle and does not duplicate transaction | Not run |
| Background behavior | Use till actively during order/payment | Low-priority sync slows/pauses; critical print/payment actions continue immediately | Not run |
| Idle catch-up | Leave till idle after activity | Queues and backup sync catch up; UI does not reload hidden pages | Not run |

## Pass Criteria

- Cashier actions feel immediate during add item, send, print, and payment.
- Background jobs do not create visible pauses during active use.
- Hidden pages do not keep reloading heavy data.
- Web Orders show cached/local data immediately when the page opens.
- Cross-till updates refresh only relevant screen areas where implemented.
- Offline recovery uses idempotency and does not duplicate gift-card or order actions.
- No `InternalServerError` remains for web-order pull after the server `oi.item_name` issue is fixed.

## Automated Coverage Available

Current repo automated tests are limited to database setup/migration policy:

- `tests/OrderWeb.DatabaseSetup.Tests`

These do not replace the real till workflow tests above.
