# OrderWeb POS Release Notes

## Version 1.0.1 (Build 2) - 18 July 2026

This is the first production-labelled release of OrderWeb POS.

### Release identity

- Application title: OrderWeb POS
- Application ID: `com.orderweb.pos`
- Application display version: `1.0.1`
- Application build version: `2`
- Database setup utility version: `1.0.1`
- Installer version: `1.0.1`

### Included capabilities

- Live and web order handling with background synchronization.
- Local reporting, reservations, staff time clock, receipt printing, and kitchen routing.
- Mother and Child terminal database setup and schema verification.
- Admin-managed manual payments and refunds.

### Production hardening

- Windows 10 22H2/build 19045 and Windows 11 are supported on Pro, Enterprise, and Education editions; Windows 10 requires active ESU or another applicable supported servicing programme.
- PIN authentication now verifies stored BCrypt hashes and temporarily locks repeated failures.
- Manager actions require a valid Manager or Admin PIN and record approver details.
- Manual refunds record the administrator, reason, terminal, and external reference and cannot exceed the original order total.
- The unused inbound HTTP webhook and legacy runtime database migration service are disabled.
- Sensitive values are sanitized from production diagnostics.
- Finalized OrderWeb orders are retained locally for seven days; older authoritative records remain searchable in OrderWeb.net.
- Cloud financial reports upload only from an explicit Administrator action and become immutable after a successful upload.
- Local test orders may be removed by an Administrator only before that date is uploaded; an audit tombstone is retained and no cloud deletion is sent.
- Removing an employee archives and disables the account while preserving time-clock and audit history.
- Windows database credentials are protected with DPAPI instead of ordinary application preferences.
- OrderWeb endpoints require HTTPS/WSS and credentials are not placed in WebSocket URLs.
- Local and installer database backups use authenticated AES-256 encryption.
- Order History receipt reprinting now sends a real queued print job.
- Database schema 28 separates table service-charge settings and order snapshots from delivery fees without changing historical orders.

### Database requirement

Database schema version `28` is required. Apply bundled migration `028_table_service_charge_schema.sql` and run the schema verifier before starting this release against a production database.

For every subsequent release, increment the integer `ApplicationVersion` build number and keep the same display version in the app project, database setup utility, installer, and release notes.
