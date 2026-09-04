# OrderWeb history search contract

The POS uses the existing authenticated pull endpoint for orders outside its seven-day local cache.

## Request

`GET /api/pos/pull-orders?tenant={tenantSlug}&limit=100&include_history=true&search={query}`

The request sends the same `Authorization: Bearer`, `X-API-Key`, and `Accept: application/json` headers as live order polling.

- `include_history=true` means the search is not restricted to the normal live/recent pull window.
- `search` must match a full or partial `order_number`, `order_id`, or customer phone number.
- Phone matching should ignore spaces and common punctuation.
- Results must be tenant-scoped and limited to 100 records, newest first.
- This is read-only. It must not change order status, print status, or acknowledgement state.

## Response

Return the normal OrderWeb contract-v2 envelope and full order objects:

```json
{
  "contract_version": 2,
  "success": true,
  "orders": []
}
```

Each result must contain the same complete contract-v2 data used by live orders, including IDs, status, timestamps, customer/contact/address, order type, scheduled time, instructions, items/add-ons/variants, all totals/fees/tax/service charge/tips, payment method/status/amount/provider/reference/currency, voucher/promo, masked gift-card details, and loyalty details.

No match is a successful response with an empty `orders` array. Authentication, tenant isolation, validation, and server errors should use the existing API error format and appropriate HTTP status.
