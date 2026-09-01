# Phase 9: Table And Order Workflow

## Mother POS Behavior Checked

The Mother POS table workflow uses:

- floor pills
- visual table cards
- table/session status
- active table sessions
- linked open orders
- cover count popup
- green table = idle/available
- amber table = active/editable order/session
- red table = reserved/cleaning/problem attention
- live refresh when another terminal changes table/order state

The Client POS now follows the same working model.

## Table Screen

Client table screen now reads from SQLite cache:

- floors
- tables
- table status
- open bill/order id
- covers
- current server/waiter
- session status
- current total
- table version

SQLite is still display/cache only.

## Open/Create Order

When a table is tapped:

- if it already has an open order, the Client opens it through Mother command placeholder
- if it is free, the Client asks for covers
- Client sends create/open command to Mother placeholder
- Mother validates and returns order state
- Client saves returned order state into SQLite

Future real endpoint:

```text
POST /api/client/orders/table/open
```

## Order Screen

Order screen now displays from SQLite/Mother state:

- categories
- products/items
- modifiers
- current basket/order lines
- notes
- quantity
- subtotal
- VAT/tax
- total
- order version

## Add/Edit/Remove Items

The Client does not directly finalize order edits.

Every action goes through Mother command placeholder first:

- add item
- update quantity
- remove item
- add note
- void request

Mother returns the latest order state, then Client saves it to SQLite.

Future real endpoints:

```text
POST /api/client/orders/{orderId}/items
PATCH /api/client/orders/{orderId}/items/{lineId}
DELETE /api/client/orders/{orderId}/items/{lineId}
POST /api/client/orders/{orderId}/notes
```

## Send To Kitchen

Client requests send/print through Mother.

Mother updates order status and handles printer routing.

Future real endpoint:

```text
POST /api/client/orders/{orderId}/send-to-kitchen
```

## Conflict Handling

If another Client changes the order:

- Mother returns the latest order state
- Client saves latest state into SQLite
- Client shows a conflict/latest-state message
- order screen refreshes from Mother state

Current demo has a `Refresh Latest` button and command placeholder conflict path.
