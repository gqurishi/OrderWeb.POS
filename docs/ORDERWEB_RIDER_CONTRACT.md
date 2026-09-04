# OrderWeb Rider API contract

The POS Rider board is backed by committed delivery orders. OrderWeb must expose
the authenticated endpoints below under the configured `/api` base URL. The POS
sends both `Authorization: Bearer` and `X-API-Key`, plus the same stable value in
`Idempotency-Key` and `X-Idempotency-Key`.

## Request a quote

`POST /api/pos/rider/quotes`

Required request fields:

- `tenant`
- `pos_order_id`
- `cloud_order_id` (nullable for telephone/local orders)
- `order_number`
- `source` (`web` or `local`)
- `pickup.name`, `pickup.phone`, `pickup.address`, `pickup.city`, `pickup.postcode`
- `customer.name`, `customer.phone`, `customer.address`
- `requested_delivery_at` (nullable ISO-8601 value for ASAP orders)
- `order_value`, `currency`
- `payment_method`, `payment_status`
- `cash_collection_amount`
- `device_id`, `idempotency_key`

Successful response must include `quote_id` and a numeric `price`/`amount`.
It should also include `currency`, `expires_at`, `estimated_pickup_at`,
`estimated_delivery_at` and `provider_name`.

## Confirm and dispatch

`POST /api/pos/rider/dispatches`

Required request fields:

- `tenant`
- `quote_id`
- `pos_order_id`
- `cloud_order_id` (nullable)
- `cash_collection_amount`
- `idempotency_key`

Successful response must include a permanent `dispatch_id`/`job_id` and
`status`. Rider name and telephone can be returned inside a `rider` object.

OrderWeb must treat both endpoints as idempotent. Repeating the same key must
return the original quote or dispatch and must never book a second rider.

## Required follow-up contract

To provide live progress and safe recovery after a timeout or POS restart,
OrderWeb must additionally provide authenticated get/cancel endpoints and status
events for the permanent dispatch ID. Status values should map to:

- `GET /api/pos/rider/dispatches/{dispatch_id}?tenant={tenant}`
- `GET /api/pos/rider/dispatches/by-idempotency/{key}?tenant={tenant}` for safe timeout recovery

`finding_rider`, `rider_assigned`, `collected`, `delivering`, `delivered`,
`cancelled`.

Until those recovery endpoints are live, the POS deliberately places an
uncertain confirmation into `dispatch_check_required` and will not send another
dispatch automatically.
