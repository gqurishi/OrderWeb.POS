# Phase 13: Online Orders

## Rule

Client POS does not poll the cloud directly in multi-terminal mode.

Mother POS owns:
- online order cloud polling
- acknowledgement back to cloud
- status changes
- print routing
- broadcast events to clients

Client POS owns:
- displaying online orders from SQLite cache
- sending allowed actions to Mother
- saving Mother updates into SQLite cache
- showing reconnect/demo status

## Client Display

Online orders are read from `online_orders_cache`.

Each order shows:
- order number
- customer name
- collection/delivery/table type
- due time
- status
- total

## Client Actions

Allowed actions go through Mother:
- view order
- accept/status update
- print request
- mark ready
- complete
- cancel when permission allows it

## Event Flow

1. Mother receives or updates an online order.
2. Mother broadcasts the event over WebSocket.
3. Client saves the event to SQLite.
4. Client refreshes the Live Order screen from SQLite.

The current implementation includes a Mother broadcast simulator so the UI can be built before the real WebSocket endpoint is connected.
