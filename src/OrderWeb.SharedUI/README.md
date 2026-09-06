# OrderWeb.SharedUI

The shared .NET MAUI presentation library used by OrderWeb Mother and Client.
Mother supplies local authoritative services; Client supplies remote API/cache
services. SharedUI owns presentation and depends only on `OrderWeb.Contracts`.

## Structure

- `Themes`: semantic colours, typography, dimensions, and component styles.
- `Resources`: resource policy and organization. Packaged fixed images remain in
  `Assets/Fixed` as the established canonical location.
- `Controls`: reusable visual building blocks.
- `Dialogs`: shared dialog presentation.
- `Shell`: shared authenticated application frame and navigation presentation.
- `Views`: complete screens shared by both hosts.
- `ViewModels`: host-neutral presentation logic for shared screens.
- `Presentation`: reusable loading, error, empty, offline, and success states.
- `Converters`: presentation-only MAUI value converters.
- `Behaviors`: reusable UI interaction behaviors.
- `Responsive`: shared breakpoints and adaptive-layout helpers.

## Boundaries

SharedUI must not access MariaDB, SQLite, HTTP, WebSockets, printer/payment
drivers, or platform APIs directly. It requests operations through contracts and
receives host-specific implementations through dependency injection.

Common screens are migrated one at a time. Existing working types keep their
namespaces until both hosts consume their shared replacement, preventing a
folder reorganization from becoming an application rewrite.

The visual values are derived from the current Mother POS. New shared screens
use the semantic `Pos*` tokens; existing `Ow*` tokens remain supported during
the incremental migration.
