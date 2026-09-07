# Client POS Operations, Authorization, and Release

## Security boundary

Client POS is a daily-operations terminal. Mother POS is the authorization authority and the only place for Administrator access. Client visibility is not a security control: every protected Mother API request validates the paired terminal, active Mother-issued session, current user role, and required capability.

## Manager operating policy

Managers use their own Manager PIN; no shared approval PIN is stored on Client POS.

| Action | Client POS policy | Required approval |
| --- | --- | --- |
| Take orders and manage customers | Allowed when Mother grants the capability. | Signed-in user |
| Payments, discounts, voids, refunds, table transfers, receipt reprints, cash drawer | Allowed only when Mother grants the Manager capability. | Signed-in Manager; Mother validates every request |
| Order history, reservations, gift cards, loyalty, web orders | Available only to Managers when the applicable Client feature is enabled. | Signed-in Manager |
| User/role administration, menu setup, restaurant configuration, terminal/printer/database/backup/system setup, advanced reports | Never available on Client POS. | Mother POS Administrator |

## Offline policy

- Cached menu, table, and customer information may be displayed while Mother is unavailable.
- Basic daily work may be queued only where the existing Client offline queue supports it; queued work is not authoritative until Mother accepts it.
- Payments, refunds, discounts, cash drawer opening, print requests, Manager approvals, and all configuration actions require a live Mother POS connection.
- Administrator access is never permitted offline or online from Client POS.

## Login and audit policy

- An Admin PIN submitted by Client POS receives `403 Forbidden` and `errorCode: admin_mother_only`.
- Mother does not issue or persist a Client session token for that request.
- Client clears any locally stored session state, returns to its normal PIN screen, and displays the Mother-only message without technical details.
- Mother records known-user Client session creation, Admin-login rejection, capability denial, and protected-operation outcomes in `client_security_audit`. PIN values and session-token values must never be logged.

## Verification checklist

Run Mother POS and Client POS together and verify each result:

1. Pair a Client terminal successfully.
2. A basic User can sign in and access only daily order/customer workflows granted by Mother.
3. A Manager can sign in and complete approved daily-management actions.
4. An Admin PIN on Client POS shows the Mother-only popup; no dashboard opens and no Client session/token is retained.
5. The same Admin PIN signs in normally on Mother POS.
6. An incorrect PIN and an inactive user are rejected without a Client session.
7. A Manager cannot reach an Administrator-only navigation route or API operation.
8. Disconnect Mother POS: cached data may be viewed, while protected live actions are rejected or wait for Mother according to the offline policy.
9. Restart Client POS after an Admin rejection and confirm it starts at the PIN screen.
10. Verify two paired Clients maintain separate sessions and terminal identities.
11. Verify expired, revoked, and legacy Admin Client tokens are rejected by Mother.
12. Review `client_security_audit` for successful sessions and rejected/denied actions, confirming that it contains no PIN or token values.

## Release checklist

1. Build Mother POS and Client POS independently.
2. Deploy/restart Mother POS first, or deploy both versions together.
3. Deploy/restart Client POS after Mother is updated.
4. Complete the verification checklist on a real paired terminal.
5. Confirm ordinary User and Manager workflows remain available before rolling out to every terminal.
6. Keep a rollback package for both applications and record the released versions.
