# Phase 6: Bootstrap Sync

## Purpose

After pairing, the Client POS requests its first complete snapshot from Mother POS. The snapshot is saved into SQLite so the tablet can open fast and work from a local cache.

Mother POS and MariaDB remain the source of truth.

## Bootstrap Request

The client sends:

- Mother IP/API address.
- Pairing code.
- Terminal name.

The current demo app uses a Mother API placeholder. Later this becomes the real HTTP call to Mother POS.

## Bootstrap Payload

The payload model now includes:

- restaurant info
- terminal config
- menu categories
- products
- modifiers
- product modifier mapping
- prices
- tax/VAT
- floors/tables
- open orders
- current online orders
- reservations
- basic customer cache
- permission snapshot
- sync versions

## Save To SQLite

The bootstrap payload is saved into the Phase 5 cache tables:

- `restaurant_info`
- `mother_connection`
- `device_config`
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
- `permissions_cache`
- `sync_state`
- `event_checkpoint`

## Progress UI

The Client POS now shows:

- Connecting
- Downloading menu
- Downloading tables
- Downloading open orders
- Preparing POS

## Failure UI

If bootstrap fails, the client shows:

- Retry Bootstrap
- Reset Pairing
- Use Demo Mode

For demo testing, entering `fail` as the Mother IP or `000000` as the pairing code triggers the failure screen.

## Next Production Work

Replace the placeholder bootstrap client with the real Mother API endpoint:

```text
POST /api/client/bootstrap
```

The real endpoint should validate pairing, return the bootstrap payload, and include the latest event checkpoint so WebSocket sync can continue from the correct event ID.
