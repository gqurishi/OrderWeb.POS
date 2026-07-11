# Background Sync Audit

Phase 0 read-only audit of timers, polling loops, WebSocket handlers, queue flushers, startup background tasks, and page auto-refresh paths.

## Summary

The app has the right building blocks for a reliable till, but the background work is scattered across startup code, services, and page constructors. The largest performance risks are duplicate online-order paths, aggressive reservation and direct database polling, page-owned cloud timers, full-screen reloads from broad refresh events, and a few hidden-screen handlers/timers.

## Startup And Always-On Jobs

| Owner | Trigger / interval | Work | DB/API calls | Runs on child tills | UI refresh | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| `TerminalHealthService` | Startup after about 3s, then every 30s | Upserts current terminal health | Local/MariaDB `terminal_health` | Yes, when configured | Connection state indirectly | Useful, but should be manager-owned. |
| `DatabaseChangeMonitorService` | Startup after about 5s, then every 2.5s | Polls `terminal_events` for other till changes | Local/MariaDB | Yes | Calls `AppDataRefreshService` | Important but frequent; can fan out to many pages. |
| `DatabaseBackupService` | Mother only, starts after about 3s; first check after 20s, then hourly | Daily backup check/export | Local/MariaDB + filesystem | No | No | Low priority; should run only idle/night. |
| `CleanupSchedulerService` | Mother only, startup after about 8s; first check after 5m, then hourly | Deletes old cached OrderWeb data | Local/MariaDB | No | No | Low priority. |
| `ReportSchedulerService` | Mother only, startup after about 2s; first check after 30s, then hourly | Daily/weekly/monthly reports and OrderWeb daily upload | Local DB + OrderWeb API | No | No | Heavy low-priority job. |
| `PrinterHealthService` | Startup after about 1.5s, then every 30s | Checks all printer reachability | Printer network + DB status writes | Yes, if terminal configured | Events only | Good, but duplicate with printer setup page status refresh. |
| `NetworkPrintQueueService` | Startup after about 1.5s if printing policy allows; every 5s | Processes pending print jobs | DB queue + printer network | Policy controlled | Print events | Critical, but should slow down when queue is empty. |
| `AppShell` / `InactivityService` / `TopBar` | Every 1s | Clock and inactivity checks | No regular DB/API | Yes | UI thread every second | Lightweight, but duplicated across shell/topbar/login/dashboards. |

## Cloud And Online Jobs

| Owner | Trigger / interval | Work | DB/API calls | Runs on child tills | UI refresh | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| `OrderWebConnectionKeeperService` | App cloud init after about 5s; health monitor starts after 60s, repeats 90s | Owns OrderWeb WebSocket, polling, heartbeat, ACK retry, catch-up sync | Local DB + OrderWeb API | No, role-gated online master | Status event | Correct central-ish owner, but not the only owner yet. |
| `OrderWebWebSocketService` | Persistent receive loop; keepalive every 30s; reconnect backoff 5s/10s/30s/60s | Live order/reservation/gift-card/loyalty events | OrderWeb WebSocket + local DB through processors | No, role-gated | Events into pages/services | Preferred live path. |
| `CloudOrderService` polling | Immediate poll, then every 15s | Pulls confirmed online orders | `GET /pos/pull-orders`, local order writes | No | `OnOrdersUpdated` | Backup path, but duplicates WebSocket/webhook/direct DB/page sync. |
| `CloudOrderService` ACK retry | Starts after 30s, then every 60s | Retries pending order ACKs | `pending_acks`, OrderWeb ACK API, limit 50 | No | No | Good reliability job. |
| `CloudOrderService` backfill sync | On connection config/reconnect, Web Orders page load, sync button | Pulls last 7 days with fallback endpoint shapes | OrderWeb API + local order writes | No | `OnOrdersUpdated` | Heavy and duplicated. Should not run just because page opens. |
| `OrderWebWebhookListenerService` | HTTP listener loop, no interval | Receives cloud push webhooks | Local HTTP listener, local DB/API processors | No, app cloud init role-gated | Through processors | Optional. Overlaps WebSocket. |
| `OrderWebDirectDatabaseService` | Optional, if `direct_db_enabled=True`; every 500ms | Polls remote OrderWeb DB for orders | Remote DB + local DB | No, app cloud init role-gated | `OnNewOrdersDetected` | Very aggressive. Should be disabled or slowed to 5s+ if ever enabled. |
| `ReservationSyncService` inbound | Config `reservation_poll_seconds`, default/clamp can be 5s | Pulls today's reservations | OrderWeb API + local `cloud_reservations` | No | `SyncCompleted` / notifications | Aggressive when WebSocket/webhook also active. |
| `ReservationSyncService` outbound | Every 60s | Uploads pending reservations and reservation ACKs | Local DB + OrderWeb API | No | No | Important but should be queue-aware. |
| `OfflineQueueService` | Immediate, then every 30s | Flushes generic offline API queue | `offline_queue` + configured endpoints | No, app cloud init role-gated | `QueueProcessed` event | Should run only when queue has work or after network restored. |
| `GiftCardActivationQueueService` | Starts after 15s, then every 30s | Flushes failed gift-card activations, max 50 | Preferences + OrderWeb gift-card flush API | No, app cloud init role-gated | No | Should run only when pending activations exist. |
| `HeartbeatService` | Immediate, then every 30s | Cloud POS heartbeat and stats | DB counts + `POST /pos/heartbeat` | No | No | Main cloud heartbeat. |
| `CloudSyncService` | Constructor starts 60s heartbeat | Older cloud heartbeat/sync helper | Cloud heartbeat endpoint | Role-gated internally | Settings events | Duplicate heartbeat. `UnifiedSettingsPage` creates it manually. |
| `ConnectionManager` | Registered, not currently started | Would monitor DB/API/OrderWeb failover every 30s and reconnect every 60s | DB/API tests | Unknown | Events | Currently unused. Candidate to remove or fold into new manager. |
| `BackgroundSyncService` | Not currently initialized by page; if initialized, 60s sync + 30s status transitions | Legacy online order sync/status worker | Old API service + local order writes | Unknown | `SyncStatusChanged` | `OrderManagementPage` constructs it but does not initialize it. Candidate to remove. |

