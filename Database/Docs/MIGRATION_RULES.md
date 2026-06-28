# OrderWeb POS Migration Rules

This folder is the production database contract for `OrderWeb.DatabaseSetup.exe` and the Inno Setup installer.

## Execution Order

Run migrations in filename order only:

1. `001_initial_schema.sql`
2. `002_users_permissions.sql`
3. `003_menu.sql`
4. `004_orders.sql`
5. `005_printers_cash_till.sql`
6. `006_tables_terminals.sql`
7. `007_online_orders.sql`
8. `008_reports.sql`
9. `009_delivery_zones.sql`
10. `010_postcode_orderweb_api.sql`
11. `011_production_schema_gaps.sql`

Future migrations must continue with `011_...`, `012_...`, and so on. Never insert a new migration between existing numbers after release.

## Mother And Child Rules

Only the Mother terminal runs migrations. Child terminals must never create, alter, seed, or migrate database schema.

Child terminals may only:

- connect to the Mother database
- check `app_schema_version`
- block startup if the Mother database version is older than the app requires

## Safety Rules

Every production migration must be safe to run once and tracked in `migration_history`.

`OrderWeb.DatabaseSetup.exe` must:

- create a database backup before running migrations
- run migrations inside a transaction where MariaDB allows it
- record success/failure in `migration_history`
- update `app_schema_version` only after successful verify
- run `verify` after migration

## SQL Rules

Use `CREATE TABLE IF NOT EXISTS`, `ALTER TABLE ... ADD COLUMN IF NOT EXISTS`, and idempotent seed statements.

Do not put sample restaurant data in production migrations. Do not create default admin users or default PINs. The first admin must be created by the setup wizard.

Avoid destructive SQL in normal migrations:

- no `DROP TABLE`
- no `TRUNCATE`
- no column drops
- no data deletes

Destructive changes require a special migration with a backup, explicit release note, and rollback plan.

## Required Verification

The verifier must confirm these foundation tables exist:

- `app_schema_version`
- `migration_history`
- `users`
- `orders`
- `order_items`
- `settings`
- `cloud_config`
- `business_info`
- `customer_data`
- `network_printers`
- `network_print_queue`
- `cash_drawer_events`
- `discount_events`
- `terminal_health`
- `terminal_pairings`
- `till_expenses`
- `MealDeals`
- `postcode_lookup_settings`
- `order_number_settings`
- `z_report_print_log`
