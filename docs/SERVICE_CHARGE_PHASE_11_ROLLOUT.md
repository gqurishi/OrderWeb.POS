# Table Service Charge — Controlled Rollout

## Pre-deployment

1. Create and verify an OrderWeb database backup.
2. Confirm schema version 28 and the `table_service_charge_settings`, `order_service_charge_events`, and service-charge order columns exist.
3. Deploy to one mother terminal and one child terminal outside trading hours.
4. Keep the setting on **No Service Charge** until smoke testing is complete.

## Automated acceptance

- Calculator tests cover 0%, 10%, 12.5%, decimal percentages, discount-before-charge, penny rounding, removed status, collection/delivery isolation, setting snapshot isolation, partial-payment modification protection, tip gating, and split-tip reconciliation.
- Run: `dotnet test OrderWeb.DatabaseSetup.Tests/OrderWeb.DatabaseSetup.Tests.csproj`.
- Run: `dotnet build POS-in-NET.csproj -f net10.0-windows10.0.19041.0`.

## Manual acceptance matrix

- 10-inch Windows tablet at 1280×800 and 100%/125% scaling.
- Touch, mouse, physical keyboard, and on-screen numeric keyboard.
- New table order, draft save, restart/reopen, transfer, merge, split/pay-by-items, kitchen send, partial payment, and final payment.
- Manager-PIN removal and restoration, including reason and amount confirmation.
- Verify Add Tip appears only for no configured table charge; collection/delivery remain independent.
- Cash, card, split, and partial-payment tips; refund and payment-void tip reversal.
- Table bill, paid receipt, reprint, split receipt, PDF receipt, 58 mm and 80 mm printers.
- Daily screen, CSV, PDF, Z-report, historical/date-range, payment-method, order-type, and removal audit totals.
- Offline restart, queued cloud retry, mother/child consistency, and version-2 financial payload inspection.

## Reconciliation examples

- Subtotal £50.00, discount £5.00, 10% charge: basis £45.00, charge £4.50, total £49.50.
- Collection/delivery with the same item values: service charge £0.00; delivery fee remains a separate value.
- Removed charge: order amount decreases by £4.50, the removal event retains £4.50 as potential removed value, and tips remain suppressed for that table order.

## Rollout

1. Enable a test percentage and complete one test table order.
2. Reconcile POS total, printed receipt, payment rows, Daily Report, Z-report, and cloud payload.
3. Roll out to remaining child terminals only after the mother terminal passes.
4. Monitor service-charge removal events, failed report uploads, and payment/refund differences for the first seven business days.
5. To disable operationally, select **No Service Charge** in Settings. Existing open orders retain their saved percentage by design.

## Recovery

- Disable the policy in Settings to stop charges on new table orders.
- Do not edit existing order snapshots manually.
- If database recovery is required, follow `Database/Docs/028_TABLE_SERVICE_CHARGE_RECOVERY.md` and restore the verified pre-deployment backup.
