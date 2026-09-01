# Phase 2: Demo Mode First

This phase creates the first visual Client POS demo using sample restaurant data.

## Job 2.1: Sample Restaurant Data

The demo app now includes in-memory sample data for:

- Restaurant name.
- Floors and tables.
- Menu categories.
- Menu items.
- Modifiers/addons.
- Open orders.
- Online orders.
- Customers.
- Payments placeholder.
- Sample report totals.

## Demo Screens Created

- Connect to Mother POS screen with Demo Mode entry.
- PIN login screen based on the Mother POS login style.
- Dashboard with Restaurant, Delivery, Collection, and Live Order actions.
- Sidebar navigation matching the current Mother POS direction.
- Restaurant Layout demo with floor tabs and table cards.
- Guest count dialog.
- Table Order screen with menu categories, item cards, basket, and action buttons.
- More Options dialog.
- Collection Order form.
- Delivery Order form.
- Live Order list.
- Payment placeholder screen.

## Demo Login PINs

```text
1111 = Staff
2222 = Manager
3333 = Admin
```

These are demo-only and must not become production default credentials.

## Current Limitations

- No real Mother API connection yet.
- No real WebSocket connection yet.
- SQLite cache is now handled in Phase 5.
- No real printing.
- No real payment processing.
- No real gift card/loyalty.

This is intentionally a UI/demo milestone.
