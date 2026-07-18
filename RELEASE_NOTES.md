# OrderWeb POS Release Notes

## Version 1.0.0 (Build 1) - 18 July 2026

This is the first production-labelled release of OrderWeb POS.

### Release identity

- Application title: OrderWeb POS
- Application ID: `com.orderweb.pos`
- Application display version: `1.0.0`
- Application build version: `1`
- Database setup utility version: `1.0.0`
- Installer version: `1.0.0`

### Included capabilities

- Live and web order handling with background synchronization.
- Local reporting, reservations, staff time clock, receipt printing, and kitchen routing.
- Mother and Child terminal database setup and schema verification.
- Admin-managed manual payments and refunds.

### Production hardening

- PIN authentication now verifies stored BCrypt hashes and temporarily locks repeated failures.
- Manager actions require a valid Manager or Admin PIN and record approver details.
- Manual refunds record the administrator, reason, terminal, and external reference and cannot exceed the original order total.
- The unused inbound HTTP webhook and legacy runtime database migration service are disabled.
- Sensitive values are sanitized from production diagnostics.

### Database requirement

Database schema version `26` is required. Apply bundled migration `026_manual_refund_audit.sql` and run the schema verifier before starting this release against a production database.

For every subsequent release, increment the integer `ApplicationVersion` build number and keep the same display version in the app project, database setup utility, installer, and release notes.
