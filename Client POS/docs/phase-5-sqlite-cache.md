# Phase 5: SQLite Cache

## Purpose

The Client POS uses SQLite as a local cache only. It helps the iPad/tablet stay fast and responsive, but it is not the source of truth.

Mother POS and MariaDB remain the source of truth.

## Cache Tables

The client cache now creates these areas:

- `device_config`
- `mother_connection`
- `sync_state`
- `event_checkpoint`
- `permissions_cache`
- `categories`
- `products`
- `prices`
- `modifier_groups`
- `modifiers`
- `product_modifiers`
- `tax_rates`
- `floors`
- `tables`
- `open_orders`
- `order_items`
- `online_orders_cache`
- `reservations_cache`
- `customers_cache`
- `pending_actions`
- `sync_errors`

## Versioning

Cache version and sync checkpoints are tracked in `sync_state`:

- `schema_version`
- `last_bootstrap_time`
- `last_sync_time`
- `last_event_id`
- `last_full_refresh_time`

Event stream checkpoints are also stored in `event_checkpoint`.

## Reset

The client app has a reset path that clears cache tables and re-bootstraps demo data.

In production this reset should call Mother API again and download:

- restaurant setup
- users/permissions
- menu
- prices/tax
- floors/tables
- open orders
- online order queue
- reservations
- customer cache

## Safety Rule

SQLite must never finalize a business write by itself.

Examples:

- Payment close must go to Mother.
- Send to kitchen must go to Mother.
- Receipt print request must go to Mother.
- Void/discount/table transfer must go to Mother.

When offline or waiting, the client can add a record to `pending_actions`, but Mother must accept and confirm it before the action becomes final.