## Page Refresh Jobs

| Page | Trigger / interval | Work | DB/API calls | Hidden-screen behavior | Notes |
| --- | --- | --- | --- | --- | --- |
| `WebOrdersPage` | Constructor subscribes WebSocket, cloud polling, and direct DB events; 10s status timer; page load syncs last 7 days | Loads all local orders, filters/paginates, updates list | Local DB; page load also OrderWeb API | Timer disposed on disappear; service event handlers are not unsubscribed | High risk for stacked handlers and refresh while hidden/recreated. Debug order dump is noisy in DEBUG. |
| `OrderManagementPage` | Constructor starts 30s timer; refresh events while visible | Reloads all orders | Local DB | Timer disposed on disappear | Timer should start on appearing, not constructor. |
| `LiveOrderPage` | `AppDataRefreshService.DataChanged` while visible | Reloads all open/collection/delivery/table sessions, batched by gate | Local DB | Unsubscribes on disappear | Good gating, but still full reload on any relevant change. |
| `OrderPlacementPageSimple` | Live update subscription while visible; sends refresh on order changes/disappear | Reloads current order when changed elsewhere | Local DB | Unsubscribes | Good targeted refresh. |
| `VisualTablePage` | Visible-only auto-refresh every 8s; basic-user idle every 1s; app refresh events | Reloads floor/table/session layout | Local DB | Stops timers and unsubscribes | Expensive 8s polling plus live-update polling duplicates work. |
| `RestaurantPage` | Constructor starts 10s status timer | Updates connection/status UI | No heavy DB seen | Timer is not disposed on disappear | Clear hidden-screen timer issue. |
| `TablePage` / `FloorPage` | App refresh events while visible | Reloads table/floor lists | Local DB | Unsubscribes | Has basic throttle and operation guard. |
| `ReportPage` | App refresh events while visible | Reloads current report and historical state | Local DB + possible OrderWeb upload state | Unsubscribes | Heavy full reload on broad refresh. |
| `PrinterSetupPage` | Visible-only 10s timer | Updates printer/queue status | Local DB/service stats | Disposes on disappear | Duplicates global printer health polling, but acceptable for visible settings. |
| `CloudSettingsPage` / `UnifiedSettingsPage` | Visible-only 3s status timer | Updates cloud status labels | Service state mostly | Disposes on disappear | `UnifiedSettingsPage` also creates unmanaged `CloudSyncService`. |
| Login/user/manager dashboards, time clock, virtual keyboard | 1s clock/idle timers; keyboard blink 500ms | UI-only | No regular DB/API | Mostly stops on disappear | Lightweight, but duplicated UI-thread work. |

## Main Duplicates And Aggressive Areas

1. Online orders have too many active paths: WebSocket, 15s API polling, optional webhook, optional 500ms direct DB polling, Web Orders page backfill, and manual update backfill.
2. Reservations can be received by WebSocket/webhook and also pulled as often as every 5 seconds.
3. Heartbeats are duplicated: terminal DB heartbeat, cloud heartbeat, and older `CloudSyncService` heartbeat.
4. Live till updates poll `terminal_events` every 2.5 seconds, while visual table also reloads every 8 seconds.
5. Print queue checks every 5 seconds even when empty; gift-card/offline queues flush every 30 seconds even when empty.
6. Broad `AppDataRefreshService.RequestRefresh(All)` can make several visible heavy pages reload everything.

