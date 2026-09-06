# Service contracts

The core host-neutral service interfaces are declared in
`CoreServiceContracts.cs`. Keep database, HTTP, cache, hardware, and platform
details out of every interface.

Mother implementations call authoritative domain/database/hardware services.
Client implementations call the Mother API and use its local cache. SharedUI
receives the applicable implementation through constructor injection.

The interfaces are the migration boundary, not a requirement to rewrite all
working services immediately. Implement and register each adapter when its first
shared operational screen is migrated.
