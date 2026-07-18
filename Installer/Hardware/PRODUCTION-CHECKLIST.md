# OrderWeb POS printer and card-terminal go-live checklist

Complete this checklist on the live POS network. The application cannot configure a printer's DHCP reservation, create paper-out conditions, or reconcile the acquirer's terminal by itself.

## Printer addressing and ownership

- [ ] Give every printer a private static IP or DHCP reservation and record it below.
- [ ] In **Settings > Printers**, configure the same IP and TCP port (normally 9100).
- [ ] Confirm each physical printer appears only once and is assigned to the correct print group.
- [ ] Confirm the Mother terminal is the print-queue owner. Use a named queue terminal only when intentionally configured; never allow several terminals to process the same queue.

| Printer / location | Static or reserved IP | Port | Print group | Test result |
|---|---:|---:|---|---|
| | | 9100 | | |
| | | 9100 | | |
| | | 9100 | | |

## Printer acceptance test

- [ ] Use **Test All** and physically confirm one readable test receipt from every enabled printer.
- [ ] Place one test item in every print group and confirm it reaches only the intended station.
- [ ] Send an addition to an already-sent order and confirm an ADD ticket.
- [ ] Reduce a sent item's quantity and confirm a VOID/reduction ticket.
- [ ] Change notes, modifiers, variant, or item preparation and confirm the old preparation is voided and the replacement is added.
- [ ] Void a sent item and then a complete sent order; confirm the correct void tickets and reason.
- [ ] Disconnect one printer, send a test order, and confirm the job remains visible in **Manage Queue** as pending/failed.
- [ ] Reconnect the printer, select **Manage Queue > Retry Failed**, and confirm exactly one ticket prints.
- [ ] Repeat the failure/retry test with the printer out of paper.
- [ ] Confirm no duplicate ticket is produced by a Child terminal.

Do not cancel failed jobs until a manager has established whether the physical ticket printed. Retrying a job that printed before its network acknowledgement can produce a duplicate; the kitchen must verify before retrying.

## Manual card terminal acceptance test

- [ ] Confirm the POS card dialog and terminal both display the same amount.
- [ ] Confirm **YES** remains disabled until staff tick the approved/amount-match confirmation and enter the card receipt/reference.
- [ ] Run one approved transaction and verify its reference is stored in the order payment record.
- [ ] Run one declined/cancelled transaction and verify the order is not marked paid.
- [ ] Keep the merchant card receipts with the till's daily paperwork.

## Daily card reconciliation

At close of business:

1. Print the card terminal's end-of-day/batch report.
2. Run the OrderWeb POS Z report for the identical trading period.
3. Compare the POS **local card total and transaction count** with the terminal total and count. Do not include web-paid orders in the manual-terminal total.
4. Investigate differences using order payment references and retained merchant receipts.
5. Record and sign any variance; never alter or delete payment records to force a match.

| Business date | POS local card total/count | Terminal total/count | Variance | Checked by / time |
|---|---:|---:|---:|---|
| | | | | |

Go live only after every acceptance item passes with the production printers, production network, and production card terminal.
