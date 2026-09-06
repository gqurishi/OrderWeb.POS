# Dialogs

Shared dialogs belong here as they are migrated. Dialogs expose presentation
and commands only; payment, database, printing, and network work is performed
by host services.

The existing `ConfirmationDialog` and `ErrorDialog` remain in `Controls` until
their consumers can be migrated without namespace churn.
