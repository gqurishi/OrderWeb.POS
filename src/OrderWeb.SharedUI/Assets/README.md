# Shared application assets

`Fixed` is the canonical source for images that belong to the OrderWeb application itself.
Mother and Client link these files into their MAUI packages, preserving the existing simple
filenames such as `dashboard.png` and `table_1.png`.

## Fixed assets

- `Brand`: OrderWeb company logo and mark.
- `Navigation`: fixed home and logout graphics.
- `Dashboard`: navigation and feature icons shared by every terminal.
- `Defaults`: fallback food/table graphics and temporary compatibility aliases.
- `Payments`: built-in cash, card, and gift-card icons.
- `Status`: built-in connection and operation status icons.

`SharedImageNames` is the stable code catalog for these packaged assets. Warning
and error artwork is provided by `status_warning.svg` and `status_error.svg`;
MAUI exposes SVG resources to application code using their generated `.png`
resource names.

## Never place here

Restaurant-controlled content must remain outside Shared UI:

- Restaurant logo
- Menu and category photographs
- Floor-plan background images
- Promotional images

Mother owns those dynamic files and their metadata. The Mother API transfers them to a Client,
and the Client stores them in its application cache. A missing or failed dynamic image may use a
fixed fallback from this folder, but dynamic content must never overwrite a packaged asset.

The old Mother and Client image folders are intentionally retained during migration for rollback,
but their fixed POS images are no longer the packaged source of truth.