## Hidden-Screen / Lifecycle Risks

- `WebOrdersPage` subscribes to WebSocket, cloud order, and direct DB events in the constructor and does not unsubscribe on disappear.
- `RestaurantPage` starts a 10-second timer in the constructor and does not dispose it on disappear.
- `UnifiedSettingsPage` creates `CloudSyncService` directly; that service starts a heartbeat timer in its constructor and is not owned by DI or a central manager.
- `ManagerDashboardPage` recreates its timer on each appearance and only stops it, leaving old timer instances to be collected later.
- `BackgroundSyncService` and `ConnectionManager` appear to be legacy/unused but still registered or instantiated, which makes ownership unclear.

## Recommended Phase 1 Quick Wins

1. Make WebSocket the primary online-order path and keep 15s polling as backup only; remove page-open backfill from `WebOrdersPage`.
2. Move `WebOrdersPage` event subscriptions into `OnAppearing`/`OnDisappearing` with named handlers.
3. Disable or slow `OrderWebDirectDatabaseService` to 5s+ if direct DB mode is ever enabled.
4. Increase reservation backup polling when WebSocket/webhook is active; keep immediate manual sync.
5. Remove the page-owned `CloudSyncService` heartbeat or convert it into explicit "test connection" only.
6. Move `RestaurantPage` timer lifecycle to visible-only.
7. Make print, offline, and gift-card queues queue-aware: fast when pending, slow or stopped when empty.
8. Add refresh batching inside `AppDataRefreshService` and send specific change kinds instead of broad reloads.
9. Reduce DEBUG noise around full API responses and full local order dumps.

## Build Direction

Phase 1 should stop the worst lag without changing behavior. Phase 2 should introduce a `BackgroundSyncManager` where jobs register name, priority, interval, idle interval, failure backoff, mother/child eligibility, required resources, and UI refresh signal. After that, page refreshes should consume batched change events instead of running their own polling loops.

## Phase 2 Status

Implemented the first central `BackgroundSyncManager` pass. Jobs now register with:

- name
- priority
- normal interval
- idle interval
- failure backoff
- required resources
- terminal scope
- whether they can run during payment/order entry
- last run, next run, last error, failure count, and status snapshots

The manager is registered in DI and started from app startup and first-time terminal setup. `InactivityService` now reports user activity and critical activity to the manager, so lower-priority jobs can pause while the cashier is in payment/order-entry flows.

Manager-owned jobs in this pass:

- `terminal-health-heartbeat`
- `database-backup-scheduler`
- `printer-health-check`
- `network-print-queue`

Pending before Phase 3:

- OrderWeb cloud connection keeper, WebSocket, heartbeat, ACK retry, and backup polling
- reservation sync timers
- gift card activation queue flush
- offline queue flush
- cleanup/report schedulers
- live database change monitor
- page-owned refresh timers

## Phase 3 Status

Moved the next scheduler batch into `BackgroundSyncManager`. WebSocket remains live/event-driven; API polling now runs as a managed backup job.

Manager-owned after Phase 3:

- `cloud-heartbeat`
- `orderweb-connection-health`
- `gift-card-activate-flush`
- `offline-api-queue-flush`
- `online-order-api-backup-poll`
- `online-order-ack-retry`
- `reservation-inbound-poll`
- `reservation-maintenance`
- `till-live-update-poll`
- `database-cleanup-scheduler`
- `report-generation-scheduler`

Direct startup timers removed for:

- cloud heartbeat repeat loop
- online order API polling repeat loop
- online order ACK retry repeat loop
- gift card activation queue auto-flush
- offline API queue auto-processing
- reservation inbound polling
- reservation upload/ACK maintenance
- till live update polling
- cleanup scheduler
- report scheduler

Still intentionally outside the manager:

- OrderWeb WebSocket receive loop and keepalive, because it is event-driven.
- Webhook listener, because it is a listener, not polling.
- Page-local visible timers such as clocks, keyboard idle behavior, and visible settings/status refreshes.
- Optional direct OrderWeb database monitor, which remains disabled unless configured and should be reviewed separately before enabling in production.

## Phase 4 And 5 Status

Implemented priority and user-activity rules in `BackgroundSyncManager`.

Priority behavior:

- Critical jobs can run while the till is active or in payment/order-entry mode.
- Important jobs continue to run on controlled intervals, but heavy important jobs can opt out of active cashier time.
- Low jobs wait while the cashier is active and run when the till becomes idle.

User activity behavior:

