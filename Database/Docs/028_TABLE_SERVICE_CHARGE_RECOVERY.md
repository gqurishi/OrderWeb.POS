# Migration 028 — Table Service Charge Recovery

## Scope

Migration `028_table_service_charge_schema.sql` is additive. It creates typed service-charge settings and audit tables, adds service-charge snapshot/removal columns to `orders`, and leaves `delivery_fee` unchanged.

It does not delete rows, rename columns, drop columns, drop tables, or reinterpret historical delivery fees.

## Pre-Migration Evidence

Production database reviewed: `orderweb_pos`  
Previous schema version: `27`  
Review date: `2026-07-21`  

Pre-migration row counts:

| Table | Rows |
| --- | ---: |
| `orders` | 6 |
| `order_items` | 9 |
| `order_payments` | 14 |

Historical `delivery_fee` review:

| Order type | Orders | Non-zero delivery-fee rows | Delivery-fee total |
| --- | ---: | ---: | ---: |
| Pickup | 1 | 0 | £0.00 |
| Delivery | 1 | 0 | £0.00 |
| Table | 4 | 0 | £0.00 |

Because all historical values are zero, no backfill or reclassification is required. Existing orders receive the safe defaults `service_charge_status = 'not_configured'` and zero percentage, basis, and amount.

## Backup

An encrypted pre-migration backup was created at:

```text
C:\ProgramData\OrderWebPOS\Backups\orderweb_pos_manual_20260721_232640_v1.0.1.orderwebbackup
```

Evidence:

```text
Size:   971022 bytes
SHA256: 07493FA65CFEBB7A68B3FA5A17DDCA1B3DDD2FB3DF2849FB6D3A2E93E6800EE7
```

The production migration runner also created its automatic pre-migration backup:

```text
Path:   C:\ProgramData\OrderWebPOS\Backups\orderweb_pos_pre_migrate_20260721_232826_v1.0.1.orderwebbackup
Size:   971028 bytes
SHA256: 87CC4AB8B2A77741354A55A0E972F3EEB6B782C75A5B6ADAA26047A93ACA778D
```

The package uses the OrderWeb encrypted backup format. The restore service validates the package and its internal SQL checksum before replacing database data, and creates another safety backup before restore.

## Preferred Application Rollback

The schema change is backward compatible. If the later application feature must be rolled back, deploy the previous application version and leave the additive schema in place. Older application code continues to use `delivery_fee` and ignores the new tables and columns.

Do not manually lower `app_schema_version`, delete migration history, or remove the new columns.

## Emergency Database Restore

Use a full restore only when migration validation fails or the database is unusable immediately after migration. A full restore replaces the database with the backup state and therefore removes transactions created after the backup timestamp.

1. Stop every POS application connected to the Mother database.
2. Preserve/export any transactions created after the backup when possible.
3. Confirm the backup path, size, and SHA256 shown above.
4. Run the database setup utility on the Mother terminal, using the newest confirmed pre-migration backup:

```powershell
OrderWeb.DatabaseSetup.exe restore --config-path "C:\ProgramData\OrderWebPOS\orderweb-database.json" --input "C:\ProgramData\OrderWebPOS\Backups\orderweb_pos_pre_migrate_20260721_232826_v1.0.1.orderwebbackup"
```

5. Run schema verification for the restored application version.
6. Start the Mother POS and validate orders, payments, and reporting.
7. Start Child terminals only after the Mother is confirmed healthy.

The restore command must be run only by an authorised administrator with a confirmed maintenance window.

## Post-Migration Verification

Verification must confirm:

- Schema version and migration history both report `28`.
- `table_service_charge_settings` contains exactly the singleton row with ID `1`.
- The default setting is disabled, 0%, and optional.
- All required service-charge columns exist on `orders`.
- `table_service_charge_setting_events` and `order_service_charge_events` exist.
- `delivery_fee` still exists and historical values are unchanged.
- Pre-existing `orders`, `order_items`, and `order_payments` row counts are unchanged.
- Schema verification and bundled migration tests pass.
