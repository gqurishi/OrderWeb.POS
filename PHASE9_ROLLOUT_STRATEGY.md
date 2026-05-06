# Phase 9: Rollout Strategy (Safe Migration)

This phase introduces controlled feature flags so lifecycle behavior can be enabled in production in small, reversible steps.

## Rollout Flags

The app reads these keys from the `settings` table:

- `order.lifecycle.rollout.schema_migration_ready`
- `order.lifecycle.rollout.lifecycle_reads_enabled`
- `order.lifecycle.rollout.lifecycle_writes_enabled`
- `order.lifecycle.rollout.draft_save_table_enabled`
- `order.lifecycle.rollout.draft_save_collection_enabled`
- `order.lifecycle.rollout.draft_save_delivery_enabled`
- `order.lifecycle.rollout.resume_path_enabled`
- `order.lifecycle.rollout.send_durability_enabled`
- `order.lifecycle.rollout.payment_lines_enabled`
- `order.lifecycle.rollout.strict_finalize_enabled`
- `order.lifecycle.rollout.legacy_fallback_paths_removed`
- `order.lifecycle.rollout.validation_window_closed`

Safe defaults are inserted automatically and start with writes disabled.

## Recommended Activation Sequence

1. Schema migration first (backward-compatible)
- Keep `schema_migration_ready=true`.
- Confirm all added columns/tables exist without breaking old reads.

2. Lifecycle reads before lifecycle writes
- `lifecycle_reads_enabled=true`
- Keep `lifecycle_writes_enabled=false`

3. Enable draft-save for one channel first (table)
- `lifecycle_writes_enabled=true`
- `draft_save_table_enabled=true`
- Keep collection and delivery draft-save disabled.

4. Enable resume path
- `resume_path_enabled=true`

5. Enable send durability
- `send_durability_enabled=true`

6. Enable payment lines and strict finalize rules
- `payment_lines_enabled=true`
- `strict_finalize_enabled=true`

7. Remove old fallback paths after validation window
- `legacy_fallback_paths_removed=true`
- After monitoring period ends: `validation_window_closed=true`

## What This Controls in Code

- Draft autosave and lifecycle writes are gated by channel and rollout flags.
- Resume loading is gated by `resume_path_enabled`.
- Send durability (batch tracking/writeback) is gated by `send_durability_enabled`.
- Payment line entries in `order_payments` are gated by `payment_lines_enabled`.
- Finalize checks (all items sent and fully settled payment) are gated by `strict_finalize_enabled`.
- Legacy print-group and price fallbacks remain active until both fallback removal flags are enabled.

## Rollback Guidance

If issues appear:
- Disable the most recent flag change first.
- Keep schema migration in place.
- Re-run with a narrower channel scope (table only) before re-enabling globally.
