# Toshiba B-FV4D Phase 5 Database Design

Phase 5 is implemented by production migration `039_label_media_profiles_and_jobs.sql` and the Mother-only label persistence models/service.

## Printed-content rule

Each physical label prints only one authoritative value:

- item name; or
- component name.

Order number, date/time, quantity text, modifiers, notes, customer/table details, allergens, prices, and other fields are not printable content. Order/item IDs and copy quantity remain database metadata for audit and correct processing.

The immutable JSON content snapshot contains only `ContentType` and `Name`. A separate `printable_name_snapshot` supports safe operational lookup. This prevents later order/menu edits from changing a retry or reprint.

## Tables

### `network_printers`

The existing printer record now also stores the structured label identity/capability fields, `toshiba_tpcl` module identifier, default-label flag, last successful physical test, and detectable firmware version. It stores the Windows driver name as configuration guidance only—never a driver executable or installer.

### `label_media_profiles`

Stores version-independent physical media/calibration settings. Phase 5 seeds:

- Toshiba 60 × 40 mm Container;
- Toshiba 51 × 30 mm Compact;
- Toshiba 80 × 50 mm Delivery.

Database constraints enforce dimensions, gap/sensor consistency, speed, darkness, and offset ranges.

### `label_print_jobs`

Stores immutable job identity, printer/profile/order/item references, source terminal, idempotency key, item/component type, content snapshot, template version, copy count, optional generated TPCL bytes/hash, durable status, retries, timings, errors, and the original job referenced by a reprint.

Client request IDs are unique per source terminal. Retrying the same accepted Client request resolves to the existing job rather than producing another job.

## Software boundary

`LabelContentSnapshot` captures the item/component name together with quantity, modifiers, order context, preparation time and optional fixed label text. `LabelPrintDatabaseService` serialises that immutable snapshot so a later order edit cannot alter a reprint.

No Toshiba executable, driver, Setting Tool, firmware image, or proprietary installer is stored in these tables or application resources.
