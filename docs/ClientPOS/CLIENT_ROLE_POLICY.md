# Client POS Role Policy

## Purpose

Client POS terminals are for day-to-day restaurant operations. Mother POS is the only terminal that provides Administrator access and system control.

## Allowed roles on Client POS

| Role | Permitted on Client POS | Blocked on Client POS |
| --- | --- | --- |
| Basic user (`User`) | Take and submit orders, view tables, manage customers, and use the daily sales workflow granted by Mother POS. | Administrator tools, terminal setup, user and role management, database work, system settings, menu configuration, printer configuration, and reports reserved for Mother POS. |
| Manager | Basic-user operations plus Mother-approved daily-management capabilities, such as payments, discounts, voids, refunds, cash-drawer access, receipt reprints, and order history. | All Administrator tools and configuration. |
| Administrator (`Admin`) | No Client POS session is permitted. | All Client POS access. The user must sign in on Mother POS. |
| Staff (`Staff`) | No normal Client POS sales session. Staff PINs remain available for their designated time-clock workflow. | Sales and management dashboards. |

Mother POS remains the source of truth for the user role, permissions, and capabilities. Client POS may use those capabilities to present its interface, but Mother POS must enforce all authorization decisions.

## Administrator login rule

When a paired Client POS submits an active Administrator PIN, Mother POS returns HTTP `403 Forbidden` with the stable error code `admin_mother_only`. It must not issue or persist a Client session token. The Client POS shows this message:

> Administrator access is available on the Mother POS only. Please use the Mother POS terminal.

Mother POS records the rejected attempt in `client_security_audit` without recording the PIN.