- `InactivityService` reports touches, entry changes, and payment/order-entry critical sections to the manager.
- Jobs with `CanRunDuringPaymentOrOrderEntry = false` pause during payment/order-entry.
- Jobs with `CanRunWhileUserActive = false` pause while the cashier is actively using the till.
- Idle intervals let backup sync and queue flushing catch up faster after the cashier stops interacting.

Immediate wakeups added:

- New print jobs request `network-print-queue` immediately.
- New offline API queue items request `offline-api-queue-flush` promptly, but that job still waits for idle if the cashier is active.
- New queued gift-card activations request `gift-card-activate-flush` promptly, but that job still waits for idle/payment-safe timing.

Current priority map:

- Critical: `network-print-queue`
- Important: cloud heartbeat, OrderWeb connection health, online order backup polling, online order ACK retry, till live update polling, reservation polling, reservation maintenance, gift-card activation flush, offline queue flush
- Low: database backup, database cleanup, report scheduler, printer health check

Direct cashier actions such as payment, gift-card lookup/redeem, and order send still run in their own user-action paths, not as scheduled background jobs.

## Phase 6 Status

Implemented smart app-data refresh batching.

Refresh behavior now includes:

- Rapid app-data updates are batched for 400ms before notifying pages.
- Refresh events carry typed refresh flags: orders, tables, reservations, settings, and printers.
- Legacy full-refresh subscribers only run for true manual/full refresh requests, not for partial updates like orders + tables.
- Live terminal events preserve entity metadata, including entity type and entity ID, so pages can move toward row/card-level updates.
- Table/floor layout changes now request table refresh only.
- Order changes from the order-entry flow now request orders + tables only.
- Visible page handlers use typed checks such as orders or table layout instead of treating every refresh as a full reload.

Updated visible-page refresh handlers:

- `VisualTablePage`
- `TablePage`
- `FloorPage`
- `RestaurantPage`
- `LiveOrderPage`
- `OrderManagementPage`
- `OrderPlacementPageSimple`
- `ReportPage`

Remaining follow-up:

- Some heavy pages still reload their full local data set within their refresh type. The broad full-app reload is reduced, but row/card-level updates should be added screen by screen where it is worth the extra complexity.
- Reservations and printer settings now have typed refresh channels, but their pages mostly use their existing sync/status events. They can be migrated later if cross-till live refresh is needed there.

## Phase 7 Status

Implemented the first heavy-screen optimization pass.

Web Orders:

- Keeps a local in-memory order cache after the first database load.
- Date changes, search, and pagination repaint from cache instead of hitting the database every time.
- Search input is debounced for 250ms.
- Manual sync, cloud events, order completion, and print queue actions still force a fresh local reload.
- Reopening the page shows cached data first, then refreshes the local cache in the background.

Visual Table:

- Order updates now refresh the current floor's table states in place where possible.
- Full layout rebuild is reserved for floor/table layout changes or when the table set has changed.
- The visible auto-refresh timer is now a slower backup, not the primary status update path.

Food Menu:

- `FoodMenuPageNew` keeps existing collections on screen instead of clearing them on every appearance.
- Menu tabs load lazily: items, categories, sub-categories, and notes load only when needed.
- `FoodMenuManagement` loads core category/item data first, then loads meal deals and tasting menus lazily/background.

Reports:

- Initial report data loads first.
- Historical report list and OrderWeb upload banner refresh in the background.
- Live order updates refresh the current report only, without reloading historical snapshots every time.

Settings:

- Settings tab data now lazy-loads once per tab during normal navigation.
- Explicit save/refresh actions still call their reload paths immediately.

Customer Screens:

- Recent Customers search is debounced for 250ms.
- Customer cache purge is throttled instead of running on every search keypress.

Remaining follow-up:

- Some XAML screens still contain nested `ScrollView` + list layouts. The next UI-only pass should flatten those layouts where scrolling feels sticky.
- More row/card-level updates can be added for reports and menu management, but this pass keeps behavior conservative.

## Phase 8 Status

Added the real till workflow testing checklist in `docs/PHASE_8_TILL_WORKFLOW_TESTING.md`.

Phase 8 covers:

- add item
- kitchen/bar print
- payment
- web order arrival
- other till table update
- reservation sync
- gift card lookup/redeem
- offline then online recovery
- active-user background throttling
- idle catch-up behavior

Known blocker:

- Web Orders still has a separate OrderWeb server-side API problem: `/api/pos/pull-orders` is returning `Unknown column 'oi.item_name'`.
- The performance/sync work makes POS polling and refresh behavior smarter, but that server API query/schema issue must be fixed on OrderWeb before pull-orders can reliably populate Web Orders.

Automated coverage note:

- The repo currently has automated tests for database setup/migration policy under `OrderWeb.DatabaseSetup.Tests`.
- The till workflows in Phase 8 require manual/integration testing with printers, OrderWeb cloud, gift cards, reservations, and a second till.
