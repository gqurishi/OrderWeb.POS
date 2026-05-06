# Order Lifecycle Phase 2 - MariaDB Contract Alignment

Status: Drafted contract for approval before code changes.
Scope: MariaDB schema contract for local POS lifecycle reliability, analytics, and auditability.
Depends on: ORDER_LIFECYCLE_PHASE1.md

## 1) Why Phase 2 is required

Current system has a lifecycle vocabulary gap:
- Existing orders status enum is operational order flow only (`new`, `kitchen`, `preparing`, `ready`, `delivering`, `completed`).
- Some query logic expects `cancelled` / `void` semantics in filters.
- Lifecycle concepts from Phase 1 (Draft, Active, SentPartial, SentFull, PaymentPartial, Paid, Voided) are not persisted explicitly.

Evidence references:
- Orders schema creation: Infrastructure/Database/Services/DatabaseService.cs
- Table session active filter: Features/Orders/Pages/LiveOrderPage.xaml.cs
- Order number settings already correct: Features/Orders/Services/OrderNumberService.cs

## 2) Contract goals

- Normalize status vocabulary across DB and query expectations.
- Add explicit lifecycle fields on `orders` for local POS flow.
- Add durable, append-only records for payments and lifecycle events.
- Add item-level send tracking for kitchen/bar routing reliability.
- Keep `order_number_settings` unchanged (already compliant with 3-char prefix).

## 3) Status normalization policy

### 3.1 Canonical split of concerns
- `orders.status`: keep operational pipeline status (kitchen/preparing/etc.) for cross-channel compatibility.
- `orders.local_lifecycle_state`: add local POS lifecycle state from Phase 1.

### 3.2 Allowed values
- `orders.status` (existing): `new`, `kitchen`, `preparing`, `ready`, `delivering`, `completed`
- `orders.local_lifecycle_state` (new):
  - `draft`
  - `active`
  - `sent_partial`
  - `sent_full`
  - `payment_partial`
  - `paid`
  - `voided`

### 3.3 Mapping rules
- `local_lifecycle_state = paid` implies `status = completed`
- `local_lifecycle_state = voided` implies order is terminal but does not require changing legacy `status` enum if avoiding enum expansion.
- Local POS open-order screens must primarily use `is_open` + `local_lifecycle_state`, not `status NOT IN (...)` filters.

## 4) Orders table additions

Add the following fields to `orders`:
- `local_lifecycle_state` ENUM('draft','active','sent_partial','sent_full','payment_partial','paid','voided')
  - default `draft` for new local orders
- `is_open` BOOLEAN/TINYINT(1)
  - default 1 for local editable orders
  - must become 0 for terminal states (`paid`,`voided`)
- `void_reason` VARCHAR(255) NULL
- `voided_at` DATETIME NULL
- `voided_by` VARCHAR(100) NULL (or user id style if standardizing later)
- `paid_at` DATETIME NULL

Recommended indexes:
- `(is_open, source_channel, order_type, created_at)`
- `(local_lifecycle_state, created_at)`
- `(voided_at)`
- `(paid_at)`

## 5) Durable payment detail table

Create `order_payments` (append-only, one row per tender event):
- `id` PK
- `order_id` FK -> `orders.id`
- `attempt_no` INT
- `payment_method` ENUM('cash','card','gift_card','refund','tip_adjust')
- `amount` DECIMAL(10,2)
- `currency_code` CHAR(3) default 'GBP'
- `status` ENUM('attempted','approved','failed','voided')
- `reference` VARCHAR(100) NULL (card ref, gift card ref)
- `tip_amount` DECIMAL(10,2) default 0
- `metadata_json` JSON/TEXT NULL
- `created_at` DATETIME
- `created_by` VARCHAR(100) NULL

Indexes:
- `(order_id, created_at)`
- `(order_id, status)`
- `(payment_method, created_at)`

Rules:
- Never overwrite approved rows.
- Use additional rows for corrections/refunds.

## 6) Order event log table

Create `order_events` (append-only timeline):
- `id` PK
- `order_id` FK -> `orders.id`
- `event_type` ENUM(
  'created','line_added','line_removed','line_updated',
  'sent','resend','send_failed',
  'payment_attempt','payment_approved','payment_failed',
  'void_requested','voided','reopened',
  'table_transferred','table_merged',
  'state_changed'
)
- `actor_type` ENUM('user','system','manager')
- `actor_id` VARCHAR(100) NULL
- `actor_name` VARCHAR(150) NULL
- `event_at` DATETIME
- `payload_json` JSON/TEXT NULL

Indexes:
- `(order_id, event_at)`
- `(event_type, event_at)`

Rules:
- Append only.
- Use `payload_json` for flexible metadata without frequent schema churn.

## 7) Item-level send tracking table

Create `order_item_send_tracking`:
- `id` PK
- `order_id` FK -> `orders.id`
- `order_item_id` FK -> `order_items.id`
- `send_batch_id` VARCHAR(36)
- `station_type` ENUM('kitchen','bar','receipt','other')
- `print_group_id` VARCHAR(36) NULL
- `route_target` VARCHAR(120) NULL (printer name/ip alias)
- `send_status` ENUM('queued','sent','printed','failed','retrying')
- `sent_at` DATETIME NULL
- `printed_at` DATETIME NULL
- `failure_reason` VARCHAR(255) NULL
- `attempt_count` INT default 0
- `created_at` DATETIME
- `updated_at` DATETIME

Indexes:
- `(order_item_id, created_at)`
- `(send_batch_id)`
- `(send_status, updated_at)`

Rules:
- New row per meaningful route attempt/batch for full audit.
- Latest status can be derived by max(created_at/id) per item+station.

## 8) Table session invariant support

To enforce “one open order per active table session”:
- Keep `TableSessions.CurrentOrderId` as active pointer.
- Add DB-side consistency check in service layer transaction logic:
  - when assigning a new open local order to active session, verify no existing open link.
- Optional (recommended): add unique constraint strategy using a helper relation table if MariaDB constraints on filtered uniqueness are insufficient.

## 9) Migration order (safe rollout)

1. Add new columns to `orders` with defaults and nullable terminal metadata.
2. Backfill existing local orders:
   - `is_open = 0` when status is completed
   - `local_lifecycle_state = paid` when status is completed
   - else set to `active` for non-completed local legacy orders
3. Create new tables (`order_payments`, `order_events`, `order_item_send_tracking`).
4. Add indexes after backfill for speed.
5. Deploy read compatibility first (app can read both legacy/new).
6. Deploy write path after read validation.

## 10) Backward compatibility rules

- Do not break existing web-order flows using `orders.status`.
- Local POS screens should migrate to `is_open` + `local_lifecycle_state` filters.
- Keep `order_number_settings` unchanged.

## 11) Validation checklist for Phase 2 sign-off

- `orders` contains new lifecycle and terminal metadata fields.
- `order_payments` exists and can represent split payments.
- `order_events` exists and captures lifecycle actions.
- `order_item_send_tracking` exists and captures send/print failures.
- Existing order_number_settings remains `order_prefix VARCHAR(3)`.
- No breaking change to current web-order status pipeline.

---
Owner: POS architecture track
Last updated: 2026-04-21
