# Phase 10: Collection And Delivery

## Mother POS Behavior Checked

Mother POS uses separate Collection and Delivery customer screens.

Collection flow:

- customer name
- phone number
- search existing customer
- select existing customer
- continue to order

Delivery flow:

- customer name
- phone number
- address/postcode lookup
- structured address fields
- customer search
- delivery zone lookup
- delivery fee
- continue to order

The Client POS now follows the same style and workflow direction.

## Collection Flow

Client Collection now supports:

- customer name
- phone
- pickup time
- notes
- customer lookup
- order item entry
- payment flow

Client asks Mother placeholder to create the collection order, then saves the returned order state to SQLite cache.

## Delivery Flow

Client Delivery now supports:

- customer name
- phone
- address/postcode lookup field
- street address
- city
- county
- postcode
- delivery zone quote
- delivery fee
- scheduled time
- notes
- order item entry
- payment flow

If the postcode has no known zone, Client marks it as Mother/admin review behavior in the UI.

## Customer Lookup

The Client asks Mother placeholder for customer search and then caches recent results in SQLite:

- `customers_cache`

The Client also searches local cache so recent customers can appear fast.

Future real endpoints:

```text
GET /api/client/customers/search
POST /api/client/customers/upsert
GET /api/client/delivery-zones/quote?postcode=
POST /api/client/orders/customer/create
```

## Source Of Truth

SQLite is only recent customer/order cache.

Mother POS remains the source of truth for:

- customer save/update
- delivery zone and fee
- collection/delivery order creation
- payment
- printing
