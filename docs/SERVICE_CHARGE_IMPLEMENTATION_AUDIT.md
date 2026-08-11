# Table Service Charge — Phase 1–11 Implementation Audit

Audit date: 22 July 2026  
Business-day boundary: 1:00 AM local time  
Scope: table orders only

## Completion matrix

| Phase | Status | Evidence |
| --- | --- | --- |
| 1. Business rules | Implemented, signature pending | `SERVICE_CHARGE_PHASE_1_BUSINESS_RULES.md`; optional/compulsory policy and 1:00 AM boundary documented. The business owner must still sign the decision. |
| 2. Database | Complete | Migration 028, encrypted pre-migration backup, additive order snapshot fields, audit tables, indexes, schema verification, migration tests, and recovery guide. `delivery_fee` remains separate. |
| 3. Admin settings | Complete | Admin-only settings card, no-charge/percentage modes, 0.01–30.00% validation, two decimal places, example, confirmation, and transactional setting audit. |
| 4. Calculation | Complete | Central decimal calculator applies percentage after discount, rounds once, excludes non-table orders, delivery fees, tips, and other charges. |
| 5. Lifecycle | Complete in application | New orders snapshot policy; persistence/reopen/send/transfer retain the snapshot. Merge retains each existing order; split/pay-by-items allocates adjustments without changing the order snapshot. Partial payment locks removal/restoration. |
| 6. Order UI | Complete | Legacy per-order percentage selector removed. Breakdown shows subtotal, discount, service charge or “Not included”, and total. Manager-approved removal/restoration is audited. |
| 7. Tips/payments | Complete | Tip gating follows the order policy snapshot. Tips live on approved payment rows, split proportionally, are deduplicated by transaction reference, and are excluded when a payment is voided or reversed by refund. Paid orders cannot be voided before payment reversal/refund. |
| 8. Receipts | Complete | Table bill, paid/reprint/split receipt, PDF, preview, and template output distinguish applied, removed, and not-configured service charges. Cash paid/change and payment-method tips are printed. |
| 9. Reports | Complete | Daily screen/PDF/CSV and Z summary/detail separate sales, discounts, service charges, removals, potential value, cash/card tips, delivery fees, refunds, VAT, and collected money. Removal reasons and manager approval are visible/exported. All report ranges use the 1:00 AM boundary. |
| 10. Cloud | Complete client-side | Version-2 order and daily-report payloads keep delivery fees separate and include snapshots, full removal details, tip methods, removal count/value, and compatibility handling. Production server acceptance remains an integration deployment check. |
| 11. Testing/rollout | Automated complete; hardware validation pending | Automated policy/schema/boundary tests and Windows build. Follow `SERVICE_CHARGE_PHASE_11_ROLLOUT.md` for tablet, printer, offline, mother/child, and cloud checks. |

## Required external acceptance

The following cannot be certified from source code alone:

1. Business-owner signature for optional/compulsory classification and the 1:00 AM boundary.
2. Physical 10-inch tablet checks at the deployed resolution/scaling.
3. 58 mm and 80 mm receipt-printer output.
4. Mother/child terminal restart and offline recovery.
5. Confirmation that the production OrderWeb server accepts contract version 2.

Do not enable the percentage policy in production until these controlled-rollout checks pass.
