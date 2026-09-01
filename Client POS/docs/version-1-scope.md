# Client POS Version 1 Scope

## Job 0.2: Version 1 Includes

Version 1 should prove the daily Client POS flow works from install to live operation.

### Required In Version 1

- Connect to Mother screen.
- Demo Mode.
- Pairing with Mother POS.
- Login through Mother POS.
- Role-based dashboard.
- Table/order workflow.
- Payment and close order workflow.
- Print request flow.
- Online order display.
- Reconnect status.
- Diagnostics screen.

## Version 1 User Flow

```text
Install Client POS
Open app
Connect to Mother POS or use Demo Mode
Pair device with Mother
Download bootstrap cache
Login with Mother user using the same PIN-style experience as Mother POS
Use dashboard
Take order
Take payment
Request print
Receive live updates
```

## Leave For Later

These features are intentionally outside the first version unless they become necessary for pilot testing.

- Full reports.
- Menu editing.
- Gift cards and loyalty.
- Reservations.
- User management.
- Advanced admin settings.
- Full offline transaction mode.
- Auto-discovery and QR pairing.

## Version 1 Success Criteria

Version 1 is successful when:

- Client POS runs on at least Windows plus one tablet platform.
- Demo Mode supports dashboard, tables, order, and payment screens.
- Real pairing flow can connect a Client to Mother.
- Client can download bootstrap data into SQLite.
- User can log in using Mother credentials.
- Client can create/update/close an order through Mother.
- Client receives live order/table updates from Mother.
- Reconnect behavior is clear and safe.
