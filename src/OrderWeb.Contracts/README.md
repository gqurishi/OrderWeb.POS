# OrderWeb.Contracts

Platform-independent contracts shared by OrderWeb Mother, Client, and SharedUI.

The initial foundation contains:

- standard operation result and error models;
- permission models plus capability and feature identifiers;
- Client access policy (what Mother may grant, what is blocked on every Client);
- capability-driven navigation metadata;
- synchronization version and event models;
- Client/API compatibility models;
- guarded extension points for service interfaces and DTOs.

It must not contain MAUI UI, database access, HTTP implementations, hardware
integration, or platform-specific code. Add contracts incrementally as shared
screens and host implementations require them.

Dependency direction:

```text
OrderWeb.Contracts
        ^
        |-- OrderWeb.SharedUI
        |-- OrderWeb.Mother
        `-- OrderWeb.Client
```
