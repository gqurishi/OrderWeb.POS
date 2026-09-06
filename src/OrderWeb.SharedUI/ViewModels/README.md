# ViewModels

Shared view-models contain host-neutral presentation logic and depend on
interfaces from `OrderWeb.Contracts`. They must not resolve services through a
service locator or access database, network, hardware, or platform APIs.
