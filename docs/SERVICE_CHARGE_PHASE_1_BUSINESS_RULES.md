# Table Service Charge — Phase 1 Business Rules

Status: Implemented; business-owner signature still required  
Scope: POS table orders only  
Currency: GBP  

## 1. Purpose

This document defines the agreed behaviour for the table service-charge feature before database, user-interface, payment, receipt, report, and cloud-sync development begins.

The feature must keep table service charges, delivery fees, and customer tips as three separate financial concepts.

## 2. Configuration

Only an administrator can configure the table service-charge policy.

The administrator can select one of two modes:

1. **No Service Charge**
2. **Percentage Service Charge**

Percentage mode uses one restaurant-wide percentage, for example 10% or 12.5%. It is not a fixed currency amount. The percentage supports up to two decimal places.

Changing the setting affects new table orders only. An order stores a snapshot of the policy and percentage that applied when it was created. Existing open orders must not change when the administrator later changes the setting.

Every settings change must record the previous value, new value, administrator, and date/time.

## 3. Order-Type Rules

| Order type | Table service charge | Delivery fee | Table tip rule |
| --- | --- | --- | --- |
| Table | Apply the order's configured snapshot | Not applicable | Controlled by the service-charge mode |
| Collection | Never apply | Not applicable | Outside this feature |
| Delivery | Never apply | Apply independently when appropriate | Outside this feature |

A table service charge must never be written to, displayed as, or reported as a delivery fee.

## 4. Calculation Rules

The service charge is calculated after order-level discounts and before tips.

```text
Eligible table-item subtotal - order discount = service-charge basis
Service-charge basis x configured percentage = service-charge amount
Service-charge basis + service-charge amount = order total before tip
```

The service-charge basis cannot be less than zero. Calculate using decimal values and round the final service-charge amount to two decimal places using one consistent financial rounding rule.

The charge must recalculate while an eligible order is unpaid whenever an item, quantity, price, void, or discount changes. It must not be charged on tips, delivery fees, or on the service charge itself.

### Example A — 10% without a discount

```text
Item subtotal                         £50.00
Discount                               £0.00
Service-charge basis                  £50.00
Service charge (10%)                   £5.00
Order total before tip                £55.00
```

### Example B — 10% after a discount

```text
Item subtotal                         £60.00
Discount                             -£10.00
Service-charge basis                  £50.00
Service charge (10%)                   £5.00
Order total before tip                £55.00
```

### Example C — 12.5% with penny rounding

```text
Item subtotal                         £19.99
Discount                               £0.00
Service-charge basis                  £19.99
Unrounded service charge              £2.49875
Service charge (12.5%)                 £2.50
Order total before tip                £22.49
```

## 5. No-Service-Charge Mode

When the administrator selects **No Service Charge**:

- New table orders have no service charge.
- The table bill and customer receipt show `Service charge not included`.
- The Add Tip step is available during table-order payment.
- Reports show zero collected table service charge for those orders.

## 6. Percentage-Service-Charge Mode

When the administrator selects **Percentage Service Charge**:

- New table orders automatically receive the configured percentage.
- Staff do not choose or change the percentage on the order screen.
- The order screen, bill, and receipt show both percentage and amount, for example `Service charge (10%): £5.00`.
- The current `SERVICE FEE` percentage-selection action is not shown.
- The Add Tip step is not shown for the table order.

## 7. Removing and Restoring a Charge

An unpaid table order with an applied charge provides **Remove Service Charge** under More Options.

Removal requires:

- A selected or entered reason
- Manager or administrator approval
- Confirmation showing the percentage and amount being removed
- An audit record containing the employee, approving manager, reason, percentage, amount, and date/time

After removal:

- The order total recalculates immediately.
- The order stores a `removed` status rather than pretending that service charge was never configured.
- The bill and receipt show `Service charge (10%): Removed`.
- The Add Tip step remains unavailable because the order was created under percentage-service-charge policy.
- More Options provides **Restore Service Charge** while the order remains eligible for changes.

Restoration recalculates the charge from the current eligible basis and creates another audit record.

A fully paid, voided, or closed order cannot have its service-charge state changed through normal order placement.

## 8. Tips

For table orders, tip availability is determined by the policy snapshot captured when the order was created:

| Order policy snapshot | Charge state | Add Tip step |
| --- | --- | --- |
| No service charge | Not configured | Available |
| Percentage service charge | Applied | Hidden |
| Percentage service charge | Removed | Hidden |

Tips must remain separate from sales, service charges, and delivery fees. Approved tips are stored against their payment method so cash and card tips can be reconciled separately.

## 9. Receipts and Customer Display

An applied charge is displayed as:

```text
Subtotal:                              £50.00
Service charge (10%):                   £5.00
TOTAL:                                 £55.00
```

No-service-charge mode is displayed as:

```text
Service charge not included
```

A removed charge is displayed as:

```text
Service charge (10%): Removed
```

The terminology must be `Service charge` throughout settings, orders, receipts, reports, and exports. `Service fee` must not be used for this feature.

## 10. Reporting Rules

Every reporting surface must keep the following values separate:

- Item sales
- Discounts
- Table service charges collected
- Count and value of service charges removed
- Delivery fees
- Cash tips
- Card tips
- Total tips
- Refunds
- VAT
- Customer payments collected

The coverage includes the Daily Report screen, PDF, CSV, printed Z-report, Z-report detail, report history, date-range reports, payment-method reports, order-type reports, and cloud daily-report upload.

Only paid/completed orders and approved payments contribute positive collected totals. Drafts, voids, failed attempts, removed charges, and refunded amounts must not be counted as positive revenue.

Service charge is already included in the order total and must not be added to gross sales twice.

## 11. Proposed Legal and Tax Classification — Approval Required

**Proposed decision:** the table service charge is discretionary/optional because it can be removed at the customer's request.

Customer-facing material must clearly state that it is optional. The final VAT and payroll/tronc implementation must follow the confirmed classification and be reviewed with the business accountant or adviser.

This decision must be explicitly approved by the business owner before Phase 2.

## 12. Proposed Business-Day Boundary — Approval Required

**Proposed decision:** retain the application's existing 1:00 AM local-time trading-day rollover.

Transactions from midnight up to, but not including, 1:00 AM belong to the previous business date. Service-charge and tip reporting must use the same boundary as other end-of-day reporting.

This decision must be explicitly approved by the business owner before reporting implementation.

## 13. Phase 1 Acceptance Criteria

Phase 1 is complete when the business owner confirms all of the following:

- Table orders only
- No-charge or one administrator-configured percentage
- Percentage calculated after discounts
- Existing orders retain their policy snapshot
- Manager approval and reason required for removal
- Tips available only for orders created under no-service-charge policy
- Removed charges do not activate tipping
- Discretionary/optional classification
- 1:00 AM business-day rollover
- Receipt wording and report categories

## 14. Sign-Off

Business owner: ______________________________  
Decision: Approved / Changes required  
Date: __________________  
Notes: ________________________________________________________________
