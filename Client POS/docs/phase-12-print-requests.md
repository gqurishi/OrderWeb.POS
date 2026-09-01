# Phase 12: Print Requests

## Rule

Client POS never owns printer setup, printer routing, printer drivers, or cash drawer hardware.

Mother POS owns:
- kitchen printer routing
- receipt printer routing
- bill printing
- reprints
- cash drawer open command
- printer status

Client POS only sends a request to Mother and shows the returned status.

## Client Actions

Client can request:
- kitchen ticket
- receipt
- bill
- reprint
- cash drawer open when permission allows it

## Statuses

Client stores recent print requests in SQLite and displays:
- queued
- printing
- printed
- failed
- printer offline

## Why

iPadOS and Android printing can be inconsistent across restaurants and printer models. Keeping all printing in Mother POS gives the system one stable hardware owner while tablets stay fast, simple, and easy to replace.
